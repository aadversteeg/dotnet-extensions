using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Fluent;
using Ave.Documents.Layout;
using Ave.Documents.PageModel;
using Ave.Documents.Render.Pdf;
using Ave.Documents.Samples;
using Ave.Documents.TestSupport;
using UglyToad.PdfPig;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// The timesheet sample, the stage-2 proof, end to end for the reporting PoC's three data sets in Dutch and English:
/// page counts, golden files of the three text serialisations (new fragment and page-model shapes: tables, grid lines,
/// header and footer areas, images), the text read back with PdfPig, and byte-identical output on repeated runs.
/// </summary>
public class TimesheetTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    private static readonly Brand Brand = Brand.Poc([.. File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "logo.png"))]);

    private sealed record Stages(StyledDocument Model, ValueList<PageFragment> Fragments, PageDocument Pages, ImmutableArray<byte> Pdf);

    private static Stages Run(string dataSet, string culture)
    {
        var data = TimesheetData.Load(Path.Combine(AppContext.BaseDirectory, "data", $"sample-{dataSet}.json"))
            .WithCleanText(FontCoverage.Displayable(TestFonts.Regular));
        var model = DocumentTranslator.ToModel(Timesheets.Timesheet(data, Brand, CultureInfo.GetCultureInfo(culture))).Value;
        var fragments = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model).Value;
        var pages = PagePainter.Paint(model.Metadata, fragments);
        return new Stages(model, fragments, pages, PdfRenderer.Render(pages, Options).Value);
    }

    [Theory]
    [InlineData("normal", "nl-NL", 2)]
    [InlineData("normal", "en-US", 2)]
    [InlineData("long", "nl-NL", 7)]
    [InlineData("long", "en-US", 7)]
    [InlineData("edge", "nl-NL", 1)]
    [InlineData("edge", "en-US", 1)]
    public void Every_data_set_renders_with_the_expected_page_count(string dataSet, string culture, int pages)
    {
        // The PoC's Typst output has 2, 5 and 1 pages. The long set differs by design of the line box: see the
        // stage-2 report (CSS line-height: normal is 1.2 em for Lato; Typst sets a line from cap height to baseline).
        Run(dataSet, culture).Pages.Pages.Should().HaveCount(pages);
    }

    [Fact]
    public void The_normal_Dutch_timesheet_matches_its_golden_files()
    {
        var timesheet = Run("normal", "nl-NL");

        Golden.Compare("timesheet-normal.nl.model.txt", DocumentModelText.Write(timesheet.Model)).Should().BeNull();
        Golden.Compare("timesheet-normal.nl.fragments.txt", FragmentText.Write(timesheet.Fragments)).Should().BeNull();
        Golden.Compare("timesheet-normal.nl.page.txt", PageModelText.Write(timesheet.Pages)).Should().BeNull();
    }

    [Theory]
    [InlineData("nl-NL", "Pagina {0} van {1}", "Uitgevoerd op", "64,50")]
    [InlineData("en-US", "Page {0} of {1}", "Executed at", "64.50")]
    public void An_independent_reader_finds_page_x_of_y_the_repeated_header_the_logo_and_the_total(string culture, string pageOf, string header, string total)
    {
        using var pdf = PdfDocument.Open([.. Run("normal", culture).Pdf]);

        var texts = pdf.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))).ToArray();
        texts.Should().HaveCount(2);
        for (var number = 1; number <= 2; number++)
        {
            var text = texts[number - 1];
            text.Should().Contain(string.Format(CultureInfo.InvariantCulture, pageOf, number, 2));
            text.Should().Contain(header, "the header row repeats on every page");
            pdf.GetPage(number).GetImages().Should().ContainSingle("the logo is in the header of every page");
        }

        texts[1].Should().Contain(total);
        texts[0].Should().NotContain("Jansen & Zn. Installatietechniek SmallBusiness onderhoud 2026-10-01", "page 1 has no right column in its header");
    }

    [Fact]
    public void Numbers_follow_the_culture_s_format()
    {
        using var nl = PdfDocument.Open([.. Run("edge", "nl-NL").Pdf]);
        using var en = PdfDocument.Open([.. Run("edge", "en-US").Pdf]);

        string Words(PdfDocument pdf) => string.Join(" ", pdf.GetPage(1).GetWords().Select(word => word.Text));
        Words(nl).Should().Contain("1.234,50").And.Contain("-1,25");
        Words(en).Should().Contain("1,234.50").And.Contain("-1.25");
    }

    [Fact]
    public void Two_runs_give_byte_identical_PDFs_with_images() =>
        Run("long", "en-US").Pdf.Should().Equal(Run("long", "en-US").Pdf);
}
