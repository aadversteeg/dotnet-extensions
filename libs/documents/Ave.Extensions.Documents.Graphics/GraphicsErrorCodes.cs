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

    /// <summary>A shape whose exact coverage asks for more work than a fill may do: its pixels where contours may overlap
    /// or cross, counted as <see cref="Canvas.MaxFillWork"/> says, add up to more than that limit, or with the fills
    /// before it on the same canvas to more than <see cref="Canvas.MaxCanvasWork"/>, or one row holds more than
    /// <see cref="Canvas.MaxRowPieces"/> pieces of edges in such pixels. Nothing of the shape is painted. Metadata: Value
    /// (the work asked for), <see cref="Limit"/>.</summary>
    public static readonly ErrorCode TooMuchWork = _ / "TooMuchWork";

    /// <summary>A pixel whose exact coverage could not be established: the pieces of edges in it and the integral of the
    /// winding number over it do not agree to 2^-10 (a guard; no shape is known that reaches it). The rows above the
    /// pixel have been painted, its row and the rows below it not. Metadata: Value (the pixel, x,y).</summary>
    public static readonly ErrorCode InexactCoverage = _ / "InexactCoverage";

    /// <summary>The metadata key of the limit a <see cref="TooMuchWork"/> error names.</summary>
    public const string Limit = "Limit";
}
