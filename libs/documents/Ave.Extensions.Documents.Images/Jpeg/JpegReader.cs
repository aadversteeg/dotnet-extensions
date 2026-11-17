using System;
using System.Buffers.Binary;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Images.Jpeg;

/// <summary>
/// Reads the header of a JPEG file (ITU-T T.81 | ISO/IEC 10918-1) far enough to embed the file unchanged: size and
/// number of colour components, from the frame header. The image data itself is decoded by the PDF viewer
/// (DCTDecode, ISO 32000-1 §7.4.8).
/// </summary>
/// <remarks>
/// <para>File structure (T.81 Annex B): the SOI marker FF D8, then marker segments — FF, a marker code, a 2-byte
/// big-endian length that counts itself but not the marker — until the frame header (SOFn). Any number of FF fill
/// bytes may precede a marker (§B.1.1.2).</para>
/// <para>Supported, because DCTDecode reads them: SOF0 baseline, SOF1 extended sequential and SOF2 progressive
/// Huffman coding, 8-bit precision, 1 component (greyscale) or 3 (YCbCr or RGB; DCTDecode's default ColorTransform
/// turns YCbCr into RGB). Named as unsupported: lossless and hierarchical processes, arithmetic coding, 12-bit
/// precision, 4 components (CMYK/YCCK, whose Adobe inversion convention needs its own handling), and a height left
/// to a DNL marker.</para>
/// </remarks>
internal static class JpegReader
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte EndOfImage = 0xD9;
    private const byte StartOfScan = 0xDA;

    /// <summary>The shortest scan header: Ls = 6 + 2 × Ns with one component (T.81 Table B.3).</summary>
    private const int MinimumScanHeaderLength = 8;

    public static bool HasSignature(ReadOnlySpan<byte> file) =>
        file.Length >= 3 && file[0] == MarkerPrefix && file[1] == StartOfImage && file[2] == MarkerPrefix;

    public static Result<ImageData, Error> Read(ReadOnlyMemory<byte> file)
    {
        var span = file.Span;
        var offset = 2;
        while (true)
        {
            // Skip fill bytes: "Any marker may optionally be preceded by any number of fill bytes" (§B.1.1.2).
            if (offset >= span.Length || span[offset] != MarkerPrefix)
            {
                return ImageErrors.Invalid<ImageData>("expected a marker before the frame header.", offset);
            }

            while (offset < span.Length && span[offset] == MarkerPrefix)
            {
                offset++;
            }

            if (offset + 2 >= span.Length)
            {
                return ImageErrors.Invalid<ImageData>("the file ends before the frame header.", offset);
            }

            var marker = span[offset];
            var length = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 1)..]);
            if (length < 2 || offset + 1 + length > span.Length)
            {
                return ImageErrors.Invalid<ImageData>($"marker segment FF{marker:X2} has an impossible length {length}.", offset);
            }

            if (IsStartOfFrame(marker))
            {
                var afterFrame = offset + 1 + length;
                return HasScan(span, afterFrame)
                    ? Frame(file, marker, span.Slice(offset + 3, length - 2), offset)
                    : ImageErrors.Invalid<ImageData>("the frame header is not followed by a scan (no SOS marker), so there is no image data.", afterFrame);
            }

            if (marker is StartOfScan or EndOfImage)
            {
                return ImageErrors.Invalid<ImageData>("the scan data starts before any frame header.", offset);
            }

            offset += 1 + length;
        }
    }

    /// <summary>
    /// True when a scan follows the frame header, after any table and miscellaneous segments: a frame "shall begin
    /// with a frame header and shall contain one or more scans" (T.81 §B.2.1, Figure B.2), and without a scan there is
    /// no image data to embed. The end-of-image marker (EOI, FF D9) ends the search: what follows it is not part of
    /// the image.
    /// </summary>
    private static bool HasScan(ReadOnlySpan<byte> span, int offset)
    {
        while (offset < span.Length && span[offset] == MarkerPrefix)
        {
            while (offset < span.Length && span[offset] == MarkerPrefix)
            {
                offset++;
            }

            if (offset + 2 >= span.Length || span[offset] == EndOfImage)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 1)..]);
            if (span[offset] == StartOfScan)
            {
                return IsCompleteScan(span, offset + 1, length);
            }

            if (length < 2)
            {
                return false;
            }

            offset += 1 + length;
        }

        return false;
    }

    /// <summary>
    /// True when the scan header at <paramref name="offset"/> (its length field) is complete and image data follows
    /// it. The header is Ls, Ns, then Ns component selectors of two bytes each, then Ss, Se and Ah/Al, so Ls is 6 + 2 ×
    /// Ns, with Ns from 1 to 4 (T.81 §B.2.3, Table B.3). Our rule, beyond the syntax: the first entropy-coded segment
    /// after the header has at least one byte, because a scan without coded data has no image to show.
    /// </summary>
    private static bool IsCompleteScan(ReadOnlySpan<byte> span, int offset, int length)
    {
        if (length < MinimumScanHeaderLength || offset + length > span.Length)
        {
            return false;
        }

        var components = span[offset + 2];
        var afterHeader = offset + length;
        return components is >= 1 and <= 4
            && length == 6 + (2 * components)
            && HasCodedByte(span, afterHeader);
    }

    /// <summary>
    /// True when an entropy-coded byte starts at <paramref name="offset"/>: any byte but FF, or FF followed by the
    /// stuffed 00 byte that marks a coded FF (byte stuffing, T.81 §F.1.2.3); any other byte after FF makes it a
    /// marker.
    /// </summary>
    private static bool HasCodedByte(ReadOnlySpan<byte> span, int offset) =>
        offset < span.Length && (span[offset] != MarkerPrefix || (offset + 1 < span.Length && span[offset + 1] == 0x00));

    /// <summary>The SOFn markers, Table B.1: C0–CF except C4 (DHT), C8 (reserved, JPG) and CC (DAC).</summary>
    private static bool IsStartOfFrame(byte marker) => marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);

    /// <summary>
    /// The frame header (§B.2.2, Figure B.3): sample precision P (1 byte), number of lines Y (2), samples per line X
    /// (2), number of components Nf (1), then 3 bytes per component.
    /// </summary>
    private static Result<ImageData, Error> Frame(ReadOnlyMemory<byte> file, byte marker, ReadOnlySpan<byte> header, int offset)
    {
        if (marker is not (0xC0 or 0xC1 or 0xC2))
        {
            return ImageErrors.Unsupported<ImageData>($"JPEG {ProcessName(marker)} (SOF{marker - 0xC0})");
        }

        if (header.Length < 6)
        {
            return ImageErrors.Invalid<ImageData>("the frame header is too short.", offset);
        }

        int precision = header[0];
        int height = BinaryPrimitives.ReadUInt16BigEndian(header[1..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(header[3..]);
        int components = header[5];
        if (header.Length < 6 + 3 * components)
        {
            return ImageErrors.Invalid<ImageData>("the frame header is shorter than its component list.", offset);
        }

        return (precision, height, width, components) switch
        {
            (not 8, _, _, _) => ImageErrors.Unsupported<ImageData>($"JPEG with {precision}-bit precision"),
            (_, 0, _, _) => ImageErrors.Unsupported<ImageData>("JPEG whose height is given by a DNL marker"),
            (_, _, 0, _) => ImageErrors.Invalid<ImageData>("the frame header gives a width of 0.", offset),
            (_, _, _, 1) => Image(file, width, height, ImageColorSpace.Gray),
            (_, _, _, 3) => Image(file, width, height, ImageColorSpace.Rgb),
            _ => ImageErrors.Unsupported<ImageData>($"JPEG with {components} colour components"),
        };
    }

    private static Result<ImageData, Error> Image(ReadOnlyMemory<byte> file, int width, int height, ImageColorSpace colorSpace) =>
        Result<ImageData, Error>.Success(new JpegImage(ImageId.Of(file.Span), width, height, colorSpace, [.. file.Span]));

    /// <summary>The coding process of each SOF marker, Table B.1.</summary>
    private static string ProcessName(byte marker) => marker switch
    {
        0xC3 => "lossless",
        0xC5 or 0xC6 or 0xC7 => "hierarchical (differential)",
        _ => "arithmetic coding",
    };
}
