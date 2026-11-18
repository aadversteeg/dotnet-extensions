using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// Errors the graphics library reports (<c>Documents.Graphics</c>). Every value a caller passes is checked and a value the
/// library cannot draw with is answered with one of these, never with an exception; only a null reference is the caller's
/// bug (<see cref="System.ArgumentNullException"/>).
/// </summary>
public static class GraphicsErrorCodes
{
    /// <summary><c>Documents.Graphics</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Graphics";

    /// <summary>A value outside what the library draws with: a transform or a coordinate that is not finite, a point that
    /// lies farther than <see cref="Canvas.MaxCoordinate"/> from the origin after the transform, a clip box that is not a
    /// finite rectangle or lies outside ±<see cref="Canvas.MaxPixelCoordinate"/>, a size below 1, or pixels that do not
    /// match their size. Metadata: Value, Property.</summary>
    public static readonly ErrorCode InvalidValue = _ / "InvalidValue";

    /// <summary>A canvas or a coverage mask of more than <see cref="Canvas.MaxPixels"/> pixels, or a mask with more than
    /// that many on a side. Metadata: Value.</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";
}
