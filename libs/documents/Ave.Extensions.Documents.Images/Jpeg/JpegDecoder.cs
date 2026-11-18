using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images.Jpeg;

/// <summary>
/// Decodes a JPEG file to samples (ITU-T T.81 | ISO/IEC 10918-1), for the variants <see cref="JpegReader"/> accepts:
/// baseline (SOF0), extended sequential (SOF1) and progressive (SOF2) DCT with Huffman coding, 8-bit precision, one
/// component (grey) or three (YCbCr or RGB). Arithmetic coding, 12-bit precision, the lossless and hierarchical
/// processes and four components stay refused by the header reader, which runs first.
/// </summary>
/// <remarks>
/// <para>Steps: marker segments (Annex B) → quantisation and Huffman tables, restart interval, the frame → every scan
/// decoded into the quantised coefficients of each block (Annex F.2 for the sequential process, Annex G.1.2 for the
/// progressive one; the coefficients are kept for all scans) → dequantisation and the inverse DCT
/// (<see cref="InverseDct"/>) → <see cref="JpegColor"/> for up-sampling and the colour transform.</para>
/// <para>Bounded work (docs/architecture.md): the frame's size is checked against <see cref="ImageLimits.MaxPixels"/>
/// before anything is allocated, so the coefficient planes take at most 2 bytes per coefficient of the padded block grid
/// (at most 2^26 × 3 × 2 bytes plus the padding of the last blocks). A progressive file is a list of scans, each of which
/// visits blocks; a scan costs a few bytes in the file whatever it visits, so the blocks all scans visit together are
/// limited to <see cref="MaxPasses"/> times the blocks of the frame (<c>Documents.Image.TooLarge</c>). Typical files
/// use 1 (sequential) to about 4 (progressive).</para>
/// </remarks>
internal sealed class JpegDecoder
{
    /// <summary>How many times, in total, the scans of a file may visit the blocks of its frame: 32.</summary>
    public const int MaxPasses = 32;

    private const byte DefineHuffmanTables = 0xC4;
    private const byte DefineQuantisationTables = 0xDB;
    private const byte DefineRestartInterval = 0xDD;
    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;
    private const byte DefineNumberOfLines = 0xDC;
    private const byte AdobeApplication = 0xEE;

    private readonly ReadOnlyMemory<byte> _file;
    private readonly ushort[]?[] _quantisation = new ushort[4][];
    private readonly HuffmanTable?[] _dcTables = new HuffmanTable[4];
    private readonly HuffmanTable?[] _acTables = new HuffmanTable[4];
    private int _restartInterval;
    private int? _adobeTransform;
    private Frame? _frame;
    private long _visits;

    private JpegDecoder(ReadOnlyMemory<byte> file) => _file = file;

    /// <summary>One component of the frame (§B.2.2) and its coefficients, block by block, 64 per block in zig-zag order.</summary>
    private sealed class Component(int id, int horizontal, int vertical, int table)
    {
        public int Id { get; } = id;

        public int Horizontal { get; } = horizontal;

        public int Vertical { get; } = vertical;

        public int Table { get; } = table;

        /// <summary>Width and height in samples: ⌈X · Hi / Hmax⌉, ⌈Y · Vi / Vmax⌉ (§A.1.1).</summary>
        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>The block grid, padded to whole MCUs.</summary>
        public int BlocksPerLine { get; set; }

        public int BlockRows { get; set; }

        public short[] Coefficients { get; set; } = [];

        public int DcPrediction { get; set; }
    }

    /// <summary>The frame header (§B.2.2) and what follows from it.</summary>
    private sealed record Frame(bool Progressive, int Width, int Height, ImmutableArray<Component> Components, int MaxHorizontal, int MaxVertical, int McusPerLine, int McuRows);

    /// <summary>A scan's parameters (§B.2.3).</summary>
    private sealed record Scan(ImmutableArray<(Component Component, int Dc, int Ac)> Components, int Start, int End, int High, int Low);

    /// <summary>Decodes a JPEG file that <see cref="JpegReader"/> accepted.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The samples, or a <c>Documents.Image</c> error.</returns>
    public static Result<RasterImage, Error> Decode(JpegImage image)
    {
        if ((long)image.PixelWidth * image.PixelHeight > ImageLimits.MaxPixels)
        {
            return ImageErrors.TooLarge<RasterImage>(image.PixelWidth, image.PixelHeight);
        }

        var decoder = new JpegDecoder(image.File.AsMemory());
        return decoder.Segments().OnSuccessBind(_ => decoder.Samples(image));
    }

    /// <summary>Reads the marker segments in order until EOI (§B.2.1, B.5), decoding each scan where it starts.</summary>
    private Result<bool, Error> Segments()
    {
        var span = _file.Span;
        var offset = 2;
        while (true)
        {
            if (offset >= span.Length || span[offset] != 0xFF)
            {
                return _frame is not null && _visits > 0
                    ? ImageErrors.Invalid<bool>("expected a marker after the image data (the file ends without EOI or is damaged).", offset)
                    : ImageErrors.Invalid<bool>("expected a marker.", offset);
            }

            while (offset < span.Length && span[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= span.Length)
            {
                return ImageErrors.Invalid<bool>("the file ends inside a marker.", offset);
            }

            var marker = span[offset];
            if (marker == EndOfImage)
            {
                return _frame is not null && _visits > 0
                    ? Result<bool, Error>.Success(true)
                    : ImageErrors.Invalid<bool>("the image ends before any scan.", offset);
            }

            if (marker is >= 0xD0 and <= 0xD7)
            {
                return ImageErrors.Invalid<bool>($"restart marker RST{marker - 0xD0} outside the image data.", offset);
            }

            if (offset + 2 >= span.Length)
            {
                return ImageErrors.Invalid<bool>("the file ends inside a marker segment.", offset);
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 1)..]);
            if (length < 2 || offset + 1 + length > span.Length)
            {
                return ImageErrors.Invalid<bool>($"marker segment FF{marker:X2} has an impossible length {length}.", offset);
            }

            var segment = _file.Slice(offset + 3, length - 2);
            var after = offset + 1 + length;
            var result = marker switch
            {
                DefineQuantisationTables => QuantisationTables(segment.Span, offset),
                DefineHuffmanTables => HuffmanTables(segment.Span, offset),
                DefineRestartInterval => RestartInterval(segment.Span, offset),
                0xC0 or 0xC1 or 0xC2 => FrameHeader(marker, segment.Span, offset),
                StartOfScan => ScanData(segment.Span, after, offset),
                DefineNumberOfLines => ImageErrors.Invalid<int>("a DNL marker: the frame already gives its number of lines.", offset),
                AdobeApplication => Adobe(segment.Span),
                _ => Result<int, Error>.Success(after),
            };

            if (result.IsFailure)
            {
                return Result<bool, Error>.Failure(result.Error);
            }

            offset = marker == StartOfScan ? result.Value : after;
        }
    }

    /// <summary>DQT (§B.2.4.1): per table Pq/Tq, then 64 values of 8 bits (Pq 0) or 16 bits (Pq 1), in zig-zag order.</summary>
    private Result<int, Error> QuantisationTables(ReadOnlySpan<byte> segment, int offset)
    {
        var position = 0;
        while (position < segment.Length)
        {
            int precision = segment[position] >> 4;
            int id = segment[position] & 15;
            var size = precision == 0 ? 64 : 128;
            if (precision > 1 || id > 3 || position + 1 + size > segment.Length)
            {
                return ImageErrors.Invalid<int>($"a quantisation table with precision {precision} and id {id} does not fit its segment.", offset);
            }

            var table = new ushort[64];
            for (var index = 0; index < 64; index++)
            {
                table[index] = precision == 0 ? segment[position + 1 + index] : BinaryPrimitives.ReadUInt16BigEndian(segment[(position + 1 + (2 * index))..]);
            }

            _quantisation[id] = table;
            position += 1 + size;
        }

        return Result<int, Error>.Success(0);
    }

    /// <summary>DHT (§B.2.4.2): per table Tc/Th, 16 counts (BITS), then the values (HUFFVAL).</summary>
    private Result<int, Error> HuffmanTables(ReadOnlySpan<byte> segment, int offset)
    {
        var position = 0;
        while (position < segment.Length)
        {
            if (position + 17 > segment.Length)
            {
                return ImageErrors.Invalid<int>("a Huffman table header does not fit its segment.", offset);
            }

            int tableClass = segment[position] >> 4;
            int id = segment[position] & 15;
            var counts = segment.Slice(position + 1, 16);
            var total = 0;
            foreach (var count in counts)
            {
                total += count;
            }

            if (tableClass > 1 || id > 3 || total > 256 || position + 17 + total > segment.Length)
            {
                return ImageErrors.Invalid<int>($"Huffman table class {tableClass} id {id} with {total} codes does not fit its segment.", offset);
            }

            var table = HuffmanTable.Create(counts, segment.Slice(position + 17, total));
            if (table is null)
            {
                return ImageErrors.Invalid<int>($"Huffman table class {tableClass} id {id} has more codes of some length than fit.", offset);
            }

            (tableClass == 0 ? _dcTables : _acTables)[id] = table;
            position += 17 + total;
        }

        return Result<int, Error>.Success(0);
    }

    /// <summary>DRI (§B.2.4.4): the number of MCUs between restart markers, 0 for none.</summary>
    private Result<int, Error> RestartInterval(ReadOnlySpan<byte> segment, int offset)
    {
        if (segment.Length != 2)
        {
            return ImageErrors.Invalid<int>("a DRI segment is 4 bytes long.", offset);
        }

        _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment);
        return Result<int, Error>.Success(0);
    }

    /// <summary>
    /// The Adobe APP14 segment ("Adobe" + version, flags0, flags1, transform): transform 0 means the three components are
    /// RGB, 1 that they are YCbCr. ISO 32000-1 §7.4.8 (DCTDecode, ColorTransform) lets this marker decide over the default.
    /// </summary>
    private Result<int, Error> Adobe(ReadOnlySpan<byte> segment)
    {
        if (segment.Length >= 12 && segment[..5].SequenceEqual("Adobe"u8))
        {
            _adobeTransform = segment[11];
        }

        return Result<int, Error>.Success(0);
    }

    /// <summary>SOFn (§B.2.2): the header reader has checked the process, the precision, the size and the component count.</summary>
    private Result<int, Error> FrameHeader(byte marker, ReadOnlySpan<byte> segment, int offset)
    {
        if (_frame is not null)
        {
            return ImageErrors.Invalid<int>("a second frame header; a JPEG file of these processes has one frame.", offset);
        }

        int height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        int count = segment[5];
        var components = ImmutableArray.CreateBuilder<Component>(count);
        for (var index = 0; index < count; index++)
        {
            var record = segment.Slice(6 + (3 * index), 3);
            int horizontal = record[1] >> 4;
            int vertical = record[1] & 15;
            int table = record[2];
            if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || table > 3)
            {
                return ImageErrors.Invalid<int>($"component {record[0]} has sampling factors {horizontal}x{vertical} or quantisation table {table} outside the allowed range.", offset);
            }

            foreach (var other in components)
            {
                if (other.Id == record[0])
                {
                    return ImageErrors.Invalid<int>($"two components have the identifier {record[0]}.", offset);
                }
            }

            components.Add(new Component(record[0], horizontal, vertical, table));
        }

        var maxHorizontal = 1;
        var maxVertical = 1;
        foreach (var component in components)
        {
            maxHorizontal = int.Max(maxHorizontal, component.Horizontal);
            maxVertical = int.Max(maxVertical, component.Vertical);
        }

        // §A.2.4: an MCU of an interleaved scan is Hmax × Vmax blocks of 8 × 8 samples of full resolution.
        var mcusPerLine = (width + (8 * maxHorizontal) - 1) / (8 * maxHorizontal);
        var mcuRows = (height + (8 * maxVertical) - 1) / (8 * maxVertical);
        foreach (var component in components)
        {
            component.Width = (int)(((long)width * component.Horizontal + maxHorizontal - 1) / maxHorizontal);
            component.Height = (int)(((long)height * component.Vertical + maxVertical - 1) / maxVertical);
            component.BlocksPerLine = mcusPerLine * component.Horizontal;
            component.BlockRows = mcuRows * component.Vertical;
            component.Coefficients = new short[(long)component.BlocksPerLine * component.BlockRows * 64];
        }

        _frame = new Frame(marker == 0xC2, width, height, components.MoveToImmutable(), maxHorizontal, maxVertical, mcusPerLine, mcuRows);
        return Result<int, Error>.Success(0);
    }

    /// <summary>
    /// SOS (§B.2.3) and the entropy-coded data after it: Ns, then per component its selector and DC/AC table ids, then
    /// Ss, Se, Ah/Al. Returns the offset of the marker that ends the data.
    /// </summary>
    private Result<int, Error> ScanData(ReadOnlySpan<byte> segment, int dataStart, int offset)
    {
        if (_frame is not { } frame)
        {
            return ImageErrors.Invalid<int>("a scan before the frame header.", offset);
        }

        int count = segment.Length > 0 ? segment[0] : 0;
        if (count is < 1 or > 4 || segment.Length != 4 + (2 * count))
        {
            return ImageErrors.Invalid<int>($"a scan header for {count} components has the wrong length.", offset);
        }

        var components = ImmutableArray.CreateBuilder<(Component, int, int)>(count);
        var blocksPerMcu = 0;
        for (var index = 0; index < count; index++)
        {
            int selector = segment[1 + (2 * index)];
            var component = Find(frame, selector);
            int dc = segment[2 + (2 * index)] >> 4;
            int ac = segment[2 + (2 * index)] & 15;
            if (component is null)
            {
                return ImageErrors.Invalid<int>($"the scan names component {selector}, which the frame does not have.", offset);
            }

            if (dc > 3 || ac > 3)
            {
                return ImageErrors.Invalid<int>($"the scan uses Huffman table {dc}/{ac}; there are four of each class.", offset);
            }

            blocksPerMcu += component.Horizontal * component.Vertical;
            components.Add((component, dc, ac));
        }

        var scan = new Scan(components.MoveToImmutable(), segment[1 + (2 * count)], segment[2 + (2 * count)], segment[3 + (2 * count)] >> 4, segment[3 + (2 * count)] & 15);
        if (CheckScan(frame, scan, blocksPerMcu) is { } problem)
        {
            return ImageErrors.Invalid<int>(problem, offset);
        }

        foreach (var (component, dc, ac) in scan.Components)
        {
            var needsDc = scan.Start == 0 && scan.High == 0;
            var needsAc = scan.Start > 0 || !frame.Progressive;
            if ((needsDc && _dcTables[dc] is null) || (needsAc && _acTables[ac] is null) || _quantisation[component.Table] is null)
            {
                return ImageErrors.Invalid<int>($"the scan of component {component.Id} uses a Huffman or quantisation table that was not defined before it.", offset);
            }
        }

        var blocks = scan.Components.Length == 1
            ? (long)BlocksAcross(scan.Components[0].Component) * BlocksDown(scan.Components[0].Component)
            : (long)frame.McusPerLine * frame.McuRows * blocksPerMcu;
        _visits += blocks;
        var frameBlocks = 0L;
        foreach (var component in frame.Components)
        {
            frameBlocks += (long)BlocksAcross(component) * BlocksDown(component);
        }

        if (_visits > MaxPasses * frameBlocks)
        {
            return ImageErrors.TooLarge<int>(frame.Width, frame.Height);
        }

        return Entropy(frame, scan, dataStart);
    }

    private static Component? Find(Frame frame, int id)
    {
        foreach (var component in frame.Components)
        {
            if (component.Id == id)
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>The scan parameters each process allows (§B.2.3; G.1.1.1.1 for the progressive restrictions), or null.</summary>
    private static string? CheckScan(Frame frame, Scan scan, int blocksPerMcu)
    {
        if (scan.Components.Length > 1 && blocksPerMcu > 10)
        {
            return $"an interleaved MCU of {blocksPerMcu} blocks; at most 10 are allowed (§B.2.3).";
        }

        if (!frame.Progressive)
        {
            return scan.Start == 0 && scan.End == 63 && scan.High == 0 && scan.Low == 0
                ? null
                : "a sequential scan must have Ss = 0, Se = 63, Ah = Al = 0.";
        }

        if (scan.Start > scan.End || scan.End > 63 || scan.High > 13 || scan.Low > 13)
        {
            return $"progressive scan parameters Ss {scan.Start}, Se {scan.End}, Ah {scan.High}, Al {scan.Low} are out of range.";
        }

        if (scan.Start == 0 && scan.End != 0)
        {
            return "a progressive DC scan must have Se = 0.";
        }

        if (scan.Start > 0 && scan.Components.Length != 1)
        {
            return "a progressive AC scan must have one component.";
        }

        return scan.High != 0 && scan.High != scan.Low + 1
            ? "a refinement scan must have Ah = Al + 1."
            : null;
    }

    /// <summary>Blocks of a component a non-interleaved scan covers: ⌈width / 8⌉ × ⌈height / 8⌉ (§A.2.2).</summary>
    private static int BlocksAcross(Component component) => (component.Width + 7) / 8;

    private static int BlocksDown(Component component) => (component.Height + 7) / 8;

    /// <summary>
    /// Decodes the entropy-coded segments of one scan: MCUs in order, a restart marker after every restart interval
    /// (§B.2.5, F.2.2.5: the bits left in the byte are padding, predictions and the end-of-band run start again), and
    /// returns the position of the marker after the last MCU.
    /// </summary>
    private Result<int, Error> Entropy(Frame frame, Scan scan, int dataStart)
    {
        var single = scan.Components.Length == 1;
        var across = single ? BlocksAcross(scan.Components[0].Component) : frame.McusPerLine;
        var down = single ? BlocksDown(scan.Components[0].Component) : frame.McuRows;
        var total = (long)across * down;
        var reader = new BitReader(_file, dataStart);
        var endOfBandRun = 0;
        var nextRestart = 0;
        foreach (var (component, _, _) in scan.Components)
        {
            component.DcPrediction = 0;
        }

        for (var mcu = 0L; mcu < total; mcu++)
        {
            if (_restartInterval > 0 && mcu > 0 && mcu % _restartInterval == 0)
            {
                reader.AlignToByte();
                if (Restart(reader, nextRestart) is { } position)
                {
                    reader = new BitReader(_file, position);
                    nextRestart = (nextRestart + 1) % 8;
                    endOfBandRun = 0;
                    foreach (var (component, _, _) in scan.Components)
                    {
                        component.DcPrediction = 0;
                    }
                }
                else
                {
                    return ImageErrors.Invalid<int>($"restart marker RST{nextRestart} expected after {mcu} MCUs.", reader.Position);
                }
            }

            var row = (int)(mcu / across);
            var column = (int)(mcu % across);
            foreach (var (component, dc, ac) in scan.Components)
            {
                var (horizontal, vertical) = single ? (1, 1) : (component.Horizontal, component.Vertical);
                for (var v = 0; v < vertical; v++)
                {
                    for (var h = 0; h < horizontal; h++)
                    {
                        var blockRow = (row * vertical) + v;
                        var blockColumn = (column * horizontal) + h;
                        var block = component.Coefficients.AsSpan((int)((((long)blockRow * component.BlocksPerLine) + blockColumn) * 64), 64);
                        var ok = frame.Progressive
                            ? Progressive(reader, scan, component, _dcTables[dc], _acTables[ac], block, ref endOfBandRun)
                            : Sequential(reader, component, _dcTables[dc]!, _acTables[ac]!, block);
                        if (!ok)
                        {
                            return ImageErrors.Invalid<int>($"the entropy-coded data of component {component.Id} has a code that no table defines, or a run past the end of a block.", reader.Position);
                        }
                    }
                }
            }

            if (reader.MissingBits > 0)
            {
                return ImageErrors.Invalid<int>("the entropy-coded data ends before the scan's last block.", reader.Position);
            }
        }

        reader.AlignToByte();
        return Result<int, Error>.Success(reader.Position);
    }

    /// <summary>The position after restart marker RSTm at the reader's position, or null when another byte is there.</summary>
    private int? Restart(BitReader reader, int expected)
    {
        var span = _file.Span;
        var position = reader.Position;
        return position + 1 < span.Length && span[position] == 0xFF && span[position + 1] == 0xD0 + expected ? position + 2 : null;
    }

    /// <summary>
    /// One block of a sequential scan (F.2.2.1, F.2.2.2): the DC difference (category, then that many bits), added to the
    /// prediction; then the AC coefficients as run/size pairs, ZRL (0xF0) for 16 zeros, EOB (0x00) for the rest.
    /// </summary>
    private static bool Sequential(BitReader reader, Component component, HuffmanTable dcTable, HuffmanTable acTable, Span<short> block)
    {
        if (dcTable.Decode(reader) is not { } category || category > 11)
        {
            return false;
        }

        component.DcPrediction += reader.Extended(category);
        block[0] = (short)component.DcPrediction;
        for (var k = 1; k < 64;)
        {
            if (acTable.Decode(reader) is not { } symbol)
            {
                return false;
            }

            var run = symbol >> 4;
            var size = symbol & 15;
            if (size == 0)
            {
                if (run != 15)
                {
                    return true;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > 63 || size > 10)
            {
                return false;
            }

            block[k++] = (short)reader.Extended(size);
        }

        return true;
    }

    /// <summary>One block of a progressive scan (G.1.2): DC first or refinement, AC first or refinement.</summary>
    private static bool Progressive(BitReader reader, Scan scan, Component component, HuffmanTable? dcTable, HuffmanTable? acTable, Span<short> block, ref int endOfBandRun)
    {
        if (scan.Start == 0)
        {
            // G.1.2.1: the first DC scan codes the difference like a sequential one, shifted by Al; a refinement scan sends
            // one bit per block, the bit Al of the coefficient.
            if (scan.High == 0)
            {
                if (dcTable!.Decode(reader) is not { } category || category > 11)
                {
                    return false;
                }

                component.DcPrediction += reader.Extended(category);
                block[0] = (short)(component.DcPrediction * (1 << scan.Low));
            }
            else if (reader.Bit() == 1)
            {
                block[0] |= (short)(1 << scan.Low);
            }

            return true;
        }

        return scan.High == 0
            ? AcFirst(reader, scan, acTable!, block, ref endOfBandRun)
            : AcRefine(reader, scan, acTable!, block, ref endOfBandRun);
    }

    /// <summary>
    /// G.1.2.2: the first scan of a band of AC coefficients. Run/size pairs as in a sequential scan, values shifted by Al;
    /// a symbol with size 0 and run r below 15 starts an end-of-band run of 2^r plus r more bits of blocks (this one
    /// included) whose band is all zero.
    /// </summary>
    private static bool AcFirst(BitReader reader, Scan scan, HuffmanTable table, Span<short> block, ref int endOfBandRun)
    {
        if (endOfBandRun > 0)
        {
            endOfBandRun--;
            return true;
        }

        for (var k = scan.Start; k <= scan.End;)
        {
            if (table.Decode(reader) is not { } symbol)
            {
                return false;
            }

            var run = symbol >> 4;
            var size = symbol & 15;
            if (size == 0)
            {
                if (run < 15)
                {
                    endOfBandRun = (1 << run) - 1 + reader.Bits(run);
                    return true;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > scan.End || size > 10)
            {
                return false;
            }

            block[k++] = (short)(reader.Extended(size) * (1 << scan.Low));
        }

        return true;
    }

    /// <summary>
    /// G.1.2.3: a refinement scan of a band. A new coefficient (it was zero) has size 1 and comes with its sign bit; on the
    /// way to it, every coefficient that is already nonzero gets one correction bit (bit Al, added away from zero), and
    /// zero coefficients are counted against the run. The rest of the band after an end-of-band symbol still gets its
    /// correction bits, in this block and in every block of the end-of-band run.
    /// </summary>
    private static bool AcRefine(BitReader reader, Scan scan, HuffmanTable table, Span<short> block, ref int endOfBandRun)
    {
        var plus = 1 << scan.Low;
        var minus = -1 << scan.Low;
        var k = scan.Start;
        if (endOfBandRun == 0)
        {
            for (; k <= scan.End; k++)
            {
                if (table.Decode(reader) is not { } symbol)
                {
                    return false;
                }

                var run = symbol >> 4;
                var size = symbol & 15;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                    {
                        return false;
                    }

                    value = reader.Bit() == 1 ? plus : minus;
                }
                else if (run != 15)
                {
                    endOfBandRun = (1 << run) + reader.Bits(run);
                    break;
                }

                // Skip `run` zero coefficients (correcting the nonzero ones on the way) and stop on the zero one after them.
                for (; k <= scan.End; k++)
                {
                    if (block[k] != 0)
                    {
                        Correct(reader, ref block[k], plus, minus);
                    }
                    else if (--run < 0)
                    {
                        break;
                    }
                }

                if (value != 0)
                {
                    if (k > scan.End)
                    {
                        return false;
                    }

                    block[k] = (short)value;
                }
            }
        }

        if (endOfBandRun > 0)
        {
            for (; k <= scan.End; k++)
            {
                if (block[k] != 0)
                {
                    Correct(reader, ref block[k], plus, minus);
                }
            }

            endOfBandRun--;
        }

        return true;
    }

    /// <summary>A correction bit of a nonzero coefficient: when 1 (and bit Al is not yet set) it moves away from zero.</summary>
    private static void Correct(BitReader reader, ref short coefficient, int plus, int minus)
    {
        if (reader.Bit() == 1 && (coefficient & plus) == 0)
        {
            coefficient += (short)(coefficient >= 0 ? plus : minus);
        }
    }

    /// <summary>Dequantises and transforms every block, then up-samples and converts the colours.</summary>
    private Result<RasterImage, Error> Samples(JpegImage image)
    {
        var frame = _frame!;
        var planes = new List<(byte[] Samples, int Stride, int Width, int Height, int Horizontal, int Vertical)>();
        foreach (var component in frame.Components)
        {
            planes.Add((Plane(component), component.BlocksPerLine * 8, component.Width, component.Height, component.Horizontal, component.Vertical));
            component.Coefficients = [];
        }

        var rgb = frame.Components.Length == 3;
        var transform = rgb && (_adobeTransform ?? 1) != 0;
        var samples = JpegColor.Convert(frame.Width, frame.Height, frame.MaxHorizontal, frame.MaxVertical, planes, transform);
        return Result<RasterImage, Error>.Success(new RasterImage(image.Id, frame.Width, frame.Height, rgb ? ImageColorSpace.Rgb : ImageColorSpace.Gray, samples, null));
    }

    /// <summary>A component's samples: every block dequantised (F.2.1.4, coefficient × quantisation value) and inverse
    /// transformed, on the padded block grid, row stride <c>BlocksPerLine × 8</c>.</summary>
    private byte[] Plane(Component component)
    {
        var table = _quantisation[component.Table]!;
        var stride = component.BlocksPerLine * 8;
        var samples = new byte[(long)stride * component.BlockRows * 8];
        Span<double> dequantised = stackalloc double[64];
        for (var blockRow = 0; blockRow < component.BlockRows; blockRow++)
        {
            for (var blockColumn = 0; blockColumn < component.BlocksPerLine; blockColumn++)
            {
                var block = component.Coefficients.AsSpan((int)((((long)blockRow * component.BlocksPerLine) + blockColumn) * 64), 64);
                for (var k = 0; k < 64; k++)
                {
                    dequantised[InverseDct.Natural[k]] = block[k] * (double)table[k];
                }

                InverseDct.Transform(dequantised, samples, (blockRow * 8 * stride) + (blockColumn * 8), stride);
            }
        }

        return samples;
    }

    /// <summary>One component as decoded, for the tests that compare with the T.83 conformance data: its size in samples,
    /// the padded block grid, the quantised coefficients (zig-zag order per block), its quantisation table (zig-zag) and
    /// its samples after the inverse DCT (row stride <c>BlocksPerLine × 8</c>), before any up-sampling.</summary>
    internal sealed record DecodedComponent(int Id, int Width, int Height, int BlocksPerLine, int BlockRows, short[] Coefficients, ushort[] Quantisation, byte[] Samples);

    /// <summary>Decodes the scans of any frame of these processes, whatever its component count (the header reader would
    /// refuse the four components of the conformance streams), and returns every component without converting colours.</summary>
    internal static Result<ImmutableArray<DecodedComponent>, Error> Components(ReadOnlyMemory<byte> file)
    {
        var decoder = new JpegDecoder(file);
        return decoder.Segments().OnSuccessMap(_ => ImmutableArray.CreateRange(
            decoder._frame!.Components,
            component => new DecodedComponent(component.Id, component.Width, component.Height, component.BlocksPerLine, component.BlockRows, component.Coefficients, decoder._quantisation[component.Table]!, decoder.Plane(component))));
    }
}
