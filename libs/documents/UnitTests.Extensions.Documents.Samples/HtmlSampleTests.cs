using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The HTML samples: the caller's template fills the HTML from the data, and what it writes is, byte for byte, the
/// document the family measures against Chromium (<c>tools/reference/chromium/documents</c>), so the engine's output
/// that is equal to Chromium's is the sample's output. Every data value is escaped. Golden files pin the pages, the
/// structure tree and the pixels at 96 dpi as for the code samples (update with <c>UPDATE_GOLDEN=1</c>).
/// </summary>
public class HtmlSampleTests
{
    private static readonly RasterOptions Screen = new(96);

    [Theory]
    [InlineData("html-timesheet-normal.nl", "timesheet-normal")]
    [InlineData("html-timesheet-long.nl", "timesheet-pages")]
    [InlineData("html-timesheet-edge.nl", "timesheet-one-page")]
    [InlineData("html-invoice", "invoice")]
    public void The_filled_HTML_is_the_document_measured_against_Chromium_byte_for_byte(string name, string reference)
    {
        var sample = SampleFiles.Html(name);

        sample.ReferenceName.Should().Be(reference);
        Encoding.UTF8.GetBytes(sample.Html).Should().Equal(File.ReadAllBytes(ReferenceDocument(reference)));
    }

    [Fact]
    public void The_filled_HTML_has_LF_line_ends_whatever_the_checkout_of_the_templates()
    {
        SampleFiles.HtmlDocuments.Should().AllSatisfy(sample => sample.Html.Should().NotContain("\r", sample.Name));
        HtmlText.LineFeeds("<p>\r\n</p>\r<p>\n").Should().Be("<p>\n</p>\n<p>\n", "a template checked out with CR LF writes the same document");
    }

    [Theory]
    [InlineData("html-timesheet-normal.nl", 2)]
    [InlineData("html-timesheet-long.nl", 5)]
    [InlineData("html-timesheet-edge.nl", 1)]
    [InlineData("html-invoice", 1)]
    public void Every_HTML_sample_renders_without_a_diagnostic(string name, int pages)
    {
        var rendered = SampleFiles.HtmlPages(SampleFiles.Html(name));

        rendered.Diagnostics.Should().BeEmpty();
        rendered.Output.Pages.Should().HaveCount(pages);
    }

    [Fact]
    public void Every_data_value_is_escaped_in_the_HTML_and_in_the_CSS_strings()
    {
        const string Customer = "A <b>&amp; \"B\" 'C'</style></td>";
        var data = SampleFiles.Data("edge") with { CustomerName = Customer };

        var html = TimesheetHtml.Write(data, Brand.Poc([.. File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "logo.png"))]), CultureInfo.GetCultureInfo("nl-NL"));

        html.Should().Contain("<p class=\"name\">A &lt;b&gt;&amp;amp; &quot;B&quot; &#x27;C&#x27;&lt;/style&gt;&lt;/td&gt;</p>");
        html.Should().Contain("content: \"A \\3C b>&amp; \\\"B\\\" 'C'\\3C /style>\\3C /td>\\A ", "the top-right margin box shows it as a CSS string");
        html.Split("</style>").Should().HaveCount(2, "the value cannot end the style element");
        var sample = new HtmlSample("escaped", "timesheet-one-page", html);
        var pages = SampleFiles.HtmlPages(sample).Output;
        PageText(pages).Should().Contain(Customer.Replace(" ", string.Empty, StringComparison.Ordinal), "the reader sees the value as it was written (spaces left out: a line may break at one)");
    }

    [Fact]
    public void A_file_the_resolver_does_not_know_is_refused()
    {
        var sample = SampleFiles.Html("html-invoice");
        var html = sample.Html.Replace("href=\"invoice.css\"", "href=\"other.css\"", StringComparison.Ordinal);

        var result = HtmlRendering.ToPages(html, HtmlSamples.Options(HtmlSamples.Resources(AppContext.BaseDirectory), sample));

        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("html-timesheet-normal.nl")]
    [InlineData("html-timesheet-long.nl")]
    [InlineData("html-invoice")]
    public void An_HTML_sample_matches_its_golden_page_and_structure_files(string name)
    {
        var sample = SampleFiles.Html(name);

        Golden.Compare($"{name}.page.txt", PageModelText.Write(SampleFiles.HtmlPages(sample).Output)).Should().BeNull();
        Golden.Compare($"{name}.structure.txt", PdfStructureReadBack.Outline(PdfStructureReadBack.Read(SampleFiles.HtmlPdf(sample)))).Should().BeNull();
    }

    [Theory]
    [InlineData("html-timesheet-normal.nl", 1)]
    [InlineData("html-timesheet-normal.nl", 2)]
    [InlineData("html-invoice", 1)]
    public void An_HTML_sample_page_at_96_dpi_has_the_pixels_of_its_golden_PNG(string name, int page)
    {
        var images = SampleFiles.HtmlPages(SampleFiles.Html(name)).Output.ToImages(Screen);

        images.IsSuccess.Should().BeTrue(images.IsFailure ? images.Error.ToString() : string.Empty);
        var image = images.Value[page - 1];
        GoldenImages.ComparePixels($"{name}.page{page}.96dpi.png", image.Width, image.Height, image.Pixels.AsSpan()).Should().BeNull();
    }

    [Fact]
    public void Two_runs_give_byte_identical_PDFs()
    {
        var sample = SampleFiles.Html("html-timesheet-long.nl");

        SampleFiles.HtmlPdf(sample).Should().Equal(SampleFiles.HtmlPdf(sample));
    }

    /// <summary>The reference document of that name, found from this source file.</summary>
    private static string ReferenceDocument(string name, [CallerFilePath] string testFile = "") =>
        Path.Combine(Path.GetDirectoryName(testFile)!, "..", "tools", "reference", "chromium", "documents", name + ".html");

    private static string PageText(PageDocument pages) =>
        string.Concat(pages.Pages.SelectMany(page => page.Items.OfType<GlyphRun>()).SelectMany(run => run.Glyphs.Select(glyph => glyph.Text)))
            .Replace(" ", string.Empty, StringComparison.Ordinal);
}
