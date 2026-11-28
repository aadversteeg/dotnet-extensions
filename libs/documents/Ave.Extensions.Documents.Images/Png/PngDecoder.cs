using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images.Png;

/// <summary>
/// A PNG decoder for the variants a document needs (PNG Specification, Second Edition = ISO/IEC 15948:2004):
/// non-interlaced greyscale, truecolour, greyscale-with-alpha and truecolour-with-alpha at 8 bits per sample, and
/// indexed colour at 1, 2, 4 or 8 bits. The result is 8-bit grey or RGB samples plus an optional alpha channel —
/// exactly what a PDF image XObject and its soft mask take (ISO 32000-1 §8.9.5, §11.6.5.3).
/// </summary>
/// <remarks>
/// Steps: signature → chunks (with CRC check) → IHDR → PLTE and tRNS → the IDAT data concatenated and inflated with
/// the base library's zlib (§10) → filters undone (§9) → samples expanded to 8 bits per channel (palette looked up,
/// transparency turned into alpha). Every other variant is reported as <c>Documents.Image.Unsupported</c> with its
/// name, never decoded half-way.
/// </remarks>
internal static class PngDecoder
{
    /// <summary>The PNG signature, §5.2: 137 80 78 71 13 10 26 10.</summary>
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>The first buffer the data is inflated into: 4 KiB, or the announced size when that is less.</summary>
    internal const int FirstBuffer = 4096;

    public static bool HasSignature(ReadOnlySpan<byte> file) => file.StartsWith(Signature);

    public static Result<ImageData, Error> Decode(ReadOnlyMemory<byte> file, ImagePixelBudget? budget) =>
        PngChunk.ReadAll(file, Signature.Length).OnSuccessBind(chunks => Decode(file, chunks, budget));

    private static Result<ImageData, Error> Decode(ReadOnlyMemory<byte> file, ImmutableArray<PngChunk> chunks, ImagePixelBudget? budget)
    {
        if (OrderProblem(chunks) is { } problem)
        {
            return ImageErrors.Invalid<ImageData>(problem.Message, problem.Offset);
        }

        var unknownCritical = chunks.FirstOrDefault(chunk => chunk.IsCritical && chunk.Type is not ("IHDR" or "PLTE" or "IDAT" or "IEND"));
        if (unknownCritical is not null)
        {
            return ImageErrors.Unsupported<ImageData>($"PNG with the unknown critical chunk {unknownCritical.Type}");
        }

        // The document's budget is charged from the header, before the data is inflated or a sample is made, and given
        // back when the data does not decode; what such a failed decode produced stays counted (ImagePixelBudget).
        return PngHeader.Parse(chunks[0]).OnSuccessBind(header =>
            PngPalette.Read(header, chunks).OnSuccessBind(palette =>
                budget is null ? Samples(file, header, palette, chunks, out _)
                : budget.Charge(header.Width, header.Height).OnSuccessBind(_ =>
                {
                    var samples = Samples(file, header, palette, chunks, out var produced);
                    if (samples.IsFailure)
                    {
                        budget.Failed(header.Width, header.Height, produced);
                    }

                    return samples;
                })));
    }

    /// <summary>The samples of the image, with the pixels its data produced: those of the rows the inflated data filled
    /// or began, at least one row; all of them once the data inflated in full.</summary>
    private static Result<ImageData, Error> Samples(ReadOnlyMemory<byte> file, PngHeader header, PngPalette palette, ImmutableArray<PngChunk> chunks, out long produced)
    {
        var scanlines = Inflate(header, chunks, out var inflated);
        var rows = Math.Clamp((inflated + header.Stride) / (header.Stride + 1), 1, header.Height);
        produced = rows * header.Width;
        return scanlines.OnSuccessBind(lines => PngSamples.Expand(ImageId.Of(file.Span), header, palette, lines));
    }

    /// <summary>
    /// The chunk ordering rules of §5.6 (Table 5.3) for the critical chunks, or null when they hold: IHDR first and only
    /// once; PLTE at most once and before the first IDAT; the IDAT chunks consecutive (Table 5.3: "Multiple IDAT chunks
    /// shall be consecutive"; §11.2.4). IEND ends the list
    /// (<see cref="PngChunk.ReadAll"/> stops there).
    /// </summary>
    private static (string Message, long Offset)? OrderProblem(ImmutableArray<PngChunk> chunks)
    {
        if (chunks[0].Type != "IHDR")
        {
            return ("the first chunk must be IHDR.", chunks[0].Offset);
        }

        var firstData = chunks.IndexOf(chunks.FirstOrDefault(chunk => chunk.Type == "IDAT")!);
        var lastData = chunks.LastIndexOf(chunks.LastOrDefault(chunk => chunk.Type == "IDAT")!);
        return chunks.Skip(1).FirstOrDefault(chunk => chunk.Type == "IHDR") is { } second ? ("there is a second IHDR chunk.", second.Offset)
            : chunks.Where(chunk => chunk.Type == "PLTE").Skip(1).FirstOrDefault() is { } palette ? ("there is a second PLTE chunk.", palette.Offset)
            : firstData >= 0 && chunks.Skip(firstData).FirstOrDefault(chunk => chunk.Type == "PLTE") is { } late ? ("the PLTE chunk comes after image data.", late.Offset)
            : firstData >= 0 && chunks.Skip(firstData).Take(lastData - firstData + 1).FirstOrDefault(chunk => chunk.Type != "IDAT") is { } between
                ? ($"a {between.Type} chunk sits between the IDAT chunks, which must be consecutive.", between.Offset)
            : null;
    }

    /// <summary>
    /// Concatenates the IDAT chunks (§11.2.4: "the compressed datastream is then the concatenation of the contents of
    /// the data fields of all the IDAT chunks"), inflates them (zlib, §10) and undoes the filters. The data must be
    /// exactly height × (1 + stride) bytes: less means a damaged file, more is not allowed either. With the bytes it
    /// inflated, also when it fails.
    /// </summary>
    private static Result<byte[], Error> Inflate(PngHeader header, ImmutableArray<PngChunk> chunks, out long inflated)
    {
        inflated = 0;
        var idat = chunks.Where(chunk => chunk.Type == "IDAT").ToImmutableArray();
        if (idat.IsEmpty)
        {
            return ImageErrors.Invalid<byte[]>("there is no IDAT chunk.", chunks[^1].Offset);
        }

        // The header holds at most ImageLimits.MaxPixels pixels of at most 4 bytes, so this is at most 256 MiB plus a
        // filter byte per row (PngHeader.Parse).
        var expected = header.Height * (header.Stride + 1);
        var compressed = idat.SelectMany(chunk => chunk.Data.ToArray()).ToArray();

        // The buffer grows with the data, four times at a time up to what the header announces: a header that announces
        // more than its data holds costs what the data holds, not what the header announces.
        var scanlines = new byte[Math.Min(expected, FirstBuffer)];
        inflated = 0;
        bool trailing;
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            while (true)
            {
                inflated += zlib.ReadAtLeast(scanlines.AsSpan((int)inflated), scanlines.Length - (int)inflated, throwOnEndOfStream: false);
                if (inflated < scanlines.Length || scanlines.Length == expected)
                {
                    break;
                }

                Array.Resize(ref scanlines, (int)Math.Min(expected, 4L * scanlines.Length));
            }

            trailing = inflated == expected && zlib.ReadByte() >= 0;
        }
        catch (InvalidDataException exception)
        {
            return ImageErrors.Invalid<byte[]>($"the IDAT data is not valid zlib data ({exception.Message})", idat[0].Offset);
        }

        if (inflated != expected || trailing)
        {
            return ImageErrors.Invalid<byte[]>($"the image data does not decompress to the {expected} bytes the header announces.", idat[0].Offset);
        }

        var badRow = ScanlineFilters.Unfilter(scanlines, (int)header.Stride, Math.Max(1, header.BitsPerPixel / 8));
        return badRow < 0
            ? Result<byte[], Error>.Success(scanlines)
            : ImageErrors.Invalid<byte[]>($"scanline {badRow} has an undefined filter type.", idat[0].Offset);
    }
}
