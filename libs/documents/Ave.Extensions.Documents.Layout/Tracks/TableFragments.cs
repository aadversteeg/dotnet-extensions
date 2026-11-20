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
        var lines = outcome.BottomLine is { } bottomLine ? content.Lines.Add(bottomLine) : content.Lines;
        var bottom = content.Bottom + (outcome.BottomLine is { Part: TablePage.LinePart.Whole } line ? table.HorizontalWidth(line.Above, line.Below, line.Context) / 2 : 0);
        ValueList<PlacedFragment> children =
        [
            .. Groups(table, content),
            .. HorizontalRules(table, lines),
            .. VerticalRules(table, content.Rows),
        ];
        return new BoxFragment(table.Box, new Size(table.Width, bottom), children);
    }

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
        var innerWidth = table.LineX[^1] - table.LineX[0];
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
                new Point(table.LineX[0], top),
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
                    new Point(table.LineX[table.Grid.Cells[cell.Cell].Column] - table.LineX[0], topOf[cell.TopRow] - row.Top),
                    cell.Fragment)),
        ];
        return new BoxFragment(table.Grid.Rows[row.Row].Box, new Size(innerWidth, row.Height), cells);
    }

    /// <summary>
    /// The horizontal lines: per line, runs of columns with the same resolved border become one rectangle, centred on
    /// the line (or, at a break between rows, its half on this page, as thick as <see cref="HalfThickness"/> says) and
    /// reaching half the width of the vertical lines at both ends so that the corners are filled. A line whose page
    /// draws no part of it (<see cref="TablePage.LinePart.Unpainted"/>) gives no rectangle.
    /// </summary>
    private static IEnumerable<PlacedFragment> HorizontalRules(TableGeometry table, ImmutableList<TablePage.LineAt> lines)
    {
        var halves = table.Environment.Profile.SplitLineHalves;
        foreach (var line in lines.Where(line => line.Part != TablePage.LinePart.Unpainted))
        {
            var sides = Enumerable.Range(0, table.Grid.ColumnCount)
                .Select(column => CollapsedBorders.Horizontal(table.Box, table.Grid, line.Above, line.Below, column, line.Context))
                .ToImmutableArray();
            foreach (var (first, last, side) in Runs(sides))
            {
                var left = table.LineX[first] - table.LineWidths[first] / 2;
                var right = table.LineX[last + 1] + table.LineWidths[last + 1] / 2;
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

    /// <summary>The vertical lines: per column boundary, consecutive rows with the same resolved border become one
    /// rectangle from the top of the first row to the bottom of the last, centred on the line.</summary>
    private static IEnumerable<PlacedFragment> VerticalRules(TableGeometry table, ImmutableList<TablePage.PlacedRow> rows)
    {
        for (var boundary = 0; boundary <= table.Grid.ColumnCount; boundary++)
        {
            var line = boundary;
            var sides = rows.Select(row => CollapsedBorders.Vertical(table.Box, table.Grid, row.Row, line)).ToImmutableArray();
            foreach (var (first, last, side) in Runs(sides))
            {
                var width = side.Width.Points;
                var top = rows[first].Top;
                var bottom = rows[last].Top + rows[last].Height;
                yield return new PlacedFragment(new Point(table.LineX[line] - width / 2, top), new RuleFragment(side.Color, new Size(width, bottom - top)));
            }
        }
    }

    /// <summary>Runs of equal, visible borders: (first index, last index, border).</summary>
    private static IEnumerable<(int First, int Last, BorderSide Side)> Runs(ImmutableArray<BorderSide> sides)
    {
        var start = 0;
        for (var index = 1; index <= sides.Length; index++)
        {
            if (index == sides.Length || sides[index] != sides[start])
            {
                if (sides[start].IsVisible)
                {
                    yield return (start, index - 1, sides[start]);
                }

                start = index;
            }
        }
    }
}
