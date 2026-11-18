using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Layout.TablePage;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The rows and cells of one page of a tracks box: a unit (or the rest of one) measured with unlimited height, its
/// row heights (CSS 2.1 §17.5.3), and its cells placed whole or sliced at a page end. Used by <see cref="TablePage"/>
/// and <see cref="TableBreaks"/>.
/// </summary>
internal static class TableSegments
{
    /// <summary>A cell being laid out on this page.</summary>
    /// <param name="Cell">The cell index.</param>
    /// <param name="StartRow">Its first row on this page.</param>
    /// <param name="Token">Its break token, or null when it starts here.</param>
    /// <param name="IsComplete">True when its content is complete and it only goes on covering rows.</param>
    internal sealed record ActiveCell(int Cell, int StartRow, BreakToken? Token, bool IsComplete);

    /// <summary>A unit, or the rest of one, measured with unlimited height.</summary>
    /// <param name="FirstRow">Its first row on this page.</param>
    /// <param name="LastRow">Its last row.</param>
    /// <param name="Cells">The cells that start in it, or continue into it.</param>
    /// <param name="Heights">Each row's height.</param>
    /// <param name="Natural">Each cell's fragment at its natural height; absent for a complete cell.</param>
    /// <param name="Above">The grid line above its first row; its centre is where the segment starts.</param>
    internal sealed record Segment(
        int FirstRow, int LastRow, ImmutableArray<ActiveCell> Cells, ImmutableArray<double> Heights, ImmutableDictionary<int, BoxFragment> Natural, LineAt Above)
    {
        public double Top => Above.Y;

        public double RowTop(int row) => Top + Heights.Take(row - FirstRow).Sum();

        public double RowsHeight(int first, int last) => Heights.Skip(first - FirstRow).Take(last - first + 1).Sum();

        public double Height => Heights.Sum();
    }

    /// <summary>
    /// A cut cell: its content laid out in the slice height less its <see cref="ReopenedInset"/> at the top; an empty
    /// slice when it is complete or nothing of it fits. A cell whose content goes on past the cut keeps
    /// <paramref name="clearance"/> free above the cut (clone keeps it clear of the line drawn there,
    /// <see cref="TablePage"/>); a cell that ends in the slice needs no clearance, since its own box already holds the half
    /// of the line below it (<see cref="CellLayoutBox"/>). So the cell is laid out in the whole slice first, and only when
    /// it does not end there again, without the clearance. Returns the fragment, stretched to the slice, and where the
    /// cell continues.
    /// </summary>
    internal static Result<(BoxFragment Fragment, CellBreak Next), Error> Slice(TableGeometry table, Segment segment, ActiveCell cell, double sliceHeight, double clearance = 0)
    {
        var info = table.Grid.Cells[cell.Cell];
        var box = CellLayoutBox(table, segment, cell);
        var width = table.CellWidth(info);
        if (cell.IsComplete)
        {
            return Result<(BoxFragment, CellBreak), Error>.Success((EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, null, true)));
        }

        var inset = ReopenedInset(table, segment, cell);
        Result<LayoutResult, Error> LayOutIn(double height) =>
            table.Environment.Layout(box, new ConstraintSpace(width, Math.Max(0, height - inset), IsAtPageStart: false, IsNewFormattingContext: true), cell.Token);
        return LayOutIn(sliceHeight)
            .OnSuccessBind(whole => clearance <= 0 || whole is LayoutResult.Placed { Continuation: null } ? Result<LayoutResult, Error>.Success(whole) : LayOutIn(sliceHeight - clearance))
            .OnSuccessMap(result => result switch
            {
                LayoutResult.Placed placed => (Stretch(Lowered(placed.Fragment, inset), sliceHeight, VerticalAlign.Top),
                    new CellBreak(cell.Cell, placed.Continuation, placed.Continuation is null)),
                _ => (EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, cell.Token, false)),
            });
    }

    /// <summary>
    /// The rest of a cut cell as the next page would lay it out, with unlimited height: what is left of it after the
    /// cut (<see cref="TableBreaks"/>, CutShows, asks whether it draws anything).
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="segment">The segment the cut is in.</param>
    /// <param name="next">Where the cell continues; not complete.</param>
    /// <returns>The rest's fragment, or an error.</returns>
    internal static Result<BoxFragment, Error> Rest(TableGeometry table, Segment segment, CellBreak next)
    {
        var info = table.Grid.Cells[next.Cell];
        var box = CellLayoutBox(table, segment, new ActiveCell(next.Cell, info.Row, next.Token, false));
        return table.Environment.Layout(box, new ConstraintSpace(table.CellWidth(info), double.PositiveInfinity, false, true), next.Token)
            .OnSuccessMap(result => result is LayoutResult.Placed placed ? placed.Fragment : EmptyCell(box, table.CellWidth(info), 0));
    }

    /// <summary>
    /// With clone, the rest of every cell that continues from the previous page — a cell cut inside its own row, or a
    /// spanning cell cut between or inside the rows it spans — starts under the whole line that starts the page, which
    /// runs across the full width (see <see cref="TablePage"/>): half that line's width lies in the cell, as for a whole
    /// cell (<see cref="CellLayoutBox"/>), but the cell's own layout gives a continued box no top inset, so the rest is
    /// lowered by it here. Only the first segment of a page has continued cells. 0 for every other cell, and with slice.
    /// </summary>
    internal static double ReopenedInset(TableGeometry table, Segment segment, ActiveCell cell) =>
        TablePage.IsCloned(table) && cell.Token is not null
            ? table.HorizontalWidth(segment.Above.Above, segment.Above.Below, segment.Above.Context) / 2
            : 0;

    /// <summary>A fragment moved <paramref name="inset"/> down inside itself, and that much taller.</summary>
    private static BoxFragment Lowered(BoxFragment fragment, double inset) =>
        inset <= 0
            ? fragment
            : new BoxFragment(
                fragment.Box,
                new Size(fragment.Size.Width, fragment.Size.Height + inset),
                [.. fragment.Children.Select(child => child with { Offset = child.Offset + new Point(0, inset) })],
                fragment.Border,
                fragment.IsRepeated);

    /// <summary>Places a measured segment whole: its rows at their heights, every cell as tall as its rows.</summary>
    internal static Content PlaceWhole(TableGeometry table, Content content, Segment segment, bool isRepeated)
    {
        var rows = content.Rows.AddRange(Enumerable.Range(segment.FirstRow, segment.LastRow - segment.FirstRow + 1)
            .Select(row => new PlacedRow(row, segment.RowTop(row), segment.Heights[row - segment.FirstRow], isRepeated)));
        var cells = content.Cells.AddRange(segment.Cells.Select(cell => Whole(table, segment, cell)));
        var lines = content.Lines.AddRange(Enumerable.Range(segment.FirstRow + 1, segment.LastRow - segment.FirstRow)
            .Select(row => new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, segment.RowTop(row))));
        return content with { Rows = rows, Cells = cells, Lines = lines, Bottom = segment.Top + segment.Height };
    }

    /// <summary>A whole cell: its natural fragment stretched to the rows it covers, content aligned (§17.5.3).</summary>
    internal static PlacedCell Whole(TableGeometry table, Segment segment, ActiveCell cell)
    {
        var info = table.Grid.Cells[cell.Cell];
        var height = segment.RowsHeight(cell.StartRow, info.LastRow);
        var fragment = segment.Natural.TryGetValue(cell.Cell, out var natural)
            ? Stretch(natural, height, info.Box.Style.VerticalAlign)
            : EmptyCell(CellLayoutBox(table, segment, cell), table.CellWidth(info), height);
        return new PlacedCell(cell.Cell, cell.StartRow, fragment);
    }

    /// <summary>
    /// Measures a segment: every cell laid out with unlimited height, then the row heights (see remarks). A cell that
    /// still breaks with unlimited height holds a forced page break, which a table cell cannot honour.
    /// </summary>
    internal static Result<Segment, Error> Measure(TableGeometry table, int firstRow, int lastRow, ImmutableArray<ActiveCell> cells, LineAt above)
    {
        var shell = new Segment(firstRow, lastRow, cells, [], ImmutableDictionary<int, BoxFragment>.Empty, above);
        return cells.Where(cell => !cell.IsComplete)
            .TraverseAll(cell => table.Environment
                .Layout(CellLayoutBox(table, shell, cell), new ConstraintSpace(table.CellWidth(table.Grid.Cells[cell.Cell]), double.PositiveInfinity, false, true), cell.Token)
                .OnSuccessBind(result => result is LayoutResult.Placed { Continuation: null } placed
                    ? Result<KeyValuePair<int, BoxFragment>, Error>.Success(KeyValuePair.Create(cell.Cell, Lowered(placed.Fragment, ReopenedInset(table, shell, cell))))
                    : Result<KeyValuePair<int, BoxFragment>, Error>.Failure(
                        LayoutErrors.InvalidContent(table.Grid.Cells[cell.Cell].Box, "a table cell cannot hold a forced page break."))))
            .OnSuccessMap(natural =>
            {
                var fragments = natural.ToImmutableDictionary();
                return shell with { Heights = RowHeights(table.Grid, firstRow, lastRow, cells, fragments), Natural = fragments };
            });
    }

    /// <summary>The row heights of §17.5.3 for one segment: single-row cells first, then each spanning cell grows the
    /// last row it spans by what is missing.</summary>
    internal static ImmutableArray<double> RowHeights(
        TableGrid grid, int firstRow, int lastRow, ImmutableArray<ActiveCell> cells, ImmutableDictionary<int, BoxFragment> natural)
    {
        var heights = new double[lastRow - firstRow + 1];
        double HeightOf(ActiveCell cell) => natural.TryGetValue(cell.Cell, out var fragment) ? fragment.Size.Height : 0;
        foreach (var cell in cells.Where(cell => grid.Cells[cell.Cell].LastRow == cell.StartRow))
        {
            heights[cell.StartRow - firstRow] = Math.Max(heights[cell.StartRow - firstRow], HeightOf(cell));
        }

        foreach (var cell in cells.Where(cell => grid.Cells[cell.Cell].LastRow > cell.StartRow)
                     .OrderBy(cell => grid.Cells[cell.Cell].LastRow).ThenByDescending(cell => cell.StartRow))
        {
            var last = grid.Cells[cell.Cell].LastRow;
            var missing = HeightOf(cell) - heights.Skip(cell.StartRow - firstRow).Take(last - cell.StartRow + 1).Sum();
            heights[last - firstRow] += Math.Max(0, missing);
        }

        return [.. heights];
    }

    /// <summary>The cells of a segment: those that continue from the previous page, then those that start in it.</summary>
    internal static ImmutableArray<ActiveCell> ActiveCells(TableGrid grid, int firstRow, int lastRow, ValueList<CellBreak> continuing)
    {
        var resumed = continuing.Select(cell => new ActiveCell(cell.Cell, firstRow, cell.Token, cell.IsComplete)).ToImmutableArray();
        var fresh = grid.CellsStartingInRows(firstRow, lastRow)
            .Where(index => !continuing.Any(resume => resume.Cell == index))
            .Select(index => new ActiveCell(index, grid.Cells[index].Row, null, false));
        return [.. resumed, .. fresh];
    }

    /// <summary>
    /// The box a cell is laid out as on this page (<see cref="TracksLayout.CellBox"/>): half of the vertical lines
    /// beside it, half of the line above it when it starts here (the line above the segment, or the line between two
    /// of its rows), and half of the line below its last row. A cell that continues from an earlier page has no top
    /// inset (slice); its token already tells its algorithm so.
    /// </summary>
    internal static Box CellLayoutBox(TableGeometry table, Segment segment, ActiveCell cell)
    {
        var grid = table.Grid;
        var info = grid.Cells[cell.Cell];
        var startsSegment = cell.StartRow == segment.FirstRow;
        var above = startsSegment ? segment.Above.Above : cell.StartRow - 1;
        var context = startsSegment ? segment.Above.Context : CollapsedBorders.LineContext.Interior;
        var top = cell.Token is null ? table.HorizontalWidth(above, cell.StartRow, context) / 2 : 0;
        var bottom = table.HorizontalWidth(info.LastRow, TablePage.Next(grid, info.LastRow), TablePage.EdgeContext(grid, info.LastRow)) / 2;
        var halves = new Edges(top.Pt(), (table.LineWidths[info.Column + info.ColumnSpan] / 2).Pt(), bottom.Pt(), (table.LineWidths[info.Column] / 2).Pt());
        return TracksLayout.CellBox(info, halves);
    }

    internal static BoxFragment EmptyCell(Box box, double width, double height) => new(box, new Size(width, height), []);

    /// <summary>A cell fragment made as tall as its rows; with middle or bottom alignment its content moves down by half
    /// or all of the extra height (§17.5.3). Background and box keep the full height.</summary>
    internal static BoxFragment Stretch(BoxFragment fragment, double height, VerticalAlign align)
    {
        var extra = Math.Max(0, height - fragment.Size.Height);
        var shift = align switch
        {
            VerticalAlign.Middle => extra / 2,
            VerticalAlign.Bottom => extra,
            _ => 0,
        };
        ValueList<PlacedFragment> children = [.. fragment.Children.Select(child => child with { Offset = child.Offset + new Point(0, shift) })];
        return new BoxFragment(fragment.Box, new Size(fragment.Size.Width, Math.Max(height, fragment.Size.Height)), children, fragment.Border, fragment.IsRepeated);
    }
}
