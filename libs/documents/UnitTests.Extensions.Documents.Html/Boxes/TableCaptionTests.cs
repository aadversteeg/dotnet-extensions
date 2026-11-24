using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// A table's caption from HTML to the page and the tagged PDF (cases <c>tc-*</c> measure the print): the caption is the
/// table's first structure kid wherever it is drawn, the table's own border and background lie around the grid alone, and
/// a caption that does not fit with the first or the last row is refused at the table.
/// </summary>
public class TableCaptionTests
{
    private static readonly HtmlRenderOptions Options = new()
    {
        Style = HtmlStyleOptions.Default with
        {
            Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null,
        },
        SourceName = "t.html",
    };

    private static PdfRenderOptions Tagged(PdfProfile profile) => new(new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.Zero)) { Profile = profile };

    private static string Doc(string body, string css = "", string page = "300pt 500pt") =>
        "<!doctype html><html lang=\"en\"><head><title>Caption</title><style>@font-face { font-family: Lato; src: url(lato.ttf) }\n"
        + $"@page {{ size: {page}; margin: 12pt }} html, body {{ margin: 0; font-family: Lato; font-size: 10.5pt }} p {{ margin: 0 }}\n"
        + $"td {{ border: 0.75pt solid #0000ff; padding: 2pt 4pt }}\n{css}</style></head><body>{body}</body></html>";

    private static string Rows(int count) => string.Concat(Enumerable.Range(1, count).Select(row => $"<tr><td>row{row}</td><td>two</td></tr>"));

    /// <summary>The documents the tagging tests write: a caption above, a caption below, and a caption below a table with a
    /// header row that breaks over two pages.</summary>
    public static TheoryData<string, string> Documents { get; } = new()
    {
        { "top", Doc($"<table><caption>Caption</caption><thead><tr><td>head</td><td>two</td></tr></thead>{Rows(2)}</table>") },
        { "bottom", Doc($"<table style=\"caption-side: bottom\"><caption>Caption</caption>{Rows(2)}</table>") },
        { "bottom-pages", Doc($"<table style=\"caption-side: bottom; border-collapse: separate; border-spacing: 3pt\"><caption>Caption</caption><thead><tr><td>head</td><td>two</td></tr></thead>{Rows(12)}</table>", page: "300pt 200pt") },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void The_caption_is_the_first_kid_of_its_table_wherever_it_is_drawn(string name, string html)
    {
        _ = name;
        var pdf = HtmlRendering.ToPdf(html, Options, Tagged(PdfProfile.PdfA3a.AndPdfUa1()));

        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var table = PdfStructureReadBack.Read(pdf.Value.Output).Single().DepthFirst().Single(element => element.Type == "Table");
        table.Children[0].Type.Should().Be("Caption");
        table.Children[0].DepthFirst().Should().Contain(element => element.Text.Contains("Caption", StringComparison.Ordinal));
        table.Children.Skip(1).Select(kid => kid.Type).Should().OnlyContain(type => type == "THead" || type == "TBody");
    }

    [Fact]
    public void The_tables_background_and_border_lie_around_the_grid_and_not_the_caption()
    {
        // tc-box b: the caption above (12.75 pt of text) and the separated table's border, padding and background below it.
        var html = Doc("<table style=\"border-collapse: separate; border-spacing: 3pt; border: 1.5pt solid #ff0000; padding: 1.5pt; background: #ffff00\"><caption>Caption</caption><tr><td>one</td><td>two</td></tr></table>");

        var page = HtmlRendering.ToPages(html, Options).Value.Output.Pages.Single();
        var fills = page.Items.OfType<FillRectangle>().ToList();

        var background = fills.Single(fill => fill.Color == new Color(255, 255, 0));
        background.Area.Origin.Y.Should().BeApproximately(24.75, 0.001, "the grid starts below the caption's 12.75 pt");
        fills.Where(fill => fill.Color == new Color(255, 0, 0)).Should().OnlyContain(fill => fill.Area.Origin.Y >= 24.75 - 0.001);
    }

    [Theory]
    // tc-break-first a: the caption fits at the page end, the first row below it does not.
    [InlineData("<p style=\"padding-top: 140pt\">first</p><table><caption>Caption</caption><tr><td>one</td><td>two</td></tr></table>")]
    // The caption fits, and the first row below it breaks inside its first cell.
    [InlineData("<p style=\"padding-top: 100pt\">first</p><table><caption>Caption</caption><tr><td>a<br>b<br>c<br>d<br>e<br>f<br>g<br>h<br>i<br>j</td><td>two</td></tr></table>")]
    // The caption below the last row that does not fit below it on the page.
    [InlineData("<p style=\"padding-top: 140pt\">first</p><table style=\"caption-side: bottom\"><caption>Caption</caption><tr><td>one</td><td>two</td></tr></table>")]
    public void A_caption_that_does_not_fit_with_its_first_or_last_row_is_refused_at_the_table(string body)
    {
        var pages = HtmlRendering.ToPages(Doc(body, page: "300pt 200pt"), Options);

        pages.IsFailure.Should().BeTrue();
        pages.Error.Code.Should().Be(LayoutErrorCodes.TableBreak);
        pages.Error.Message.Should().Contain("caption");
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void Tables_with_a_caption_pass_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-caption-").FullName;
        try
        {
            foreach (var row in Documents)
            {
                var pdf = HtmlRendering.ToPdf((string)row[1], Options, Tagged(PdfProfile.PdfA3a.AndPdfUa1()));
                File.WriteAllBytes(Path.Combine(folder, (string)row[0] + ".pdf"), [.. pdf.Value.Output]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(Documents.Count);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
