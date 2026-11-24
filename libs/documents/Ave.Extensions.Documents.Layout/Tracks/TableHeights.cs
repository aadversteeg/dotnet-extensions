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
/// Over a page break a row with a least height is cut at the page end like any row, also where its cells' content ends on
/// the page, and its part on the next page is the rest of that height: the least height less the part from the row's top
/// to the page end (<see cref="TracksBreakToken.RowRest"/>), with its content at the top of the part where it is cut
/// (<see cref="CutRowAlignment.TopOnEveryPage"/>). Measured in both border models (cases <c>tg-break</c>: a row of 60 pt
/// cut after 43.125 pt goes on 16.875 pt; <c>tg-break-rows</c>, <c>tg-break-rows-separated</c>: rows of 30 pt and of a
/// 24 pt cell below a header that repeats; <c>tg-break-cut-two</c>, <c>tg-break-cut-align</c>, <c>tg-break-cut-cell</c>;
/// <c>tg-break-tall</c>: rows of 250 and 400 pt over two and four pages; <c>tg-break-table</c>: the shares of a table's
/// height). Where the page end cuts the text of a cell, the row is cut between two of its lines as a row without a height
/// is, by the cell's orphans and widows (a cell of two lines whose second line does not fit moves the row whole to the next
/// page), and its part on the next page is the rest of its height or, where that is taller, the rest of its content
/// (cases <c>tg-text-late</c>, <c>tg-text-three</c>: the rest of the height, printed 15.375 and 30 pt; <c>tg-text-equal</c>,
/// <c>tg-text-header</c>: the rest of the text, printed 28.125 and 28.5 pt, where the rest of the height is about 21 and 25 pt;
/// <c>tg-text-two</c>, <c>tg-text-align</c>: the row moves). Not measured, so refused (<see cref="LayoutErrors.TableBreak"/>):
/// a table with heights where a cell over several rows is cut.
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

    /// <summary>The result, or the refusal of a table with a height of its own, of a row or of a cell that breaks where no
    /// case measured it (see remarks): a cut cell over several rows.</summary>
    /// <param name="box">The tracks box.</param>
    /// <param name="grid">Its grid.</param>
    /// <param name="space">The table's space.</param>
    /// <param name="result">Its layout on this page.</param>
    /// <returns>The result, or the error.</returns>
    public static Result<LayoutResult, Error> Fits(Box box, TableGrid grid, ConstraintSpace space, LayoutResult result) =>
        result is not LayoutResult.Placed { Continuation: TracksBreakToken next }
        || !next.Cells.Any(cut => grid.Cells[cut.Cell].Row != grid.Cells[cut.Cell].LastRow)
        || !HasAnyHeight(box)
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
