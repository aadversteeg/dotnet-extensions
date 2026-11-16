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
    private static StyledDocument Document(double offset, double pageHeight, bool header, double spanLine, double? spanTop = null, double? spanBottom = null, bool shortSpan = false, int units = 12)
    {
        var t = new TableModels();
        var thin = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(2.Pt(), Color.Black)) };
        var thick = TableModels.ReferenceCell with
        {
            Border = Border.All(new BorderSide(spanLine.Pt(), Color.Black)) with
            {
                Top = new BorderSide((spanTop ?? spanLine).Pt(), Color.Black),
                Bottom = new BorderSide((spanBottom ?? spanLine).Pt(), Color.Black),
            },
            Span = new CellSpan(1, 2),
        };
        Box Cell(BoxStyle style, params string[] lines) => t.Lines(lines) with { Style = style };
        var rows = Enumerable.Range(0, units).SelectMany(unit => new[]
        {
            shortSpan
                ? t.Row(Cell(thick, $"s{unit:00}x"), Cell(thin, $"a{unit:00}w", $"a{unit:00}x", $"a{unit:00}y", $"a{unit:00}z"))
                : t.Row(Cell(thick, $"s{unit:00}x", $"s{unit:00}y", $"s{unit:00}z"), Cell(thin, $"a{unit:00}")),
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
    [InlineData(false, 2.0, false)]
    [InlineData(true, 2.0, false)]
    [InlineData(false, 4.0, false)]
    [InlineData(true, 4.0, false)]
    [InlineData(false, 2.0, true)]
    [InlineData(true, 4.0, true)]
    public void With_clone_every_break_closes_the_table_across_its_full_width_on_both_pages(bool header, double spanLine, bool shortSpan)
    {
        // Offsets move every break through every position in a unit: before it, inside its first row (the spanning cell
        // crosses the line below that row) and inside its second row. With a short spanning cell (one line, beside four
        // in the first row) the second row's one line has nothing beside it that could be cut, so where it does not fit
        // the page breaks between the unit's rows, across the spanning cell that goes on covering the second row.
        var breaks = 0;
        for (var offset = 0.0; offset < 60; offset += 0.5)
        {
            var because = $"header {header}, spanning cell's border {spanLine}, short spanning cell {shortSpan}, offset {offset}";
            var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(offset, 120, header, spanLine, shortSpan: shortSpan));
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

    /// <summary>
    /// One unit alone, on page areas from 10 pt up in quarter points: the unit is the page's first content, so no break
    /// before it exists, and every page end steps through the lines of the unit, among them the narrow window in which
    /// the second row cannot be cut and the page breaks between the rows across the spanning cell with text left. Every
    /// page is closed across its full width at both ends, no text lies under a line, or the table does not fit at all.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_clone_a_unit_broken_anywhere_on_small_pages_is_closed_and_keeps_its_text_clear(bool header)
    {
        for (var pageHeight = 10.0; pageHeight < 80; pageHeight += 0.25)
        {
            var because = $"header {header}, page area {pageHeight}";
            var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(0, pageHeight, header, 4, units: 1));
            if (result.IsFailure)
            {
                result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit, because);
                continue;
            }

            var tables = result.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
            for (var page = 0; page < tables.Count; page++)
            {
                var table = tables[page];
                if (page + 1 < tables.Count)
                {
                    var last = Rules(table).OrderByDescending(rule => rule.Bottom - rule.Top).First(rule => Math.Abs(rule.Bottom - table.Size.Height) < Epsilon);
                    ClosedAt(table, (last.Top + last.Bottom) / 2).Should().BeTrue(because + $": page {page + 1} ends with a line across the whole table");
                }

                if (page > 0)
                {
                    // A page too small for the header and some body content goes on without the header (TablePage).
                    var hasHeader = Boxes(table, new Point(0, 0), SemanticRole.TableHead).Any();
                    var centre = hasHeader ? HeaderBottom(table) : Rules(table).Where(rule => Math.Abs(rule.Top) < Epsilon).Max(rule => rule.Bottom) / 2;
                    ClosedAt(table, centre).Should().BeTrue(because + $": page {page + 1} starts its body under a line across the whole table");
                }

                foreach (var line in TextLines(table, new Point(0, 0)))
                {
                    Rules(table).Where(rule => rule.Left < line.Right - Epsilon && line.Left < rule.Right - Epsilon && rule.Top < line.Bottom - Epsilon && line.Top < rule.Bottom - Epsilon)
                        .Should().BeEmpty(because + $": page {page + 1}, no line over the text at {line.Top:0.##}");
                }
            }
        }
    }

    [Theory]
    [InlineData(4.0, 4.0)]
    [InlineData(4.0, 1.0)]
    [InlineData(1.0, 4.0)]
    public void A_spanning_cell_cut_by_a_break_is_closed_by_its_own_borders(double top, double bottom)
    {
        // The other cells' borders are 2 pt. In the spanning cell's column the line at the break is resolved as a line
        // between the part of the cell above it and the part below: the cell's bottom and top border compete as two
        // cells' borders do, and the wider wins (§17.6.2.1), on both pages, since the line at a break is resolved once.
        // So the closing lines in its column are 4 pt whichever side is the wide one. Offset 0, page area 120: find a
        // break that cuts a spanning cell (its words on both pages).
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(0, 120, header: false, spanLine: 4, spanTop: top, spanBottom: bottom));
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
            end.Should().ContainSingle().Which.Should().Match<(double Left, double Top, double Right, double Bottom)>(rule => Math.Abs(rule.Bottom - rule.Top - 4) < Epsilon, "the wider of the spanning cell's own borders ends the page in its column");
            var start = Rules(tables[page + 1]).Where(rule => rule.Left < 80 && Math.Abs(rule.Top) < Epsilon).ToList();
            start.Should().ContainSingle().Which.Should().Match<(double Left, double Top, double Right, double Bottom)>(rule => Math.Abs(rule.Bottom - rule.Top - 4) < Epsilon, "and starts the next page in its column");
        }

        cuts.Should().BePositive("some break cuts a spanning cell");
    }
}
