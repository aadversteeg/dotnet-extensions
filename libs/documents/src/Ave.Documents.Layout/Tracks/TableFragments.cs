using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Layout;

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
        var bottom = content.Bottom + (outcome.BottomLine is { } line ? table.HorizontalWidth(line.Above, line.Below, line.Context) / 2 : 0);
        ValueList<PlacedFragment> children =
        [
            .. Groups(table, content),
            .. HorizontalRules(table, lines),
            .. VerticalRules(table, content.Rows),
        ];
        return new BoxFragment(table.Box, new Size(table.Width, bottom), children);
    }

    /// <summary>Consecutive rows of the same group (and the same repeated flag) become one group fragment.</summary>
    private static IEnumerable<PlacedFragment> Groups(TableGeometry table, TablePage.Content content)
    {
        var innerWidth = table.LineX[^1] - table.LineX[0];
        var runs = new List<List<TablePage.PlacedRow>>();
        foreach (var row in content.Rows)
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

    /// <summary>A row with the cells drawn with it; a cell that spans rows reaches below the row's own box.</summary>
    private static BoxFragment Row(TableGeometry table, TablePage.Content content, TablePage.PlacedRow row, double innerWidth)
    {
        ValueList<PlacedFragment> cells =
        [
            .. content.Cells.Where(cell => cell.Row == row.Row)
                .Select(cell => new PlacedFragment(new Point(table.LineX[table.Grid.Cells[cell.Cell].Column] - table.LineX[0], 0), cell.Fragment)),
        ];
        return new BoxFragment(table.Grid.Rows[row.Row].Box, new Size(innerWidth, row.Height), cells);
    }

    /// <summary>
    /// The horizontal lines: per line, runs of columns with the same resolved border become one rectangle, centred on
    /// the line and reaching half the width of the vertical lines at both ends so that the corners are filled.
    /// </summary>
    private static IEnumerable<PlacedFragment> HorizontalRules(TableGeometry table, ImmutableList<TablePage.LineAt> lines)
    {
        foreach (var line in lines)
        {
            var sides = Enumerable.Range(0, table.Grid.ColumnCount)
                .Select(column => CollapsedBorders.Horizontal(table.Box, table.Grid, line.Above, line.Below, column, line.Context))
                .ToImmutableArray();
            foreach (var (first, last, side) in Runs(sides))
            {
                var left = table.LineX[first] - table.LineWidths[first] / 2;
                var right = table.LineX[last + 1] + table.LineWidths[last + 1] / 2;
                var width = side.Width.Points;
                yield return new PlacedFragment(new Point(left, line.Y - width / 2), new RuleFragment(side.Color, new Size(right - left, width)));
            }
        }
    }

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
