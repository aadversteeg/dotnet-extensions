using System.Text;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>The HTML entry of the composition package (<see cref="HtmlRendering"/>).</summary>
public class HtmlRenderingTests
{
    private const string Html =
        "<!doctype html><html lang=\"en\"><head><title>T</title><style>@font-face { font-family: Body; src: url(lato.ttf) }" +
        " @page { size: 300pt 174pt; margin: 12pt } body { font-family: Body; margin: 0 }</style></head><body><p>Hello</p></body></html>";

    private static readonly HtmlRenderOptions Options = new()
    {
        Style = HtmlStyleOptions.Default with { Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null },
        SourceName = "hello.html",
    };

    [Fact]
    public void An_HTML_document_becomes_pages_a_PDF_and_PNG_files()
    {
        var pages = HtmlRendering.ToPages(Html, Options);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value).PageTexts.Should().Equal("Hello");

        var pdf = HtmlRendering.ToPdf(Html, Options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero)));
        Encoding.ASCII.GetString([.. pdf.Value.Take(5)]).Should().Be("%PDF-");
        HtmlRendering.ToPng(Html, Options, 1, new RasterOptions(72)).Value.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
        HtmlRendering.ToPngPages(Html, Options, new RasterOptions(72)).Value.Should().ContainSingle();
        HtmlRendering.ToImages(Html, Options, new RasterOptions(72)).Value.Single().Width.Should().Be(300);
    }

    [Theory]
    // No option: A4 with no margins (210 x 297 mm print as 594.96 x 841.92 pt), portrait.
    [InlineData("", false, 594.96, 841.92, 0)]
    [InlineData("@page { size: auto }", false, 594.96, 841.92, 0)]
    // Media queries see the page without rules (here A4), not the page the rules make.
    [InlineData("@page { size: landscape }", false, 841.92, 594.96, 0)]
    // The caller's page, 300 x 198 pt with 12 pt margins: its size, its margins, and media queries on its page area
    // (276 x 174 pt).
    [InlineData("", true, 300, 198, 12 + 30)]
    [InlineData("@page { size: auto; margin: 0 }", true, 300, 198, 30)]
    [InlineData("@page { size: portrait }", true, 198, 300, 12 + 30)]
    [InlineData("@page { size: 96pt 150pt }", true, 96, 150, 12 + 30)]
    public void Without_a_page_rule_the_page_is_the_caller_s_and_media_queries_see_it(string pageRule, bool callersPage, double width, double height, double firstWordX)
    {
        var html = "<!doctype html><html><head><style>@font-face { font-family: Body; src: url(lato.ttf) } body { font-family: Body; margin: 0 }" +
                   $" @media (max-height: 200pt) {{ p {{ margin-left: 30pt }} }} {pageRule}</style></head><body><p>Hello</p></body></html>";
        var options = callersPage
            ? Options with { Style = Options.Style with { Page = new(300.Pt(), 198.Pt(), new Edges(12.Pt(), 12.Pt(), 12.Pt(), 12.Pt())) } }
            : Options;

        var page = PrintedPages.Engine(HtmlRendering.ToPages(html, options).Value).Pages.Single();

        page.Width.Should().BeApproximately(width, 1e-6);
        page.Height.Should().BeApproximately(height, 1e-6);
        page.Words.Single().X0.Should().BeApproximately(firstWordX, 1e-6);
    }

    [Fact]
    public void What_the_front_end_does_not_draw_is_refused_with_the_document_s_name_line_and_column()
    {
        var refused = HtmlRendering.ToPages(Html.Replace("<p>Hello</p>", "<p>Hello\n<br></p>", StringComparison.Ordinal), Options);
        refused.IsFailure.Should().BeTrue();
        var error = refused.Error.Parts().Single();
        (error.Metadata![ErrorMetadataKeys.SourceFile], error.Metadata[ErrorMetadataKeys.SourceLine], error.Metadata[ErrorMetadataKeys.SourceColumn])
            .Should().Be(("hello.html", 2, 1));
    }
}
