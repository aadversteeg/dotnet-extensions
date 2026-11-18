using System;
using System.Collections.Immutable;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// An image of 8-bit RGBA pixels, row by row from the top, four bytes per pixel: red, green, blue in sRGB (the encoded
/// values, IEC 61966-2-1), then alpha, not premultiplied (0 transparent, 255 opaque). What a <see cref="Canvas"/> makes
/// and what it draws as an image.
/// </summary>
public sealed class RgbaImage
{
    /// <summary>Wraps pixels.</summary>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="pixels">width × height × 4 bytes.</param>
    /// <exception cref="ArgumentException">The size is not positive or does not match the pixels.</exception>
    public RgbaImage(int width, int height, ImmutableArray<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (pixels.IsDefault || pixels.Length != (long)width * height * 4)
        {
            throw new ArgumentException($"A {width} x {height} image has {(long)width * height * 4} bytes of pixels.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels: width × height × 4 bytes, R, G, B, A.</summary>
    public ImmutableArray<byte> Pixels { get; }
}
