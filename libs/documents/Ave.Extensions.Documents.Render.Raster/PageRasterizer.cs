using System;
using System.Collections.Immutable;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Render.Raster;

/// <summary>
/// The raster back-end: a page of the page model in, its pixels out. The same page model the PDF back-end writes, with
/// the same contract (<see cref="PageModelRules"/>), drawn in plain managed code: glyphs as their outlines filled at their
/// exact positions (no hinting, no snapping to pixels), rectangles, and images with an exact box filter, every edge
/// anti-aliased by the exact area it covers (Ave.Extensions.Documents.Graphics). Structure and artifact markers say what
/// content means; they draw nothing and are passed over.
/// </summary>
public static class PageRasterizer
{
    /// <summary>
    /// Draws one page. Page coordinates (points, y down) map to pixels by the factor <c>Resolution / 72</c>; pixel (i, j)
    /// covers the page from (i, j) to (i + 1, j + 1) pixel units.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>The pixels (8-bit RGBA, sRGB, alpha not premultiplied), or an error: the page model's contract
    /// (<c>Documents.Raster.InvalidPageModel</c>), the options (<c>InvalidOptions</c>), the size (<c>TooLarge</c>), an image
    /// too far from the page's origin to be drawn exactly (<c>ImageOutOfReach</c>), a glyph outline or an image that
    /// cannot be decoded (<c>Documents.Font.Parse.*</c>, <c>Documents.Image.*</c>), or a fill of the canvas that fails
    /// (<c>Documents.Graphics.TooMuchWork</c>, <c>Documents.Graphics.InexactCoverage</c>, see <see cref="Canvas.Fill"/>):
    /// both kinds of fill error leave by one path, the canvas's result handed back unchanged by the painter and by this
    /// call, and the canvas they leave behind is dropped.</returns>
    public static Result<RgbaImage, Error> Render(Page page, RasterOptions options) =>
        Checked(options, code => PageModelRules.Check(page, code))
            .OnSuccessBind(checkedPage => Draw(checkedPage, options, new PagePainter.Images()));

    /// <summary>Draws every page of a document, after checking the whole document by the contract. Each distinct image is
    /// decoded once for all pages.</summary>
    /// <param name="document">The page document.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>One image per page, in order, or the first error (see <see cref="Render(Page, RasterOptions)"/>).</returns>
    public static Result<ImmutableArray<RgbaImage>, Error> Render(PageDocument document, RasterOptions options) =>
        Checked(options, code => PageModelRules.Check(document, code))
            .OnSuccessBind(checkedDocument =>
            {
                var images = ImmutableArray.CreateBuilder<RgbaImage>(checkedDocument.Pages.Count);
                var decoded = new PagePainter.Images();
                foreach (var page in checkedDocument.Pages)
                {
                    var drawn = Draw(page, options, decoded);
                    if (drawn.IsFailure)
                    {
                        return Result<ImmutableArray<RgbaImage>, Error>.Failure(drawn.Error);
                    }

                    images.Add(drawn.Value);
                }

                return Result<ImmutableArray<RgbaImage>, Error>.Success(images.MoveToImmutable());
            });

    /// <summary>
    /// Draws one page of a document, after checking the whole document by the contract (so a page drawn alone is refused
    /// for the same reasons as the document it belongs to).
    /// </summary>
    /// <param name="document">The page document.</param>
    /// <param name="pageNumber">The page, counting from 1.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>The page's pixels, <c>Documents.Raster.PageNotFound</c> for a number outside 1 to the page count, or an
    /// error of <see cref="Render(Page, RasterOptions)"/>.</returns>
    public static Result<RgbaImage, Error> Render(PageDocument document, int pageNumber, RasterOptions options) =>
        Checked(options, code => PageModelRules.Check(document, code))
            .OnSuccessBind(checkedDocument => pageNumber >= 1 && pageNumber <= checkedDocument.Pages.Count
                ? Draw(checkedDocument.Pages[pageNumber - 1], options, new PagePainter.Images())
                : Result<RgbaImage, Error>.Failure(
                    new Error(RasterErrorCodes.PageNotFound, $"The document has {checkedDocument.Pages.Count} pages; there is no page {pageNumber}.")
                        .With(ErrorMetadataKeys.Value, pageNumber.ToString(CultureInfo.InvariantCulture))));

    /// <summary>The size in pixels of a page at a resolution: ⌈points × resolution / 72⌉ each way.</summary>
    /// <param name="page">The page size in points.</param>
    /// <param name="resolution">Pixels per inch.</param>
    /// <returns>Width and height in pixels (as 64-bit numbers, since a large page at a high resolution may not fit 32
    /// bits). A positive side at a positive resolution is at least 1 pixel, as the formula says, also where the product
    /// is too small for a double and rounds to 0 (a resolution near 5e-324). A page or a resolution that
    /// <see cref="Render(Page, RasterOptions)"/> refuses by the contract or the options (a side not from
    /// <see cref="PageModelRules.MinPageSide"/> to <see cref="PageModelRules.MaxNumber"/>, not a number, or a resolution
    /// that is not a positive finite number) has no image: the answer is (0, 0). A page the contract accepts but too large
    /// to draw gets its size; <see cref="Render(Page, RasterOptions)"/> refuses it with <c>TooLarge</c> and names it.</returns>
    public static (long Width, long Height) PixelSize(Size page, double resolution) =>
        Drawable(page.Width) && Drawable(page.Height) && resolution is > 0 and < double.PositiveInfinity
            ? (Pixels(page.Width, resolution), Pixels(page.Height, resolution))
            : (0, 0);

    /// <summary>A page side the contract accepts (<c>PageModelRules</c>, page size).</summary>
    private static bool Drawable(double side) => side is >= PageModelRules.MinPageSide and <= PageModelRules.MaxNumber;

    private static long Pixels(double points, double resolution)
    {
        var pixels = (long)Math.Ceiling(points * resolution / 72);
        return pixels == 0 && points > 0 && resolution > 0 ? 1 : pixels;
    }

    /// <summary>The one check every entry point makes: the options, then the page model's contract (by the shared
    /// <see cref="PageModelRules"/>, reported under <c>Documents.Raster.InvalidPageModel</c>).</summary>
    private static Result<T, Error> Checked<T>(RasterOptions? options, Func<ErrorCode, Result<T, Error>> contract) =>
        CheckOptions(options).OnSuccessBind(_ => contract(RasterErrorCodes.InvalidPageModel));

    private static Result<RasterOptions, Error> CheckOptions(RasterOptions? options) =>
        options is { Resolution: > 0 and < double.PositiveInfinity }
            ? Result<RasterOptions, Error>.Success(options)
            : Result<RasterOptions, Error>.Failure(
                new Error(RasterErrorCodes.InvalidOptions, $"The resolution must be a positive finite number of pixels per inch; it is {options?.Resolution.ToString(CultureInfo.InvariantCulture) ?? "missing"}.")
                    .With(ErrorMetadataKeys.Value, options?.Resolution.ToString(CultureInfo.InvariantCulture) ?? "null"));

    private static Result<RgbaImage, Error> Draw(Page page, RasterOptions options, PagePainter.Images images)
    {
        var (width, height) = PixelSize(page.Size, options.Resolution);
        // Each side is checked on its own first, so the product cannot overflow (a side converts to at most long.MaxValue).
        if (width > Canvas.MaxPixels || height > Canvas.MaxPixels || width * height > Canvas.MaxPixels)
        {
            return Result<RgbaImage, Error>.Failure(
                new Error(RasterErrorCodes.TooLarge, $"The page is {width} x {height} pixels at {options.Resolution.ToString(CultureInfo.InvariantCulture)} pixels per inch; the raster back-end draws at most {Canvas.MaxPixels} pixels.")
                    .With(ErrorMetadataKeys.Value, $"{width}x{height}"));
        }

        return Canvas.Create((int)width, (int)height, options.Background)
            .OnSuccessBind(canvas => PagePainter.Paint(page, canvas, options.Resolution / 72, images).OnSuccessMap(_ => canvas.ToImage()));
    }
}
