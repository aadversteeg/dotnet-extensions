using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Page and table rules that no other test pins. Each test builds the one situation in which the rule decides the
/// outcome.
/// </summary>
public class PageAndTableRuleTests
{
    // One word per line; orphans and widows 1, so where a page breaks depends only on the rules these tests are about.
    private static readonly BoxStyle Narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()), Orphans = 1, Widows = 1 };

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, Func<Box, bool> wanted) =>
        fragment is BoxFragment box
            ? (wanted(box.Box) ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, wanted)))
            : [];

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(PageFragment page, Func<Box, bool> wanted) =>
        Boxes(page.Root.Fragment, page.Root.Offset, wanted);

    private static IEnumerable<(LineFragment Line, Point At)> Lines(Fragment fragment, Point at) => fragment switch
    {
        LineFragment line => [(line, at)],
        BoxFragment box => box.Children.SelectMany(child => Lines(child.Fragment, at + child.Offset)),
        _ => [],
    };

    private static string TextOf(LineFragment line) =>
        string.Concat(line.Children.Select(child => child.Fragment).OfType<TextFragment>().SelectMany(text => text.Glyphs).Select(glyph => glyph.Text));

    [Fact]
    public void The_root_s_top_margin_is_on_the_first_page_only()
    {
        // CSS Fragmentation 3 §5.4 (box-decoration-break: slice): the top margin belongs to the first fragment; a
        // continuation starts at the top of the page area. 12 one-word lines of 12 pt, a 20 pt top margin, 80 pt pages:
        // page 1 holds 5 lines (20 + 60), page 2 holds 6 (72), page 3 the last one.
        var document = Document(Paragraph(1, Words(12), Narrow));
        document = document with { Root = document.Root with { Style = BoxStyle.Initial with { Margin = new Edges(20.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) } } };

        var pages = Paginate(document);

        pages.Select(page => Lines(page.Root.Fragment, page.Root.Offset).Count()).Should().Equal(5, 6, 1);
        Lines(pages[1].Root.Fragment, pages[1].Root.Offset).First().At.Y.Should().BeApproximately(10, 1e-9, "the top of page 2's page area");
    }

    [Fact]
    public void A_paragraph_of_only_a_page_counter_has_a_line_box_and_stops_margin_collapsing()
    {
        // CSS 2.1 §9.4.2: a line box holding content is not a zero-height line box, and §8.3.1: only a box without line
        // boxes lets its top and bottom margins collapse through it. The counter shows the digit 1, so the paragraph
        // is 12 pt high and both of its 10 pt margins stay: "b" starts at 12 + 10 + 12 + 10 = 44.
        var gap = (double top, double bottom) => BoxStyle.Initial with { Margin = new Edges(top.Pt(), 0.Pt(), bottom.Pt(), 0.Pt()) };
        var counter = new Box(new NodeId(2), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, gap(10, 10),
            [new CounterRun(new NodeId(102), PageCounter.PageNumber, Text(), At(0))], At(0));

        var page = Paginate(Document(Paragraph(1, "a", gap(0, 10)), counter, Paragraph(3, "b", gap(10, 0)))).Single();

        var lines = Lines(page.Root.Fragment, new Point(0, 0)).ToList();
        lines.Select(line => TextOf(line.Line)).Should().Equal("a", "1", "b");
        lines.Select(line => line.At.Y).Should().Equal(0, 22, 44);
    }

    [Fact]
    public void A_fixed_height_area_whose_content_and_bottom_margin_exceed_it_is_an_error()
    {
        // The area's content keeps its margins inside the area (a new formatting context, CSS 2.1 §8.3.1): one 12 pt line
        // and a 10 pt bottom margin need 22 pt, more than the fixed 20 pt.
        var content = Flow(50, [Paragraph(51, "Head")], BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), 10.Pt(), 0.Pt()) });
        var tooSmall = new PageTemplate(new PageArea(content, 20.Pt()), null);
        var fits = new PageTemplate(new PageArea(content, 22.Pt()), null);
        var document = Document(Paragraph(1, "Body"));

        var error = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document with { Page = document.Page with { Template = tooSmall } }).Error;

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        Paginate(document with { Page = document.Page with { Template = fits } }).Single().Root.Offset.Y.Should().Be(10 + 22);
    }

    [Fact]
    public void An_empty_box_whose_margin_passes_the_page_end_is_put_at_the_page_end()
    {
        // CSS 2.1 §8.3.1: a box whose margins collapse through it has no height and needs no room; where its margin alone
        // would pass the page end it is put at the end of the page and starts no new page.
        var empty = Flow(2, [], BoxStyle.Initial with { Margin = new Edges(100.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) });

        var pages = Paginate(Document(Paragraph(1, Words(5), Narrow), empty));

        pages.Should().ContainSingle();
        var (_, at) = Boxes(pages[0], box => box.Id == new NodeId(2)).Single();
        at.Y.Should().BeApproximately(10 + 80, 1e-9, "the end of the page area");
    }

    [Fact]
    public void A_spanning_cell_that_ends_later_grows_its_row_after_one_that_ends_earlier_even_when_it_starts_first()
    {
        // CSS 2.1 §17.5.3 row heights: each spanning cell, by its last row (the lowest first), grows that row by what is
        // missing. A spans rows 0–3 (6 lines, 76.75 pt), B spans rows 1–2 (4 lines, 52.75 pt); rows 0 and 3 hold one line
        // (16.75 pt). B first: row 2 becomes 52.75 and A already fits (16.75 + 0 + 52.75 + 16.75). In document order A
        // would first grow row 3 by 43.25.
        var t = new TableModels();
        var a = t.Lines(["a1", "a2", "a3", "a4", "a5", "a6"]) with { Style = TableModels.ReferenceCell with { Span = new CellSpan(1, 4) } };
        var b = t.Lines(["b1", "b2", "b3", "b4"]) with { Style = TableModels.ReferenceCell with { Span = new CellSpan(1, 2) } };
        var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body(t.Row(a, t.Cell("c0")), t.Row(b), t.Row(), t.Row(t.Cell("c3"))));

        var page = Paginate(TableModels.Page(180, 400, table)).Single();

        var rows = Boxes(page, box => box.Role == SemanticRole.TableRow).Select(row => row.Box.Size.Height).ToList();
        rows.Should().HaveCount(4);
        rows[3].Should().BeApproximately(rows[0], 1e-9, "row 3 holds one line, like row 0");
    }

    [Fact]
    public void A_header_alone_at_the_end_of_a_page_is_not_taken_even_when_the_break_before_the_table_is_avoided()
    {
        // The header-alone outcome is allowed only at the start of an empty page. Here the break before the table
        // violates break-after: avoid, the same as a header alone would; the table still moves to the next page whole.
        var keep = BoxStyle.Initial with { BreakAfter = BreakBetween.Avoid, Padding = new Edges(36.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) };
        var t = new TableModels();
        var table = t.Table([new TrackSize.Fill(1)], t.Head(t.Row(t.Cell("head"))), t.Body(t.Row(t.Cell("first"))));

        var pages = Paginate(TableModels.Page(180, 48 + 16.75 + 8, Paragraph(100, "filler", keep), table));

        pages.Should().HaveCount(2);
        Boxes(pages[0], box => box.Role == SemanticRole.Table).Should().BeEmpty();
        Boxes(pages[1], box => box.Role == SemanticRole.TableRow).Should().HaveCount(2);
    }

    [Fact]
    public void A_row_group_kept_together_is_not_broken_inside_one_of_its_rows_when_it_can_move_whole()
    {
        // CSS Fragmentation 3 §3.2: break-inside: avoid on a row group applies to every break inside it, inside its rows
        // too. The kept group (a one-line row and a four-line row) moves to page 2 rather than splitting its second row.
        var t = new TableModels();
        var kept = t.Body(BoxStyle.Initial with { BreakInside = BreakInside.Avoid }, t.Row(t.Cell("a")), t.Row(t.Lines(["b1", "b2", "b3", "b4"])));
        var table = t.Table([new TrackSize.Fill(1)], t.Body(t.Row(t.Cell("first"))), kept);

        var pages = Paginate(TableModels.Page(180, 80, table));

        pages.Select(page => Boxes(page, box => box.Role == SemanticRole.TableRow).Count()).Should().Equal(1, 2);
    }

    /// <summary>
    /// A forced break before a box's first child is a break before the box itself: "a break-before value on a first
    /// in-flow child box is propagated to its container" (CSS Fragmentation 3 §3.1.1), and a flow breaks only between
    /// its children, never before the first one of a fragment. The first cell of a table's first row passes it on up
    /// to the table (each is its container's first child), which then moves to the next page; any other cell is no
    /// first child, and the break meets no flow that could honour it (cells of a row are side by side). So a hand-made
    /// second cell whose first paragraph has <c>break-before: page</c> is laid out as without it (round-2 mutant N15).
    /// </summary>
    [Fact]
    public void A_forced_break_before_the_first_child_of_a_later_cell_is_the_cell_s_own_and_breaks_nothing()
    {
        var t = new TableModels();
        var paragraph = new Box(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial with { BreakBefore = BreakBetween.Page },
            [new TextRun(t.Next(), "second", Models.Text(), Models.At(0))], Models.At(0));
        var broken = new Box(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, TableModels.ReferenceCell, [paragraph], Models.At(0));
        var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body(t.Row(t.Cell("first"), broken)));
        var before = Models.Paragraph(90, "before", BoxStyle.Initial);

        var pages = Paginate(TableModels.Page(280, 150, before, table));

        pages.Should().ContainSingle("the cell's break has nothing to break between");
        Lines(pages[0].Root.Fragment, new Point(0, 0)).Select(line => TextOf(line.Line)).Should().Equal("before", "first", "second");
    }

    /// <summary>
    /// A row whose text cell has no line on the page yet moves to the next page whole, also where an empty block with
    /// padding and a background beside it fits: cutting the row there would break the text cell before its first line, a
    /// last resort, and the break before the row is perfect. Chromium prints the same (cases <c>c-cell-first-line</c> and
    /// <c>fr-cell-first-line-block-fits</c>, Fluent <c>OrphansWidowsTests</c>). Milestone 1 cut the row there and ended the
    /// page with the coloured block alone (probe run 35, SP-1).
    /// </summary>
    [Fact]
    public void A_row_whose_cell_has_no_line_on_the_page_yet_moves_whole_although_a_coloured_block_beside_it_fits()
    {
        // A cell holding only a padded coloured block (6 pt) beside a cell "x" (12 pt). Where the block fits on the page
        // but the line does not, cutting the row would break the text cell before its first line, a last resort; the
        // break before the row is perfect, so the row moves to the next page whole. Chromium prints the same
        // (tools/reference/chromium, case c-cell-first-line). (Milestone 1 cut the row there and placed the block alone.)
        var blue = new Color(0x20, 0x40, 0xC0);
        var moved = 0;
        for (var offset = 120.0; offset < 150; offset += 0.5)
        {
            var t = new TableModels();
            var block = new Box(t.Next(), SemanticRole.Generic, LayoutAlgorithmId.Flow,
                BoxStyle.Initial with { Padding = new Edges(3.Pt(), 0.Pt(), 3.Pt(), 0.Pt()), Background = blue }, [], Models.At(0));
            var cells = new Box(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, BoxStyle.Initial, [block], Models.At(0));
            var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body(t.Row(t.Cell("x", BoxStyle.Initial), cells)));
            // A paragraph before the table, its bottom margin moving the table down (as the spot check's probes do).
            var intro = Models.Paragraph(900, "intro", BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), (offset - 12).Pt(), 0.Pt()) });

            var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), TableModels.Page(280, 150, intro, table));
            result.IsSuccess.Should().BeTrue($"offset {offset}: " + (result.IsFailure ? result.Error.ToString() : string.Empty));
            var pages = result.Value;

            var text = Lines(pages[0].Root.Fragment, new Point(0, 0)).Any(line => TextOf(line.Line) == "x");
            Boxes(pages[0], box => box.Id == block.Id).Where(found => found.Box.Size.Height > 0).Should().HaveCount(text ? 1 : 0, $"offset {offset}: the block is on page 1 only with the text beside it");
            moved += text ? 0 : 1;
        }

        moved.Should().BePositive("where the text line does not fit, the row moves to page 2 whole");
    }
}
