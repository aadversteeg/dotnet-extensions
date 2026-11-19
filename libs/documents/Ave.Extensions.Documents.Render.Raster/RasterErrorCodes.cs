using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Render.Raster;

/// <summary>Errors the raster back-end raises (<c>Documents.Raster</c>). A glyph whose outline cannot be decoded and an
/// image that cannot be decoded keep the errors of the font and image layers (<c>Documents.Font.Parse</c>,
/// <c>Documents.Image</c>).</summary>
public static class RasterErrorCodes
{
    /// <summary><c>Documents.Raster</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Raster";

    /// <summary>A page model that breaks the back-ends' contract, whoever produced it (the list is on
    /// <c>PageModelRules</c> in Ave.Extensions.Documents.PageModel; the PDF back-end checks the same rules). Metadata:
    /// Value, and SourceFile, SourceLine where the page model has a location.</summary>
    public static readonly ErrorCode InvalidPageModel = _ / "InvalidPageModel";

    /// <summary>Options the back-end cannot draw with: a resolution that is not a positive finite number. Metadata:
    /// Value.</summary>
    public static readonly ErrorCode InvalidOptions = _ / "InvalidOptions";

    /// <summary>A page number that is not a page of the document: below 1 or above the page count. Metadata: Value (the
    /// number asked for).</summary>
    public static readonly ErrorCode PageNotFound = _ / "PageNotFound";

    /// <summary>A page that would have more pixels at the resolution asked for than the back-end draws (2^26, the
    /// limit of a canvas). Metadata: Value (width x height in pixels).</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";

    /// <summary>An image whose rectangle reaches so far from the page's origin, at the resolution asked for, that its
    /// pixel edges cannot be placed within a fraction of a pixel (beyond <c>Canvas.MaxImageReach</c>, 2^40 pixels; about
    /// 8.25 × 10^11 pt at 96 dpi). The page model allows it and the PDF back-end writes it; the raster back-end refuses
    /// it rather than draw it wrong. The reach is counted from the page's origin, not from the page: along x it is
    /// (|x| + width) × resolution / 72 for a rectangle at x, and the same along y. So an image centred on the origin
    /// reaches 1.5 times its width and is refused from two thirds of the width an image that starts at the origin may
    /// have; and an image that lies wholly off the page beyond the reach is refused although it would show nothing,
    /// where a rectangle at the same place is drawn. Metadata: Value (the reach in pixels).</summary>
    public static readonly ErrorCode ImageOutOfReach = _ / "ImageOutOfReach";
}
