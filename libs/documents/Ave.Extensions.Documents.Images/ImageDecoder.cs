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
    public static Result<ImageData, Error> Decode(ReadOnlyMemory<byte> file) => Decode(file, null);

    /// <summary>
    /// Decodes an image file as <see cref="Decode(ReadOnlyMemory{byte})"/> does, charging the pixels it decodes to
    /// samples (a PNG's; a JPEG is only read for its header) to the document's budget before any sample is made.
    /// </summary>
    /// <param name="file">The complete file.</param>
    /// <param name="budget">The document's budget, or null for none.</param>
    /// <returns>The image, or a <c>Documents.Image</c> error; <c>Documents.Image.TooLarge</c> when the image does not
    /// fit in what is left of the budget.</returns>
    public static Result<ImageData, Error> Decode(ReadOnlyMemory<byte> file, ImagePixelBudget? budget) =>
        PngDecoder.HasSignature(file.Span) ? PngDecoder.Decode(file, budget)
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
    public static Result<RasterImage, Error> DecodeSamples(ImageData image) => DecodeSamples(image, null);

    /// <summary>
    /// The decoded samples of an image as <see cref="DecodeSamples(ImageData)"/> gives them, charging the image's pixels
    /// to the budget first: a JPEG's before it is decoded (given back when its data does not decode), a
    /// <see cref="RasterImage"/>'s because the caller makes its own copy of the samples to draw with.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="budget">The budget of the pixels decoded, or null for none.</param>
    /// <returns>The samples, or a <c>Documents.Image</c> error; <c>Documents.Image.TooLarge</c> when the image does not
    /// fit in what is left of the budget.</returns>
    /// <exception cref="ArgumentNullException">The image is null.</exception>
    public static Result<RasterImage, Error> DecodeSamples(ImageData image, ImagePixelBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (budget is not null && budget.Charge(image.PixelWidth, image.PixelHeight) is { IsFailure: true } refused)
        {
            return Result<RasterImage, Error>.Failure(refused.Error);
        }

        return image switch
        {
            RasterImage raster => Result<RasterImage, Error>.Success(raster),

            // A JPEG whose data does not decode gives its pixels back.
            JpegImage jpeg => budget is null ? JpegDecoder.Decode(jpeg) : budget.Kept(jpeg.PixelWidth, jpeg.PixelHeight, JpegDecoder.Decode(jpeg)),

            // ImageData's constructor is private to this assembly: these two are all there are.
            _ => throw new InvalidOperationException($"Unknown image type {image.GetType().Name}."),
        };
    }
}
