using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents;

/// <summary>How to render an HTML document (<see cref="HtmlRendering"/>).</summary>
public sealed record HtmlRenderOptions
{
    /// <summary>No fonts, no resources, refuse what is not drawn; a document without <c>@page</c> rules is printed on A4
    /// with no margins, and its media queries and viewport units see that page (<see cref="HtmlStyleOptions.Page"/>).</summary>
    public static HtmlRenderOptions Default { get; } = new();

    /// <summary>The fonts the document may use, besides those its <c>@font-face</c> rules name.</summary>
    public FontSet Fonts { get; init; } = new([]);

    /// <summary>The families the generic families and the initial value of <c>font-family</c> stand for, looked up as a
    /// written family name is; a generic family they do not map is refused, as there are no system fonts
    /// (<see cref="Html.Boxes.GenericFamilies"/>).</summary>
    public GenericFamilies GenericFamilies { get; init; } = GenericFamilies.None;

    /// <summary>The style options: the page without <c>@page</c> rules, media, values, the resolver of style sheets and
    /// font files, the base URL and the policy for what is not supported; one set for style resolution and for box
    /// generation.</summary>
    public HtmlStyleOptions Style { get; init; } = HtmlStyleOptions.Default;

    /// <summary>The document's name in every diagnostic (for example its file name).</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>The most pages the document may have (<see cref="LayoutEnvironment.MaxPages"/>; default
    /// <see cref="LayoutEnvironment.DefaultMaxPages"/>): a longer one is refused with <c>Documents.Layout.TooManyPages</c>
    /// when pagination reaches the limit.</summary>
    public int MaxPages { get; init; } = LayoutEnvironment.DefaultMaxPages;
}

/// <summary>What an <see cref="HtmlRendering"/> call gives: its output and the diagnostics of every stage.</summary>
/// <typeparam name="T">The output: the pages, a PDF file, PNG files or images.</typeparam>
/// <param name="Output">The output.</param>
/// <param name="Diagnostics">The diagnostics of parsing, style resolution and box generation, in order; under
/// <see cref="UnsupportedPolicy.Warn"/> also every refusal, each with what was left out or changed for it.</param>
public sealed record HtmlRendered<T>(T Output, ValueList<Error> Diagnostics);

/// <summary>
/// The HTML front-end's pipeline in one call, beside <see cref="DocumentRendering"/>: HTML text and its style sheets →
/// parse (<see cref="HtmlParser"/>) → styles (<see cref="HtmlStyleResolver"/>) → boxes (<see cref="HtmlBoxGenerator"/>)
/// → pages (<see cref="DocumentLayout"/>) → PDF or PNG. The engine takes finished HTML: filling a template with data is
/// the caller's. What the front-end does not draw is refused with its line and column (or, with
/// <see cref="UnsupportedPolicy.Warn"/>, reported in the diagnostics and left out as <see cref="HtmlBoxGenerator"/> says).
/// Every entry returns the diagnostics beside its output (<see cref="HtmlRendered{T}"/>).
/// </summary>
public static class HtmlRendering
{
    /// <summary>Lays an HTML document out into pages: what every back-end draws.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <returns>The pages and the diagnostics, or the error with the document's line and column.</returns>
    /// <exception cref="ArgumentNullException">The text or the options are null.</exception>
    public static Result<HtmlRendered<PageDocument>, Error> ToPages(string html, HtmlRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        var style = options.Style;
        return HtmlParser.Parse(html, new HtmlParseOptions(options.SourceName))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, style))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions
            {
                Fonts = options.Fonts,
                GenericFamilies = options.GenericFamilies,
                Resources = style.Resources,
                BaseUrl = style.BaseUrl,
                AllowBaseElement = style.AllowBaseElement,
                Unsupported = style.Unsupported,
                MissingCharacters = Shaper.MissingCharacters,
            }))
            .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, LayoutEnvironment.Create(boxes.Fonts) with { MaxPages = options.MaxPages }).OnSuccessMap(pages => new HtmlRendered<PageDocument>(pages, boxes.Diagnostics)));
    }

    /// <summary>Renders an HTML document to PDF: <see cref="ToPages"/>, then the PDF back-end.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="pdf">The PDF file's creation date and identifier.</param>
    /// <returns>The PDF bytes and the diagnostics, or the first failing step's error.</returns>
    public static Result<HtmlRendered<ImmutableArray<byte>>, Error> ToPdf(string html, HtmlRenderOptions options, PdfRenderOptions pdf) =>
        ToPages(html, options).OnSuccessBind(pages => Beside(pages, pages.Output.ToPdf(pdf)));

    /// <summary>Draws one page of an HTML document as a PNG file.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="pageNumber">The page, counting from 1.</param>
    /// <param name="raster">The resolution and background.</param>
    /// <returns>The PNG file and the diagnostics, or the error.</returns>
    public static Result<HtmlRendered<ImmutableArray<byte>>, Error> ToPng(string html, HtmlRenderOptions options, int pageNumber, RasterOptions raster) =>
        ToPages(html, options).OnSuccessBind(pages => Beside(pages, pages.Output.ToPng(pageNumber, raster)));

    /// <summary>Draws every page of an HTML document as a PNG file.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="raster">The resolution and background.</param>
    /// <returns>One PNG file per page, in order, and the diagnostics, or the error.</returns>
    public static Result<HtmlRendered<ImmutableArray<ImmutableArray<byte>>>, Error> ToPngPages(string html, HtmlRenderOptions options, RasterOptions raster) =>
        ToPages(html, options).OnSuccessBind(pages => Beside(pages, pages.Output.ToPngPages(raster)));

    /// <summary>Draws every page of an HTML document as pixels.</summary>
    /// <param name="html">The HTML text.</param>
    /// <param name="options">The fonts, the style options and the document's name.</param>
    /// <param name="raster">The resolution and background.</param>
    /// <returns>One image per page (8-bit RGBA, sRGB), in order, and the diagnostics, or the error.</returns>
    public static Result<HtmlRendered<ImmutableArray<RgbaImage>>, Error> ToImages(string html, HtmlRenderOptions options, RasterOptions raster) =>
        ToPages(html, options).OnSuccessBind(pages => Beside(pages, pages.Output.ToImages(raster)));

    /// <summary>A back-end's output with the diagnostics of the pages it was made from.</summary>
    private static Result<HtmlRendered<T>, Error> Beside<T>(HtmlRendered<PageDocument> pages, Result<T, Error> output) =>
        output.OnSuccessMap(value => new HtmlRendered<T>(value, pages.Diagnostics));
}
