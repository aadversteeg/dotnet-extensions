using System;
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
/// <item><see cref="CollapsedBorders"/>: one resolved line per grid line (the collapsing border model, §17.6.2). In the
/// separated borders model (§17.6.1, <see cref="BoxStyle.BorderModel"/>) there are no grid lines: each cell keeps its own
/// border, and <see cref="BoxStyle.BorderSpacing"/> lies between the columns and between the rows.</item>
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
    /// (<see cref="TracksBreakToken.Passed"/>) is a finite height, not negative, and never inside a row, painted only
    /// where something passed. What lies inside a cell is the cell's algorithm's to check. Linear in the cut cells.
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

        if (token.Passed > 0 && token.RowIsSplit)
        {
            return $"a height passes the page end only below a row placed whole, not inside row {token.NextRow}";
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
    /// (<see cref="TrackSizing.ColumnRanges"/>), plus the outer halves of the left and right grid lines; in the separated
    /// borders model plus the table's border and padding and the spacing left of, between and right of the columns
    /// (<see cref="Insets"/>).
    /// </summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">The tracks box.</param>
    /// <returns>The widths, or an error.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        TableGrid.Build(box).OnSuccessBind(grid =>
        {
            var lines = VerticalLineWidths(box, grid);
            return ColumnRanges(environment, box, grid).OnSuccessMap(sized =>
            {
                var least = sized.Ranges.Sum(range => range.MinContent);
                var most = Math.Max(least, Math.Max(sized.Ranges.Sum(range => range.MaxContent), TrackSizing.PercentWidth(sized.Columns, sized.Ranges) ?? 0));
                var widths = new IntrinsicWidths(least, most).Plus(Outer(box, lines, sized.Ranges.Length));
                return box.Style.Width is BoxWidth.Fixed { Width.Points: var given }
                    ? new IntrinsicWidths(Math.Max(given, widths.MinContent), Math.Max(given, widths.MinContent))
                    : widths;
            });
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

    /// <summary>True for a table in the separated borders model (CSS 2.1 §17.6.1): no grid lines, each cell has its own
    /// border, and the table its own border, padding and background.</summary>
    internal static bool IsSeparated(Box box) => box.Style.BorderModel == TableBorderModel.Separate;

    /// <summary>
    /// The room left of the first column, right of the last, and between two columns. In the collapsing border model the
    /// outer halves of the outer vertical grid lines, and no gap (§17.6.2). In the separated borders model the table's
    /// border and padding and one horizontal border spacing on each side, and one spacing between two columns: CSS 2.1
    /// §17.6.1, measured in Chromium 159 (cases <c>ts-plain</c>, <c>ts-table-border</c>, <c>ts-table-padding</c>,
    /// <c>ts-spacing-one</c>, <c>ts-spacing-two</c>: the cells start one spacing inside the table's padding edge, and
    /// each next column one spacing after the last).
    /// </summary>
    internal static (double Left, double Right, double Gap) Insets(Box box, ImmutableArray<double> lines)
    {
        if (!IsSeparated(box))
        {
            return (lines[0] / 2, lines[^1] / 2, 0);
        }

        var style = box.Style;
        var spacing = style.BorderSpacing.Horizontal.Points;
        return (style.Border.Widths.Left.Points + style.Padding.Left.Points + spacing, style.Border.Widths.Right.Points + style.Padding.Right.Points + spacing, spacing);
    }

    /// <summary>The width a table takes besides its columns: <see cref="Insets"/> on both sides and the gaps between
    /// <paramref name="columns"/> columns.</summary>
    private static double Outer(Box box, ImmutableArray<double> lines, int columns)
    {
        if (!IsSeparated(box))
        {
            return (lines[0] + lines[^1]) / 2;
        }

        var (left, right, gap) = Insets(box, lines);
        return left + right + (Math.Max(0, columns - 1) * gap);
    }

    /// <summary>The box a cell is laid out as in the separated borders model: its own border and padding, which it paints
    /// itself (CSS 2.1 §17.6.1; case <c>ts-paint</c>: one stroke per cell, a cell without a border takes no room for one,
    /// a 6 pt border widens its own column alone), and no margins (§17.5).</summary>
    internal static Box SeparatedCellBox(TableGrid.Cell cell) => cell.Box with { Style = cell.Box.Style with { Margin = Edges.Zero } };

    /// <summary>The width of each vertical grid line: per column boundary the widest resolved line over all rows, so
    /// every row's cells line up. At the table's edges half of it is the table's left and right border, on every page:
    /// measured in Chromium 159, a 6 pt left or right border on one cell of the first row, of a later row, or of a row
    /// on the next page puts the centre of every row's outer line 3 pt inside the table's edge (cases
    /// <c>te-left-first</c>, <c>te-left-later</c>, <c>te-right-first</c>, <c>te-right-later</c>, <c>te-break</c>), and a
    /// table border wider than its cells' counts the same way (<c>te-table-border</c>). Where each cell's content
    /// starts is half of its own line (<see cref="CollapsedBorders.OwnVerticalWidth"/>), not of this one.</summary>
    internal static ImmutableArray<double> VerticalLineWidths(Box box, TableGrid grid) =>
        IsSeparated(box) ? [.. Enumerable.Repeat(0.0, grid.ColumnCount + 1)] :
        [.. Enumerable.Range(0, grid.ColumnCount + 1).Select(boundary =>
            Enumerable.Range(0, grid.Rows.Length).Select(row => CollapsedBorders.Vertical(box, grid, row, boundary).Width.Points).DefaultIfEmpty(0).Max())];

    /// <summary>The columns' kinds with the cells' widths (<see cref="CellColumns"/>) and their ranges, from every cell
    /// measured with half of its own vertical lines beside it (<see cref="CollapsedBorders.OwnVerticalWidth"/>), as it is
    /// laid out; in the separated borders model with its own border, and a cell over several columns gives them its widths
    /// less the spacing between them, which it covers (case <c>ts-span</c>: a cell 139.969 pt wide over two columns of
    /// 66.328 and 67.641 pt and the 6 pt between them). A cell over several columns with a width in the automatic layout
    /// asks that width of them as its maximum, never less than its min-content (cases <c>twd-cell-span</c> a and c: 158.742
    /// and 159.492 pt over two columns of "one" and "two", shared as a cell's maximum is).</summary>
    private static Result<(ValueList<TrackSize> Columns, ImmutableArray<IntrinsicWidths> Ranges), Error> ColumnRanges(LayoutEnvironment environment, Box box, TableGrid grid)
    {
        var gap = IsSeparated(box) ? box.Style.BorderSpacing.Horizontal.Points : 0;
        return grid.Cells
            .TraverseAll(cell =>
            {
                var laid = IsSeparated(box) ? SeparatedCellBox(cell) : CellBox(cell, new Edges(
                    Length.Zero,
                    (CollapsedBorders.OwnVerticalWidth(box, grid, cell, cell.Column + cell.ColumnSpan) / 2).Pt(),
                    Length.Zero,
                    (CollapsedBorders.OwnVerticalWidth(box, grid, cell, cell.Column) / 2).Pt()));
                return environment.Measure(laid).OnSuccessMap(widths => (Cell: cell, Given: GivenWidth(laid), Widths: widths));
            })
            .OnSuccessMap(measured =>
            {
                var columns = CellColumns(box.Style.Columns, measured.Select(item => (item.Cell, item.Given, item.Cell.Box.Style.Width is BoxWidth.Percent { Value: var percent } ? percent : (double?)null)));
                var contributions = measured.Select(item =>
                {
                    var widths = item.Cell.ColumnSpan > 1 && item.Given is { } given && Enumerable.Range(item.Cell.Column, item.Cell.ColumnSpan).All(index => columns[index] is TrackSize.Auto or TrackSize.Preferred)
                        ? new IntrinsicWidths(item.Widths.MinContent, Math.Max(item.Widths.MinContent, given))
                        : item.Widths;
                    return new TrackSizing.Contribution(item.Cell.Column, item.Cell.ColumnSpan, item.Cell.ColumnSpan > 1 && gap > 0 ? Covering(widths, (item.Cell.ColumnSpan - 1) * gap) : widths);
                });
                return (columns, TrackSizing.ColumnRanges(columns, contributions));
            });
    }

    /// <summary>The border-box width a cell asks for with its <see cref="BoxWidth.Fixed"/> width, which is of its content
    /// box (CSS 2.1 §17.5.2; cases <c>twd-cell-*</c>: a cell of <c>width: 80pt</c> with padding 4 pt each side is 88.734 pt
    /// wide with collapsed lines of 0.75 pt, 89.484 pt with its own border of 0.75 pt), as laid out: its padding and
    /// border, in the collapsing model half of its lines in its padding. Null without a width.</summary>
    private static double? GivenWidth(Box laid) =>
        laid.Style.Width is BoxWidth.Fixed { Width.Points: var width }
            ? width + laid.Style.Padding.Left.Points + laid.Style.Padding.Right.Points + laid.Style.Border.Widths.Left.Points + laid.Style.Border.Widths.Right.Points
            : null;

    /// <summary>
    /// The column kinds with the widths of the cells in one column (CSS 2.1 §17.5.2), as Chromium 159 prints them. In the
    /// automatic layout every row counts: an auto column with such cells is a <see cref="TrackSize.Preferred"/> column of
    /// the widest, and a preferred column (a column element's length, its border box) takes the larger of its width and
    /// theirs (cases <c>twd-cell-row</c> a, b: 80 pt in the first or the second row; <c>twd-cell-two</c> a: 60 then
    /// 90 pt give 90; <c>twd-cell-col</c> a: col 60 pt and cell 90 pt give 90, c: col 90 pt and cell 60 pt give 90). In
    /// the fixed layout only the first row counts (CSS 2.1 §17.5.2.1: "a cell in the first row with a value other than
    /// 'auto' for the 'width' property determines the width for that column"): a fill column whose first-row cell has a
    /// width is a <see cref="TrackSize.Fixed"/> column of it, a later row's width is not read, and a column element's
    /// length wins over a cell's (<c>twd-cell-row</c> c, d; <c>twd-cell-two</c> b, c: the first row's 60 or 90 pt;
    /// <c>twd-cell-col</c> b: col 60 pt beside a cell of 90 pt gives 60). A cell's percentage (<see cref="BoxWidth.Percent"/>)
    /// makes an auto column a <see cref="TrackSize.Percent"/> column, in any row (cases <c>tpa-auto</c> a, e: 30% in the
    /// first or a later row). Cells over several columns do not set a column's kind.
    /// </summary>
    internal static ValueList<TrackSize> CellColumns(ValueList<TrackSize> columns, IEnumerable<(TableGrid.Cell Cell, double? Given, double? Percent)> cells)
    {
        var kinds = columns.ToArray();
        foreach (var (cell, given, percent) in cells)
        {
            if (cell.ColumnSpan == 1 && percent is { } share)
            {
                kinds[cell.Column] = kinds[cell.Column] switch
                {
                    TrackSize.Auto => new TrackSize.Percent(share),
                    TrackSize.Percent column => new TrackSize.Percent(Math.Max(column.Value, share)),
                    var kept => kept,
                };
            }

            if (cell.ColumnSpan != 1 || given is not { } width)
            {
                continue;
            }

            kinds[cell.Column] = kinds[cell.Column] switch
            {
                TrackSize.Auto => new TrackSize.Preferred(width.Pt()),
                TrackSize.Preferred preferred => new TrackSize.Preferred(Math.Max(preferred.Width.Points, width).Pt()),
                TrackSize.Fill when cell.Row == 0 => new TrackSize.Fixed(width.Pt()),
                var kept => kept,
            };
        }

        return [.. kinds];
    }

    /// <summary>The widths a cell gives the columns it spans, less the <paramref name="gaps"/> between them, never below 0.</summary>
    private static IntrinsicWidths Covering(IntrinsicWidths widths, double gaps) =>
        new(Math.Max(0, widths.MinContent - gaps), Math.Max(0, widths.MaxContent - gaps));

    /// <summary>The step a percentage of a width is floored to: 1/64 px under <see cref="LengthRounding.SixtyFourthPixels"/>,
    /// none under <see cref="LengthRounding.Exact"/>.</summary>
    private static double Step(LayoutEnvironment environment) =>
        environment.Profile.LengthRounding == LengthRounding.SixtyFourthPixels ? 0.75 / 64 : 0;

    /// <summary>
    /// Places the columns: column j starts at x = <see cref="TablePreparation.ColumnX"/>[j] and is as wide as track
    /// sizing says; the next column starts the column gap after its end (<see cref="Insets"/>). In the collapsing border
    /// model the gap is zero,
    /// a column (a cell's border box) runs from the centre of the grid line on its left to the centre of the line on its
    /// right, and the first line is centred half its width inside the table's left edge (the table's border box holds
    /// half of the outer lines, §17.6.2).
    /// </summary>
    private static Result<TablePreparation, Error> Prepare(LayoutEnvironment environment, Box box, TableGrid grid, double availableWidth)
    {
        var lines = VerticalLineWidths(box, grid);
        var outer = Outer(box, lines, grid.ColumnCount);
        var (left, right, gap) = Insets(box, lines);
        var given = box.Style.Width switch
        {
            BoxWidth.Fixed { Width.Points: var length } => length,
            BoxWidth.Percent { Value: var percent } => TrackSizing.Floor(percent * availableWidth / 100, Step(environment)),
            _ => (double?)null,
        };
        if (given is { } tooWide && !Fitting.Fits(tooWide, availableWidth))
        {
            return Result<TablePreparation, Error>.Failure(LayoutErrors.ColumnsDoNotFit(box, tooWide, availableWidth));
        }

        return ColumnRanges(environment, box, grid).OnSuccessBind(sized =>
        {
            var (columns, ranges) = sized;
            var mode = given is null ? box.Style.Width : BoxWidth.Stretch;
            var space = (given ?? availableWidth) - outer;
            if (given is null && box.Style.Width == BoxWidth.Auto && TrackSizing.PercentWidth(columns, ranges) is { } wanted)
            {
                // A table of width auto with percent columns is as wide as they ask, within its container (TrackSizing.PercentWidth).
                space = Math.Min(space, Math.Max(wanted, ranges.Sum(range => range.MinContent)));
                mode = BoxWidth.Stretch;
            }

            var (widths, needed) = TrackSizing.Resolve(columns, ranges, space, mode, Step(environment));
            if (needed is { } least && given is not null && Fitting.Fits(least + outer, availableWidth))
            {
                // A table of a given width is never narrower than its columns' minimum (cases twd-table-narrow a, c: 40 pt
                // gives 63.773 and 79.523 pt, the content's min-content; twd-sum c: 150 pt over two fixed columns of
                // 108.738 pt gives 218.227 pt).
                (widths, needed) = TrackSizing.Resolve(columns, ranges, least, mode, Step(environment));
            }

            if (needed is { } width)
            {
                return Result<TablePreparation, Error>.Failure(LayoutErrors.ColumnsDoNotFit(box, width + outer, availableWidth));
            }

            var start = left;
            var x = ImmutableArray.CreateBuilder<double>(widths.Length);
            var end = start;
            foreach (var columnWidth in widths)
            {
                x.Add(x.Count == 0 ? start : end + gap);
                end = x[^1] + columnWidth;
            }

            return Result<TablePreparation, Error>.Success(new TablePreparation(box, grid, x.MoveToImmutable(), widths, gap, start, end, lines, end + right, availableWidth));
        });
    }
}

/// <summary>What a tracks box's pages share, prepared once per layout pass: the grid and the placed columns.</summary>
/// <param name="Table">The tracks box they were prepared for (compared by reference: a token of another table does not
/// carry its grid over).</param>
/// <param name="Grid">The grid.</param>
/// <param name="ColumnX">The x where every column starts, from the table's left edge.</param>
/// <param name="ColumnWidths">The width of every column.</param>
/// <param name="ColumnGap">The room between two columns: zero in the collapsing border model.</param>
/// <param name="GridStart">The x where the first column starts.</param>
/// <param name="GridEnd">The x where the last column ends (<paramref name="GridStart"/> without columns).</param>
/// <param name="LineWidths">The width of every vertical grid line.</param>
/// <param name="Width">The table's border-box width.</param>
/// <param name="AvailableWidth">The width the columns were placed in; a page of another width prepares again.</param>
internal sealed record TablePreparation(
    Box Table, TableGrid Grid, ImmutableArray<double> ColumnX, ImmutableArray<double> ColumnWidths, double ColumnGap, double GridStart, double GridEnd,
    ImmutableArray<double> LineWidths, double Width, double AvailableWidth);

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

    /// <summary>True where the page ends with the first body row cut right below the header rows (<see cref="TablePage"/>,
    /// remarks): the cells of that row lie below the header as below start insets, so content in them may take its own
    /// last-resort cut there.</summary>
    public bool CutsBelowHeaderRows { get; init; }

    /// <summary>The <see cref="ConstraintSpace.FragmentainerBlockSize"/> of the cells' content, on the page and in the
    /// measurements with unlimited height alike: the table's, less the room its repeated header rows take on this page
    /// (<see cref="TablePage"/>, RepeatsHeader and Reserved: on a later page also the outer half of the table's top grid
    /// line), never below zero.</summary>
    public double CellFragmentainerBlockSize { get; init; }

    /// <summary>The grid.</summary>
    public TableGrid Grid => Prepared.Grid;

    /// <summary>The x where every column starts.</summary>
    public ImmutableArray<double> ColumnX => Prepared.ColumnX;

    /// <summary>The x where the first column starts.</summary>
    public double GridStart => Prepared.GridStart;

    /// <summary>The x where the last column ends.</summary>
    public double GridEnd => Prepared.GridEnd;

    /// <summary>The x where column <paramref name="column"/> ends.</summary>
    public double ColumnEnd(int column) => ColumnX[column] + Prepared.ColumnWidths[column];

    /// <summary>The x of the centre of vertical grid line <paramref name="boundary"/> in the collapsing border model, where
    /// the column gap is zero: where the column right of it starts, and at the right edge where the last column ends.</summary>
    public double LineX(int boundary) => boundary < ColumnX.Length ? ColumnX[boundary] : GridEnd;

    /// <summary>The width of every vertical grid line.</summary>
    public ImmutableArray<double> LineWidths => Prepared.LineWidths;

    /// <summary>The table's border-box width.</summary>
    public double Width => Prepared.Width;

    /// <summary>The width of a cell's border box: from the start of its first column to the end of its last, with the
    /// gaps between them.</summary>
    public double CellWidth(TableGrid.Cell cell) => ColumnEnd(cell.Column + cell.ColumnSpan - 1) - ColumnX[cell.Column];

    /// <summary>True in the separated borders model (<see cref="TracksLayout.IsSeparated"/>).</summary>
    public bool IsSeparated => TracksLayout.IsSeparated(Box);

    /// <summary>The room between two rows: the vertical border spacing in the separated borders model (CSS 2.1 §17.6.1;
    /// cases <c>ts-spacing-one</c>, <c>ts-spacing-two</c>), zero in the collapsing one.</summary>
    public double RowGap => IsSeparated ? Box.Style.BorderSpacing.Vertical.Points : 0;

    /// <summary>In the separated borders model, the room above the first row: the table's top border and padding and one
    /// vertical spacing (CSS 2.1 §17.6.1; cases <c>ts-table-padding</c>, <c>ts-spacing-two</c>).</summary>
    public double TopInset => Box.Style.Border.Widths.Top.Points + Box.Style.Padding.Top.Points + RowGap;

    /// <summary>In the separated borders model, the room below the last row: one vertical spacing and the table's bottom
    /// padding and border.</summary>
    public double BottomInset => RowGap + Box.Style.Padding.Bottom.Points + Box.Style.Border.Widths.Bottom.Points;

    /// <summary>The width of a horizontal grid line between two rows on a page: the widest of its column segments; zero in
    /// the separated borders model, which has no grid lines.</summary>
    public double HorizontalWidth(int? above, int? below, CollapsedBorders.LineContext context) =>
        IsSeparated ? 0 : Enumerable.Range(0, Grid.ColumnCount)
            .Select(column => CollapsedBorders.Horizontal(Box, Grid, above, below, column, context).Width.Points)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>A cell's own part of a horizontal grid line: the widest of the line's segments over the cell's columns
    /// (<see cref="TableSegments.CellLayoutBox"/>).</summary>
    public double HorizontalWidth(int? above, int? below, CollapsedBorders.LineContext context, TableGrid.Cell cell) =>
        IsSeparated ? 0 : Enumerable.Range(cell.Column, cell.ColumnSpan)
            .Max(column => CollapsedBorders.Horizontal(Box, Grid, above, below, column, context).Width.Points);
}
