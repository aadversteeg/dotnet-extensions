using System;
using System.Collections.Immutable;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Images.Png;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

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
    /// <returns>The PNG file; <c>Documents.Image.InvalidPixels</c> for a side below 1 or pixels that do not match the
    /// size, <c>Documents.Image.TooLarge</c> for more than <see cref="ImageLimits.MaxPixels"/> pixels.</returns>
    public static Result<ImmutableArray<byte>, Error> EncodePng(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var size = $"{width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}";
        if (width < 1 || height < 1)
        {
            return Failure(ImageErrorCodes.InvalidPixels, $"An image is at least 1 x 1 pixels; asked for {width} x {height}.", size, "size");
        }

        if ((long)width * height > ImageLimits.MaxPixels)
        {
            return Failure(ImageErrorCodes.TooLarge, $"A {width} x {height} image has more than the {ImageLimits.MaxPixels} pixels the engine encodes.", size, "size");
        }

        if (rgba.Length != (long)width * height * 4)
        {
            return Failure(
                ImageErrorCodes.InvalidPixels,
                $"A {width} x {height} image has {(long)width * height * 4} bytes of RGBA pixels, not {rgba.Length}.",
                rgba.Length.ToString(CultureInfo.InvariantCulture),
                "rgba");
        }

        return Result<ImmutableArray<byte>, Error>.Success(PngEncoder.Encode(width, height, rgba));
    }

    private static Result<ImmutableArray<byte>, Error> Failure(ErrorCode code, string message, string value, string property) =>
        Result<ImmutableArray<byte>, Error>.Failure(new Error(code, message)
            .With(ErrorMetadataKeys.Value, value)
            .With(ErrorMetadataKeys.Property, property));
}
