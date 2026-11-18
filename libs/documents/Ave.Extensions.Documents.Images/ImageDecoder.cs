using System;
using Ave.Extensions.Documents.Images.Jpeg;
using Ave.Extensions.Documents.Images.Png;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images;

/// <summary>The entry point of the image layer: image file bytes in, <see cref="ImageData"/> out.</summary>
public static class ImageDecoder
{
    /// <summary>
    /// Decodes an image file. The format is recognised by its signature, never by a file name: PNG starts with the
    /// eight bytes of the PNG signature (PNG §5.2), JPEG with the SOI marker FF D8 (ITU-T T.81 §B.1.1.3).
    /// </summary>
    /// <param name="file">The complete file.</param>
    /// <returns>The image, or a <c>Documents.Image.Invalid</c> / <c>Documents.Image.Unsupported</c> error.</returns>
    public static Result<ImageData, Error> Decode(ReadOnlyMemory<byte> file) =>
        PngDecoder.HasSignature(file.Span) ? PngDecoder.Decode(file)
        : JpegReader.HasSignature(file.Span) ? JpegReader.Read(file)
        : ImageErrors.Unsupported<ImageData>("a format other than PNG or JPEG");

    /// <summary>
    /// The decoded samples of an image: a <see cref="RasterImage"/> as it is; a <see cref="JpegImage"/> decoded (ITU-T
    /// T.81: baseline, extended sequential and progressive Huffman coding, 8-bit, grey or three components; see the
    /// decoder's remarks for the up-sampling and colour rules). For a back-end that draws pixels itself instead of
    /// embedding the file.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The samples (8-bit grey or RGB, plus alpha for a PNG that has it), or a <c>Documents.Image</c> error for
    /// damaged JPEG data or one above <see cref="ImageLimits.MaxPixels"/>.</returns>
    /// <exception cref="ArgumentNullException">The image is null.</exception>
    public static Result<RasterImage, Error> DecodeSamples(ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image switch
        {
            RasterImage raster => Result<RasterImage, Error>.Success(raster),
            JpegImage jpeg => JpegDecoder.Decode(jpeg),

            // ImageData's constructor is private to this assembly: these two are all there are.
            _ => throw new InvalidOperationException($"Unknown image type {image.GetType().Name}."),
        };
    }
}
