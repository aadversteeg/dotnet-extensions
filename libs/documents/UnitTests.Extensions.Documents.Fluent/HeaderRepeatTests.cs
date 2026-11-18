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
/// (<c>tools/reference/chromium</c>, cases <c>a5-header-quarter-*</c>): a header of one line with top padding above 60
/// one-line body rows, at two page areas (180 and 360 pt) and on both sides of the step there (45.0 and 45.75 pt; 90.0
/// and 90.75 pt). Page width 300 pt, margin 12 pt, Lato 12 pt, line height 15 pt, 0.75 pt grid lines.
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
    [InlineData("a5-header-quarter-180-4500", 180, 29.25, 8, true)]
    [InlineData("a5-header-quarter-180-4575", 180, 30, 6, false)]
    [InlineData("a5-header-quarter-360-9000", 360, 74.25, 4, true)]
    [InlineData("a5-header-quarter-360-9075", 360, 75, 3, false)]
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
}
