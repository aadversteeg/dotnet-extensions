using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Where tables break, against Chromium's print (cases <c>fr-f4-*</c> to <c>fr-f9-*</c> of <c>tools/reference/chromium</c>).
/// Page area 276 × 150 pt, Lato
/// 12 pt on 18 pt lines, 0.75 pt grid lines split at breaks (the line drawing Chromium has), orphans and widows 1 unless a
/// case says otherwise.
/// </summary>
public class TableBreakReferenceTests
{
    private static ParagraphElement Lines(params string[] lines) =>
        Paragraph([.. lines.SelectMany((line, index) => index == 0 ? new[] { Text(line) } : [LineBreak(), Text(line)])]);

    private static TableElement Grid(TableElement table) => table.GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static void ShouldPrintLikeChromium(string caseName, int orphans, int widows, params Element[] body)
    {
        // The case's body sets orphans and widows; the front-end sets them on boxes, so every top-level element gets them.
        Element[] withValues = [.. body.Select(element => element switch
        {
            ParagraphElement paragraph => paragraph.Orphans(orphans).Widows(widows),
            BlockElement block => block.Orphans(orphans).Widows(widows),
            TableElement table => table.Orphans(orphans).Widows(widows),
            _ => element,
        })];
        var document = Document([.. withValues], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var engine = DocumentTranslator.ToModel(document)
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    [Fact]
    public void A_row_moves_whole_when_a_cells_lines_fit_and_only_its_share_of_the_grid_line_does_not()
    {
        // "a" ends exactly at the page end, the line below it does not fit. Control 2.25 pt higher: cut.
        ShouldPrintLikeChromium("fr-f4-one-line-cell-beside-cut-cell", 1, 1,
            Paragraph("i").MarginBottom(113.25.Pt()), Grid(Table([Fill(), Fill()], [TableRow([Cell([Paragraph("a")]), Cell([Lines("b0", "b1", "b2")])])])));
        ShouldPrintLikeChromium("fr-f4-one-line-cell-beside-cut-cell-control", 1, 1,
            Paragraph("i").MarginBottom(111.Pt()), Grid(Table([Fill(), Fill()], [TableRow([Cell([Paragraph("a")]), Cell([Lines("b0", "b1", "b2")])])])));
        ShouldPrintLikeChromium("fr-f4-one-line-cell-widows3", 1, 3,
            Paragraph("i").MarginBottom(113.25.Pt()), Grid(Table([Fill(), Fill()], [TableRow([Cell([Paragraph("a")]), Cell([Lines("b0", "b1", "b2")])])])));
    }

    [Fact]
    public void A_row_moves_whole_when_a_cells_lines_fit_and_only_its_bottom_padding_does_not()
    {
        ShouldPrintLikeChromium("fr-f4-padding-second-row", 1, 1,
            Paragraph("i").MarginBottom(29.25.Pt()),
            Grid(Table([Fill()], [TableRow([Cell([Paragraph("r")])]), TableRow([Cell([Lines("a0", "a1", "a2", "a3")])])]).CellPadding(3.Pt(), 3.Pt())),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f4-grid-line-second-row", 1, 1,
            Paragraph("i").MarginBottom(40.5.Pt()),
            Grid(Table([Fill()], [TableRow([Cell([Paragraph("r")])]), TableRow([Cell([Lines("a0", "a1", "a2", "a3")])])])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f4-padding-two-cells", 1, 1,
            Paragraph("i").MarginBottom(29.25.Pt()),
            Grid(Table([Fill(), Fill()], [
                TableRow([Cell([Paragraph("r")]), Cell([Paragraph("r'")])]),
                TableRow([Cell([Lines("a0", "a1", "a2", "a3")]), Cell([Lines("b0", "b1", "b2", "b3", "b4", "b5")])]),
            ]).CellPadding(3.Pt(), 3.Pt())),
            Paragraph("z"));
    }

    [Fact]
    public void At_the_top_of_a_page_such_a_row_is_cut_where_Chromium_leaves_a_blank_page()
    {
        // Listed difference (docs/architecture.md): Chromium prints a blank first page and the row whole on page 2; the
        // engine cuts the row as a last resort, because a table at the top of the page cannot move to a better page.
        var document = Document([Grid(Table([Fill()], [TableRow([Cell([Lines("a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7")]).Padding(3.Pt())])])).Orphans(1).Widows(1), Paragraph("z").Orphans(1).Widows(1)],
            Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var engine = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine).Value;

        engine.PageTexts.Should().Equal("a0 a1 a2 a3 a4 a5 a6", "a7 z");
        PrintedPages.Chromium("fr-f4-padding-page-start").PageTexts.Should().Equal(string.Empty, "a0 a1 a2 a3 a4 a5 a6 a7", "z");
    }

    [Fact]
    public void Inside_a_row_group_kept_together_and_taller_than_the_page_the_rows_break_between_them()
    {
        // The group's keep must break anyway; inside it the break between the rows is the better one, not a
        // cut inside a kept row nor one against orphans 3.
        ShouldPrintLikeChromium("fr-f6-kept-group-kept-row-fits-a-page", 1, 1,
            Grid(Table([Fill()], [RowGroup([TableRow([Cell([Lines("a0", "a1", "a2", "a3")])]), TableRow([Cell([Lines("b0", "b1", "b2", "b3", "b4")])]).KeepTogether()]).KeepTogether()])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f6-kept-group-orphans3", 3, 1,
            Grid(Table([Fill()], [RowGroup([TableRow([Cell([Lines("a0", "a1", "a2", "a3", "a4", "a5")])]), TableRow([Cell([Lines("b0", "b1", "b2")])])]).KeepTogether()])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f6-kept-table-orphans3-control", 3, 1,
            Grid(Table([Fill()], [TableRow([Cell([Lines("a0", "a1", "a2", "a3", "a4", "a5")])]), TableRow([Cell([Lines("b0", "b1", "b2")])])]).KeepTogether()),
            Paragraph("z"));
    }

    [Fact]
    public void A_row_kept_together_keeps_the_rows_its_spanning_cell_reaches_into()
    {
        // A kept row with a spanning cell, with its two controls.
        TableElement Spanned(bool keptRow, bool keptCell)
        {
            var spanning = Cell([Paragraph("s")]).RowSpan(2);
            var first = TableRow([Cell([Paragraph("a")]), keptCell ? spanning.KeepTogether() : spanning]);
            return Grid(Table([Fill(), Fill()], [keptRow ? first.KeepTogether() : first, TableRow([Cell([Lines("b0", "b1", "b2")])])]));
        }

        ShouldPrintLikeChromium("fr-f8-kept-row-with-spanning-cell", 1, 1, Paragraph("i").MarginBottom(72.Pt()), Spanned(keptRow: true, keptCell: false), Paragraph("z"));
        ShouldPrintLikeChromium("fr-f8-row-not-kept-control", 1, 1, Paragraph("i").MarginBottom(72.Pt()), Spanned(keptRow: false, keptCell: false), Paragraph("z"));
        ShouldPrintLikeChromium("fr-f8-kept-spanning-cell-control", 1, 1, Paragraph("i").MarginBottom(72.Pt()), Spanned(keptRow: false, keptCell: true), Paragraph("z"));
    }

    [Fact]
    public void A_cell_that_spans_rows_fills_the_page_when_the_page_breaks_between_those_rows()
    {
        // The spanning cell goes on to the page end, not only to the end of its first row.
        ShouldPrintLikeChromium("fr-f7-span-cut-at-row-boundary", 1, 1,
            Paragraph("i").MarginBottom(72.Pt()),
            Grid(Table([Fill(), Fill()], [TableRow([Cell([Lines("s0", "s1", "s2", "s3")]).RowSpan(2), Cell([Paragraph("a")])]), TableRow([Cell([Lines("b0", "b1", "b2")])]).KeepTogether()])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f7-span-orphans-moves-table", 3, 1,
            Paragraph("i").MarginBottom(72.Pt()),
            Grid(Table([Fill(), Fill()], [TableRow([Cell([Lines("s0", "s1", "s2", "s3")]).RowSpan(2), Cell([Paragraph("a")])]), TableRow([Cell([Lines("b0", "b1", "b2")])])])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f7-span-fits-control", 1, 1,
            Paragraph("i").MarginBottom(72.Pt()),
            Grid(Table([Fill(), Fill()], [TableRow([Cell([Paragraph("s0")]).RowSpan(2), Cell([Paragraph("a")])]), TableRow([Cell([Lines("b0", "b1", "b2")])]).KeepTogether()])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f7-rows-of-spanning-cells", 1, 1,
            Paragraph("i").MarginBottom(100.Pt()),
            Grid(Table([Fill(), Fill()], [TableRow([Cell([Paragraph("s")]).RowSpan(2), Cell([])]), TableRow([Cell([Paragraph("t")]).RowSpan(2)]), TableRow([Cell([Paragraph("c")])])])),
            Paragraph("z"));
    }

    [Fact]
    public void Where_the_only_break_inside_a_box_or_a_cell_breaks_orphans_the_box_or_the_row_still_breaks_inside()
    {
        // The box and the cell keep the break they chose; their parents do not trade it for the break
        // before them.
        ShouldPrintLikeChromium("fr-f9-box-orphans3", 3, 1,
            Paragraph("i").MarginBottom(60.Pt()), Block([Lines("a0", "a1", "a2").KeepWithNext(), Lines("q0", "q1", "q2", "q3").KeepTogether()]), Paragraph("z"));
        ShouldPrintLikeChromium("fr-f9-cell-orphans3", 3, 1,
            Paragraph("i").MarginBottom(60.Pt()), Grid(Table([Fill()], [TableRow([Cell([Lines("a0", "a1", "a2").KeepWithNext(), Lines("q0", "q1", "q2", "q3").KeepTogether()])])])), Paragraph("z"));
        ShouldPrintLikeChromium("fr-f9-cell-orphans3-second-row", 3, 1,
            Paragraph("i").MarginBottom(42.Pt()),
            Grid(Table([Fill()], [TableRow([Cell([Paragraph("r")])]), TableRow([Cell([Lines("a0", "a1", "a2").KeepWithNext(), Lines("q0", "q1", "q2", "q3").KeepTogether()])])])),
            Paragraph("z"));
        ShouldPrintLikeChromium("fr-f9-padded-box-orphans3", 3, 1,
            Paragraph("i").MarginBottom(63.Pt()), Block([Lines("a0", "a1", "a2")]).Padding(9.Pt()).Border(0.75.Pt(), Color.Black), Paragraph("z"));
        ShouldPrintLikeChromium("fr-f9-padded-box-in-cell-orphans3", 3, 1,
            Paragraph("i").MarginBottom(63.Pt()), Grid(Table([Fill()], [TableRow([Cell([Block([Lines("a0", "a1", "a2")]).Padding(9.Pt()).Border(0.75.Pt(), Color.Black)])])])), Paragraph("z"));
    }

    [Fact]
    public void A_keep_rule_never_reaches_back_into_a_table_or_a_box_that_continues_from_the_page_before()
    {
        ShouldPrintLikeChromium("fr-f5-continued-table", 1, 1,
            Paragraph("i").MarginBottom(72.Pt()),
            Grid(Table([Fill()], [.. Enumerable.Range(0, 10).Select(row => (TablePart)TableRow([Cell([Paragraph($"r{row}")])]))])).KeepWithNext(),
            Lines("q0", "q1").KeepTogether());
        ShouldPrintLikeChromium("fr-f5-continued-box-table-padding", 1, 1,
            Block([Grid(Table([Fill()], [
                TableRow([Cell([Lines("a0", "a1", "a2", "a3", "a4")])]),
                TableRow([Cell([Lines("b0", "b1", "b2", "b3", "b4", "b5", "b6", "b7")])]),
                TableRow([Cell([Paragraph("c")])]),
            ]))]).Padding(24.Pt()),
            Paragraph("z"));
    }
}
