using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The fragment of one page of a tracks box: the table's box, inside it a box per row group, inside those a box per
/// row, inside those the cells — so the structure markers of the page model keep the table's structure (table, row
/// group, row, cell) for tagged PDF later — and after them the resolved grid lines, painted on top of the cells.
/// </summary>
internal static class TableFragments
{
    /// <summary>Builds the fragment for a page's outcome.</summary>
    /// <param name="table">The table with its columns.</param>
    /// <param name="outcome">What the page holds.</param>
    /// <returns>The table's fragment, as wide as the table and as tall as its rows plus the outer halves of the top
    /// and bottom lines.</returns>
    public static BoxFragment Build(TableGeometry table, TablePage.Outcome outcome)
    {
        var content = outcome.Content;
        if (table.IsSeparated)
        {
            return Separated(table, outcome);
        }

        var lines = outcome.BottomLine is { } bottomLine ? content.Lines.Add(bottomLine) : content.Lines;
        var bottom = content.Bottom + (outcome.BottomLine is { Part: TablePage.LinePart.Whole } line ? table.HorizontalWidth(line.Above, line.Below, line.Context) / 2 : 0);
        ValueList<PlacedFragment> children =
        [
            .. Groups(table, content),
            .. HorizontalRules(table, lines),
            .. VerticalRules(table, content.Rows, lines),
        ];
        return new BoxFragment(table.Box, new Size(table.Width, bottom), children);
    }

    /// <summary>
    /// The fragment of a table in the separated borders model, complete on its page (<see cref="TablePage"/> refuses one
    /// that breaks): the table's border box down to the spacing, padding and border below its last row
    /// (<see cref="TableGeometry.BottomInset"/>), painting its own border and its background under it as a block box does,
    /// the cells over them, each painting its own border; no grid lines (CSS 2.1 §17.6.1; case <c>ts-paint</c>: the table's
    /// background under its whole border box, spacing included, then its border, then each cell's background and border).
    /// </summary>
    private static BoxFragment Separated(TableGeometry table, TablePage.Outcome outcome)
    {
        var content = outcome.Content;
        return new BoxFragment(table.Box, new Size(table.Width, content.Bottom + table.BottomInset), [.. Groups(table, content)], table.Box.Style.Border);
    }

    /// <summary>
    /// The grid lines of a page's fragment that pass <paramref name="end"/>, each cut there and moved up by it: the part
    /// of the table that passed the page end, as the next page draws it at its top (<c>TablePage.Passed</c>). Chromium
    /// draws the passed lines again on the next page offset up by the page area's height, clipped to that page's area, so
    /// only the part below the page end shows (cases <c>a2-row-end</c>, <c>a2-row-end-after</c>, <c>a2-row-end-6pt-after</c>:
    /// the side lines and the bottom line from the top of page 3, as tall as what passed).
    /// </summary>
    /// <param name="fragment">The page's fragment, with its grid lines as direct children.</param>
    /// <param name="end">The page end, from the table's top edge.</param>
    /// <returns>The passed lines, from the next page's top.</returns>
    internal static ValueList<PlacedFragment> Below(BoxFragment fragment, double end) =>
    [
        .. fragment.Children
            .Where(child => child.Fragment is RuleFragment && child.Offset.Y + child.Fragment.Size.Height > end + Tolerance)
            .Select(child =>
            {
                var rule = (RuleFragment)child.Fragment;
                var top = Math.Max(child.Offset.Y, end);
                return new PlacedFragment(new Point(child.Offset.X, top - end), new RuleFragment(rule.Color, new Size(rule.Size.Width, child.Offset.Y + rule.Size.Height - top)));
            }),
    ];

    /// <summary>
    /// Consecutive rows of the same group (and the same repeated flag) become one group fragment. Every cell is drawn
    /// inside the row it starts in, so that the structure markers keep it a kid of that row on every page (ISO 32000-1
    /// §14.8.4.3.4: a TD or TH is a kid of its TR). A cell that spans rows and continues from an earlier page has its
    /// row's box on this page too: a continuation of that row, zero high at the top of the cell's part, from which the
    /// cell reaches down over the rows it covers here — as a spanning cell reaches below its own row on the page it
    /// starts on.
    /// </summary>
    private static IEnumerable<PlacedFragment> Groups(TableGeometry table, TablePage.Content content)
    {
        var innerWidth = table.GridEnd - table.GridStart;
        var runs = new List<List<TablePage.PlacedRow>>();
        foreach (var row in WithContinuedRows(table.Grid, content))
        {
            var group = table.Grid.Rows[row.Row].Group;
            if (runs.Count > 0 && table.Grid.Rows[runs[^1][0].Row].Group == group && runs[^1][0].IsRepeated == row.IsRepeated)
            {
                runs[^1].Add(row);
            }
            else
            {
                runs.Add([row]);
            }
        }

        foreach (var run in runs)
        {
            var top = run[0].Top;
            var height = run[^1].Top + run[^1].Height - top;
            ValueList<PlacedFragment> rows = [.. run.Select(row => new PlacedFragment(new Point(0, row.Top - top), Row(table, content, row, innerWidth)))];
            yield return new PlacedFragment(
                new Point(table.GridStart, top),
                new BoxFragment(table.Grid.Rows[run[0].Row].Group, new Size(innerWidth, height), rows, IsRepeated: run[0].IsRepeated));
        }
    }

    /// <summary>
    /// The page's rows, plus a zero-high continuation of every row that is not on the page but has a cell on it (a
    /// cell that spans rows and continues from an earlier page), at the top of that cell, in row order. A row group
    /// holds every row a cell spans (<see cref="TableGrid"/> refuses a span past its group), so a continued row joins
    /// the run of its group.
    /// </summary>
    private static IEnumerable<TablePage.PlacedRow> WithContinuedRows(TableGrid grid, TablePage.Content content)
    {
        var placed = content.Rows.Select(row => row.Row).ToHashSet();
        var topOf = content.Rows.ToDictionary(row => row.Row, row => row.Top);
        var continued = content.Cells
            .Where(cell => !placed.Contains(grid.Cells[cell.Cell].Row))
            .GroupBy(cell => grid.Cells[cell.Cell].Row)
            .Select(cells => new TablePage.PlacedRow(cells.Key, cells.Min(cell => topOf[cell.TopRow]), 0, IsRepeated: false));
        return content.Rows.Concat(continued).OrderBy(row => row.Row);
    }

    /// <summary>A row with the cells that start in it; a cell that spans rows reaches below the row's own box.</summary>
    private static BoxFragment Row(TableGeometry table, TablePage.Content content, TablePage.PlacedRow row, double innerWidth)
    {
        var topOf = content.Rows.ToDictionary(placed => placed.Row, placed => placed.Top);
        ValueList<PlacedFragment> cells =
        [
            .. content.Cells.Where(cell => table.Grid.Cells[cell.Cell].Row == row.Row)
                .Select(cell => new PlacedFragment(
                    new Point(table.ColumnX[table.Grid.Cells[cell.Cell].Column] - table.GridStart, topOf[cell.TopRow] - row.Top),
                    cell.Fragment)),
        ];
        return new BoxFragment(table.Grid.Rows[row.Row].Box, new Size(innerWidth, row.Height), cells);
    }

    /// <summary>
    /// The horizontal lines: per line, a segment per column, from corner to corner as <see cref="Corner"/> says (over
    /// the corner where it paints it, else up to the corner's near side), centred on the line (or, at a break between
    /// rows, its half on this page, as thick as <see cref="HalfThickness"/> says); consecutive segments with the same
    /// resolved border that meet become one rectangle. A line whose page draws no part of it
    /// (<see cref="TablePage.LinePart.Unpainted"/>) gives no rectangle.
    /// </summary>
    private static IEnumerable<PlacedFragment> HorizontalRules(TableGeometry table, ImmutableList<TablePage.LineAt> lines)
    {
        var halves = table.Environment.Profile.SplitLineHalves;
        foreach (var line in lines.Where(line => line.Part != TablePage.LinePart.Unpainted))
        {
            var corners = Enumerable.Range(0, table.Grid.ColumnCount + 1).Select(boundary => Corner(table, line, boundary)).ToImmutableArray();
            var segments = Enumerable.Range(0, table.Grid.ColumnCount)
                .Select(column => (
                    Start: table.LineX(column) + (corners[column].CoversRight ? -corners[column].Width : corners[column].Width) / 2,
                    End: table.LineX(column + 1) + (corners[column + 1].CoversLeft ? corners[column + 1].Width : -corners[column + 1].Width) / 2,
                    Side: CollapsedBorders.Horizontal(table.Box, table.Grid, line.Above, line.Below, column, line.Context)));
            foreach (var (left, right, side) in Joined(segments))
            {
                var width = side.Width.Points;
                var (top, height) = line.Part switch
                {
                    TablePage.LinePart.UpperHalf => (line.Y - width / 2, HalfThickness(width, halves)),
                    TablePage.LinePart.LowerHalf => (line.Y, HalfThickness(width, halves)),
                    _ => (line.Y - width / 2, width),
                };
                yield return new PlacedFragment(new Point(left, top), new RuleFragment(side.Color, new Size(right - left, height)));
            }
        }
    }

    /// <summary>
    /// How thick the half of a line of width <paramref name="width"/> is drawn at a break between rows: half the width,
    /// or under <see cref="SplitLineHalves.AtLeastOnePixel"/> at least one CSS pixel (0.75 pt), from the half's top edge
    /// down, as Chromium prints it (<see cref="SplitLineHalves.AtLeastOnePixel"/> names the measured cases). Paint only:
    /// the half's place and the room layout keeps for it stay as they are.
    /// </summary>
    internal static double HalfThickness(double width, SplitLineHalves halves) =>
        halves == SplitLineHalves.AtLeastOnePixel ? Math.Max(width / 2, OnePixel) : width / 2;

    /// <summary>One CSS pixel, 1/96 inch, in points (CSS Values 4 §6.2).</summary>
    private const double OnePixel = 0.75;

    /// <summary>
    /// The vertical lines: per column boundary, a segment per row, centred on the line, from the corner above to the
    /// corner below as <see cref="Corner"/> says; consecutive rows with the same resolved border whose segments meet
    /// become one rectangle. At a break between rows (a half line), the segment ends or starts at the line's centre, the
    /// page's edge, whoever paints the corner: so Chromium prints it (cases <c>te-break</c>, <c>tj-break</c>,
    /// <c>b4-row-span</c>), with the half line painted over it — except under the half line of the column left of it at
    /// the top of a page, which Chromium paints first.
    /// </summary>
    private static IEnumerable<PlacedFragment> VerticalRules(TableGeometry table, ImmutableList<TablePage.PlacedRow> rows, ImmutableList<TablePage.LineAt> lines)
    {
        var halves = table.Environment.Profile.SplitLineHalves;
        for (var boundary = 0; boundary <= table.Grid.ColumnCount; boundary++)
        {
            var line = boundary;
            var segments = rows.Select(row =>
            {
                var side = CollapsedBorders.Vertical(table.Box, table.Grid, row.Row, line);
                var top = lines.FirstOrDefault(placed => placed.Below == row.Row && placed.Part != TablePage.LinePart.Unpainted) is { } above
                    ? Top(table, above, line, side, halves)
                    : row.Top;
                var bottom = lines.FirstOrDefault(placed => placed.Above == row.Row && placed.Part != TablePage.LinePart.Unpainted) is { } below
                    ? Bottom(table, below, line, side)
                    : row.Top + row.Height;
                return (Start: top, End: bottom, Side: side);
            });
            foreach (var (top, bottom, side) in Joined(segments))
            {
                var width = side.Width.Points;
                yield return new PlacedFragment(new Point(table.LineX(line) - width / 2, top), new RuleFragment(side.Color, new Size(width, bottom - top)));
            }
        }
    }

    /// <summary>Where a vertical segment starts at the line above its row: over the corner when it covers it (up to the
    /// line's centre when a horizontal line covers the corner too, as before the corners were resolved: the two look
    /// the same), else at the corner's lower side.</summary>
    private static double Top(TableGeometry table, TablePage.LineAt line, int boundary, BorderSide side, SplitLineHalves halves)
    {
        var corner = Corner(table, line, boundary);
        if (line.Part == TablePage.LinePart.LowerHalf)
        {
            return corner.CoversRight && corner.Look != side ? line.Y + HalfThickness(corner.Height, halves) : line.Y;
        }

        return corner.CoversBelow
            ? corner.CoversLeft || corner.CoversRight ? line.Y : line.Y - corner.Height / 2
            : line.Y + corner.Height / 2;
    }

    /// <summary>Where a vertical segment ends at the line below its row, as <see cref="Top"/> for the corner below.</summary>
    private static double Bottom(TableGeometry table, TablePage.LineAt line, int boundary, BorderSide side)
    {
        var corner = Corner(table, line, boundary);
        if (line.Part == TablePage.LinePart.UpperHalf)
        {
            return (corner.CoversLeft || corner.CoversRight) && corner.Look != side ? line.Y - corner.Height / 2 : line.Y;
        }

        return corner.CoversAbove
            ? corner.CoversLeft || corner.CoversRight ? line.Y : line.Y + corner.Height / 2
            : line.Y - corner.Height / 2;
    }

    /// <summary>
    /// A corner where grid lines meet: the area as wide as the widest vertical line there and as high as the widest
    /// horizontal one, centred on the crossing, the border it shows, and which of the (up to) four lines that meet there
    /// cover it: the lines that paint it and every other line with the same border (which shows the same pixels).
    /// </summary>
    /// <param name="Width">The widest vertical line at the corner.</param>
    /// <param name="Height">The widest horizontal line at the corner.</param>
    /// <param name="Look">The border the corner shows.</param>
    /// <param name="CoversAbove">True when the vertical line above covers the corner.</param>
    /// <param name="CoversLeft">True when the horizontal line on the left covers it.</param>
    /// <param name="CoversBelow">True when the vertical line below covers it.</param>
    /// <param name="CoversRight">True when the horizontal line on the right covers it.</param>
    private readonly record struct CornerLines(double Width, double Height, BorderSide Look, bool CoversAbove, bool CoversLeft, bool CoversBelow, bool CoversRight);

    /// <summary>
    /// Which line paints a corner. CSS 2.1 §17.6.2.1 resolves the conflict between the borders of one edge and
    /// leaves the corners to the implementation; the corners here follow Chromium 159's print. The line that wins
    /// at the corner, by the same order (wider; then cell over row over row group over table; then the box earlier
    /// in the table, <see cref="CollapsedBorders.Edge.Beats"/>), paints it, and so does every other line at the
    /// corner that is as wide and comes from the same box; every other line stops at the corner's near side. Where
    /// two lines paint it in different colours, the one Chromium paints last shows: per row, the vertical line left
    /// of a column before the horizontal line over it, so the line right of the corner over the one below it, over
    /// the one on the left, over the one above; here the corner shows that one's border, a line with the same
    /// border covers it too (the same pixels) and every line with another border stops at its side. Measured cases:
    /// <c>tj-same-cell</c> (two sides of one cell in two colours at every corner), <c>tj-order</c> (equal lines of
    /// the cell above right and below left, of the left and the right cell), <c>tj-cell-table</c> (a cell's line
    /// over the table's), <c>tj-outer</c>, <c>tj-four</c>, <c>tj-right-edge</c>, <c>tj-top-edge</c> (lines of
    /// different widths at outer and inner corners), and the outer borders of <c>te-*</c>.
    /// </summary>
    private static CornerLines Corner(TableGeometry table, TablePage.LineAt line, int boundary)
    {
        var grid = table.Grid;
        var isPainted = line.Part != TablePage.LinePart.Unpainted;
        CollapsedBorders.Edge Horizontal(int column) => isPainted && column >= 0 && column < grid.ColumnCount
            ? CollapsedBorders.HorizontalEdge(table.Box, grid, line.Above, line.Below, column, line.Context)
            : CollapsedBorders.Edge.None;
        CollapsedBorders.Edge Vertical(int? row) => row is { } r ? CollapsedBorders.VerticalEdge(table.Box, grid, r, boundary) : CollapsedBorders.Edge.None;

        // In Chromium's paint order: above, left, below, right.
        ImmutableArray<CollapsedBorders.Edge> edges = [Vertical(line.Above), Horizontal(boundary - 1), Vertical(line.Below), Horizontal(boundary)];
        var visible = edges.Where(edge => edge.Side.IsVisible).ToList();
        if (visible.Count == 0)
        {
            return new CornerLines(0, 0, BorderSide.None, false, false, false, false);
        }

        var winner = visible.Aggregate((best, edge) => edge.Beats(best) ? edge : best);
        var painting = edges.Select(edge => edge.Side.IsVisible && edge.Ties(winner)).ToArray();
        var look = edges[Array.LastIndexOf(painting, true)].Side;

        // At a break between rows the vertical lines end at the page's edge, so they do not fill the corner: there a
        // horizontal line covers it only where it paints it (case b4-row-span: a line beside a cut spanning cell stops
        // at the vertical line of that cell, which ends at the page's edge, and the corner stays empty).
        var isHalf = line.Part is TablePage.LinePart.UpperHalf or TablePage.LinePart.LowerHalf;
        var covers = edges.Select((edge, index) => edge.Side.IsVisible && edge.Side == look && (painting[index] || !(isHalf && index % 2 == 1))).ToArray();
        return new CornerLines(
            Math.Max(edges[0].Side.Width.Points, edges[2].Side.Width.Points),
            Math.Max(edges[1].Side.Width.Points, edges[3].Side.Width.Points),
            look,
            covers[0],
            covers[1],
            covers[2],
            covers[3]);
    }

    /// <summary>Consecutive segments with the same visible border that meet or overlap, joined: (start, end, border).</summary>
    private static IEnumerable<(double Start, double End, BorderSide Side)> Joined(IEnumerable<(double Start, double End, BorderSide Side)> segments)
    {
        (double Start, double End, BorderSide Side)? run = null;
        foreach (var segment in segments)
        {
            if (run is { } current && segment.Side == current.Side && segment.Start <= current.End + Tolerance)
            {
                run = (current.Start, Math.Max(current.End, segment.End), current.Side);
                continue;
            }

            if (run is { } done && done.Side.IsVisible && done.End > done.Start)
            {
                yield return done;
            }

            run = segment;
        }

        if (run is { } final && final.Side.IsVisible && final.End > final.Start)
        {
            yield return final;
        }
    }

    /// <summary>How far apart two segment ends may be and still meet (rounding of the same sums).</summary>
    private const double Tolerance = 1e-9;
}
