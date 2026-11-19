using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents;

/// <summary>How to render an HTML document (<see cref="HtmlRendering"/>).</summary>
public sealed record HtmlRenderOptions
{
    /// <summary>No fonts, no resources, print on A4 media, refuse what is not drawn.</summary>
    public static HtmlRenderOptions Default { get; } = new();

    /// <summary>The fonts the document may use, besides those its <c>@font-face</c> rules name.</summary>
    public FontSet Fonts { get; init; } = new([]);

    /// <summary>The style options: media, values, the resolver of style sheets and font files, the base URL and the policy
    /// for what is not supported; one set for style resolution and for box generation.</summary>
    public HtmlStyleOptions Style { get; init; } = HtmlStyleOptions.Default;

    /// <summary>The document's name in every diagnostic (for example its file name).</summary>
    public string SourceName { get; init; } = string.Empty;
}

/// <summary>
/// The HTML front-end's pipeline in one call, beside <see cref="DocumentRendering"/>: HTML text and its style sheets →
/// parse (<see cref="HtmlParser"/>) → styles (<see cref="HtmlStyleResolver"/>) → boxes (<see cref="HtmlBoxGenerator"/>)
/// → pages (<see cref="DocumentLayout"/>) → PDF or PNG. The engine takes finished HTML: filling a template with data is
/// the caller's. What the front-end does not draw is refused with its line and column (or, with
/// <see cref="UnsupportedPolicy.Warn"/>, reported in the diagnostics and left out as <see cref="HtmlBoxGenerator"/> says).
/// </summary>
public static class HtmlRendering
{
    /// <summary>Lays an HTML document out into pages: what every back-end draws.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <returns>The pages, or the error with the document's line and column.</returns>
    /// <exception cref="ArgumentNullException">The text or the options are null.</exception>
    public static Result<PageDocument, Error> ToPages(string html, HtmlRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        var style = options.Style;
        return HtmlParser.Parse(html, new HtmlParseOptions(options.SourceName))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, style))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions
            {
                Fonts = options.Fonts,
                Resources = style.Resources,
                BaseUrl = style.BaseUrl,
                Unsupported = style.Unsupported,
            }))
            .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts));
    }

    /// <summary>Renders an HTML document to PDF: <see cref="ToPages"/>, then the PDF back-end.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="pdf">The PDF file's creation date and identifier.</param>
    /// <returns>The PDF bytes, or the first failing step's error.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPdf(string html, HtmlRenderOptions options, PdfRenderOptions pdf) =>
        ToPages(html, options).OnSuccessBind(pages => pages.ToPdf(pdf));

    /// <summary>Draws one page of an HTML document as a PNG file.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="pageNumber">The page, counting from 1.</param>
    /// <param name="raster">The resolution and background.</param>
    /// <returns>The PNG file, or the error.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPng(string html, HtmlRenderOptions options, int pageNumber, RasterOptions raster) =>
        ToPages(html, options).OnSuccessBind(pages => pages.ToPng(pageNumber, raster));

    /// <summary>Draws every page of an HTML document as a PNG file.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="raster">The resolution and background.</param>
    /// <returns>One PNG file per page, in order, or the error.</returns>
    public static Result<ImmutableArray<ImmutableArray<byte>>, Error> ToPngPages(string html, HtmlRenderOptions options, RasterOptions raster) =>
        ToPages(html, options).OnSuccessBind(pages => pages.ToPngPages(raster));

    /// <summary>Draws every page of an HTML document as pixels.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="raster">The resolution and background.</param>
    /// <returns>One image per page (8-bit RGBA, sRGB), in order, or the error.</returns>
    public static Result<ImmutableArray<RgbaImage>, Error> ToImages(string html, HtmlRenderOptions options, RasterOptions raster) =>
        ToPages(html, options).OnSuccessBind(pages => pages.ToImages(raster));
}
