using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Which line paints a corner where the grid lines of a collapsed table meet, and where the other lines stop. CSS 2.1
/// §17.6.2.1 resolves the borders of one edge and leaves the corners to the implementation; the engine follows
/// Chromium 159's print (tools/reference/chromium, cases <c>tj-*</c>): the line that wins at the corner (wider; then
/// cell over row over row group over table; then the box earlier in the table) paints it, and so does every other line
/// as wide from the same box; where those differ in colour the one Chromium paints last shows; every other line stops
/// at the corner's near side. At a break between rows the vertical lines end and start at the line's centre, the page's
/// edge. Each test reads the colour the page shows at a point, the last rule over it in painting order.
/// </summary>
public class CollapsedJointTests
{
    private const double Thin = 1.5;

    private static readonly Color Red = new(255, 0, 0);
    private static readonly Color Green = new(0, 128, 0);
    private static readonly Color Blue = new(0, 0, 255);
    private static readonly Color Magenta = new(255, 0, 255);
    private static readonly Color Orange = new(255, 128, 0);
    private static readonly Color Cyan = new(0, 255, 255);

    private static BorderSide Line(double width, Color colour) => new(width.Pt(), colour);

    private static readonly BoxStyle Lined = TableModels.ReferenceCell with { Border = Border.All(Line(Thin, Color.Black)) };

    private static BoxStyle Coloured(Color colour) => Lined with { Border = Border.All(Line(Thin, colour)) };

    /// <summary>A table of <paramref name="columns"/> equal columns and <paramref name="rows"/> one-line rows, on a page
    /// area 200 pt wide; <paramref name="style"/> gives the style of cell (row, column).</summary>
    private static StyledDocument Table(int rows, int columns, Func<int, int, BoxStyle> style, double height = 400, BoxStyle? table = null)
    {
        var t = new TableModels(10.5);
        var body = t.Body([.. Enumerable.Range(0, rows).Select(row => t.Row([.. Enumerable.Range(0, columns).Select(column => t.Cell($"{(char)('a' + column)}{row}", style(row, column)))]))]);
        var box = t.Table([.. Enumerable.Range(0, columns).Select(_ => (TrackSize)new TrackSize.Fill(1))], body);
        return TableModels.Page(200, height, table is null ? box : box with { Style = table with { Columns = box.Style.Columns } });
    }

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static BoxFragment TableOn(PageFragment page) => Find(page.Root.Fragment, SemanticRole.Table).Single();

    private static IEnumerable<BoxFragment> Find(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Find(child.Fragment, role)))
            : [];

    private static List<(double X0, double Y0, double X1, double Y1, Color Colour)> Rules(PageFragment page) =>
        [.. TableOn(page).Children
            .Where(child => child.Fragment is RuleFragment)
            .Select(child => (child.Offset.X, child.Offset.Y, child.Offset.X + child.Fragment.Size.Width, child.Offset.Y + child.Fragment.Size.Height, ((RuleFragment)child.Fragment).Color))];

    /// <summary>The colour shown at a point of the table's border box: the last rule over it, or null.</summary>
    private static Color? At(PageFragment page, double x, double y) =>
        Rules(page).LastOrDefault(rule => rule.X0 <= x && x < rule.X1 && rule.Y0 <= y && y < rule.Y1) is { Y1: > 0 } found ? found.Colour : null;

    /// <summary>The centres of the vertical lines (x) and of the horizontal lines (y), in order.</summary>
    private static (List<double> X, List<double> Y) Centres(PageFragment page)
    {
        var rules = Rules(page);
        static double Rounded(double value) => Math.Round(value, 6);
        return (
            [.. rules.Where(rule => rule.Y1 - rule.Y0 > rule.X1 - rule.X0).Select(rule => Rounded((rule.X0 + rule.X1) / 2)).Distinct().Order()],
            [.. rules.Where(rule => rule.X1 - rule.X0 > rule.Y1 - rule.Y0).Select(rule => Rounded((rule.Y0 + rule.Y1) / 2)).Distinct().Order()]);
    }

    [Fact]
    public void Of_two_equal_lines_of_one_cell_the_one_Chromium_paints_last_shows_at_the_corner()
    {
        // tj-same-cell: every cell's top blue, left red, right green, bottom magenta. Top left: the top line over the
        // left one (blue); top middle: a0's right line over its top (green); middle: a0's bottom over its right
        // (magenta); bottom middle: a1's bottom over its right (magenta).
        var page = Paginate(Table(2, 2, (_, _) => Lined with { Border = new Border(Line(Thin, Blue), Line(Thin, Green), Line(Thin, Magenta), Line(Thin, Red)) })).Single();
        var (x, y) = Centres(page);

        At(page, x[0], y[0]).Should().Be(Blue);
        At(page, x[1], y[0]).Should().Be(Green);
        At(page, x[1], y[1]).Should().Be(Magenta);
        At(page, x[0], y[1]).Should().Be(Magenta);
        At(page, x[1], y[2]).Should().Be(Magenta);
        At(page, x[2], y[0]).Should().Be(Green);
    }

    [Fact]
    public void Of_equal_lines_of_different_cells_the_cell_earlier_in_the_table_paints_the_corner()
    {
        // tj-order, table t: a0 has no right and no bottom border, so the middle corner is met by b0 (red, above right)
        // and a1 (blue, below left): b0, first in the table, paints it. Table t2: no line above the middle corner; a1
        // (blue, left) wins over b1 (green, right).
        var none = BorderSide.None;
        var t = Paginate(Table(2, 2, (row, column) => (row, column) switch
        {
            (0, 0) => Lined with { Border = Lined.Border with { Right = none, Bottom = none } },
            (0, 1) => Coloured(Red),
            (1, 0) => Coloured(Blue),
            _ => Coloured(Green),
        })).Single();
        var t2 = Paginate(Table(2, 2, (row, column) => (row, column) switch
        {
            (0, 0) => Lined with { Border = Lined.Border with { Right = none, Bottom = none } },
            (0, 1) => Coloured(Red) with { Border = Coloured(Red).Border with { Left = none, Bottom = none } },
            (1, 0) => Coloured(Blue) with { Border = Coloured(Blue).Border with { Right = none } },
            _ => Coloured(Green),
        })).Single();

        var (x, y) = Centres(t);
        At(t, x[1], y[1]).Should().Be(Red);
        At(t, x[0], y[1]).Should().Be(Color.Black, "a0's left line, above, is earlier than a1's");
        var (x2, y2) = Centres(t2);
        At(t2, x2[1], y2[1]).Should().Be(Blue);
    }

    [Fact]
    public void A_cell_s_line_paints_the_corner_over_the_table_s_line_of_the_same_width()
    {
        // tj-cell-table: a 1.5 pt blue table border, a0 without a top border: at the top left the table's line meets
        // a0's left line (a cell's, black), which paints the corner and starts at the table's top edge.
        var page = Paginate(Table(2, 2, (row, column) => row == 0 && column == 0 ? Lined with { Border = Lined.Border with { Top = BorderSide.None } } : Lined,
            table: BoxStyle.Initial with { Border = Border.All(Line(Thin, Blue)) })).Single();
        var (x, y) = Centres(page);

        At(page, x[0], y[0]).Should().Be(Color.Black);
        At(page, x[0], 0.01).Should().Be(Color.Black);
        At(page, x[0] + (Thin / 2) + 0.01, y[0]).Should().Be(Blue, "the table's line starts at the right side of a0's left line");
        At(page, x[1], y[0]).Should().Be(Color.Black, "a0's right line paints the top middle corner");
    }

    [Fact]
    public void The_widest_line_paints_the_corner_and_every_other_line_stops_at_its_side()
    {
        // tj-four, left inner corner: above 0.75 pt black, left 3 pt green, right 1.5 pt red, below 2.25 pt blue. The
        // green line paints the corner, 2.25 pt wide (the widest vertical line) and 3 pt high (the widest horizontal);
        // the red line starts at its right side, the blue one at its lower side, the black one ends at its upper side.
        var page = Paginate(Table(2, 3, (row, column) => (row, column) switch
        {
            (0, 0) => Lined with { Border = Lined.Border with { Right = Line(0.75, Color.Black), Bottom = Line(3, Green) } },
            (0, 1) => Lined with { Border = Lined.Border with { Left = Line(0.75, Color.Black), Bottom = Line(Thin, Red), Right = Line(3, Orange) } },
            (0, 2) => Lined with { Border = Lined.Border with { Bottom = Line(0.75, Cyan) } },
            (1, 0) => Lined with { Border = Lined.Border with { Right = Line(2.25, Blue) } },
            (1, 1) => Lined with { Border = Lined.Border with { Right = Line(2.25, Magenta) } },
            _ => Lined with { Border = Lined.Border with { Top = Line(0.75, Color.Black) } },
        })).Single();
        var (x, y) = Centres(page);
        var line = y.First(centre => centre > 5);

        At(page, x[1] + 1.1, line).Should().Be(Green);
        At(page, x[1] + 1.2, line).Should().Be(Red);
        At(page, x[1], line + 1.4).Should().Be(Green);
        At(page, x[1], line + 1.6).Should().Be(Blue);
        At(page, x[1], line - 1.6).Should().Be(Color.Black);

        // The right inner corner: the 3 pt orange line above paints it, 1.5 pt high (the red line on its left is the
        // widest horizontal there); the magenta line below starts 0.75 pt under the line's centre.
        At(page, x[2], line + 0.7).Should().Be(Orange);
        At(page, x[2], line + 0.8).Should().Be(Magenta);
        At(page, x[2] - 1.4, line).Should().Be(Orange);
        At(page, x[2] - 1.6, line).Should().Be(Red);
    }

    [Fact]
    public void A_thin_row_line_starts_at_its_own_vertical_line_not_at_the_edge_a_wider_line_in_another_row_sets()
    {
        // te-left-later: a2's 6 pt left line sets the table's left border (3 pt); the thin rows' lines start at the outer
        // side of their own 1.5 pt vertical line, 2.25 pt in (38.25 in Chromium's print, table at 36), and the line
        // above a2 at the inner side of the 6 pt line.
        var page = Paginate(Table(3, 2, (row, column) => row == 2 && column == 0 ? Lined with { Border = Lined.Border with { Left = Line(6, Red) } } : Lined)).Single();
        var (_, y) = Centres(page);

        At(page, 2.2, y[0]).Should().BeNull();
        At(page, 2.3, y[0]).Should().Be(Color.Black);
        At(page, 2.3, y[1]).Should().Be(Color.Black);
        At(page, 5.9, y[2]).Should().Be(Red, "the 6 pt line paints the corner above a2");
        At(page, 6.1, y[2]).Should().Be(Color.Black);
    }

    [Fact]
    public void At_a_page_break_the_vertical_lines_end_and_start_at_the_line_s_centre()
    {
        // tj-break: the line where the page breaks is 3 pt red under a(k), the row below has green left and right lines.
        // Page 1: the vertical lines end at the line's centre, under its red upper half. Page 2: the red lower half
        // over a(k+1)'s left line at the left edge, which Chromium paints first, and a(k+1)'s right line over the red
        // half at the middle corner, painted after it.
        var plain = Paginate(Table(30, 2, (_, _) => Lined, height: 300));
        var last = Find(plain[0].Root.Fragment, SemanticRole.TableCell).Count() / 2 - 1;
        var pages = Paginate(Table(30, 2, (row, column) => (row, column) switch
        {
            _ when row == last && column == 0 => Lined with { Border = Lined.Border with { Bottom = Line(3, Red) } },
            _ when row == last + 1 && column == 0 => Lined with { Border = Lined.Border with { Left = Line(Thin, Green), Right = Line(Thin, Green) } },
            _ => Lined,
        }, height: 300));

        (Find(pages[0].Root.Fragment, SemanticRole.TableCell).Count() / 2 - 1).Should().Be(last, "the wider line does not move the break");
        var (x1, _) = Centres(pages[0]);
        var bottom = TableOn(pages[0]).Size.Height;
        At(pages[0], x1[0], bottom - 0.1).Should().Be(Red);
        Rules(pages[0]).Where(rule => rule.Y1 - rule.Y0 > rule.X1 - rule.X0).Max(rule => rule.Y1).Should().BeApproximately(bottom, 1e-9);

        var (x2, _) = Centres(pages[1]);
        At(pages[1], x2[0], 0.1).Should().Be(Red);
        At(pages[1], x2[1], 0.1).Should().Be(Green);
        At(pages[1], x2[0], 1.6).Should().Be(Green);
    }
}
