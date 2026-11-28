using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Images;

/// <summary>Errors about images: decoding the bytes a caller passed in, and encoding the pixels a caller passed in. A
/// value the image layer cannot work with is answered with one of these, never with an exception.</summary>
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

    /// <summary>An image with more pixels than the engine decodes or encodes (<c>ImageLimits.MaxPixels</c>, 2^26), or
    /// than is left of the document's budget (<c>ImageLimits.MaxDocumentPixels</c>). Metadata: Value (width x height).
    /// Also an image that is not decoded because the failed decodes of its document have reached
    /// <c>ImageLimits.MaxFailedDocumentPixels</c>; Value is then the pixels they produced and the limit.</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";

    /// <summary>A JPEG whose scans together visit the blocks of its frame more often than the decoder does work for
    /// (<c>JpegDecoder.MaxPasses</c>, 32 times): a progressive file can ask for much work in few bytes. Metadata: Value
    /// (the passes asked for, as blocks visited per block of the frame).</summary>
    public static readonly ErrorCode TooMuchWork = _ / "TooMuchWork";

    /// <summary>Pixels to encode that are not an image (<c>ImageEncoder.EncodePng</c>): a side below 1, or a number of
    /// bytes other than width × height × 4. Metadata: Value, Property.</summary>
    public static readonly ErrorCode InvalidPixels = _ / "InvalidPixels";
}
