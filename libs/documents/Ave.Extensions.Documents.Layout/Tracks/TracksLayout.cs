using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The Tracks algorithm: columns and rows of cells. It lays out CSS tables
/// (CSS 2.1 §17) and the plain row of blocks side by side that the code front-end calls <c>Row(…)</c>, which is a
/// one-row table without borders: one algorithm, two front-end elements.
/// </summary>
/// <remarks>
/// <para>The steps, each in its own type:</para>
/// <list type="number">
/// <item><see cref="TableGrid"/>: the slots every cell covers (column and row spans).</item>
/// <item><see cref="CollapsedBorders"/>: one resolved line per grid line (the collapsing border model, §17.6.2).</item>
/// <item><see cref="TrackSizing"/>: the column widths from the column kinds and the cells' intrinsic widths
/// (§17.5.2.1 fixed, §17.5.2.2 automatic, CSS Grid <c>fr</c> for fill).</item>
/// <item><see cref="TablePage"/>: the rows on one page — row heights (§17.5.3), vertical alignment (§17.5.3),
/// repeated header rows (§17.2), and where the page breaks (CSS Fragmentation 3, with keep rules by appeal).</item>
/// <item><see cref="TableFragments"/>: the fragment tree and the grid lines.</item>
/// </list>
/// </remarks>
public static class TracksLayout
{
    /// <summary>The Tracks algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">A box with <see cref="BoxStyle.Columns"/>, whose children are row groups of rows of cells.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">A <see cref="TracksBreakToken"/>, or null to start.</param>
    /// <returns>The fragment and continuation, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        if (incoming is not (null or TracksBreakToken))
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidBreakToken(box, incoming, "it is the token of another layout algorithm"));
        }

        // A token carries the grid of the table whose layout returned it; another table prepares its own.
        var token = (TracksBreakToken?)incoming;
        var preparation = token?.Prepared is { } prepared && ReferenceEquals(prepared.Table, box) && prepared.AvailableWidth == space.AvailableWidth
            ? Result<TablePreparation, Error>.Success(prepared)
            : TableGrid.Build(box).OnSuccessBind(grid => Prepare(environment, box, grid, space.AvailableWidth));
        return preparation.OnSuccessBind(table => token is not null && InvalidToken(table.Grid, token) is { } reason
            ? Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidBreakToken(box, token, reason))
            : TablePage.LayOut(new TableGeometry(environment, box, table) { IsAtPageStart = space.IsAtPageStart, IsBelowStartInsets = space.IsBelowStartInsets, ContinuesFromPageBefore = token is not null }, space, token)
            .OnSuccessMap(result => result is LayoutResult.Placed { Continuation: TracksBreakToken next } placed
                ? placed with { Continuation = next with { Prepared = table } }
                : result));
    }

    /// <summary>
    /// Why a table cannot resume from <paramref name="token"/>, or null when it can: the next row is a body row of the
    /// table or its end, and every cut cell is a cell of the table, given once, that covers the next row (it started
    /// before it, or in it when the break fell inside the row). What passed the page end below the last row
    /// (<see cref="TracksBreakToken.Passed"/>) is a finite height, not negative, and only at the table's end, painted
    /// only where something passed. What lies inside a cell is the cell's algorithm's to check. Linear in the cut cells.
    /// </summary>
    private static string? InvalidToken(TableGrid grid, TracksBreakToken token)
    {
        if (token.NextRow < grid.HeaderRowCount || token.NextRow > grid.Rows.Length)
        {
            return $"row {token.NextRow} is not a body row of the table's {grid.Rows.Length} rows";
        }

        if (!double.IsFinite(token.Passed) || token.Passed < 0 || (token.PaintsPassed && token.Passed == 0))
        {
            return $"the height that passed the page end, {token.Passed}, is not a height that can pass it{(token.PaintsPassed ? " and be painted" : string.Empty)}";
        }

        if (token.Passed > 0 && (token.NextRow < grid.Rows.Length || token.RowIsSplit))
        {
            return $"a height passes the page end only below the table's last row, not before row {token.NextRow}";
        }

        var seen = new HashSet<int>();
        foreach (var cut in token.Cells)
        {
            if (cut.Cell < 0 || cut.Cell >= grid.Cells.Length || !seen.Add(cut.Cell))
            {
                return $"cell {cut.Cell} is not a cell of the table, or is given twice";
            }

            var cell = grid.Cells[cut.Cell];
            if (cell.LastRow < token.NextRow || cell.Row > token.NextRow || (cell.Row == token.NextRow && !token.RowIsSplit))
            {
                return $"cell {cut.Cell} does not continue in row {token.NextRow}";
            }
        }

        return null;
    }

    /// <summary>
    /// The intrinsic widths of a tracks box: the sum of its columns' minimum and maximum widths
    /// (<see cref="TrackSizing.ColumnRanges"/>), plus the outer halves of the left and right grid lines.
    /// </summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">The tracks box.</param>
    /// <returns>The widths, or an error.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        TableGrid.Build(box).OnSuccessBind(grid =>
        {
            var lines = VerticalLineWidths(box, grid);
            return ColumnRanges(environment, box, grid).OnSuccessMap(ranges =>
                new IntrinsicWidths(ranges.Sum(range => range.MinContent), ranges.Sum(range => range.MaxContent))
                    .Plus((lines[0] + lines[^1]) / 2));
        });

    /// <summary>
    /// The box a cell is laid out as. In the collapsing border model a cell's border is not its own: it is half of
    /// each resolved grid line around it (§17.6.2: the row width counts "0.5 * border-width" at the outer edges and
    /// whole lines between cells). Those halves are added to the cell's padding and its own border is dropped, so
    /// <see cref="SlicedBox"/> slices them like padding at a page break and the cell paints no border (the table
    /// paints the lines). Cells have no margins (CSS 2.1 §17.5: "Internal table elements do not have margins"), so any margin is dropped as well.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="halves">Half of the line on each side: top, right, bottom, left.</param>
    /// <returns>The box to lay out.</returns>
    internal static Box CellBox(TableGrid.Cell cell, Edges halves)
    {
        var padding = cell.Box.Style.Padding;
        return cell.Box with
        {
            Style = cell.Box.Style with
            {
                Margin = Edges.Zero,
                Border = Border.None,
                Padding = new Edges(padding.Top + halves.Top, padding.Right + halves.Right, padding.Bottom + halves.Bottom, padding.Left + halves.Left),
            },
        };
    }

    /// <summary>The width of each vertical grid line: per column boundary the widest resolved line over all rows, so
    /// every row's cells line up. At the table's edges half of it is the table's left and right border, on every page:
    /// measured in Chromium 159, a 6 pt left or right border on one cell of the first row, of a later row, or of a row
    /// on the next page puts the centre of every row's outer line 3 pt inside the table's edge (cases
    /// <c>te-left-first</c>, <c>te-left-later</c>, <c>te-right-first</c>, <c>te-right-later</c>, <c>te-break</c>), and a
    /// table border wider than its cells' counts the same way (<c>te-table-border</c>). Where each cell's content
    /// starts is half of its own line (<see cref="CollapsedBorders.OwnVerticalWidth"/>), not of this one.</summary>
    internal static ImmutableArray<double> VerticalLineWidths(Box box, TableGrid grid) =>
        [.. Enumerable.Range(0, grid.ColumnCount + 1).Select(boundary =>
            Enumerable.Range(0, grid.Rows.Length).Select(row => CollapsedBorders.Vertical(box, grid, row, boundary).Width.Points).DefaultIfEmpty(0).Max())];

    /// <summary>The columns' ranges, from every cell measured with half of its own vertical lines beside it
    /// (<see cref="CollapsedBorders.OwnVerticalWidth"/>), as it is laid out.</summary>
    private static Result<ImmutableArray<IntrinsicWidths>, Error> ColumnRanges(LayoutEnvironment environment, Box box, TableGrid grid) =>
        grid.Cells
            .TraverseAll(cell => environment
                .Measure(CellBox(cell, new Edges(
                    Length.Zero,
                    (CollapsedBorders.OwnVerticalWidth(box, grid, cell, cell.Column + cell.ColumnSpan) / 2).Pt(),
                    Length.Zero,
                    (CollapsedBorders.OwnVerticalWidth(box, grid, cell, cell.Column) / 2).Pt())))
                .OnSuccessMap(widths => new TrackSizing.Contribution(cell.Column, cell.ColumnSpan, widths)))
            .OnSuccessMap(contributions => TrackSizing.ColumnRanges(box.Style.Columns, contributions));

    /// <summary>
    /// Places the columns: grid line j at x = <see cref="TableGeometry.LineX"/>[j]. The first line is centred half
    /// its width inside the table's left edge (the table's border box holds half of the outer lines, §17.6.2), and each
    /// column (a cell's border box, from line centre to line centre) is as wide as track sizing says.
    /// </summary>
    private static Result<TablePreparation, Error> Prepare(LayoutEnvironment environment, Box box, TableGrid grid, double availableWidth)
    {
        var lines = VerticalLineWidths(box, grid);
        var outer = (lines[0] + lines[^1]) / 2;
        return ColumnRanges(environment, box, grid).OnSuccessBind(ranges =>
        {
            var (widths, needed) = TrackSizing.Resolve(box.Style.Columns, ranges, availableWidth - outer, box.Style.Width);
            if (needed is { } width)
            {
                return Result<TablePreparation, Error>.Failure(LayoutErrors.ColumnsDoNotFit(box, width + outer, availableWidth));
            }

            var x = ImmutableArray.CreateBuilder<double>(widths.Length + 1);
            x.Add(lines[0] / 2);
            foreach (var columnWidth in widths)
            {
                x.Add(x[^1] + columnWidth);
            }

            var lineX = x.MoveToImmutable();
            return Result<TablePreparation, Error>.Success(new TablePreparation(box, grid, lineX, lines, lineX[^1] + lines[^1] / 2, availableWidth));
        });
    }
}

/// <summary>What a tracks box's pages share, prepared once per layout pass: the grid and the placed columns.</summary>
/// <param name="Table">The tracks box they were prepared for (compared by reference: a token of another table does not
/// carry its grid over).</param>
/// <param name="Grid">The grid.</param>
/// <param name="LineX">The x of every vertical grid line's centre, from the table's left edge; column j runs from
/// LineX[j] to LineX[j + 1].</param>
/// <param name="LineWidths">The width of every vertical grid line.</param>
/// <param name="Width">The table's border-box width.</param>
/// <param name="AvailableWidth">The width the columns were placed in; a page of another width prepares again.</param>
internal sealed record TablePreparation(Box Table, TableGrid Grid, ImmutableArray<double> LineX, ImmutableArray<double> LineWidths, double Width, double AvailableWidth);

/// <summary>A tracks box with its grid and columns placed, on the current page: everything a page of it needs.</summary>
/// <param name="Environment">The fonts, algorithms and current page.</param>
/// <param name="Box">The tracks box.</param>
/// <param name="Prepared">The grid and the columns.</param>
internal sealed record TableGeometry(LayoutEnvironment Environment, Box Box, TablePreparation Prepared)
{
    /// <summary>True when the table starts the page (<see cref="ConstraintSpace.IsAtPageStart"/>): then a cell in the
    /// page's first row with nothing placed above it starts the page too, and content of it that is taller than the page
    /// is sliced or refused (<see cref="MonolithicSlices"/>) instead of moving on.</summary>
    public bool IsAtPageStart { get; init; }

    /// <summary>True when only the top insets of boxes that start the page lie above the table
    /// (<see cref="ConstraintSpace.IsBelowStartInsets"/>): the cells of the page's first row are below start insets too.</summary>
    public bool IsBelowStartInsets { get; init; }

    /// <summary>True when the table continues from the page before: it is laid out from a break token.</summary>
    public bool ContinuesFromPageBefore { get; init; }

    /// <summary>True on a later page where content taller than the page area starts the page right below the repeated
    /// header rows (<see cref="TablePage"/>, remarks): the cells of the page's first body row are laid out as starting
    /// the page.</summary>
    public bool SlicesBelowHeader { get; init; }

    /// <summary>The <see cref="ConstraintSpace.FragmentainerBlockSize"/> of the cells' content, on the page and in the
    /// measurements with unlimited height alike: the table's, less the room its repeated header rows take on this page
    /// (<see cref="TablePage"/>, RepeatsHeader and Reserved: on a later page also the outer half of the table's top grid
    /// line), never below zero.</summary>
    public double CellFragmentainerBlockSize { get; init; }

    /// <summary>The grid.</summary>
    public TableGrid Grid => Prepared.Grid;

    /// <summary>The x of every vertical grid line's centre.</summary>
    public ImmutableArray<double> LineX => Prepared.LineX;

    /// <summary>The width of every vertical grid line.</summary>
    public ImmutableArray<double> LineWidths => Prepared.LineWidths;

    /// <summary>The table's border-box width.</summary>
    public double Width => Prepared.Width;

    /// <summary>The width of a cell's border box: its columns from line centre to line centre.</summary>
    public double CellWidth(TableGrid.Cell cell) => LineX[cell.Column + cell.ColumnSpan] - LineX[cell.Column];

    /// <summary>The width of a horizontal grid line between two rows on a page: the widest of its column segments.</summary>
    public double HorizontalWidth(int? above, int? below, CollapsedBorders.LineContext context) =>
        Enumerable.Range(0, Grid.ColumnCount)
            .Select(column => CollapsedBorders.Horizontal(Box, Grid, above, below, column, context).Width.Points)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>A cell's own part of a horizontal grid line: the widest of the line's segments over the cell's columns
    /// (<see cref="TableSegments.CellLayoutBox"/>).</summary>
    public double HorizontalWidth(int? above, int? below, CollapsedBorders.LineContext context, TableGrid.Cell cell) =>
        Enumerable.Range(cell.Column, cell.ColumnSpan)
            .Max(column => CollapsedBorders.Horizontal(Box, Grid, above, below, column, context).Width.Points);
}
