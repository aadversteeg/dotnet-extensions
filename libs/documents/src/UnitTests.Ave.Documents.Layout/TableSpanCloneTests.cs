using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// Spot check of fix round 3, SC-2b. With <see cref="BoxDecorationBreak.Clone"/> the table is closed across its FULL
/// width on both pages of a break: the line that ends the page and the line that starts the next one also run over the
/// column of a cell that spans rows and continues over the break. Such a cell is closed there by its own bottom and top
/// border, as a cell that ends above the line and one that starts below it — clone gives every fragment its whole
/// border. Its content keeps clear of both lines, as every cell's content does.
/// </summary>
public class TableSpanCloneTests
{
    private const double Epsilon = 1e-6;

    private const double Width = 280;

    /// <summary>
    /// Twelve units of two rows: a cell spanning both, three lines high (border <paramref name="spanLine"/> wide on every
    /// side), beside a one-line cell in each row (2 pt borders); optionally a header row repeated on every page; after an
    /// empty block that moves the table <paramref name="offset"/> down.
    /// </summary>
    private static StyledDocument Document(double offset, double pageHeight, bool header, double spanLine)
    {
        var t = new TableModels();
        var thin = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(2.Pt(), Color.Black)) };
        var thick = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(spanLine.Pt(), Color.Black)), Span = new CellSpan(1, 2) };
        Box Cell(BoxStyle style, params string[] lines) => t.Lines(lines) with { Style = style };
        var rows = Enumerable.Range(0, 12).SelectMany(unit => new[]
        {
            t.Row(Cell(thick, $"s{unit:00}x", $"s{unit:00}y", $"s{unit:00}z"), Cell(thin, $"a{unit:00}")),
            t.Row(Cell(thin, $"b{unit:00}")),
        }).ToArray();
        var columns = (ValueList<TrackSize>)[new TrackSize.Fixed(80.Pt()), new TrackSize.Fill(1)];
        var table = header
            ? t.Table(columns, t.Head(t.Row(Cell(thin, "h1"), Cell(thin, "h2"))), t.Body(rows))
            : t.Table(columns, t.Body(rows));
        var spacer = new Box(t.Next(), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), offset.Pt(), 0.Pt()) }, [], Models.At(0));
        return TableModels.Page(Width, pageHeight, spacer, table with { Style = table.Style with { DecorationBreak = BoxDecorationBreak.Clone } });
    }

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    /// <summary>Every line of text in the fragment, as a rectangle (left, top, right, bottom) from the fragment's origin.</summary>
    private static IEnumerable<(double Left, double Top, double Right, double Bottom)> TextLines(Fragment fragment, Point at) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => TextLines(child.Fragment, at + child.Offset)),
        LineFragment line when line.Children.Any(child => child.Fragment is TextFragment) => [(at.X, at.Y, at.X + line.Size.Width, at.Y + line.Size.Height)],
        _ => [],
    };

    /// <summary>The horizontal rules of a table part, as rectangles.</summary>
    private static List<(double Left, double Top, double Right, double Bottom)> Rules(BoxFragment table) =>
        [.. table.Children.Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > rule.Size.Height)
            .Select(child => (child.Offset.X, child.Offset.Y, child.Offset.X + child.Fragment.Size.Width, child.Offset.Y + child.Fragment.Size.Height))];

    /// <summary>True when the rules crossing height <paramref name="y"/> together cover the table's whole width.</summary>
    private static bool ClosedAt(BoxFragment table, double y)
    {
        var reach = 0.0;
        foreach (var rule in Rules(table).Where(rule => rule.Top - Epsilon <= y && y <= rule.Bottom + Epsilon).OrderBy(rule => rule.Left))
        {
            if (rule.Left > reach + Epsilon)
            {
                return false;
            }

            reach = Math.Max(reach, rule.Right);
        }

        return reach >= table.Size.Width - Epsilon;
    }

    /// <summary>The bottom of the repeated header rows: the centre of the line between them and the body.</summary>
    private static double HeaderBottom(BoxFragment table)
    {
        var (head, at) = Boxes(table, new Point(0, 0), SemanticRole.TableHead).Single();
        return at.Y + head.Size.Height;
    }

    [Theory]
    [InlineData(false, 2.0)]
    [InlineData(true, 2.0)]
    [InlineData(false, 4.0)]
    [InlineData(true, 4.0)]
    public void With_clone_every_break_closes_the_table_across_its_full_width_on_both_pages(bool header, double spanLine)
    {
        // Offsets move every break through every position in a unit: before it, between its rows (the spanning cell is
        // cut), inside its first row (the spanning cell crosses the line below that row) and inside its second row.
        var breaks = 0;
        for (var offset = 0.0; offset < 60; offset += 0.5)
        {
            var because = $"header {header}, spanning cell's border {spanLine}, offset {offset}";
            var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(offset, 120, header, spanLine));
            result.IsSuccess.Should().BeTrue(result.IsFailure ? because + ": " + result.Error : string.Empty);
            var tables = result.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
            for (var page = 0; page < tables.Count; page++)
            {
                var table = tables[page];
                table.Size.Width.Should().BeApproximately(Width, Epsilon);
                // The segments of one line are centred on it, so a narrower segment does not reach the page end: the
                // line closes the table when its segments cover the whole width at its centre.
                if (page + 1 < tables.Count)
                {
                    var last = Rules(table).Single(rule => Math.Abs(rule.Bottom - table.Size.Height) < Epsilon && rule.Right - rule.Left > 0);
                    ClosedAt(table, (last.Top + last.Bottom) / 2).Should().BeTrue(because + $": page {page + 1} ends with a line across the whole table");
                    breaks++;
                }

                if (page > 0)
                {
                    // The line above the page's first body row: at the top, or under the repeated header.
                    var centre = header ? HeaderBottom(table) : Rules(table).Where(rule => Math.Abs(rule.Top) < Epsilon).Max(rule => rule.Bottom) / 2;
                    ClosedAt(table, centre).Should().BeTrue(because + $": page {page + 1} starts its body under a line across the whole table");
                }

                // Every cell's content keeps clear of every line, the closing lines included.
                foreach (var line in TextLines(table, new Point(0, 0)))
                {
                    Rules(table).Where(rule => rule.Left < line.Right - Epsilon && line.Left < rule.Right - Epsilon && rule.Top < line.Bottom - Epsilon && line.Top < rule.Bottom - Epsilon)
                        .Should().BeEmpty(because + $": page {page + 1}, no line over the text at {line.Top:0.##}");
                }
            }
        }

        breaks.Should().BeGreaterThan(100, "the sweep crosses many breaks");
    }

    [Fact]
    public void A_spanning_cell_cut_by_a_break_is_closed_by_its_own_borders()
    {
        // The spanning cell's border is 4 pt, the other cells' 2 pt: in its column the closing lines are 4 pt, so the
        // line that ends the page and the line that starts the next are the spanning cell's own bottom and top border.
        // Offset 0, page area 120: find a break that cuts a spanning cell (its words on both pages).
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(0, 120, header: false, spanLine: 4));
        result.IsSuccess.Should().BeTrue();
        var tables = result.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
        static IEnumerable<string> Words(Fragment fragment) => fragment switch
        {
            BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
            LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
            TextFragment text => [string.Concat(text.Glyphs.Select(glyph => glyph.Text))],
            _ => [],
        };

        var cuts = 0;
        for (var page = 0; page + 1 < tables.Count; page++)
        {
            var spanWords = Words(tables[page]).Where(word => word.StartsWith('s')).Select(word => word[..3]).ToHashSet();
            if (!Words(tables[page + 1]).Any(word => word.StartsWith('s') && spanWords.Contains(word[..3])))
            {
                continue;
            }

            cuts++;
            var end = Rules(tables[page]).Where(rule => rule.Left < 80 && Math.Abs(rule.Bottom - tables[page].Size.Height) < Epsilon).ToList();
            end.Should().ContainSingle().Which.Should().Match<(double Left, double Top, double Right, double Bottom)>(rule => Math.Abs(rule.Bottom - rule.Top - 4) < Epsilon, "the spanning cell's 4 pt bottom border ends the page in its column");
            var start = Rules(tables[page + 1]).Where(rule => rule.Left < 80 && Math.Abs(rule.Top) < Epsilon).ToList();
            start.Should().ContainSingle().Which.Should().Match<(double Left, double Top, double Right, double Bottom)>(rule => Math.Abs(rule.Bottom - rule.Top - 4) < Epsilon, "its 4 pt top border starts the next page in its column");
        }

        cuts.Should().BePositive("some break cuts a spanning cell");
    }
}
