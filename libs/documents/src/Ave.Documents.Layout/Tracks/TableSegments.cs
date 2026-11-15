using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;
using static Ave.Documents.Layout.TablePage;

namespace Ave.Documents.Layout;

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

    /// <summary>A cut cell: its content laid out in the slice height; an empty slice when it is complete or nothing
    /// of it fits. Returns the fragment, stretched to the slice, and where the cell continues.</summary>
    internal static Result<(BoxFragment Fragment, CellBreak Next), Error> Slice(TableGeometry table, Segment segment, ActiveCell cell, double sliceHeight)
    {
        var info = table.Grid.Cells[cell.Cell];
        var box = CellLayoutBox(table, segment, cell);
        var width = table.CellWidth(info);
        if (cell.IsComplete)
        {
            return Result<(BoxFragment, CellBreak), Error>.Success((EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, null, true)));
        }

        var space = new ConstraintSpace(width, sliceHeight, IsAtPageStart: false, IsNewFormattingContext: true);
        return table.Environment.Layout(box, space, cell.Token).OnSuccessMap(result => result switch
        {
            LayoutResult.Placed placed => (Stretch(placed.Fragment, sliceHeight, VerticalAlign.Top),
                new CellBreak(cell.Cell, placed.Continuation, placed.Continuation is null)),
            _ => (EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, cell.Token, false)),
        });
    }

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
                    ? Result<KeyValuePair<int, BoxFragment>, Error>.Success(KeyValuePair.Create(cell.Cell, placed.Fragment))
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
