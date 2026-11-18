using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

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
    private static StyledDocument Document(double offset, double pageHeight, bool header, double spanLine, double? spanTop = null, double? spanBottom = null, bool shortSpan = false, int units = 12, double rowPadding = 0, bool twoLineSecondRows = false)
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
                : t.Row(Cell(thick, $"s{unit:00}x", $"s{unit:00}y", $"s{unit:00}z"),
                    Cell(thin with { Padding = thin.Padding with { Bottom = (thin.Padding.Bottom.Points + rowPadding).Pt() } }, $"a{unit:00}")),
            twoLineSecondRows ? t.Row(Cell(thin, $"b{unit:00}", $"b{unit:00}y")) : t.Row(Cell(thin, $"b{unit:00}")),
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
    /// Extra bottom padding in the first row's other cell moves the line between the rows against the spanning cell's
    /// text lines, so that at some break between the rows a line of the spanning cell ends just above the line there,
    /// and has to keep clear of the half of it that ends the page.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_clone_a_unit_broken_anywhere_on_small_pages_is_closed_and_keeps_its_text_clear(bool header)
    {
        foreach (var (rowPadding, pageHeight) in Enumerable.Range(0, 25).SelectMany(padding => Enumerable.Range(0, 280).Select(height => (padding * 0.5, 10 + height * 0.25))))
        {
            var because = $"header {header}, extra row padding {rowPadding}, page area {pageHeight}";
            var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(0, pageHeight, header, 4, units: 1, rowPadding: rowPadding));
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

    /// <summary>
    /// Units of two one-line rows beside a cell of five lines that spans both; the first row of each unit has an 8 pt
    /// bottom border of its own (a row's border takes part in every column of the line, the spanning cell's too), every
    /// other line is 1 pt; the spanning cell's top padding moves its lines against the rows. After an empty block that
    /// moves the table <paramref name="offset"/> down (review 35, probe M2).
    /// </summary>
    private static StyledDocument WideLineUnits(double offset, double spanPadding, double pageHeight = 120)
    {
        var t = new TableModels();
        var thin = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(1.Pt(), Color.Black)) };
        var span = thin with { Padding = thin.Padding with { Top = (thin.Padding.Top.Points + spanPadding).Pt() }, Span = new CellSpan(1, 2) };
        var wide = BoxStyle.Initial with { Border = Border.All(BorderSide.None) with { Bottom = new BorderSide(8.Pt(), Color.Black) } };
        Box Cell(BoxStyle style, params string[] lines) => t.Lines(lines) with { Style = style };
        var rows = Enumerable.Range(0, 6).SelectMany(unit => new[]
        {
            t.Row(wide, Cell(span, [.. Enumerable.Range(0, 5).Select(line => $"s{unit}x{line}")]), Cell(thin, $"a{unit}")),
            t.Row(Cell(thin, $"b{unit}")),
        }).ToArray();
        var table = t.Table([new TrackSize.Fixed(80.Pt()), new TrackSize.Fill(1)], t.Body(rows));
        var spacer = new Box(t.Next(), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), offset.Pt(), 0.Pt()) }, [], Models.At(0));
        return TableModels.Page(Width, pageHeight, spacer, table with { Style = table.Style with { DecorationBreak = BoxDecorationBreak.Clone } });
    }

    [Fact]
    public void At_a_break_between_rows_a_cut_cell_keeps_clear_of_the_wide_line_that_ends_the_page()
    {
        // Mutant R5-2 of review 35. At a break between a unit's rows the page ends with the whole line above the row the
        // break comes before — here the 8 pt line under the first row, in the spanning cell's column too — and the
        // spanning cell, cut there, keeps clear of half of THAT line (4 pt), not of half of the 1 pt line under the row
        // below. A break between rows is taken where nothing better exists: on a page whose first unit does not fit
        // and whose end lies inside the wide line's lower half plus the half line under the next row, so page areas
        // smaller than a unit step through it; the spanning cell's top padding moves its first line into the 3.5 pt in
        // which only the wide line's half keeps it clear. From 29 pt: on a smaller page area a page that starts under the
        // 8 pt line cannot hold the spanning cell's padding and first line, and the table is refused (DoesNotFit).
        var betweenRows = 0;
        for (var spanPadding = 0.0; spanPadding <= 8; spanPadding += 1)
        {
            for (var pageHeight = 29.0; pageHeight < 60; pageHeight += 0.25)
            {
                var because = $"spanning cell's top padding {spanPadding}, page area {pageHeight}";
                var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), WideLineUnits(0, spanPadding, pageHeight));
                result.IsSuccess.Should().BeTrue(result.IsFailure ? because + ": " + result.Error : string.Empty);
                var tables = result.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
                for (var page = 0; page < tables.Count; page++)
                {
                    var table = tables[page];
                    foreach (var line in TextLines(table, new Point(0, 0)))
                    {
                        Rules(table).Where(rule => rule.Left < line.Right - Epsilon && line.Left < rule.Right - Epsilon && rule.Top < line.Bottom - Epsilon && line.Top < rule.Bottom - Epsilon)
                            .Should().BeEmpty(because + $": page {page + 1}, no line over the text at {line.Top:0.##}");
                    }

                    // A page that ends with the 8 pt line ended at a break between a unit's rows.
                    betweenRows += page + 1 < tables.Count && Rules(table).Any(rule => rule.Left < 1 && Math.Abs(rule.Bottom - table.Size.Height) < Epsilon && Math.Abs(rule.Bottom - rule.Top - 8) < Epsilon) ? 1 : 0;
                }
            }
        }

        betweenRows.Should().BeGreaterThan(10, "pages end at a break between a unit's rows, under the 8 pt line");
    }

    /// <summary>The name of a row of <see cref="Document"/>, from its last cell's text: "a07" for the first row of unit
    /// 7 (the spanning cell starts in it), "b07" for its second.</summary>
    private static string RowName(Box row) =>
        ((TextRun)((Box)((Box)row.Children[^1]).Children[0]).Children[0]).Text;

    [Theory]
    [InlineData(4.0, 4.0)]
    [InlineData(4.0, 1.0)]
    [InlineData(1.0, 4.0)]
    public void A_spanning_cell_cut_by_a_break_is_closed_by_its_own_borders(double top, double bottom)
    {
        // The other cells' borders are 2 pt. Where the line that ends or starts a page crosses the spanning cell — it lies
        // between two of the cell's rows — it is resolved as a line between the part of the cell above it and the part
        // below: the cell's bottom and top border compete as two cells' borders do, and the wider wins (§17.6.2.1), so
        // that line is 4 pt in the cell's column whichever side is the wide one. Where the line is the cell's own top or
        // bottom edge (a cut inside its first or last row) it is resolved as anywhere else, with what lies beyond it:
        // the neighbouring unit's spanning cell, or nothing at the table's edge — there the cell's own edge border alone.
        // Checked at every REAL cut of a spanning cell (its words on both pages) over start offsets that move every
        // break through every place in a unit. (Review 35: at offset 0 alone the only such breaks were empty strips,
        // which the row-cut rule no longer makes.) The second row of every unit has two lines, so a cut inside it
        // breaks between them: a cut where a cell has no line on the page at all is only a last resort, which the
        // break between the rows wins over (as Chromium joins its cells' break appeals into the row's).
        static IEnumerable<string> Words(Fragment fragment) => fragment switch
        {
            BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
            LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
            TextFragment text => [string.Concat(text.Glyphs.Select(glyph => glyph.Text))],
            _ => [],
        };

        // The width of the line in the spanning cell's column whose centre is the centre of the given line.
        static double SpanColumnLine(BoxFragment table, (double Left, double Top, double Right, double Bottom) line) =>
            Rules(table).Where(rule => rule.Left < 80 && Math.Abs((rule.Top + rule.Bottom) / 2 - (line.Top + line.Bottom) / 2) < Epsilon)
                .Select(rule => rule.Bottom - rule.Top)
                .Single();

        const int LastUnit = 11;
        var wider = Math.Max(top, bottom);
        var (cuts, atEdges) = (0, 0);
        for (var offset = 0.0; offset < 60; offset += 0.5)
        {
            var because = $"spanning cell's top {top}, bottom {bottom}, offset {offset}";
            var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(offset, 120, header: false, spanLine: 4, spanTop: top, spanBottom: bottom, twoLineSecondRows: true));
            result.IsSuccess.Should().BeTrue(result.IsFailure ? because + ": " + result.Error : string.Empty);
            var tables = result.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table).Single().Box).ToList();
            for (var page = 0; page + 1 < tables.Count; page++)
            {
                var spanWords = Words(tables[page]).Where(word => word.StartsWith('s')).Select(word => word[..3]).ToHashSet();
                if (!Words(tables[page + 1]).Any(word => word.StartsWith('s') && spanWords.Contains(word[..3])))
                {
                    continue;
                }

                cuts++;
                var (ending, next) = (tables[page], tables[page + 1]);
                var last = RowName(Boxes(ending, new Point(0, 0), SemanticRole.TableRow).Where(row => row.Box.Size.Height > Epsilon).MaxBy(row => row.At.Y).Box.Box);
                var first = RowName(Boxes(next, new Point(0, 0), SemanticRole.TableRow).Where(row => row.Box.Size.Height > Epsilon).MinBy(row => row.At.Y).Box.Box);

                // The page ends in the cell's first row (the line below it crosses the cell) or in its last row (the line
                // below it is the cell's bottom edge, above the next unit's spanning cell, or the table's bottom edge).
                // The line's widest segment reaches the page end; the others are centred on the same line.
                var endLine = Rules(ending).Where(rule => Math.Abs(rule.Bottom - ending.Size.Height) < Epsilon).MaxBy(rule => rule.Bottom - rule.Top);
                var expectedEnd = last[0] == 'b' && int.Parse(last[1..]) == LastUnit ? bottom : wider;
                SpanColumnLine(ending, endLine).Should().BeApproximately(expectedEnd, Epsilon, because + $": page {page + 1} ends in row {last}");

                // The next page starts above the cell's last row (crossing the cell) or, after a cut inside its first
                // row, above that row: the cell's top edge, under the previous unit's spanning cell.
                var startLine = Rules(next).Where(rule => Math.Abs(rule.Top) < Epsilon).MaxBy(rule => rule.Bottom - rule.Top);
                var expectedStart = first[0] == 'a' && int.Parse(first[1..]) == 0 ? top : wider;
                SpanColumnLine(next, startLine).Should().BeApproximately(expectedStart, Epsilon, because + $": page {page + 2} starts with row {first}");
                atEdges += expectedEnd != wider || expectedStart != wider ? 1 : 0;
            }
        }

        cuts.Should().BeGreaterThan(30, "the sweep crosses many real cuts of a spanning cell");
        if (bottom < top)
        {
            // A cut inside the last unit's last row: the page ends with the cell's own (narrower) bottom border.
            atEdges.Should().BeGreaterThan(0, "some cuts close the cell at the table's bottom edge with its own bottom border alone");
        }
    }
}
