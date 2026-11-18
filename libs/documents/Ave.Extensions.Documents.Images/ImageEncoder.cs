using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Images.Png;

namespace Ave.Extensions.Documents.Images;

/// <summary>The other direction of the image layer: pixels in, an image file out.</summary>
public static class ImageEncoder
{
    /// <summary>
    /// Writes 8-bit RGBA pixels (sRGB, alpha not premultiplied, row by row from the top) as a PNG file. Deterministic:
    /// the same pixels give the same bytes — a fixed chunk set, a fixed rule for the filter of each scanline and a fixed
    /// compression level (see the remarks of the encoder: RGB when every pixel is opaque, RGBA otherwise; an sRGB chunk;
    /// no time or text chunks).
    /// </summary>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="rgba">width × height × 4 bytes.</param>
    /// <returns>The PNG file.</returns>
    /// <exception cref="ArgumentException">A side is below 1, the image has more than
    /// <see cref="ImageLimits.MaxPixels"/> pixels, or the pixels do not match the size.</exception>
    public static ImmutableArray<byte> EncodePng(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)width * height, ImageLimits.MaxPixels, "width × height");
        if (rgba.Length != (long)width * height * 4)
        {
            throw new ArgumentException($"A {width} x {height} image has {(long)width * height * 4} bytes of RGBA pixels, not {rgba.Length}.", nameof(rgba));
        }

        return PngEncoder.Encode(width, height, rgba);
    }
}
