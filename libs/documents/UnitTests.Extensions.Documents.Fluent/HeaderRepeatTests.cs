using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A header row is repeated on a later page only when it is at most a quarter of the page area, as Chromium prints
/// (<c>tools/reference/chromium</c>, cases <c>a6-header-quarter-*</c>): a header of one line with top padding above 60
/// one-line body rows, at two page areas (180 and 360 pt) and on both sides of the step there (45.0 and 45.75 pt; 90.0
/// and 90.75 pt). Page width 300 pt, margin 12 pt, Lato 12 pt, line height 15 pt, 0.75 pt grid lines. Also the header
/// cases <c>fr-old-header-quarter-*</c> (page areas of 150, 180 and 240 pt) and <c>fr-header-pad-box-20</c>.
/// </summary>
public class HeaderRepeatTests
{
    private static PrintedPages Printed(double area, double padding, MonolithicOverflow overflow)
    {
        TablePart[] rows = [HeaderRow([Cell([Paragraph("H0")]).PaddingTop(padding.Pt())]), .. Enumerable.Range(0, 60).Select(i => (TablePart)TableRow([Cell([Paragraph($"r{i}l0")])]))];
        var table = Table(Columns(Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();
        var document = Document([table], Page(300.Pt(), (area + 24).Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(overflow).FontMetricRounding(FontMetricRounding.WholePixels);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData("a6-header-quarter-180-4500", 180, 29.25, 8, true)]
    [InlineData("a6-header-quarter-180-4575", 180, 30, 6, false)]
    [InlineData("a6-header-quarter-360-9000", 360, 74.25, 4, true)]
    [InlineData("a6-header-quarter-360-9075", 360, 75, 3, false)]
    public void A_header_row_is_repeated_only_when_it_is_at_most_a_quarter_of_the_page_area_as_in_Chromium(string caseName, double area, double padding, int pages, bool repeated)
    {
        var chromium = PrintedPages.Chromium(caseName);

        foreach (var overflow in new[] { MonolithicOverflow.Refuse, MonolithicOverflow.Slice })
        {
            var engine = Printed(area, padding, overflow);

            engine.PageTexts.Should().Equal(chromium.PageTexts, $"{caseName}, {overflow}");
            engine.Pages.Should().HaveCount(pages);
            engine.Pages.Skip(1).Select(page => page.Words.Any(word => word.Text == "H0")).Should().AllBeEquivalentTo(repeated);
        }
    }

    [Theory]
    [InlineData(150, 18.75, "at")]
    [InlineData(150, 19.5, "over")]
    [InlineData(180, 26.25, "at")]
    [InlineData(180, 27, "over")]
    [InlineData(240, 41.25, "at")]
    [InlineData(240, 42, "over")]
    public void A_header_with_bottom_padding_at_and_over_a_quarter_of_the_page_area_is_repeated_as_in_Chromium(int area, double padding, string side)
    {
        // Cases fr-old-header-quarter-*: a header row "h" with bottom padding above 14 one-line rows, 18 pt lines, on page
        // areas of 150, 180 and 240 pt; "at" is a header exactly a quarter of the page area high, "over" one 0.75 pt more.
        var table = Table([Fill()], [HeaderRow([Cell([Paragraph("h")]).PaddingBottom(padding.Pt())]), .. Enumerable.Range(0, 14).Select(row => (TablePart)TableRow([Cell([Paragraph($"r{row}")])]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks().Orphans(2).Widows(2);
        var document = Document([table], Page(300.Pt(), (area + 24).Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var engine = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        var chromium = PrintedPages.Chromium($"fr-old-header-quarter-{area}-{side}");

        engine.IsSuccess.Should().BeTrue();
        engine.Value.PageTexts.Should().Equal(chromium.PageTexts);
        chromium.PageTexts.Skip(1).Should().OnlyContain(page => page.StartsWith("h ", StringComparison.Ordinal) == (side == "at"), "Chromium repeats it only up to a quarter of the page area");
    }

    [Fact]
    public void Under_a_header_over_a_quarter_of_the_page_area_a_cell_whose_box_s_bottom_padding_moves_on_prints_Chromium_s_pages()
    {
        // A cell with a bordered box around "a", whose 20 pt bottom padding does not fit below it (page area 36 pt), under
        // an 18 pt header: more than a quarter of the page area, so the header is not repeated, and page 2 holds "a" (case
        // fr-header-pad-box-20). Without the header row both print a | (blank) | AFTER (fr-header-pad-none-36).
        static string Engine(TableElement table)
        {
            var document = Document([table.Orphans(1).Widows(1), Paragraph("AFTER").Orphans(1).Widows(1)], Page(300.Pt(), 60.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
            var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
            result.IsSuccess.Should().BeTrue();
            return string.Join(" | ", result.Value.PageTexts);
        }

        static string Chromium(string caseName) => string.Join(" | ", PrintedPages.Chromium(caseName).PageTexts);

        var table = Table([Fill()], [HeaderRow([Cell("H")]), TableRow([Cell([Block([Paragraph("a").PaddingBottom(20.Pt())]).Border(1.5.Pt(), Color.Black)])])])
            .GridLines(0.Pt(), Color.Black).SplitLinesAtBreaks();
        Engine(table).Should().Be(Chromium("fr-header-pad-box-20")).And.Be("H | a |  | AFTER");
        var alone = Table([Fill()], [TableRow([Cell([Block([Paragraph("a").PaddingBottom(36.Pt())]).Border(1.5.Pt(), Color.Black)])])]).GridLines(0.Pt(), Color.Black).SplitLinesAtBreaks();
        Engine(alone).Should().Be(Chromium("fr-header-pad-none-36")).And.Be("a |  | AFTER");
    }
}
