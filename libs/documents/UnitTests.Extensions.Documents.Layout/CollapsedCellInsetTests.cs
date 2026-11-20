using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Where a table's outer border lies and where each cell's content starts, in the collapsing border model, when the
/// lines along one edge are not equally wide. The table's left and right border is half of the widest line at that
/// edge over all its rows, on every page; its top border half of the widest segment of the first row's top line, its
/// bottom border half of the widest segment of the last row's bottom line. Each cell is inset by half of its OWN lines,
/// so a wider line moves only the content of the cell it belongs to. Chromium's print (tools/reference/chromium), 1.5 pt
/// lines with one 6 pt border: cases <c>te-left-later</c>, <c>te-left-first</c>, <c>te-right-later</c>,
/// <c>te-right-first</c>, <c>te-top</c>, <c>te-bottom</c>, <c>te-table-border</c>, <c>te-break</c>.
/// </summary>
public class CollapsedCellInsetTests
{
    private const double Epsilon = 1e-9;
    private const double Thin = 1.5;
    private const double Wide = 6;
    private const double PaddingLeft = 4;

    private static readonly BoxStyle Lined = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(Thin.Pt(), Color.Black)) };

    /// <summary>A cell of the reference style with 1.5 pt borders, one side of which is 6 pt.</summary>
    private static BoxStyle WideOn(string side)
    {
        var wide = new BorderSide(Wide.Pt(), Color.Black);
        var border = Lined.Border;
        return Lined with
        {
            Border = side switch
            {
                "left" => border with { Left = wide },
                "right" => border with { Right = wide },
                "top" => border with { Top = wide },
                "bottom" => border with { Bottom = wide },
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "not a side"),
            },
        };
    }

    /// <summary>A table of two equal columns and <paramref name="rows"/> one-line rows "a{i}" "b{i}", on a page area
    /// 200 pt wide; <paramref name="style"/> gives the style of cell (row, column).</summary>
    private static StyledDocument Table(int rows, Func<int, int, BoxStyle> style, double height = 400, BoxStyle? table = null)
    {
        var t = new TableModels(10.5);
        var body = t.Body([.. Enumerable.Range(0, rows).Select(row => t.Row(t.Cell($"a{row}", style(row, 0)), t.Cell($"b{row}", style(row, 1))))]);
        var box = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], body);
        return TableModels.Page(200, height, table is null ? box : box with { Style = table with { Columns = box.Style.Columns } });
    }

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>The table's fragment on a page, with its offset on the page.</summary>
    private static (BoxFragment Table, Point At) TableOn(PageFragment page) => Find(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single();

    private static IEnumerable<(BoxFragment Box, Point At)> Find(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>()).Concat(box.Children.SelectMany(child => Find(child.Fragment, at + child.Offset, role)))
            : [];

    /// <summary>Where each cell's first line starts, relative to the table's border box, cell by cell in row order.</summary>
    private static List<Point> FirstLines(PageFragment page)
    {
        var (table, at) = TableOn(page);
        return [.. Find(table, at, SemanticRole.TableCell).Select(cell => FirstLine(cell.Box, cell.At)!.Value).Select(line => new Point(line.X - at.X, line.Y - at.Y))];
    }

    private static Point? FirstLine(Fragment fragment, Point at) =>
        fragment is LineFragment
            ? at
            : fragment is BoxFragment box ? box.Children.Select(child => FirstLine(child.Fragment, at + child.Offset)).FirstOrDefault(found => found is not null) : null;

    /// <summary>The vertical rules of the table on a page: (centre x, top, bottom, width), relative to its border box.</summary>
    private static List<(double Centre, double Top, double Bottom, double Width)> VerticalRules(PageFragment page) =>
        [.. TableOn(page).Table.Children
            .Where(child => child.Fragment is RuleFragment rule && rule.Size.Height > rule.Size.Width)
            .Select(child => (child.Offset.X + child.Fragment.Size.Width / 2, child.Offset.Y, child.Offset.Y + child.Fragment.Size.Height, child.Fragment.Size.Width))];

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void A_wider_left_line_in_one_row_is_the_table_s_left_border_in_every_row_and_moves_only_its_own_cell(int wideRow)
    {
        // te-left-later (wide row last) and te-left-first (wide row first): every row's left line is centred 3 pt (half
        // of 6 pt) inside the table's edge; the content of a0, a1, a2 starts at that centre plus half of the cell's own
        // line plus the 4 pt padding: 43.746 and 45.996 in Chromium's print, 2.25 pt apart.
        var page = Paginate(Table(3, (row, column) => row == wideRow && column == 0 ? WideOn("left") : Lined)).Single();

        VerticalRules(page).Where(rule => rule.Centre < 100).Should().OnlyContain(rule => Math.Abs(rule.Centre - (Wide / 2)) < Epsilon);
        var lines = FirstLines(page);
        for (var row = 0; row < 3; row++)
        {
            var own = row == wideRow ? Wide : Thin;
            lines[2 * row].X.Should().BeApproximately((Wide / 2) + (own / 2) + PaddingLeft, Epsilon, $"row {row}");
        }
    }

    [Fact]
    public void A_wider_right_line_in_one_row_is_the_table_s_right_border_and_a_wider_line_between_columns_moves_only_its_own_cells()
    {
        // te-right-later, table t: b2's 6 pt right border puts every row's right line 3 pt inside the table's right edge;
        // table t2: c2's 6 pt right border, between the two columns, moves d2 alone 2.25 pt right (142.992, not
        // 140.742) and no column moves.
        var right = Paginate(Table(3, (row, column) => row == 2 && column == 1 ? WideOn("right") : Lined)).Single();
        var between = Paginate(Table(3, (row, column) => row == 2 && column == 0 ? WideOn("right") : Lined)).Single();

        var width = TableOn(right).Table.Size.Width;
        VerticalRules(right).Where(rule => rule.Centre > 150).Should().OnlyContain(rule => Math.Abs(rule.Centre - (width - (Wide / 2))) < Epsilon);
        var lines = FirstLines(between);
        lines[5].X.Should().BeApproximately(lines[1].X + ((Wide - Thin) / 2), Epsilon);
        lines[3].X.Should().BeApproximately(lines[1].X, Epsilon);
        VerticalRules(between).Select(rule => rule.Centre).Distinct().Should().HaveCount(3, "every row's lines are at the same three column boundaries");
    }

    [Fact]
    public void A_wider_top_line_over_one_cell_of_the_first_row_is_the_table_s_top_border_and_lowers_only_that_cell()
    {
        // te-top: b0's 6 pt top border puts the first row's top at 3 pt below the table's top edge for both cells; a0's
        // content starts half of its own 1.5 pt line below it, b0's half of 6 pt: baselines 29.25 and 31.5 in Chromium.
        var page = Paginate(Table(3, (row, column) => row == 0 && column == 1 ? WideOn("top") : Lined)).Single();

        var lines = FirstLines(page);
        lines[1].Y.Should().BeApproximately(lines[0].Y + ((Wide - Thin) / 2), Epsilon);
        lines[0].Y.Should().BeApproximately((Wide / 2) + (Thin / 2) + 2, Epsilon, "the row starts at the line's centre, 3 pt down; then a0's own half line and 2 pt padding");
        lines[2].Y.Should().BeApproximately(lines[3].Y, Epsilon);
    }

    [Fact]
    public void A_wider_bottom_line_under_one_cell_of_the_last_row_is_the_table_s_bottom_border_and_moves_no_content()
    {
        // te-bottom: a2's 6 pt bottom border makes the last row as tall as a2 with it, and the table ends 3 pt below the
        // line's centre (Chromium: table 67.453 pt high, against 62.953 with 1.5 pt lines only); a2 and b2 start level.
        var wide = Paginate(Table(3, (row, column) => row == 2 && column == 0 ? WideOn("bottom") : Lined)).Single();
        var thin = Paginate(Table(3, (_, _) => Lined)).Single();

        var lines = FirstLines(wide);
        lines[4].Y.Should().BeApproximately(lines[5].Y, Epsilon);
        lines.Should().Equal(FirstLines(thin));
        TableOn(wide).Table.Size.Height.Should().BeApproximately(TableOn(thin).Table.Size.Height + (Wide - Thin), Epsilon,
            "the row grows by a2's extra half line (2.25 pt) and the table's bottom border by as much");
    }

    [Fact]
    public void A_table_border_wider_than_its_cells_lines_is_their_line_and_a_wider_cell_line_still_widens_the_edge()
    {
        // te-table-border, table t3: a 3 pt table border wins over the cells' 1.5 pt lines at every edge, so a0 and a1
        // start half of 3 pt inside their line (44.496); a2's 6 pt left border is the widest, so the left line is
        // centred 3 pt inside the table's edge and a2 starts 3 pt inside it (45.996).
        var page = Paginate(Table(3, (row, column) => row == 2 && column == 0 ? WideOn("left") : Lined,
            table: BoxStyle.Initial with { Border = Border.All(new BorderSide(3.Pt(), Color.Black)) })).Single();

        var lines = FirstLines(page);
        lines[0].X.Should().BeApproximately((Wide / 2) + 1.5 + PaddingLeft, Epsilon);
        lines[2].X.Should().BeApproximately(lines[0].X, Epsilon);
        lines[4].X.Should().BeApproximately((Wide / 2) + (Wide / 2) + PaddingLeft, Epsilon);
    }

    [Fact]
    public void A_wider_left_line_on_a_later_page_is_the_table_s_left_border_on_the_first_page_too()
    {
        // te-break: a10's 6 pt left border is on page 2, and page 1's left lines are centred 3 pt inside the table's
        // edge as well (38.25-39.75 in Chromium's print on both pages), with a0 at 43.746 and a10 at 45.996.
        var pages = Paginate(Table(30, (row, column) => row == 25 && column == 0 ? WideOn("left") : Lined, height: 300));

        pages.Count.Should().BeGreaterThan(1);
        (FirstLines(pages[0]).Count / 2).Should().BeLessThan(25, "row 25 is on a later page");
        foreach (var page in pages)
        {
            VerticalRules(page).Where(rule => rule.Centre < 100).Should().OnlyContain(rule => Math.Abs(rule.Centre - (Wide / 2)) < Epsilon);
        }

        FirstLines(pages[0])[0].X.Should().BeApproximately((Wide / 2) + (Thin / 2) + PaddingLeft, Epsilon);
    }
}
