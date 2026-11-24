using System;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The heights of a table, its rows and its cells (<see cref="BoxStyle.TableHeight"/>), as Chromium 159 prints them. A row's
/// and a cell's height are least heights of the row (<see cref="TableSegments"/>, Least). A table's height is a least
/// height of its border box: what it asks beyond the rows' own heights goes to the body rows that have no height of their
/// own or of a cell, in proportion to their heights, each floored to the layout's step and the last one taking what is
/// left; header rows and rows with a height keep theirs (cases <c>tg-table</c> c: a row of 40 pt keeps 39.996 pt and the two
/// others take 54.621 and 54.633 pt of 150 pt; d: the header row keeps 17.484 pt; f: rows of 18.234, 30.984 and 43.734 pt
/// become 25.887, 43.992 and 62.121 pt). A height less than the rows' has no effect (<c>tg-table-small</c>).
/// </summary>
/// <remarks>
/// A table with any such height is drawn only where it fits whole on its page: Chromium breaks a row inside its height and
/// goes on with the rest of the height on the next page (case <c>tg-break</c>: a row of 60 pt is cut after 43.25 pt, a row
/// of 250 pt runs over two pages), which this version does not draw (<see cref="LayoutErrors.TableBreak"/>).
/// </remarks>
internal static class TableHeights
{
    /// <summary>The preparation with each body row's share of the table's height (<see cref="TablePreparation.RowMinimums"/>),
    /// from the row heights of a layout of the whole table with unlimited height; the preparation itself for a table without
    /// a height, or one the rows already fill.</summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">The tracks box.</param>
    /// <param name="table">Its preparation.</param>
    /// <param name="space">The table's space on its first page.</param>
    /// <param name="grid">Lays the grid out in a space.</param>
    /// <returns>The preparation, or an error.</returns>
    public static Result<TablePreparation, Error> Shared(
        LayoutEnvironment environment, Box box, TablePreparation table, ConstraintSpace space, Func<ConstraintSpace, Result<LayoutResult, Error>> grid)
    {
        if (box.Style.TableHeight is not { Points: var given })
        {
            return Result<TablePreparation, Error>.Success(table);
        }

        var unlimited = new ConstraintSpace(space.AvailableWidth, double.PositiveInfinity, IsAtPageStart: false, IsNewFormattingContext: true) { FragmentainerBlockSize = space.FragmentainerBlockSize };
        return grid(unlimited).OnSuccessMap(result =>
        {
            if (result is not LayoutResult.Placed { Continuation: null } whole)
            {
                return table;
            }

            var step = TracksLayout.Step(environment);
            var extra = TrackSizing.Floor(given, step) - whole.Fragment.Size.Height;
            var heights = RowHeights(whole.Fragment);
            var receivers = Enumerable.Range(table.Grid.HeaderRowCount, Math.Max(0, table.Grid.Rows.Length - table.Grid.HeaderRowCount))
                .Where(row => row < heights.Length && !HasHeight(table.Grid, row))
                .ToList();
            if (extra <= Fitting.TolerancePoints || receivers.Count == 0 || heights.Length != table.Grid.Rows.Length)
            {
                return table;
            }

            var natural = receivers.Sum(row => heights[row]);
            var goal = natural + extra;
            var minimums = new double[heights.Length];
            var placed = 0.0;
            foreach (var row in receivers.Take(receivers.Count - 1))
            {
                minimums[row] = TrackSizing.Floor(natural > 0 ? heights[row] * goal / natural : goal / receivers.Count, step);
                placed += minimums[row];
            }

            minimums[receivers[^1]] = goal - placed;
            return table with { RowMinimums = [.. minimums] };
        });
    }

    /// <summary>The result, or the refusal of a table with a height of its own, of a row or of a cell that does not fit
    /// whole where it starts (see remarks).</summary>
    /// <param name="box">The tracks box.</param>
    /// <param name="space">The table's space.</param>
    /// <param name="result">Its layout on this page.</param>
    /// <returns>The result, or the error.</returns>
    public static Result<LayoutResult, Error> Fits(Box box, ConstraintSpace space, LayoutResult result) =>
        result is LayoutResult.Placed { Continuation: null } || !HasAnyHeight(box)
            ? Result<LayoutResult, Error>.Success(result)
            : Result<LayoutResult, Error>.Failure(LayoutErrors.HeightBreak(box, space.AvailableHeight));

    /// <summary>True when the row or a cell that starts in it has a height.</summary>
    private static bool HasHeight(TableGrid grid, int row) =>
        grid.Rows[row].Box.Style.TableHeight is not null
        || grid.CellsStartingIn[row].Any(cell => grid.Cells[cell].Box.Style.TableHeight is not null);

    /// <summary>True when the table, one of its rows or one of its cells has a height. Walks the table's groups, rows and
    /// cells, no deeper.</summary>
    private static bool HasAnyHeight(Box table) =>
        table.Style.TableHeight is not null
        || table.Children.OfType<Box>().SelectMany(group => group.Children.OfType<Box>())
            .Any(row => row.Style.TableHeight is not null || row.Children.OfType<Box>().Any(cell => cell.Style.TableHeight is not null));

    /// <summary>The height of every row in the fragment of a whole table, in order: the rows of its row groups.</summary>
    private static ImmutableArray<double> RowHeights(BoxFragment table) =>
        [.. table.Children.Select(child => child.Fragment).OfType<BoxFragment>()
            .Where(group => group.Box.Style.TableDisplay is TableDisplay.HeaderGroup or TableDisplay.RowGroup)
            .SelectMany(group => group.Children.Select(child => child.Fragment).OfType<BoxFragment>().Where(row => row.Box.Style.TableDisplay == TableDisplay.Row))
            .Select(row => row.Size.Height)];
}
