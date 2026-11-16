using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// The grid lines at a page break INSIDE a row. With <see cref="BoxDecorationBreak.Clone"/> (the code front-end's
/// default, as word processors print tables) the page ends with the whole line below the cut row, under the row's cut
/// content, and the next page starts with the whole line above the row, over the rest of its content: the table is
/// closed on both pages. With <see cref="BoxDecorationBreak.Slice"/> nothing is drawn at the cut (CSS Fragmentation 3
/// §5.4: a box cut by a break gets no border at the cut). The tables hold one row of 40 one-word lines between short
/// rows, every line 2 pt wide, after an empty block that moves the table down; with a header row repeated on each page.
/// </summary>
public class TableRowCutLineTests
{
    private const double Epsilon = 1e-9;

    private const double Line = 2;

    private const double PageHeight = 150;

    private static readonly BoxStyle Lined = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(Line.Pt(), Color.Black)) };

    /// <summary>The words of the long row's cell: "l00" … "l39".</summary>
    private static readonly string[] LongWords = [.. Enumerable.Range(0, 40).Select(index => $"l{index:00}")];

    /// <summary>With <paramref name="empty"/> set, every row has a third cell, without content of that kind
    /// (<see cref="TableModels.EmptyKinds"/>).</summary>
    private static StyledDocument Document(BoxDecorationBreak mode, double offset, bool header, string? empty = null)
    {
        var t = new TableModels();
        Box Cell(params string[] lines) => t.Lines(lines) with { Style = Lined };
        Box Row(params Box[] cells) => empty is null ? t.Row(cells) : t.Row([.. cells, t.Empty(empty, Lined)]);
        ValueList<TrackSize> columns = empty is null ? [new TrackSize.Fill(1), new TrackSize.Fill(1)] : [new TrackSize.Fill(1), new TrackSize.Fill(1), new TrackSize.Fill(1)];
        var body = t.Body(Row(Cell("a"), Cell("b")), Row(Cell(LongWords), Cell("x")), Row(Cell("c"), Cell("d")));
        var table = header
            ? t.Table(columns, t.Head(Row(Cell("h1"), Cell("h2"))), body)
            : t.Table(columns, body);
        var spacer = new Box(t.Next(), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), offset.Pt(), 0.Pt()) }, [], Models.At(0));
        return TableModels.Page(280, PageHeight, spacer, table with { Style = table.Style with { DecorationBreak = mode } });
    }

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    /// <summary>Every word of the fragment with its top and bottom, from the fragment's own top.</summary>
    private static IEnumerable<(string Word, double Top, double Bottom)> Words(Fragment fragment, double top) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment, top + child.Offset.Y)),
        LineFragment line => line.Children.SelectMany(child => child.Fragment is TextFragment text
            ? [(string.Concat(text.Glyphs.Select(glyph => glyph.Text)), top, top + line.Size.Height)]
            : Enumerable.Empty<(string, double, double)>()),
        _ => [],
    };

    /// <summary>The horizontal rules across the whole table, with their top and bottom.</summary>
    private static List<(double Top, double Bottom)> WideRules(BoxFragment table) =>
        [.. table.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > 200)
            .Select(child => (child.Offset.Y, child.Offset.Y + child.Fragment.Size.Height))];

    private static List<BoxFragment> Tables(StyledDocument document)
    {
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        return [.. pages.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box)];
    }

    [Theory]
    [InlineData(BoxDecorationBreak.Clone, false)]
    [InlineData(BoxDecorationBreak.Clone, true)]
    [InlineData(BoxDecorationBreak.Slice, false)]
    [InlineData(BoxDecorationBreak.Slice, true)]
    public void A_cut_inside_a_row_closes_the_table_on_both_pages_with_clone_and_draws_nothing_with_slice(BoxDecorationBreak mode, bool header) =>
        LinesAtTheCut(mode, header, empty: null);

    /// <summary>The same sweep with a cell without content in every row (spot check of fix round 3, SC-1): the lines
    /// at the cut are the same, and no row part on a page is an empty strip.</summary>
    [Theory]
    [MemberData(nameof(EmptyCellCases))]
    public void A_cut_inside_a_row_beside_an_empty_cell_closes_the_table_and_leaves_no_empty_strip(string empty, BoxDecorationBreak mode, bool header) =>
        LinesAtTheCut(mode, header, empty);

    public static TheoryData<string, BoxDecorationBreak, bool> EmptyCellCases()
    {
        var cases = new TheoryData<string, BoxDecorationBreak, bool>();
        foreach (var empty in TableModels.EmptyKinds)
        {
            foreach (var mode in new[] { BoxDecorationBreak.Clone, BoxDecorationBreak.Slice })
            {
                cases.Add(empty, mode, false);
                cases.Add(empty, mode, true);
            }
        }

        return cases;
    }

    private static void LinesAtTheCut(BoxDecorationBreak mode, bool header, string? empty)
    {
        for (var offset = 0.0; offset < 40; offset += 0.25)
        {
            var because = $"{mode}, header {header}, empty cell {empty ?? "none"}, offset {offset}";
            var tables = Tables(Document(mode, offset, header, empty));
            var words = tables.Select(table => Words(table, 0).ToList()).ToList();
            var all = words.SelectMany(page => page.Select(word => word.Word)).ToList();
            all.Where(word => !word.StartsWith('h')).Should().BeEquivalentTo(["a", "b", .. LongWords, "x", "c", "d"], because + ": every word once");
            all.Where(word => word.StartsWith('l')).Should().Equal(LongWords, because + ": the long row's lines in order");

            for (var page = 0; page + 1 < tables.Count; page++)
            {
                var last = words[page].Where(word => word.Word.StartsWith('l')).Select(word => word.Word).LastOrDefault();
                var next = words[page + 1].FirstOrDefault(word => word.Word.StartsWith('l'));
                if (last is null || next.Word is null || last == LongWords[^1] || words[page + 1].Any(word => word.Word == "a"))
                {
                    continue;
                }

                // The long row is cut between page and page + 1.
                var bottom = words[page].Where(word => word.Word.StartsWith('l')).Max(word => word.Bottom);
                var below = WideRules(tables[page]).Where(rule => rule.Top >= bottom - Epsilon).ToList();
                var above = WideRules(tables[page + 1]).Where(rule => rule.Bottom <= next.Top + Epsilon).ToList();
                if (mode == BoxDecorationBreak.Clone)
                {
                    below.Should().ContainSingle(because + $": page {page + 1} ends with one line under the cut content");
                    (below[0].Bottom - below[0].Top).Should().BeApproximately(Line, Epsilon, because + ": the whole line ends the page");
                    below[0].Bottom.Should().BeApproximately(tables[page].Size.Height, Epsilon, because + ": the line closes the table's part");
                    above.Should().NotBeEmpty(because + $": page {page + 2} starts the rest of the row under a line");
                    (above[^1].Bottom - above[^1].Top).Should().BeApproximately(Line, Epsilon, because + ": the whole line starts the rest of the row");
                }
                else
                {
                    below.Should().BeEmpty(because + ": slice draws no line at a cut inside a row");
                    if (!header)
                    {
                        above.Should().BeEmpty(because + ": nor above the rest of the row");
                    }
                }
            }

            foreach (var table in tables)
            {
                foreach (var (row, rowAt) in Boxes(table, new Point(0, 0), SemanticRole.TableRow).Where(row => row.Box.Size.Height > Epsilon))
                {
                    Words(row, 0).Should().NotBeEmpty(because + $": the row part at {rowAt.Y} ({row.Size.Height} high) shows text");
                }

                WideRules(table).Where(rule => rule.Top < -Epsilon || rule.Bottom > table.Size.Height + Epsilon).Should().BeEmpty(because + ": no rule outside the table's box");
                table.Size.Height.Should().BeLessThanOrEqualTo(PageHeight + Epsilon, because + ": the table's part fits the page area");
            }
        }
    }

    [Fact]
    public void With_clone_a_long_row_cut_at_the_page_end_fills_the_page_down_to_the_bottom_of_its_line()
    {
        // No offset, no header: page 1 holds "a b", the line below them, and the long row's first part; its cut content
        // ends half a line above the page end, where the whole 2 pt line below the row is centred one point above it.
        var tables = Tables(Document(BoxDecorationBreak.Clone, 0, header: false));

        tables[0].Size.Height.Should().BeApproximately(PageHeight, 0.5, "the cut leaves at most the rest of one text line free");
        var rules = WideRules(tables[0]);
        rules[^1].Bottom.Should().BeApproximately(tables[0].Size.Height, Epsilon);
        WideRules(tables[1])[0].Should().Be((0.0, Line), "page 2 starts with the whole line above the long row");
        Words(tables[1], 0).First().Top.Should().BeGreaterThanOrEqualTo(Line, "the rest of the row starts below that line");
    }

    /// <summary>The horizontal rules of a table part, each as its left edge, width, height and colour.</summary>
    private static List<(double X, double Width, double Height, Color Color)> Segments(BoxFragment table) =>
        [.. table.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > rule.Size.Height)
            .OrderBy(child => child.Offset.X)
            .Select(child => (child.Offset.X, child.Fragment.Size.Width, child.Fragment.Size.Height, ((RuleFragment)child.Fragment).Color))];

    /// <summary>
    /// Spot check of fix round 3, SC-2a. No grid lines; only the long row's cells have a top border, and not the same
    /// one: 4 pt black over the first column, 1 pt red over the second. With clone the row cut inside itself starts again
    /// under the line above it, resolved with the row's own cells as on the page it started on: the same segments on
    /// every page the row continues on, whether a repeated header row comes first or not (one rule for the line above
    /// a row). Nothing else in the table draws a line, so each page shows exactly that line.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_clone_the_line_over_a_continued_row_is_resolved_with_the_row_s_own_top_borders(bool header)
    {
        var t = new TableModels();
        var plain = TableModels.ReferenceCell with { Border = Border.None };
        Box Cell(BoxStyle style, params string[] lines) => t.Lines(lines) with { Style = style };
        var black = plain with { Border = Border.None with { Top = new BorderSide(4.Pt(), Color.Black) } };
        var red = plain with { Border = Border.None with { Top = new BorderSide(1.Pt(), new Color(0xFF, 0, 0)) } };
        var body = t.Body(t.Row(Cell(plain, "a"), Cell(plain, "b")), t.Row(Cell(black, LongWords), Cell(red, "x")), t.Row(Cell(plain, "c"), Cell(plain, "d")));
        var table = header
            ? t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Head(t.Row(Cell(plain, "h1"), Cell(plain, "h2"))), body)
            : t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], body);

        var tables = Tables(TableModels.Page(280, PageHeight, table with { Style = table.Style with { DecorationBreak = BoxDecorationBreak.Clone } }));

        var continued = tables.Skip(1).Where(part => Words(part, 0).Any(word => word.Word.StartsWith('l'))).ToList();
        continued.Should().NotBeEmpty("the long row continues on page 2");
        var first = Segments(tables[0]);
        first.Select(segment => (segment.Height, segment.Color)).Should().Equal((4.0, Color.Black), (1.0, new Color(0xFF, 0, 0)));
        foreach (var part in continued)
        {
            Segments(part).Should().Equal(first, "the continued row starts under the same line as on its first page");
        }
    }
}
