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
