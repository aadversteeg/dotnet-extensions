using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// How the halves of a grid line are painted where a page break between two rows splits the line at its centre
/// (<c>BoxDecorationBreak.Slice</c>): as thick as <see cref="SplitLineHalves"/> says, in every column, except at the top
/// of a page where a cell that spans rows continues over the break, where nothing of the line is painted. Paint only: the
/// rows, cells and text are where they are under either value. Chromium's print (tools/reference/chromium): cases
/// <c>a2-cells-end</c> and <c>b5-row-span-kept-cell</c> (0.75 pt, halves of half a pixel printed one pixel thick),
/// <c>a2-row-break-line-1-5pt</c>, <c>a2-row-break-line-2-25pt</c>, <c>a2-row-break-line-6pt</c> and <c>b4-row-span-6pt</c>.
/// </summary>
public class TableBreakLineHalfTests
{
    private const double Epsilon = 1e-9;

    /// <summary>A two-column table of 30 one-line rows with grid lines of <paramref name="lineWidth"/>, on pages 150 pt
    /// high; with <paramref name="span"/> the first column is one cell that spans every row.</summary>
    private static StyledDocument Rows(double lineWidth, SplitLineHalves halves, bool span = false)
    {
        const int Count = 30;
        var t = new TableModels();
        var lined = BoxStyle.Initial with { Border = Border.All(new BorderSide(lineWidth.Pt(), Color.Black)) };
        Box Cell(string text, BoxStyle style) => new(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, style,
            [new Box(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [new TextRun(t.Next(), text, Models.Text(10), Models.At(0))], Models.At(0))],
            Models.At(0));
        Box[] Cells(int i) => span
            ? i == 0 ? [Cell("spanned", lined with { Span = new CellSpan(1, Count) }), Cell($"row{i:00}", lined)] : [Cell($"row{i:00}", lined)]
            : [Cell("value", lined), Cell($"row{i:00}", lined)];
        var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body([.. Enumerable.Range(0, Count).Select(i => t.Row(Cells(i)))]));
        var document = TableModels.Page(280, 150, table with { Style = table.Style with { DecorationBreak = BoxDecorationBreak.Slice } });
        return document with { Profile = LayoutProfile.Default with { SplitLineHalves = halves } };
    }

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static BoxFragment Table(PageFragment page) => Find(page.Root.Fragment, SemanticRole.Table).Single();

    private static IEnumerable<BoxFragment> Find(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Find(child.Fragment, role)))
            : [];

    /// <summary>The horizontal rules of the table's part on a page: (top, thickness), top down.</summary>
    private static List<(double Top, double Height)> HorizontalRules(PageFragment page) =>
        [.. Table(page).Children
            .Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > rule.Size.Height)
            .Select(child => (child.Offset.Y, child.Fragment.Size.Height))
            .OrderBy(rule => rule.Y)];

    /// <summary>Everything but the rules, as where it is: the place of every box and line inside the table.</summary>
    private static List<string> Placed(Fragment fragment, Point at)
    {
        if (fragment is not BoxFragment box)
        {
            return fragment is LineFragment ? [$"line {at.X:0.###} {at.Y:0.###} {fragment.Size.Height:0.###}"] : [];
        }

        return [$"box {box.Box.Role} {at.X:0.###} {at.Y:0.###} {box.Size.Width:0.###} {box.Size.Height:0.###}",
            .. box.Children.Where(child => child.Fragment is not RuleFragment).SelectMany(child => Placed(child.Fragment, at + child.Offset))];
    }

    [Theory]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0, 0.75)]
    [InlineData(1.5, 0.75)]
    [InlineData(2.25, 1.125)]
    [InlineData(6.0, 3.0)]
    public void At_least_one_pixel_paints_a_half_thinner_than_a_pixel_one_pixel_thick_from_its_top_and_moves_nothing(double lineWidth, double painted)
    {
        var exact = Paginate(Rows(lineWidth, SplitLineHalves.Exact));
        var pixel = Paginate(Rows(lineWidth, SplitLineHalves.AtLeastOnePixel));

        pixel.Count.Should().Be(exact.Count).And.BeGreaterThan(1);
        for (var index = 0; index < exact.Count; index++)
        {
            Placed(pixel[index].Root.Fragment, new Point(0, 0)).Should().Equal(Placed(exact[index].Root.Fragment, new Point(0, 0)), "layout does not change");
        }

        // The upper half ends page 1, the lower half starts page 2: both keep their top edge, only the thickness differs.
        var (upperExact, upperPixel) = (HorizontalRules(exact[0])[^1], HorizontalRules(pixel[0])[^1]);
        var (lowerExact, lowerPixel) = (HorizontalRules(exact[1])[0], HorizontalRules(pixel[1])[0]);
        upperExact.Height.Should().BeApproximately(lineWidth / 2, Epsilon);
        lowerExact.Height.Should().BeApproximately(lineWidth / 2, Epsilon);
        upperPixel.Top.Should().BeApproximately(upperExact.Top, Epsilon);
        lowerPixel.Top.Should().BeApproximately(0, Epsilon);
        upperPixel.Height.Should().BeApproximately(painted, Epsilon);
        lowerPixel.Height.Should().BeApproximately(painted, Epsilon);

        // A line that is not at a break is painted whole under either value.
        HorizontalRules(pixel[0])[1].Height.Should().BeApproximately(lineWidth, Epsilon);
    }

    [Theory]
    [InlineData(SplitLineHalves.Exact)]
    [InlineData(SplitLineHalves.AtLeastOnePixel)]
    public void Below_a_break_inside_a_cell_that_spans_rows_the_next_page_paints_no_line_at_its_top_and_keeps_its_room(SplitLineHalves halves)
    {
        // Case b4-row-span-6pt: page 2 shows no rectangle at the top of the table in either column, and row r8's text
        // starts half the 6 pt line down, as below a split line without a spanning cell (a2-row-break-line-6pt).
        var spanned = Paginate(Rows(6, halves, span: true));
        var plain = Paginate(Rows(6, halves));

        spanned.Count.Should().BeGreaterThan(1);
        for (var index = 1; index < spanned.Count; index++)
        {
            HorizontalRules(spanned[index]).Should().NotContain(rule => rule.Top < Epsilon, $"page {index + 1} starts inside the spanning cell");
        }

        HorizontalRules(plain[1])[0].Top.Should().BeApproximately(0, Epsilon, "without the spanning cell page 2 starts with the lower half");
        HorizontalRules(spanned[0])[^1].Height.Should().BeApproximately(3, Epsilon, "page 1 still ends with the upper half beside the spanning cell");
        static double FirstLine(PageFragment page) => double.Parse(Placed(Table(page), new Point(0, 0)).First(item => item.StartsWith("line", StringComparison.Ordinal)).Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
        Find(Table(spanned[1]), SemanticRole.TableRow).Should().NotBeEmpty();
        FirstLine(spanned[1]).Should().BeGreaterThanOrEqualTo(3 - Epsilon, "the room of the lower half stays");
    }
}
