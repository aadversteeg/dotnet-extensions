using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A listed difference from Chromium's print (docs/architecture.md, "Known limits"), pinned by its reference case
/// (<c>tools/reference/chromium</c>): the engine's pages are asserted, Chromium's are stated; and a row whose bottom
/// padding does not fit, which was such a difference and now prints Chromium's pages. Page 300 × 204 pt, margin
/// 12 pt (page area 276 × 180 pt), Lato 12 pt, line height 15 pt, fixed table layout, 0.75 pt grid lines.
/// </summary>
public class PinnedDifferenceTests
{
    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    private static ParagraphElement Lines(string prefix, int count) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}0") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static TableElement Table(CellElement cell) =>
        Elements.Table(Columns(Fill(1)), [TableRow([cell])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static PrintedPages Printed(Element[] body, MonolithicOverflow overflow, EmptyRowParts parts)
    {
        var document = Document([.. body], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(overflow).EmptyRowParts(parts).FontMetricRounding(FontMetricRounding.WholePixels);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData(EmptyRowParts.Avoid)]
    [InlineData(EmptyRowParts.Allow)]
    public void A_row_whose_lines_fit_but_whose_bottom_padding_does_not_moves_to_the_next_page_whole_as_in_Chromium(EmptyRowParts parts)
    {
        // Six lines, then a one-row table whose cell has five lines and 30 pt bottom padding, then "after". On page 1 the
        // five lines fit (b4 ends at 189.75 pt of 192) but the padding does not. Chromium moves the row whole: a0..a5 on
        // page 1, b0..b4 and "after" on page 2; so does the engine, under both values of EmptyRowParts (a cell never
        // breaks before its bottom padding, and the row moves whole when it can).
        var body = new Element[] { Lines("a", 6), Table(Cell([Lines("b", 5)]).PaddingBottom(30.Pt())), Paragraph("after") };

        var engine = Printed(body, MonolithicOverflow.Refuse, parts);
        var chromium = PrintedPages.Chromium("a2-row-end-bottom-padding-30");

        chromium.PageTexts.Should().Equal("a0 a1 a2 a3 a4 a5", "b0 b1 b2 b3 b4 after");
        engine.PageTexts.Should().Equal(chromium.PageTexts);
    }

    [Fact]
    public void A_piece_below_the_top_padding_of_nested_cells_takes_a_page_more_under_Refuse_than_under_Slice_and_in_Chromium()
    {
        // A 160 pt image in a table inside a cell, each cell with 15 pt top padding, then "after"; no piece is taller than
        // the page area. Chromium: page 1 holds the cells' tops only, page 2 the image (12 to 171.75 pt) and "after". Under
        // Slice the engine has Chromium's pages: the cells are cut at the deepest place, so the image starts page 2
        // (ConstraintSpace.IsBelowStartInsets, set under Slice only). Under Refuse, the default, it has a page more: the
        // image is on page 2 below the cells' top padding again, and "after" on page 3.
        var body = new Element[] { Table(Cell([Table(Cell([Image(Tall).Alt("tall").Height(160.Pt())]).PaddingTop(15.Pt()))]).PaddingTop(15.Pt())), Paragraph("after") };
        var chromium = PrintedPages.Chromium("a4-nested-padded-image-160");

        chromium.PageTexts.Should().Equal("", "after");
        Printed(body, MonolithicOverflow.Slice, EmptyRowParts.Avoid).PageTexts.Should().Equal(chromium.PageTexts);
        Printed(body, MonolithicOverflow.Refuse, EmptyRowParts.Avoid).PageTexts.Should().Equal("", "", "after");
        Printed(body, MonolithicOverflow.Refuse, EmptyRowParts.Allow).PageTexts.Should().Equal("", "", "after");
    }
}
