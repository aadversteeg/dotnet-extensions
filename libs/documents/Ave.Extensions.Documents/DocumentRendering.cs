using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents;

/// <summary>
/// The whole pipeline in one call, for callers that just want the output. This composition package is the only place that
/// joins a front-end, the layout and a back-end: the front-end knows neither of the other two, so a second front-end or a
/// second back-end is added beside it, not into it.
/// </summary>
/// <remarks>
/// Two steps: <see cref="ToPages(Document, FontSet)"/> translates and lays out a document into its pages (the page model); a back-end then
/// draws the pages: <see cref="ToPdf(PageDocument, PdfRenderOptions)"/>, <see cref="ToPng(PageDocument, int, RasterOptions)"/>,
/// <see cref="ToPngPages(PageDocument, RasterOptions)"/>, <see cref="ToImages(PageDocument, RasterOptions)"/>. The calls on a
/// document are those two steps in one. A caller that wants a preview and the PDF lays the document out once, with
/// <see cref="ToPages(Document, FontSet)"/>, and hands the pages to both.
/// <para>The calls on a document stop at <see cref="LayoutEnvironment.DefaultMaxPages"/> pages
/// (<c>Documents.Layout.TooManyPages</c>); each has an overload with a <c>maxPages</c> parameter that raises or lowers
/// that limit.</para>
/// </remarks>
public static class DocumentRendering
{
    /// <summary>
    /// Lays a document out into pages: translate to the document model, then lay out and paint the page model. The result
    /// is what every back-end draws.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <returns>The pages, or the error with the source location of the call that caused it.</returns>
    /// <exception cref="ArgumentNullException">The document or the font set is null.</exception>
    public static Result<PageDocument, Error> ToPages(this Document document, FontSet fonts) =>
        document.ToPages(fonts, LayoutEnvironment.DefaultMaxPages);

    /// <summary>Lays a document out into pages as <see cref="ToPages(Document, FontSet)"/> does, with a page limit of
    /// its own.</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="maxPages">The most pages the document may have (the other overloads take
    /// <see cref="LayoutEnvironment.DefaultMaxPages"/>): a longer one is refused with <c>Documents.Layout.TooManyPages</c>.</param>
    /// <returns>The pages, or the error with the source location of the call that caused it.</returns>
    /// <exception cref="ArgumentNullException">The document or the font set is null.</exception>
    public static Result<PageDocument, Error> ToPages(this Document document, FontSet fonts, int maxPages) =>
        Laid(document, fonts, maxPages).OnSuccessMap(laid => laid.Pages);

    /// <summary>The pages of a document and the document model they were laid out from.</summary>
    private static Result<(PageDocument Pages, StyledDocument Model), Error> Laid(Document document, FontSet fonts, int maxPages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fonts);
        return DocumentTranslator.ToModel(document).OnSuccessBind(model =>
            DocumentLayout.Run(model, LayoutEnvironment.Create(fonts) with { MaxPages = maxPages, MaxPagesName = "the maxPages parameter of DocumentRendering" })
                .OnSuccessMap(pages => (pages, model)));
    }

    /// <summary>
    /// Renders a document to PDF: <see cref="ToPages(Document, FontSet)"/>, then the PDF back-end. Each step returns errors as values; the
    /// first failing step's error is returned.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The PDF file's creation date and identifier.</param>
    /// <returns>The PDF bytes, or the error with the source location of the call that caused it.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPdf(this Document document, FontSet fonts, PdfRenderOptions options) =>
        document.ToPdf(fonts, options, LayoutEnvironment.DefaultMaxPages);

    /// <summary>Renders a document to PDF as <see cref="ToPdf(Document, FontSet, PdfRenderOptions)"/> does, with a page
    /// limit of its own (<see cref="ToPages(Document, FontSet, int)"/>).</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The PDF file's creation date and identifier.</param>
    /// <param name="maxPages">The most pages the document may have.</param>
    /// <returns>The PDF bytes, or the error with the source location of the call that caused it.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPdf(this Document document, FontSet fonts, PdfRenderOptions options, int maxPages) =>
        document.ToPages(fonts, maxPages).OnSuccessBind(pages => pages.ToPdf(options));

    /// <summary>Writes laid-out pages as a PDF file (the PDF back-end, <see cref="PdfRenderer.Render"/>).</summary>
    /// <param name="pages">The pages.</param>
    /// <param name="options">The PDF file's creation date and identifier.</param>
    /// <returns>The PDF bytes, or the error.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPdf(this PageDocument pages, PdfRenderOptions options) =>
        PdfRenderer.Render(pages, options);

    /// <summary>Draws one page of a document as a PNG file: <see cref="ToPages(Document, FontSet)"/>, then the raster back-end.</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="pageNumber">The page, counting from 1.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>The PNG file, or the error (<c>Documents.Raster.PageNotFound</c> for a page the document does not have). A
    /// font whose licence forbids embedding (OS/2 fsType) is drawn: a PNG holds the glyphs' pixels, not the font. The
    /// same document is refused by <see cref="ToPdf(Document, FontSet, PdfRenderOptions)"/>, which would embed the
    /// font (<c>Documents.Pdf.Write.FontEmbeddingNotPermitted</c>).</returns>
    public static Result<ImmutableArray<byte>, Error> ToPng(this Document document, FontSet fonts, int pageNumber, RasterOptions options) =>
        document.ToPng(fonts, pageNumber, options, LayoutEnvironment.DefaultMaxPages);

    /// <summary>Draws one page of a document as a PNG file as <see cref="ToPng(Document, FontSet, int, RasterOptions)"/>
    /// does, with a page limit of its own (<see cref="ToPages(Document, FontSet, int)"/>).</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="pageNumber">The page, counting from 1.</param>
    /// <param name="options">The resolution and background.</param>
    /// <param name="maxPages">The most pages the document may have.</param>
    /// <returns>The PNG file, or the error.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPng(this Document document, FontSet fonts, int pageNumber, RasterOptions options, int maxPages) =>
        Laid(document, fonts, maxPages).OnSuccessBind(laid => laid.Pages.ToPng(pageNumber, options));

    /// <summary>Draws one page of laid-out pages as a PNG file (<see cref="PageRasterizer"/>, then
    /// <see cref="ImageEncoder.EncodePng"/>).</summary>
    /// <param name="pages">The pages.</param>
    /// <param name="pageNumber">The page, counting from 1.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>The PNG file, or the error. A font whose licence forbids embedding is drawn, where
    /// <see cref="ToPdf(PageDocument, PdfRenderOptions)"/> refuses it.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPng(this PageDocument pages, int pageNumber, RasterOptions options) =>
        PageRasterizer.Render(pages, pageNumber, options).OnSuccessBind(Png);

    /// <summary>Draws every page of a document as a PNG file: <see cref="ToPages(Document, FontSet)"/>, then the raster back-end.</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>One PNG file per page, in order, or the error.</returns>
    public static Result<ImmutableArray<ImmutableArray<byte>>, Error> ToPngPages(this Document document, FontSet fonts, RasterOptions options) =>
        document.ToPngPages(fonts, options, LayoutEnvironment.DefaultMaxPages);

    /// <summary>Draws every page of a document as a PNG file as
    /// <see cref="ToPngPages(Document, FontSet, RasterOptions)"/> does, with a page limit of its own
    /// (<see cref="ToPages(Document, FontSet, int)"/>).</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The resolution and background.</param>
    /// <param name="maxPages">The most pages the document may have.</param>
    /// <returns>One PNG file per page, in order, or the error.</returns>
    public static Result<ImmutableArray<ImmutableArray<byte>>, Error> ToPngPages(this Document document, FontSet fonts, RasterOptions options, int maxPages) =>
        Laid(document, fonts, maxPages).OnSuccessBind(laid => laid.Pages.ToPngPages(options));

    /// <summary>Draws every page of laid-out pages as a PNG file.</summary>
    /// <param name="pages">The pages.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>One PNG file per page, in order, or the error.</returns>
    public static Result<ImmutableArray<ImmutableArray<byte>>, Error> ToPngPages(this PageDocument pages, RasterOptions options) =>
        pages.ToImages(options).OnSuccessBind(images =>
        {
            var files = ImmutableArray.CreateBuilder<ImmutableArray<byte>>(images.Length);
            foreach (var image in images)
            {
                var file = Png(image);
                if (file.IsFailure)
                {
                    return Result<ImmutableArray<ImmutableArray<byte>>, Error>.Failure(file.Error);
                }

                files.Add(file.Value);
            }

            return Result<ImmutableArray<ImmutableArray<byte>>, Error>.Success(files.MoveToImmutable());
        });

    /// <summary>Draws every page of a document as pixels: <see cref="ToPages(Document, FontSet)"/>, then the raster back-end.</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>One image per page (8-bit RGBA, sRGB), in order, or the error.</returns>
    public static Result<ImmutableArray<RgbaImage>, Error> ToImages(this Document document, FontSet fonts, RasterOptions options) =>
        document.ToImages(fonts, options, LayoutEnvironment.DefaultMaxPages);

    /// <summary>Draws every page of a document as pixels as <see cref="ToImages(Document, FontSet, RasterOptions)"/>
    /// does, with a page limit of its own (<see cref="ToPages(Document, FontSet, int)"/>).</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The resolution and background.</param>
    /// <param name="maxPages">The most pages the document may have.</param>
    /// <returns>One image per page (8-bit RGBA, sRGB), in order, or the error.</returns>
    public static Result<ImmutableArray<RgbaImage>, Error> ToImages(this Document document, FontSet fonts, RasterOptions options, int maxPages) =>
        Laid(document, fonts, maxPages).OnSuccessBind(laid => laid.Pages.ToImages(options));

    /// <summary>Draws every page of laid-out pages as pixels (<see cref="PageRasterizer"/>).</summary>
    /// <param name="pages">The pages.</param>
    /// <param name="options">The resolution and background.</param>
    /// <returns>One image per page (8-bit RGBA, sRGB), in order, or the error.</returns>
    public static Result<ImmutableArray<RgbaImage>, Error> ToImages(this PageDocument pages, RasterOptions options) =>
        PageRasterizer.Render(pages, options);

    // An RgbaImage always matches its size and holds at most 2^26 pixels, so the encoder does not refuse it.
    private static Result<ImmutableArray<byte>, Error> Png(RgbaImage image) => ImageEncoder.EncodePng(image.Width, image.Height, image.Pixels.AsSpan());
}
