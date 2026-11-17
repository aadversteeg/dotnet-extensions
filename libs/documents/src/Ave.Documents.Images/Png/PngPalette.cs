using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Images.Png;

/// <summary>
/// The colour information outside the pixel data: the palette (PLTE, PNG Second Edition §11.2.3) and simple
/// transparency (tRNS, §11.3.2.1).
/// </summary>
/// <param name="Colors">Palette entries as red, green, blue triples; empty when there is no PLTE chunk.</param>
/// <param name="Alphas">The alpha of each palette entry (tRNS for indexed colour); entries tRNS leaves out are 255
/// (opaque). Empty when the image is not indexed or has no tRNS.</param>
/// <param name="ColorKey">For greyscale and truecolour: the one sample value (grey, or red/green/blue) that is fully
/// transparent; null when there is no tRNS.</param>
internal sealed record PngPalette(ImmutableArray<byte> Colors, ImmutableArray<byte> Alphas, ImmutableArray<int>? ColorKey)
{
    /// <summary>The number of palette entries.</summary>
    public int Count => Colors.Length / 3;

    /// <summary>
    /// Reads PLTE and tRNS and checks them against the colour type:
    /// <list type="bullet">
    /// <item>PLTE "shall appear for colour type 3, and may appear for colour types 2 and 6; it shall not appear for
    /// colour types 0 and 4"; its length is a multiple of 3, with 1 to 2^bit depth entries for indexed colour.
    /// A suggested palette in a truecolour image is not used.</item>
    /// <item>tRNS holds a grey value (type 0, 2 bytes), an RGB value (type 2, 6 bytes) or one alpha byte per palette
    /// entry, at most as many as there are entries (type 3); it is not allowed for the types that have an alpha
    /// channel.</item>
    /// </list>
    /// </summary>
    public static Result<PngPalette, Error> Read(PngHeader header, ImmutableArray<PngChunk> chunks)
    {
        var plte = chunks.FirstOrDefault(chunk => chunk.Type == "PLTE");
        var trns = chunks.FirstOrDefault(chunk => chunk.Type == "tRNS");
        var indexed = header.ColorType == PngColorType.IndexedColour;
        if (plte is null)
        {
            return indexed
                ? ImageErrors.Invalid<PngPalette>("an indexed-colour image needs a PLTE chunk.", chunks[0].Offset)
                : ReadTransparency(header, trns, ImmutableArray<byte>.Empty);
        }

        var length = plte.Data.Length;
        if (header.ColorType is PngColorType.Greyscale or PngColorType.GreyscaleWithAlpha)
        {
            return ImageErrors.Invalid<PngPalette>("a greyscale image may not have a PLTE chunk.", plte.Offset);
        }

        if (length == 0 || length % 3 != 0 || length / 3 > 256 || (indexed && length / 3 > 1 << header.BitDepth))
        {
            return ImageErrors.Invalid<PngPalette>($"a PLTE chunk of {length} bytes does not hold 1 to 2^bit depth RGB entries.", plte.Offset);
        }

        return ReadTransparency(header, trns, indexed ? [.. plte.Data.Span] : ImmutableArray<byte>.Empty);
    }

    private static Result<PngPalette, Error> ReadTransparency(PngHeader header, PngChunk? trns, ImmutableArray<byte> colors)
    {
        if (trns is null)
        {
            return Result<PngPalette, Error>.Success(new PngPalette(colors, ImmutableArray<byte>.Empty, null));
        }

        var data = trns.Data.Span;
        return header.ColorType switch
        {
            PngColorType.IndexedColour when data.Length <= colors.Length / 3 =>
                Result<PngPalette, Error>.Success(new PngPalette(
                    colors, [.. data.ToArray(), .. Enumerable.Repeat((byte)255, colors.Length / 3 - data.Length)], null)),
            PngColorType.Greyscale when data.Length == 2 => Key(colors, BinaryPrimitives.ReadUInt16BigEndian(data)),
            PngColorType.Truecolour when data.Length == 6 => Key(
                colors,
                BinaryPrimitives.ReadUInt16BigEndian(data),
                BinaryPrimitives.ReadUInt16BigEndian(data[2..]),
                BinaryPrimitives.ReadUInt16BigEndian(data[4..])),
            _ => ImageErrors.Invalid<PngPalette>("the tRNS chunk does not match the colour type.", trns.Offset),
        };
    }

    private static Result<PngPalette, Error> Key(ImmutableArray<byte> colors, params int[] key) =>
        Result<PngPalette, Error>.Success(new PngPalette(colors, ImmutableArray<byte>.Empty, [.. key]));
}
