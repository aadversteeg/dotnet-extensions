using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The differences from Chromium that <c>docs/architecture.md</c> lists ("Against Chromium" and the refusals among the
/// layout limits), each pinned with both results: the engine's pages and Chromium's print of the same document (cases
/// <c>fr-old-*</c> of <c>tools/reference/chromium</c>; review 40, the differences that <c>main</c> has too). A change to
/// either side fails here, so a fixed difference is a deliberate edit of this file and of the list.
/// </summary>
public class ListedDifferenceTests
{
    private const string Refused = "refused";

    private static ParagraphElement Lines(params string[] lines) =>
        Paragraph([.. lines.SelectMany((line, index) => index == 0 ? new[] { Text(line) } : [LineBreak(), Text(line)])]);

    private static TableElement Grid(TableElement table) => table.GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    /// <summary>The engine's pages, separated by <c> | </c>, or <see cref="Refused"/> and the code.</summary>
    private static string Engine(double height, int orphans, params Element[] body)
    {
        Element[] withValues = [.. body.Select(element => element switch
        {
            ParagraphElement paragraph => paragraph.Orphans(orphans).Widows(orphans),
            BlockElement block => block.Orphans(orphans).Widows(orphans),
            TableElement table => table.Orphans(orphans).Widows(orphans),
            _ => element,
        })];
        var result = DocumentTranslator.ToModel(Document([.. withValues], Page(300.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt()))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
        return result.IsFailure ? $"{Refused} {result.Error.Code}" : string.Join(" | ", PrintedPages.Engine(result.Value).PageTexts);
    }

    private static string Chromium(string caseName) => string.Join(" | ", PrintedPages.Chromium(caseName).PageTexts);

    private static void BothAre(string caseName, string engine, string chromium, string actual)
    {
        actual.Should().Be(engine, $"the engine's pages of {caseName}");
        Chromium(caseName).Should().Be(chromium, $"Chromium's pages of {caseName}");
    }

    [Fact]
    public void A_forced_break_after_only_empty_boxes_is_not_taken()
    {
        // Chromium takes it and leaves the first page blank; the engine takes a forced break only after content.
        BothAre("fr-old-forced-after-empty", "s0", " | s0", Engine(78, 2, Block([]), PageBreak(), Paragraph("s0")));
    }

    [Fact]
    public void A_forced_break_inside_a_box_kept_together_moves_the_whole_box_first()
    {
        // Chromium counts a break inside a box that holds a forced break as perfect before its keep applies
        // (CalculateBreakAppealInside); the engine first moves the kept box to a fresh page.
        BothAre("fr-old-forced-in-keep-together", "a0 | f0 | g0", "a0 f0 | g0",
            Engine(114, 2, Paragraph("a0"), Block([Paragraph("f0"), PageBreak(), Paragraph("g0")]).KeepTogether()));
    }

    [Fact]
    public void An_empty_box_whose_margin_carries_it_past_the_page_end_stays_on_the_page()
    {
        // The engine clamps it to the page end, so the keep before it holds; Chromium breaks before it.
        BothAre("fr-old-empty-box-past-end", "a0 b0 head0 | z0", "a0 b0 | head0 z0",
            Engine(78, 2, Paragraph("a0"), Paragraph("b0"), Paragraph("head0").KeepWithNext(), Block([]).MarginTop(18.Pt()), Paragraph("z0")));
    }

    [Fact]
    public void A_box_kept_together_does_not_hide_a_better_breakpoint_before_it()
    {
        // Chromium puts the kept paragraph on the page and breaks the keep after it (it looks like an accident of its
        // early-break bookkeeping); the engine follows the stated order of break appeals.
        BothAre("fr-old-keep-together-hides", "n0 | n1 o0 o1 p0", "n0 n1 o0 o1 | p0",
            Engine(96, 1, Lines("n0", "n1").KeepWithNext(), Lines("o0", "o1").KeepWithNext().KeepTogether(), Paragraph("p0")));
    }

    [Fact]
    public void An_empty_box_past_the_end_of_the_last_page_adds_no_page()
    {
        BothAre("fr-old-blank-last-page", "a0 a1 a2", "a0 a1 a2 | ",
            Engine(78, 2, Paragraph("a0"), Paragraph("a1"), Paragraph("a2"), Block([]).MarginTop(30.Pt())));
    }

    [Theory]
    [InlineData(150, 18.75, "at")]
    [InlineData(150, 19.5, "over")]
    [InlineData(180, 26.25, "at")]
    [InlineData(180, 27, "over")]
    [InlineData(240, 41.25, "at")]
    [InlineData(240, 42, "over")]
    public void A_table_header_is_repeated_whatever_its_height(int area, double padding, string side)
    {
        // Chromium repeats a header only while it and its grid line are at most a quarter of the page area high
        // (TableLayoutAlgorithm::GenerateFragment: the fragmentainer block size divided by 4); "at" is a header exactly a
        // quarter high, "over" one 0.75 pt more. The engine repeats it on every page.
        var table = Grid(Table([Fill()], [HeaderRow([Cell([Paragraph("h")]).PaddingBottom(padding.Pt())]), .. Enumerable.Range(0, 14).Select(row => (TablePart)TableRow([Cell([Paragraph($"r{row}")])]))]));
        var engine = Engine(area + 24, 2, table);
        var chromium = Chromium($"fr-old-header-quarter-{area}-{side}");

        engine.Split(" | ").Skip(1).Should().OnlyContain(page => page.StartsWith("h ", StringComparison.Ordinal), "the engine repeats the header on every page");
        chromium.Split(" | ").Skip(1).Should().OnlyContain(page => page.StartsWith("h ", StringComparison.Ordinal) == (side == "at"), "Chromium repeats it only up to a quarter of the page area");
    }

    [Fact]
    public void Under_a_repeated_header_a_cell_whose_box_s_bottom_padding_moves_on_takes_a_page_more()
    {
        // A cell with a bordered box around "a", whose 20 pt bottom padding does not fit below it (page area 36 pt). The
        // engine repeats the 18 pt header (Chromium does not: it is more than a quarter of the page area), and on page 2
        // only the top border of the box fits below it, so page 2 holds the header and that border; Chromium prints "a"
        // there. Without the header row both print a | (blank) | AFTER (fr-header-pad-none-36).
        var table = Table([Fill()], [HeaderRow([Cell("H")]), TableRow([Cell([Block([Paragraph("a").PaddingBottom(20.Pt())]).Border(1.5.Pt(), Color.Black)])])])
            .GridLines(0.Pt(), Color.Black).SplitLinesAtBreaks();
        BothAre("fr-header-pad-box-20", "H | H | H a |  | AFTER", "H | a |  | AFTER", Engine(60, 1, table, Paragraph("AFTER")));
        var alone = Table([Fill()], [TableRow([Cell([Block([Paragraph("a").PaddingBottom(36.Pt())]).Border(1.5.Pt(), Color.Black)])])]).GridLines(0.Pt(), Color.Black).SplitLinesAtBreaks();
        BothAre("fr-header-pad-none-36", "a |  | AFTER", "a |  | AFTER", Engine(60, 1, alone, Paragraph("AFTER")));
    }

    [Fact]
    public void A_header_taller_than_the_page_is_refused()
    {
        // Chromium cuts such a header like a body row.
        var table = Grid(Table([Fill()], [HeaderRow([Cell([Lines("h0", "h1", "h2")])]), TableRow([Cell([Paragraph("r0")])]), TableRow([Cell([Paragraph("r1")])])]));
        BothAre("fr-old-header-taller-than-page", $"{Refused} {LayoutErrorCodes.DoesNotFit}", "h0 h1 | h2 r0 | r1", Engine(66, 1, table));
    }

    [Fact]
    public void A_row_taller_than_the_page_only_through_its_cell_padding_is_refused()
    {
        // Chromium leaves the first page blank, prints the row and lets its padding overflow.
        var table = Grid(Table([Fill()], [TableRow([Cell([Paragraph("w")]).PaddingBottom(140.Pt())])]));
        BothAre("fr-old-row-taller-by-padding", $"{Refused} {LayoutErrorCodes.DoesNotFit}", " | w | z", Engine(174, 1, table, Paragraph("z")));
    }

    [Fact]
    public void A_table_row_without_cells_is_refused_as_input()
    {
        // Chromium lays the row out with no height of its own.
        var table = Grid(Table([Fill(), Fill()], [TableRow([Cell([Paragraph("s")]).RowSpan(2), Cell([Paragraph("a")])]), TableRow([]), TableRow([Cell([Paragraph("c")]), Cell([Paragraph("e")])])]));
        BothAre("fr-old-row-without-cells", $"{Refused} {DocumentErrorCodes.Input.Invalid.Table}", "i s a | c e z", Engine(174, 1, Paragraph("i").MarginBottom(100.Pt()), table, Paragraph("z")));
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("bottom")]
    public void The_rest_of_a_cut_middle_or_bottom_cell_is_aligned_in_the_rest_of_its_row(string align)
    {
        // Same pages; the engine aligns the rest of the shorter cell in the row's rest, Chromium puts it at the top: a3
        // is 36 pt (middle) or 72 pt (bottom) lower in the engine.
        var alignment = align == "middle" ? Ave.Extensions.Documents.DocumentModel.VerticalAlign.Middle : Ave.Extensions.Documents.DocumentModel.VerticalAlign.Bottom;
        var table = Grid(Table([Fill(), Fill()], [TableRow([Cell([Lines("a0", "a1", "a2", "a3")]).VerticalAlign(alignment), Cell([Lines("b0", "b1", "b2", "b3", "b4", "b5", "b6", "b7")])])]));
        var document = Document([Paragraph("i").MarginBottom(72.Pt()).Orphans(1).Widows(1), table.Orphans(1).Widows(1), Paragraph("z")], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var engine = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine).Value;
        var chromium = PrintedPages.Chromium($"fr-old-cut-cell-{align}");

        engine.Pages.Select(page => page.Words.Select(word => word.Text).Order(StringComparer.Ordinal)).Should().BeEquivalentTo(
            chromium.Pages.Select(page => page.Words.Select(word => word.Text).Order(StringComparer.Ordinal)), options => options.WithStrictOrdering(), "the same words on the same pages");
        double A3(PrintedPages pages) => pages.Pages[1].Words.Single(word => word.Text == "a3").Baseline - pages.Pages[1].Words.Single(word => word.Text == "b3").Baseline;
        A3(chromium).Should().BeApproximately(0, 0.01, "Chromium puts the rest at the top of the row's rest");
        A3(engine).Should().BeApproximately(align == "middle" ? 36 : 72, 0.01, "the engine aligns it in the row's rest");
    }

    [Fact]
    public void A_margin_dropped_at_a_row_cut_does_not_stay_in_the_row_s_height()
    {
        // Same pages; Chromium's row keeps the height it has uncut, so z is 12 pt lower in Chromium.
        var table = Grid(Table([Fill()], [TableRow([Cell([Paragraph("a").MarginBottom(12.Pt()), Lines("b0", "b1")])])]));
        var document = Document([Paragraph("i").MarginBottom(113.25.Pt()).Orphans(1).Widows(1), table.Orphans(1).Widows(1), Paragraph("z")], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var engine = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine).Value;
        var chromium = PrintedPages.Chromium("fr-old-margin-at-row-cut");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        double Z(PrintedPages pages) => pages.Pages[1].Words.Single(word => word.Text == "z").Baseline;
        (Z(chromium) - Z(engine)).Should().BeApproximately(12, 0.5, "the dropped 12 pt margin");
    }

    [Fact]
    public void A_child_s_own_break_inside_avoid_lowers_an_early_break_inside_it()
    {
        // Review 40, a planted defect without a verdict (the child's own keep ignored for an early break inside it,
        // FlowLayout) that survived the whole suite: the only better-looking break lies inside the kept paragraph, and its
        // keep makes it no better than the natural break before q. Same pages as Chromium.
        var engine = Engine(114, 1, Lines("a0", "a1", "a2", "a3").KeepWithNext().KeepTogether(), Paragraph("h0").KeepWithNext(), Lines("q0", "q1", "q2").KeepTogether());
        engine.Should().Be(Chromium("fr-own-avoid-early-break"));
    }
}
