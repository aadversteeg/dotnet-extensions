using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images.Png;

/// <summary>
/// Turns unfiltered scanlines into 8-bit colour samples and an optional alpha channel (PNG §7.2 "Scanlines": pixels
/// are packed left to right: "These samples are packed into bytes with the leftmost sample in the high-order bits
/// of a byte followed by the other samples for the scanline").
/// </summary>
internal static class PngSamples
{
    /// <summary>
    /// Expands every pixel. Greyscale and truecolour are copied; indexed colour is looked up in the palette, so the
    /// result is RGB; an alpha channel is split off. Transparency from tRNS becomes alpha as well: per palette entry,
    /// or 0 for the pixels that equal the colour key (§11.3.2.1).
    /// </summary>
    public static Result<ImageData, Error> Expand(ImageId id, PngHeader header, PngPalette palette, byte[] scanlines)
    {
        var (width, height) = (header.Width, header.Height);
        var gray = header.ColorType is PngColorType.Greyscale or PngColorType.GreyscaleWithAlpha;
        var channels = gray ? 1 : 3;
        var samples = new byte[(long)width * height * channels];
        var hasAlpha = header.ColorType is PngColorType.GreyscaleWithAlpha or PngColorType.TruecolourWithAlpha
            || !palette.Alphas.IsEmpty || palette.ColorKey is not null;
        var alpha = hasAlpha ? new byte[(long)width * height] : null;
        var stride = (int)header.Stride;

        for (var y = 0; y < height; y++)
        {
            var line = scanlines.AsSpan(y * (stride + 1) + 1, stride);
            for (var x = 0; x < width; x++)
            {
                var pixel = y * width + x;
                var opacity = header.ColorType switch
                {
                    PngColorType.IndexedColour => IndexedPixel(line, x, header.BitDepth, palette, samples, pixel),
                    PngColorType.GreyscaleWithAlpha => Copy(line.Slice(x * 2, 1), samples, pixel, line[x * 2 + 1]),
                    PngColorType.TruecolourWithAlpha => Copy(line.Slice(x * 4, 3), samples, pixel, line[x * 4 + 3]),
                    _ => Copy(line.Slice(x * channels, channels), samples, pixel, KeyAlpha(line.Slice(x * channels, channels), palette.ColorKey)),
                };
                if (opacity < 0)
                {
                    return ImageErrors.Invalid<ImageData>($"pixel ({x}, {y}) uses a palette index past the {palette.Count} palette entries.", 0);
                }

                if (alpha is not null)
                {
                    alpha[pixel] = (byte)opacity;
                }
            }
        }

        // The arrays are handed over, not copied: nothing else holds them, and a copy would double the memory
        // ImageLimits promises (at the limit 192 MiB of samples and 64 MiB of alpha).
        return Result<ImageData, Error>.Success(new RasterImage(
            id,
            width,
            height,
            gray ? ImageColorSpace.Gray : ImageColorSpace.Rgb,
            ImmutableCollectionsMarshal.AsImmutableArray(samples),
            alpha is null ? null : ImmutableCollectionsMarshal.AsImmutableArray(alpha)));
    }

    /// <summary>Copies one pixel's colour samples; returns its opacity.</summary>
    private static int Copy(ReadOnlySpan<byte> source, byte[] samples, int pixel, int opacity)
    {
        source.CopyTo(samples.AsSpan(pixel * source.Length, source.Length));
        return opacity;
    }

    /// <summary>
    /// One indexed pixel: its index is <paramref name="bitDepth"/> bits wide, the leftmost pixel in the high-order
    /// bits; the palette gives its RGB and alpha. Returns −1 for an index past the palette (§11.2.3: "any out-of-range
    /// pixel value found in the image data is an error").
    /// </summary>
    private static int IndexedPixel(ReadOnlySpan<byte> line, int x, int bitDepth, PngPalette palette, byte[] samples, int pixel)
    {
        var bitOffset = x * bitDepth;
        var shift = 8 - bitDepth - bitOffset % 8;
        var index = (line[bitOffset / 8] >> shift) & ((1 << bitDepth) - 1);
        if (index >= palette.Count)
        {
            return -1;
        }

        palette.Colors.AsSpan(index * 3, 3).CopyTo(samples.AsSpan(pixel * 3, 3));
        return palette.Alphas.IsEmpty ? 255 : palette.Alphas[index];
    }

    /// <summary>0 when the pixel equals the tRNS colour key, else 255.</summary>
    private static int KeyAlpha(ReadOnlySpan<byte> pixel, ImmutableArray<int>? key)
    {
        if (key is not { } values)
        {
            return 255;
        }

        for (var channel = 0; channel < pixel.Length; channel++)
        {
            if (pixel[channel] != values[channel])
            {
                return 255;
            }
        }

        return 0;
    }
}
