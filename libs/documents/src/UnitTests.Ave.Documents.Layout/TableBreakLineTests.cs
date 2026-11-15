using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// The grid line at a page break between two rows. In the collapsing border model half of a line lies in the cells on
/// each side of it (CSS 2.1 §17.6.2), and a box cut by a break gets nothing added at the cut (CSS Fragmentation 3
/// §5.4, <c>box-decoration-break: slice</c>). So the line is split along its centre: its upper half ends the page,
/// its lower half starts the next one, and a row fits when it fits down to the centre of its bottom line. Chromium
/// 159 does exactly this (tools/reference/chromium, cases <c>mf4-line-*</c>).
/// </summary>
public class TableBreakLineTests
{
    private const double Epsilon = 1e-9;

    private static BoxStyle Lined(double width) => BoxStyle.Initial with { Border = Border.All(new BorderSide(width.Pt(), Color.Black)) };

    /// <summary>A two-column table of 30 one-line rows, every line <paramref name="lineWidth"/> wide, after an empty
    /// block whose margin moves the table <paramref name="offset"/> down.</summary>
    private static StyledDocument Rows(double lineWidth, double offset, double pageHeight, int count = 30, double fontSize = 10, BoxDecorationBreak mode = BoxDecorationBreak.Slice)
    {
        var t = new TableModels();
        var cell = Lined(lineWidth);
        Box Cell(string text) => new(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, cell,
            [new Box(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [new TextRun(t.Next(), text, Models.Text(fontSize), Models.At(0))], Models.At(0))],
            Models.At(0));
        var spacer = new Box(t.Next(), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), offset.Pt(), 0.Pt()) }, [], Models.At(0));
        var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body([.. Enumerable.Range(0, count).Select(i => t.Row(Cell($"row{i:00}"), Cell("value")))]));
        return TableModels.Page(280, pageHeight, spacer, table with { Style = table.Style with { DecorationBreak = mode } });
    }

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    private static string Text(Fragment fragment) => fragment switch
    {
        BoxFragment box => string.Concat(box.Children.Select(child => Text(child.Fragment))),
        LineFragment line => string.Concat(line.Children.Select(child => Text(child.Fragment))),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)),
        _ => string.Empty,
    };

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData(4.0, BoxDecorationBreak.Slice)]
    [InlineData(1.0, BoxDecorationBreak.Slice)]
    [InlineData(0.5, BoxDecorationBreak.Slice)]
    [InlineData(4.0, BoxDecorationBreak.Clone)]
    [InlineData(1.0, BoxDecorationBreak.Clone)]
    [InlineData(0.5, BoxDecorationBreak.Clone)]
    public void No_start_offset_leaves_a_row_without_content_or_a_line_outside_the_table(double lineWidth, BoxDecorationBreak mode)
    {
        const double PageHeight = 150;
        for (var offset = 0.0; offset < 40; offset += 0.25)
        {
            var pages = Paginate(Rows(lineWidth, offset, PageHeight, mode: mode));

            var because = $"{mode}, line width {lineWidth}, offset {offset}";
            var rows = new List<string>();
            foreach (var page in pages)
            {
                var (table, at) = Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single();
                (at.Y + table.Size.Height).Should().BeLessThanOrEqualTo(PageHeight + Epsilon, because + ": the table's part fits the page area");
                foreach (var rule in table.Children.Where(child => child.Fragment is RuleFragment))
                {
                    rule.Offset.Y.Should().BeGreaterThanOrEqualTo(-Epsilon, because);
                    (rule.Offset.Y + rule.Fragment.Size.Height).Should().BeLessThanOrEqualTo(table.Size.Height + Epsilon, because + ": no rule outside the table's box");
                }

                foreach (var (row, _) in Boxes(table, new Point(0, 0), SemanticRole.TableRow))
                {
                    var text = Text(row);
                    text.Should().NotBeEmpty(because + ": a row on a page holds content");
                    rows.Add(text);
                }
            }

            rows.Should().Equal(Enumerable.Range(0, 30).Select(i => $"row{i:00}value"), because + ": every row once, whole");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.75)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    public void A_row_that_fits_down_to_its_bottom_line_s_centre_stays_and_the_line_is_split_like_Chromium(double extra)
    {
        // Chromium (cases mf4-line-*): 3pt lines, 12pt text; the page area ends at the centre of row 8's bottom line
        // plus 0, 0.75, 1.5 or 2.25 pt. In every case rows 1–8 are on page 1, page 1 ends with the upper half of the
        // line (1.5pt), page 2 starts with its lower half, and page 2's first row is 1.5pt higher than row 1 on page 1.
        const double Line = 3, Pitch = 14.4 + Line;
        var area = Line / 2 + 8 * Pitch + extra;

        var pages = Paginate(Rows(Line, 0, area, count: 20, fontSize: 12));

        var tables = pages.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
        Boxes(tables[0], new Point(0, 0), SemanticRole.TableRow).Should().HaveCount(8);
        var lastRule = tables[0].Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > 100).MaxBy(child => child.Offset.Y)!;
        lastRule.Fragment.Size.Height.Should().BeApproximately(Line / 2, Epsilon, "the upper half of the line ends page 1");
        (lastRule.Offset.Y + Line / 2).Should().BeApproximately(tables[0].Size.Height, Epsilon);
        var firstRule = tables[1].Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > 100).MinBy(child => child.Offset.Y)!;
        firstRule.Offset.Y.Should().BeApproximately(0, Epsilon);
        firstRule.Fragment.Size.Height.Should().BeApproximately(Line / 2, Epsilon, "the lower half of the line starts page 2");
        var firstRowTop = (Func<BoxFragment, double>)(table => Boxes(table, new Point(0, 0), SemanticRole.TableRow).First().At.Y);
        (firstRowTop(tables[0]) - firstRowTop(tables[1])).Should().BeApproximately(Line / 2, Epsilon);
    }

    [Theory]
    [InlineData(0.0, 7)]
    [InlineData(0.75, 7)]
    [InlineData(1.5, 8)]
    [InlineData(2.25, 8)]
    public void With_clone_a_row_fits_only_with_the_whole_line_below_it_and_each_page_has_whole_lines(double extra, int rowsOnPageOne)
    {
        // The same 3pt grid as the Chromium cases. With clone the page area must hold row 8 AND the whole line below it:
        // the line's centre plus half its width. Page 1 then ends with the whole 3pt line, page 2 starts with the whole
        // line again, and page 2's first row sits as low as row 1 on page 1 (half a line below the table's top).
        const double Line = 3, Pitch = 14.4 + Line;
        var area = Line / 2 + 8 * Pitch + extra;

        var pages = Paginate(Rows(Line, 0, area, count: 20, fontSize: 12, mode: BoxDecorationBreak.Clone));

        var tables = pages.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
        Boxes(tables[0], new Point(0, 0), SemanticRole.TableRow).Should().HaveCount(rowsOnPageOne);
        static IEnumerable<PlacedFragment> Wide(BoxFragment table) => table.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > 100);
        var lastRule = Wide(tables[0]).MaxBy(child => child.Offset.Y)!;
        lastRule.Fragment.Size.Height.Should().BeApproximately(Line, Epsilon, "the whole line ends page 1");
        (lastRule.Offset.Y + Line).Should().BeApproximately(tables[0].Size.Height, Epsilon);
        tables[0].Size.Height.Should().BeLessThanOrEqualTo(area + Epsilon);
        var firstRule = Wide(tables[1]).MinBy(child => child.Offset.Y)!;
        firstRule.Offset.Y.Should().BeApproximately(0, Epsilon);
        firstRule.Fragment.Size.Height.Should().BeApproximately(Line, Epsilon, "the whole line starts page 2");
        var firstRowTop = (Func<BoxFragment, double>)(table => Boxes(table, new Point(0, 0), SemanticRole.TableRow).First().At.Y);
        firstRowTop(tables[1]).Should().BeApproximately(firstRowTop(tables[0]), Epsilon);
    }

    [Fact]
    public void With_clone_the_line_at_a_break_is_resolved_from_both_rows_on_both_pages()
    {
        var t = new TableModels();
        var grey = new BorderSide(1.Pt(), new Color(0x80, 0x80, 0x80));
        var black = new BorderSide(6.Pt(), Color.Black);
        var cell = BoxStyle.Initial with { Border = Border.All(grey) };
        var row = BoxStyle.Initial with { Border = Border.All(BorderSide.None) with { Bottom = black } };
        Box Cell(string text) => new(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, cell,
            [new Box(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [new TextRun(t.Next(), text, Models.Text(12), Models.At(0))], Models.At(0))],
            Models.At(0));
        var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], t.Body([.. Enumerable.Range(0, 20).Select(i => t.Row(row, Cell($"row{i:00}"), Cell("value")))]));

        var pages = Paginate(TableModels.Page(280, 150, table with { Style = table.Style with { DecorationBreak = BoxDecorationBreak.Clone } }));

        var tables = pages.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
        static IEnumerable<PlacedFragment> Wide(BoxFragment table) => table.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > 100);
        ((RuleFragment)Wide(tables[0]).MaxBy(child => child.Offset.Y)!.Fragment).Color.Should().Be(Color.Black);
        ((RuleFragment)Wide(tables[0]).MaxBy(child => child.Offset.Y)!.Fragment).Size.Height.Should().BeApproximately(6, Epsilon);
        ((RuleFragment)Wide(tables[1]).MinBy(child => child.Offset.Y)!.Fragment).Color.Should().Be(Color.Black);
        ((RuleFragment)Wide(tables[1]).MinBy(child => child.Offset.Y)!.Fragment).Size.Height.Should().BeApproximately(6, Epsilon);
    }

    /// <summary>
    /// Rows with different borders at the break: every cell has a 1pt grey border, every row a 6pt black bottom border,
    /// and the body group a 4pt red top border. Between two rows the resolved line is the 6pt black one (§17.6.2.1:
    /// the widest wins). The two halves at a break are halves of that same line: page 1 ends with 3pt black, and page 2
    /// starts with 3pt black, not with the grey or red border the first row on page 2 would have at the table's top.
    /// The group's top border belongs to the group's first row only.
    /// </summary>
    [Fact]
    public void The_two_halves_at_a_break_are_halves_of_the_same_resolved_line()
    {
        var t = new TableModels();
        var grey = new BorderSide(1.Pt(), new Color(0x80, 0x80, 0x80));
        var black = new BorderSide(6.Pt(), Color.Black);
        var red = new BorderSide(4.Pt(), new Color(0xFF, 0, 0));
        var cell = BoxStyle.Initial with { Border = Border.All(grey) };
        var row = BoxStyle.Initial with { Border = Border.All(BorderSide.None) with { Bottom = black } };
        Box Cell(string text) => new(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, cell,
            [new Box(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [new TextRun(t.Next(), text, Models.Text(12), Models.At(0))], Models.At(0))],
            Models.At(0));
        var body = t.Body(BoxStyle.Initial with { Border = Border.All(BorderSide.None) with { Top = red } }, [.. Enumerable.Range(0, 20).Select(i => t.Row(row, Cell($"row{i:00}"), Cell("value")))]);
        var document = TableModels.Page(280, 150, t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)], body));

        var pages = Paginate(document);

        pages.Count.Should().BeGreaterThan(1);
        var tables = pages.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
        static IEnumerable<PlacedFragment> Wide(BoxFragment table) => table.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > 100);
        var pageEnd = (RuleFragment)Wide(tables[0]).MaxBy(child => child.Offset.Y)!.Fragment;
        pageEnd.Color.Should().Be(Color.Black);
        pageEnd.Size.Height.Should().BeApproximately(3, Epsilon);
        var pageStart = (RuleFragment)Wide(tables[1]).MinBy(child => child.Offset.Y)!.Fragment;
        pageStart.Color.Should().Be(Color.Black, "the lower half of the same 6pt black line, not the group's red top border");
        pageStart.Size.Height.Should().BeApproximately(3, Epsilon);
        var first = (RuleFragment)Wide(tables[0]).MinBy(child => child.Offset.Y)!.Fragment;
        first.Color.Should().Be(new Color(0xFF, 0, 0), "on page 1 the group's top border is the table's first line");
    }
}
