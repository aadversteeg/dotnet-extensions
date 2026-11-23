using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Under <see cref="EmptyRowParts.Allow"/>, a row with a cell that has no room on the page for its top border and
/// padding is not cut there: the cell has no breakpoint on the page, so the row moves to the next page whole, as Chromium
/// prints it (<c>tools/reference/chromium</c>, cases <c>fr-row-later-padding-*</c>). Page area 276 × 150 pt, Lato 10.5 pt,
/// line height 12 pt, 0.75 pt grid lines split at breaks; row 1 of ten lines leaves 16.5 pt below its line (15.75 pt
/// under a header row).
/// </summary>
public class CellTopInsetTests
{
    private static ParagraphElement Lines(string prefix, int first, int count) =>
        Paragraph([.. Enumerable.Range(first, count).SelectMany(i => i == first ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static TableElement Grid(int columns, params TablePart[] rows) =>
        Table(Columns([.. Enumerable.Range(0, columns).Select(_ => Fill(1))]), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static CellElement Padded(CellElement cell, double top) => cell.PaddingTop(top.Pt()).PaddingLeft(3.Pt()).PaddingRight(3.Pt());

    /// <summary>The body of each case, as its HTML file builds it.</summary>
    private static Element[] Body(string caseName) => caseName switch
    {
        "fr-row-later-padding-beside-line" =>
        [
            Paragraph("i"),
            Grid(2,
                TableRow([Cell([Lines("a", 0, 10)]), Cell("b")]),
                TableRow([Padded(Cell([Lines("w", 1, 5)]), 18), Cell([Lines("v", 1, 5)])])),
            Paragraph("z"),
        ],
        "fr-row-later-padding-under-header" =>
        [
            Grid(1,
                HeaderRow([Cell("H")]),
                TableRow([Cell([Lines("a", 0, 10)])]),
                TableRow([Padded(Cell([Lines("w", 1, 3)]), 18)])),
            Paragraph("z"),
        ],
        "fr-row-later-padding-fits-line-not" =>
        [
            Paragraph("i"),
            Grid(1,
                TableRow([Cell([Lines("a", 0, 10)])]),
                TableRow([Padded(Cell([Lines("w", 1, 3)]), 6)])),
            Paragraph("z"),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
    };

    private static PrintedPages Printed(string caseName, EmptyRowParts parts)
    {
        var document = Document([.. Body(caseName)], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(10.5.Pt()).LineHeight(12.Pt()).Orphans(1).Widows(1)
            .EmptyRowParts(parts).FontMetricRounding(FontMetricRounding.WholePixels);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Where the lowest grid line of the page ends: with the row moved, the line below row 1.</summary>
    private static double LowestLine(PrintedPage page) => page.Rectangles.Max(box => box.Y1);

    [Theory]
    [InlineData("fr-row-later-padding-beside-line", "i a0 b a1 a2 a3 a4 a5 a6 a7 a8 a9", 145.5)]
    [InlineData("fr-row-later-padding-under-header", "H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", 146.25)]
    [InlineData("fr-row-later-padding-fits-line-not", "i a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", 145.5)]
    public void A_row_with_a_cell_that_has_no_room_for_its_top_padding_moves_to_the_next_page_whole_as_in_Chromium(string caseName, string firstPage, double rowEnd)
    {
        // beside-line: 18 pt of top padding do not fit the 16.5 pt left, though the other cell's first line would; Chromium
        // moves the row and leaves "v1" off page 1. under-header: the same below a header row, which page 2 repeats above
        // the row. fits-line-not: 6 pt of padding fit, the first line does not. Page 1 ends with row 1's line below it.
        var chromium = PrintedPages.Chromium(caseName);
        chromium.Pages[0].Text.Should().Be(firstPage);
        LowestLine(chromium.Pages[0]).Should().Be(rowEnd);
        var engine = Printed(caseName, EmptyRowParts.Allow);
        engine.PageTexts.Should().Equal(chromium.PageTexts);
        var words = engine.Pages.SelectMany(page => page.Words).ToList();
        var measured = chromium.Pages.SelectMany(page => page.Words).ToList();
        words.Select(word => (word.Page, word.Text)).Should().Equal(measured.Select(word => (word.Page, word.Text)));
        words.Zip(measured, (word, other) => Math.Abs(word.Baseline - other.Baseline)).Should().OnlyContain(apart => apart <= 0.375, "every baseline in the same whole pixel");
        LowestLine(engine.Pages[0]).Should().BeApproximately(rowEnd, 0.375, "no empty part of row 2 below row 1's line");
    }

    [Fact]
    public void Under_Avoid_a_row_whose_other_cell_places_a_line_is_cut_there_as_before()
    {
        // The policy is Allow's: under Avoid a cut where one cell shows a line on the page and the rest on the next is a
        // breakpoint like any other, and "v1" stays on page 1 (Chromium: on page 2, see above).
        Printed("fr-row-later-padding-beside-line", EmptyRowParts.Avoid).PageTexts
            .Should().Equal("i a0 b a1 a2 a3 a4 a5 a6 a7 a8 a9 v1", "v2 v3 w1 v4 w2 v5 w3 w4 w5 z");
    }
}
