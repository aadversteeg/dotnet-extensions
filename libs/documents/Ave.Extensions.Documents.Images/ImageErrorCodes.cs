using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Images;

/// <summary>Errors about images (decoding the bytes a caller passed in).</summary>
public static class ImageErrorCodes
{
    /// <summary><c>Documents.Image</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Image";

    /// <summary>Bytes that are not a well-formed PNG or JPEG file: data ends early, a checksum or a value is
    /// wrong. Metadata: Value, Offset.</summary>
    public static readonly ErrorCode Invalid = _ / "Invalid";

    /// <summary>A well-formed image in a variant this version does not decode, named in metadata Value (for
    /// example <c>PNG interlaced (Adam7)</c> or <c>JPEG arithmetic coding</c>), or a format that is neither PNG
    /// nor JPEG.</summary>
    public static readonly ErrorCode Unsupported = _ / "Unsupported";

    /// <summary>An image with more pixels than the engine decodes (<c>ImageLimits.MaxPixels</c>, 2^26). Metadata:
    /// Value (width x height).</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";
}
