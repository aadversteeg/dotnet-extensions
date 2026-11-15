using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.PageModel;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;
using static UnitTests.Ave.Documents.Layout.Models;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// Rules that no other test pinned: the review's mutation run changed each of them without a test failing.
/// </summary>
public class LayoutRuleTests
{
    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    [Fact]
    public void Runs_of_two_font_sizes_on_one_line_share_one_baseline()
    {
        // CSS 2.1 §10.8: vertical-align: baseline puts every inline box's baseline on the line's baseline.
        var small = new TextRun(new NodeId(2), "small ", Text(10), At(0));
        var large = new TextRun(new NodeId(3), "LARGE", Text(20), At(0));
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [small, large], At(0));

        var page = Paginate(Document(paragraph)).Single();

        var line = (LineFragment)((BoxFragment)((BoxFragment)page.Root.Fragment).Children[0].Fragment).Children[0].Fragment;
        var baselines = line.Children.Select(child => child.Offset.Y + ((TextFragment)child.Fragment).Ascent).ToList();
        baselines.Should().HaveCount(2);
        baselines[0].Should().BeApproximately(baselines[1], 1e-9);
        line.Children[0].Offset.Y.Should().BeGreaterThan(line.Children[1].Offset.Y, "the smaller text starts lower");
    }

    [Fact]
    public void A_glyph_raised_by_shaping_is_drawn_above_the_baseline_on_the_page()
    {
        // The shaper's offset is in font convention (y up); the page model's y grows downwards, so a raised mark gets
        // a negative y.
        var text = "A" + char.ConvertFromUtf32(0x0301);
        var shaped = Shaper.Shape(text, TestFonts.Regular, 10).Value;
        shaped[1].YOffset.Should().BeGreaterThan(0, "Lato raises the acute over a capital");

        var page = DocumentLayout.Run(Document(Paragraph(1, text)), TestFonts.Set).Value.Pages.Single();

        var run = page.Items.OfType<GlyphRun>().Single();
        run.Glyphs[1].Y.Should().BeApproximately(-shaped[1].YOffset, 1e-9);
    }

    [Fact]
    public void A_cell_spanning_auto_columns_widens_each_by_an_equal_share()
    {
        // CSS 2.1 §17.5.2.2 leaves the distribution open; the engine widens the spanned flexible columns equally.
        var columns = new ValueList<TrackSize>([TrackSize.Auto.Instance, TrackSize.Auto.Instance]);
        var ranges = TrackSizing.ColumnRanges(columns,
        [
            new TrackSizing.Contribution(0, 1, new IntrinsicWidths(10, 20)),
            new TrackSizing.Contribution(1, 1, new IntrinsicWidths(10, 20)),
            new TrackSizing.Contribution(0, 2, new IntrinsicWidths(100, 200)),
        ]);

        ranges.Select(range => (range.MinContent, range.MaxContent)).Should().Equal((50, 100), (50, 100));
    }

    [Fact]
    public void Fill_weights_that_sum_below_one_take_only_their_share()
    {
        // CSS Grid 1 §11.7.1: when the flex factors sum to less than 1, each fr is a share of the whole leftover space
        // as if the sum were 1, so the columns leave space unused.
        var columns = new ValueList<TrackSize>([new TrackSize.Fill(0.25), new TrackSize.Fill(0.25)]);

        var (widths, needed) = TrackSizing.Resolve(columns, [new IntrinsicWidths(0, 0), new IntrinsicWidths(0, 0)], 400);

        needed.Should().BeNull();
        widths.Should().Equal(100, 100);
    }

    [Fact]
    public void A_cell_border_wins_over_a_row_border_which_wins_over_the_table_border_of_equal_width()
    {
        // CSS 2.1 §17.6.2.1, rule 4: "If border styles differ only in color, then a style set on a cell wins over one on a
        // row, which wins over a row group, column, column group and, lastly, table."
        var t = new TableModels();
        var line = (Color color) => Border.All(new BorderSide(1.Pt(), color));
        var red = new Color(255, 0, 0);
        var green = new Color(0, 255, 0);
        var blue = new Color(0, 0, 255);
        var cell = BoxStyle.Initial with { Border = line(blue) };
        var table = t.Table([new TrackSize.Fill(1)], t.Body(t.Row(BoxStyle.Initial with { Border = line(green) }, t.Cell("a", cell)), t.Row(t.Cell("b"))))
            with { Style = BoxStyle.Initial with { Columns = [new TrackSize.Fill(1)], Border = line(red) } };

        var page = Paginate(TableModels.Page(180, 80, table)).Single();

        var (tableBox, _) = Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single();
        var rules = tableBox.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > rule.Size.Height).OrderBy(child => child.Offset.Y).ToList();
        ((RuleFragment)rules[1].Fragment).Color.Should().Be(blue, "the line between the rows: the cell's bottom border");
        ((RuleFragment)rules[0].Fragment).Color.Should().Be(blue, "the top line: the cell beats its row and the table");
    }

    [Fact]
    public void A_header_that_fits_but_leaves_no_room_for_a_body_row_moves_to_the_next_page_with_the_table()
    {
        // A break right after the header rows, with no body row on the page, is no breakpoint, so a header is never
        // left alone at a page end (only at the start of an empty page, where moving on gains nothing).
        var t = new TableModels();
        ValueList<Node> filler = [.. Enumerable.Range(0, 4).Select(i => (Node)Paragraph(100 + i, $"line {i}"))];
        var table = t.Table([new TrackSize.Fill(1)], t.Head(t.Row(t.Cell("head"))), t.Body(t.Row(t.Cell("first"))));

        var pages = Paginate(TableModels.Page(180, 48 + 16.75 + 8, [.. filler, table]));

        pages.Should().HaveCount(2);
        Boxes(pages[0].Root.Fragment, new Point(0, 0), SemanticRole.Table).Should().BeEmpty();
        Boxes(pages[1].Root.Fragment, new Point(0, 0), SemanticRole.TableRow).Should().HaveCount(2);
    }

    [Fact]
    public void Spanning_cells_grow_their_rows_in_the_order_of_their_last_row()
    {
        // Row heights (CSS 2.1 §17.5.3): single-row cells first, then each spanning cell — by its last row, the lowest
        // first — grows its last row by what is missing. A spans rows 0–1 (4 lines), B spans rows 1–2 (3 lines): A grows
        // row 1, after which B already fits and row 2 keeps its single line. In the other order row 2 grows too.
        var t = new TableModels();
        var a = t.Lines(["a1", "a2", "a3", "a4"]) with { Style = TableModels.ReferenceCell with { Span = new CellSpan(1, 2) } };
        var b = t.Lines(["b1", "b2", "b3"]) with { Style = TableModels.ReferenceCell with { Span = new CellSpan(1, 2) } };
        var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body(t.Row(a, t.Cell("c0")), t.Row(b), t.Row(t.Cell("c2"))));

        var page = Paginate(TableModels.Page(180, 400, table)).Single();

        var rows = Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.TableRow).Select(row => row.Box.Size.Height).ToList();
        rows[2].Should().BeApproximately(rows[0], 1e-9, "row 2 holds one line, like row 0");
    }

    [Fact]
    public void A_break_after_avoid_on_a_last_child_is_seen_at_its_parent_s_edge()
    {
        // CSS Fragmentation 3 §3.1.1: break-after on the last in-flow child propagates to its container.
        var keep = BoxStyle.Initial with { BreakAfter = BreakBetween.Avoid };
        var block = Flow(1, [Paragraph(2, "a"), Paragraph(3, "b", keep)]);

        BreakAvoidance.Between(block, Paragraph(4, "c")).Should().Be(BreakAppeal.ViolatesAvoid);
        BreakAvoidance.Between(Flow(5, [Paragraph(6, "a")]), Paragraph(7, "c")).Should().Be(BreakAppeal.Perfect);
    }
}
