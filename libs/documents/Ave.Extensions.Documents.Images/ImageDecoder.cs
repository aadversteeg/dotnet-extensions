using System;
using Ave.Documents.Images.Jpeg;
using Ave.Documents.Images.Png;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Images;

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
}
