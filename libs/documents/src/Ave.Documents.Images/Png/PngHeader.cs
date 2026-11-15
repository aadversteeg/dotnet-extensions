using System.Buffers.Binary;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Images.Png;

/// <summary>The PNG colour types (PNG §11.2.2, Table 11.1).</summary>
internal enum PngColorType
{
    /// <summary>Greyscale.</summary>
    Greyscale = 0,

    /// <summary>Truecolour: red, green, blue.</summary>
    Truecolour = 2,

    /// <summary>Indexed-colour: each pixel is a palette index.</summary>
    IndexedColour = 3,

    /// <summary>Greyscale with alpha.</summary>
    GreyscaleWithAlpha = 4,

    /// <summary>Truecolour with alpha.</summary>
    TruecolourWithAlpha = 6,
}

/// <summary>
/// The image header, chunk IHDR (PNG §11.2.2): size, bit depth, colour type, and the compression, filter and
/// interlace methods.
/// </summary>
/// <param name="Width">Width in pixels, 1 to 2^31 − 1.</param>
/// <param name="Height">Height in pixels, 1 to 2^31 − 1.</param>
/// <param name="BitDepth">Bits per sample (or per palette index).</param>
/// <param name="ColorType">The colour type.</param>
internal sealed record PngHeader(int Width, int Height, int BitDepth, PngColorType ColorType)
{
    /// <summary>Samples per pixel for each colour type (Table 11.1).</summary>
    public int SamplesPerPixel => ColorType switch
    {
        PngColorType.Greyscale or PngColorType.IndexedColour => 1,
        PngColorType.GreyscaleWithAlpha => 2,
        PngColorType.Truecolour => 3,
        _ => 4,
    };

    /// <summary>Bits per pixel.</summary>
    public int BitsPerPixel => SamplesPerPixel * BitDepth;

    /// <summary>Bytes per scanline, without the filter-type byte: ceil(width × bits per pixel / 8) (§7.2).</summary>
    public long Stride => ((long)Width * BitsPerPixel + 7) / 8;

    /// <summary>
    /// Parses IHDR and keeps only what this decoder supports, naming every other variant:
    /// <list type="bullet">
    /// <item>bit depth 8 for greyscale, truecolour and the alpha types; 1, 2, 4 or 8 for indexed colour;</item>
    /// <item>compression method 0 (zlib deflate, §10) and filter method 0 (§9) — the only ones defined;</item>
    /// <item>interlace method 0: Adam7 interlacing (§8.2) is not decoded;</item>
    /// <item>at most <see cref="ImageLimits.MaxPixels"/> pixels, checked in 64-bit arithmetic before anything is
    /// allocated, so every size computed from the header later fits.</item>
    /// </list>
    /// </summary>
    public static Result<PngHeader, Error> Parse(PngChunk chunk)
    {
        var data = chunk.Data.Span;
        if (data.Length != 13)
        {
            return ImageErrors.Invalid<PngHeader>("the IHDR chunk must be 13 bytes long.", chunk.Offset);
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(data);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        int bitDepth = data[8];
        int colorType = data[9];
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            return ImageErrors.Invalid<PngHeader>($"the image size {width}x{height} is outside 1 to 2^31-1.", chunk.Offset);
        }

        if (colorType is not (0 or 2 or 3 or 4 or 6))
        {
            return ImageErrors.Invalid<PngHeader>($"colour type {colorType} is not defined.", chunk.Offset);
        }

        if (data[10] != 0 || data[11] != 0)
        {
            return ImageErrors.Invalid<PngHeader>("compression method and filter method must be 0.", chunk.Offset);
        }

        if ((long)width * height > ImageLimits.MaxPixels)
        {
            return ImageErrors.TooLarge<PngHeader>(width, height);
        }

        var header = new PngHeader((int)width, (int)height, bitDepth, (PngColorType)colorType);
        return data[12] switch
        {
            0 => header.CheckBitDepth(),
            1 => ImageErrors.Unsupported<PngHeader>("PNG interlaced (Adam7)"),
            _ => ImageErrors.Invalid<PngHeader>($"interlace method {data[12]} is not defined.", chunk.Offset),
        };
    }

    /// <summary>The bit depths Table 11.1 allows for the colour type, and the ones this decoder supports.</summary>
    private Result<PngHeader, Error> CheckBitDepth()
    {
        var allowed = ColorType switch
        {
            PngColorType.Greyscale => BitDepth is 1 or 2 or 4 or 8 or 16,
            PngColorType.IndexedColour => BitDepth is 1 or 2 or 4 or 8,
            _ => BitDepth is 8 or 16,
        };
        var supported = ColorType == PngColorType.IndexedColour || BitDepth == 8;
        return !allowed ? ImageErrors.Invalid<PngHeader>($"bit depth {BitDepth} is not allowed for colour type {(int)ColorType}.", 8)
            : !supported ? ImageErrors.Unsupported<PngHeader>($"PNG {Describe(ColorType)} with bit depth {BitDepth}")
            : Result<PngHeader, Error>.Success(this);
    }

    private static string Describe(PngColorType type) => type switch
    {
        PngColorType.Greyscale => "greyscale",
        PngColorType.Truecolour => "truecolour",
        PngColorType.IndexedColour => "indexed-colour",
        PngColorType.GreyscaleWithAlpha => "greyscale with alpha",
        _ => "truecolour with alpha",
    };
}
