using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// An image of 8-bit RGBA pixels, row by row from the top, four bytes per pixel: red, green, blue in sRGB (the encoded
/// values, IEC 61966-2-1), then alpha, not premultiplied (0 transparent, 255 opaque). What a <see cref="Canvas"/> makes
/// and what it draws as an image.
/// </summary>
public sealed class RgbaImage
{
    internal RgbaImage(int width, int height, ImmutableArray<byte> pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Wraps pixels.</summary>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="pixels">width × height × 4 bytes.</param>
    /// <returns>The image, or <c>Documents.Graphics.InvalidValue</c> when the size is not positive or does not match the
    /// pixels.</returns>
    public static Result<RgbaImage, Error> Create(int width, int height, ImmutableArray<byte> pixels) =>
        width >= 1 && height >= 1 && !pixels.IsDefault && pixels.Length == (long)width * height * 4
            ? Result<RgbaImage, Error>.Success(new RgbaImage(width, height, pixels))
            : Result<RgbaImage, Error>.Failure(
                new Error(GraphicsErrorCodes.InvalidValue, $"A {width} x {height} image is at least 1 x 1 and has {(long)width * height * 4} bytes of pixels, not {(pixels.IsDefault ? 0 : pixels.Length)}.")
                    .With(ErrorMetadataKeys.Value, $"{width}x{height}")
                    .With(ErrorMetadataKeys.Property, "pixels"));

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels: width × height × 4 bytes, R, G, B, A.</summary>
    public ImmutableArray<byte> Pixels { get; }
}
