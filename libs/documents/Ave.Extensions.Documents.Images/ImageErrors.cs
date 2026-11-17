using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images;

/// <summary>The kinds of error image decoding reports.</summary>
internal static class ImageErrors
{
    /// <summary>The bytes break the format's rules (<c>Documents.Image.Invalid</c>).</summary>
    public static Result<T, Error> Invalid<T>(string message, long offset) =>
        Result<T, Error>.Failure(new Error(ImageErrorCodes.Invalid, $"Invalid image data at byte {offset}: {message}")
            .With(ErrorMetadataKeys.Offset, offset)
            .With(ErrorMetadataKeys.Value, message));

    /// <summary>A valid file in a variant this decoder does not handle (<c>Documents.Image.Unsupported</c>); the
    /// variant is named in the message and in metadata Value.</summary>
    public static Result<T, Error> Unsupported<T>(string variant) =>
        Result<T, Error>.Failure(new Error(ImageErrorCodes.Unsupported, $"Unsupported image variant: {variant}.")
            .With(ErrorMetadataKeys.Value, variant));

    /// <summary>An image above <see cref="ImageLimits.MaxPixels"/> (<c>Documents.Image.TooLarge</c>).</summary>
    public static Result<T, Error> TooLarge<T>(long width, long height) =>
        Result<T, Error>.Failure(new Error(ImageErrorCodes.TooLarge, $"The image is {width}x{height} pixels; the engine decodes at most {ImageLimits.MaxPixels} pixels.")
            .With(ErrorMetadataKeys.Value, $"{width}x{height}"));
}
