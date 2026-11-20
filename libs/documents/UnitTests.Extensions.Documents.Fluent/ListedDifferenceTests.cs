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
/// <c>fr-old-*</c> of <c>tools/reference/chromium</c>, and the newer ones named in each test). A change to
/// either side fails here, so a fixed difference is a deliberate edit of this file and of the list.
/// </summary>
public class ListedDifferenceTests
{
    private const string Refused = "refused";

    private static ParagraphElement Lines(params string[] lines) =>
        Paragraph([.. lines.SelectMany((line, index) => index == 0 ? new[] { Text(line) } : [LineBreak(), Text(line)])]);

    private static TableElement Grid(TableElement table) => table.GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    /// <summary>The engine's pages, separated by <c> | </c>, or <see cref="Refused"/> and the code.</summary>
    private static string Engine(double height, int orphans, params Element[] body) => Engine(height, orphans, orphans, body);

    /// <summary>The engine's pages with <c>orphans</c> and <c>widows</c> of their own.</summary>
    private static string Engine(double height, int orphans, int widows, params Element[] body)
    {
        Element[] withValues = [.. body.Select(element => element switch
        {
            ParagraphElement paragraph => paragraph.Orphans(orphans).Widows(widows),
            BlockElement block => block.Orphans(orphans).Widows(widows),
            TableElement table => table.Orphans(orphans).Widows(widows),
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

    /// <summary>The documents of <see cref="Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own"/>:
    /// a0 a1 with keep-with-next inside two boxes with bottom padding (15 pt inside, 27 pt outside), then j0 j1 j2 and z.</summary>
    private static Element[] KeepWithNextAcrossPadding(string caseName)
    {
        var j = Lines("j0", "j1", "j2");
        BlockElement Boxes(ParagraphElement a, bool innerKeeps = false, bool outerKeeps = false)
        {
            var inner = Block([a]).PaddingBottom(15.Pt());
            var outer = Block([innerKeeps ? inner.KeepWithNext() : inner]).PaddingBottom(27.Pt());
            return outerKeeps ? outer.KeepWithNext() : outer;
        }

        var lead = Boxes(Lines("a0", "a1").KeepWithNext());
        var bordered = Block([j.Widows(4)]).BorderTop(0.75.Pt(), Color.Black);
        return caseName switch
        {
            "fr-kwn-pad-then-bordered-box-widows4" => [lead, bordered, Paragraph("z")],
            "fr-kwn-pad-no-padding-control" => [Paragraph("a0").MarginBottom(9.Pt()).KeepWithNext(), j, Paragraph("z")],
            "fr-kwn-pad-on-inner-box" => [Boxes(Lines("a0", "a1"), innerKeeps: true), j, Paragraph("z")],
            "fr-kwn-pad-on-outer-box" => [Boxes(Lines("a0", "a1"), outerKeeps: true), j, Paragraph("z")],
            "fr-kwn-pad-one-box" => [Block([Lines("a0", "a1", "a2").KeepWithNext()]).PaddingBottom(27.Pt()), j, Paragraph("z")],
            "fr-kwn-pad-then-kept-para" => [lead, j.KeepTogether(), Paragraph("z")],
            "fr-kwn-pad-on-outer-box-then-bordered-box" => [Boxes(Lines("a0", "a1"), outerKeeps: true), bordered, Paragraph("z")],
            "fr-kwn-pad-no-kwn-control" => [Boxes(Lines("a0", "a1")), j, Paragraph("z")],
            _ => [lead, j, Paragraph("z")],
        };
    }

    [Theory]
    [InlineData("fr-kwn-pad-then-bordered-box-widows4", 2, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 |  | j0 j1 | j2 z")]
    [InlineData("fr-kwn-pad-then-para", 2, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 |  | j0 j1 j2 | z")]
    [InlineData("fr-kwn-pad-on-inner-box", 2, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 |  | j0 j1 j2 | z")]
    [InlineData("fr-kwn-pad-then-kept-para", 1, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 |  | j0 j1 j2 | z")]
    [InlineData("fr-kwn-pad-no-padding-control", 2, "a0 j0 | j1 j2 z", "a0 j0 | j1 j2 z")]
    [InlineData("fr-kwn-pad-on-outer-box", 2, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 | j0 | j1 j2 z")]
    [InlineData("fr-kwn-pad-one-box", 2, "a0 a1 | a2 | j0 j1 j2 | z", "a0 a1 | a2 | j0 j1 j2 | z")]
    [InlineData("fr-kwn-pad-on-outer-box-then-bordered-box", 2, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 | j0 | j1 j2 z")]
    [InlineData("fr-kwn-pad-orphans1", 1, "a0 | a1 | j0 | j1 j2 z", "a0 | a1 | j0 | j1 j2 z")]
    [InlineData("fr-kwn-pad-no-kwn-control", 2, "a0 | a1 |  | j0 j1 j2 | z", "a0 | a1 |  | j0 j1 j2 | z")]
    public void Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own(string caseName, int orphans, string engine, string chromium)
    {
        // Page area 54 pt (three lines). a1 ends page 2 and the 27 pt of the outer box's bottom padding start page 3. The
        // engine holds the keep-with-next of the paragraph a0 a1 across that padding, so it breaks j against its orphans
        // (or its widows, or its keep) after j0 on page 3; Chromium breaks before j and page 3 holds only the padding. Where
        // the keep-with-next is on the outer box, with orphans 1, without padding or without the keep, both print the same.
        BothAre(caseName, engine, chromium, Engine(78, orphans, KeepWithNextAcrossPadding(caseName)));
    }

    [Theory]
    [InlineData("fr-moved-para-widows-4", 4, "i0 i1 | a0 a1 | a2 a3 a4", "i0 i1 | a0 a1 a2 | a3 a4")]
    [InlineData("fr-moved-para-widows-3-control", 3, "i0 i1 | a0 a1 | a2 a3 a4", "i0 i1 | a0 a1 | a2 a3 a4")]
    public void A_paragraph_that_moved_to_a_new_page_whole_counts_only_its_orphans_before_a_break_there(string caseName, int widows, string engine, string chromium)
    {
        // Page area 54 pt (three lines), orphans 2. One line of a0 … a4 would fit below i0 i1, fewer than its orphans, so
        // the paragraph moves to page 2 whole and breaks there. The engine counts only its orphans on that page and keeps
        // two lines so that three follow; Chromium counts its widows there too, and where they are more than the page
        // holds (4) it breaks where the page is full. With widows 3 both print the same.
        BothAre(caseName, engine, chromium, Engine(78, 2, Lines("i0", "i1"), Block([Lines("a0", "a1", "a2", "a3", "a4").Widows(widows)])));
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

    [Theory]
    [InlineData("fr-row-later-page-padding-overflows", false)]
    [InlineData("fr-row-later-page-padding-overflows-two-cells", true)]
    public void A_row_that_starts_a_later_page_and_is_taller_than_it_through_its_padding_is_cut(string caseName, bool twoCells)
    {
        // Page 1 is full with "i" and the first row; the second row, seven lines with 12 pt top and bottom padding, is 0.75 pt
        // taller than the 150 pt page area with its grid lines, and starts page 2. The engine cuts it before w7; Chromium
        // prints all its lines on page 2 and lets the padding overflow.
        var padded = Cell([Lines("w1", "w2", "w3", "w4", "w5", "w6", "w7")]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()).PaddingLeft(3.Pt()).PaddingRight(3.Pt());
        var first = Cell([Lines("a0", "a1", "a2", "a3", "a4", "a5", "a6")]);
        var table = twoCells
            ? Grid(Table([Fill(), Fill()], [TableRow([first, Cell("b")]), TableRow([padded, Cell([Lines("v1", "v2", "v3", "v4", "v5", "v6")]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()).PaddingLeft(3.Pt()).PaddingRight(3.Pt())])]))
            : Grid(Table([Fill()], [TableRow([first]), TableRow([padded])]));
        var (engine, chromium) = twoCells
            ? ("i a0 b a1 a2 a3 a4 a5 a6 | w1 v1 w2 v2 w3 v3 w4 v4 w5 v5 w6 v6 | w7 z", "i a0 b a1 a2 a3 a4 a5 a6 | w1 v1 w2 v2 w3 v3 w4 v4 w5 v5 w6 v6 w7 | z")
            : ("i a0 a1 a2 a3 a4 a5 a6 | w1 w2 w3 w4 w5 w6 | w7 z", "i a0 a1 a2 a3 a4 a5 a6 | w1 w2 w3 w4 w5 w6 w7 | z");
        BothAre(caseName, engine, chromium, Engine(174, 3, 1, Paragraph("i"), table, Paragraph("z")));
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
        // Same pages; under the default CutRowAlignment.RestAlignedInItsPart the engine aligns the rest of the shorter
        // cell in the row's rest, Chromium puts it at the top: a3 is 36 pt (middle) or 72 pt (bottom) lower in the engine
        // (TopOnEveryPage follows Chromium, CutRowAlignmentTests).
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
        // The child's own keep, applied to an early break inside it (FlowLayout), which no other test holds: the only better-looking break lies inside the kept paragraph, and its
        // keep makes it no better than the natural break before q. Same pages as Chromium.
        var engine = Engine(114, 1, Lines("a0", "a1", "a2", "a3").KeepWithNext().KeepTogether(), Paragraph("h0").KeepWithNext(), Lines("q0", "q1", "q2").KeepTogether());
        engine.Should().Be(Chromium("fr-own-avoid-early-break"));
    }
}
