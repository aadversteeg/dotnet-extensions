using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// The grid of a tracks box: its rows in order, and every cell with the slots it covers. Built from the box tree
/// (row groups → rows → cells) with the slot-filling rule of the HTML table model, which CSS 2.1 §17.2 relies on:
/// each cell takes the first free column of its row, skipping slots that a row span from above already covers.
/// </summary>
/// <param name="Rows">Every row, header rows first, in document order.</param>
/// <param name="Cells">Every cell, in document order (row by row).</param>
/// <param name="ColumnCount">The number of columns (from the column definitions).</param>
/// <param name="HeaderRowCount">How many rows at the start belong to the header group.</param>
/// <param name="Units">The row units: runs of rows that row spans join (see <see cref="Unit"/>).</param>
/// <param name="Slots">For every row, the cell index in each column, or −1 for an empty slot.</param>
internal sealed record TableGrid(
    ImmutableArray<TableGrid.Row> Rows,
    ImmutableArray<TableGrid.Cell> Cells,
    int ColumnCount,
    int HeaderRowCount,
    ImmutableArray<TableGrid.Unit> Units,
    ImmutableArray<ImmutableArray<int>> Slots)
{
    /// <summary>One row and the group it belongs to.</summary>
    /// <param name="Box">The row box.</param>
    /// <param name="Group">Its row group.</param>
    /// <param name="IsFirstOfGroup">True for the group's first row.</param>
    /// <param name="IsLastOfGroup">True for the group's last row.</param>
    public sealed record Row(Box Box, Box Group, bool IsFirstOfGroup, bool IsLastOfGroup);

    /// <summary>One cell and the slots it covers.</summary>
    /// <param name="Box">The cell box.</param>
    /// <param name="Row">The first row it covers.</param>
    /// <param name="Column">The first column it covers.</param>
    /// <param name="RowSpan">How many rows it covers.</param>
    /// <param name="ColumnSpan">How many columns it covers.</param>
    public sealed record Cell(Box Box, int Row, int Column, int RowSpan, int ColumnSpan)
    {
        /// <summary>The last row the cell covers.</summary>
        public int LastRow => Row + RowSpan - 1;
    }

    /// <summary>
    /// A row unit: consecutive rows that row spans join, so that no cell crosses the boundary between two units. A
    /// break between units cuts no cell; a break inside one does (a spanning cell continues on the next page).
    /// </summary>
    /// <param name="FirstRow">The unit's first row.</param>
    /// <param name="LastRow">The unit's last row.</param>
    public sealed record Unit(int FirstRow, int LastRow);

    /// <summary>The cell covering a slot, or null for an empty slot or a position outside the grid.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The cell index, or null.</returns>
    public int? CellAt(int row, int column) =>
        row >= 0 && row < Rows.Length && column >= 0 && column < ColumnCount && Slots[row][column] >= 0 ? Slots[row][column] : null;

    /// <summary>The unit a row belongs to.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The unit.</returns>
    public Unit UnitOf(int row) => Units.First(unit => unit.FirstRow <= row && row <= unit.LastRow);

    /// <summary>
    /// Builds the grid, or reports why the tree is not a valid table:
    /// <list type="bullet">
    /// <item>the children must be row groups, a header group (<c>table-header-group</c>) at most once and first;</item>
    /// <item>a group's children must be rows, a row's children cells (any box);</item>
    /// <item>a cell must fit in the columns, and its row span must end inside its row group (HTML: a row span is
    /// limited to its row group).</item>
    /// </list>
    /// </summary>
    /// <param name="table">The tracks box.</param>
    /// <returns>The grid, or <c>Documents.Layout.InvalidContent</c>.</returns>
    public static Result<TableGrid, Error> Build(Box table)
    {
        var columnCount = table.Style.Columns.Count;
        if (columnCount == 0)
        {
            return Invalid(table, "a tracks box needs at least one column.");
        }

        var groups = table.Children.OfType<Box>().ToImmutableArray();
        if (groups.Length != table.Children.Count
            || groups.Any(group => group.Layout != LayoutAlgorithmId.TableHeaderGroup && group.Layout != LayoutAlgorithmId.TableRowGroup))
        {
            return Invalid(table, "the children of a tracks box must be row groups.");
        }

        if (groups.Skip(1).Any(group => group.Layout == LayoutAlgorithmId.TableHeaderGroup))
        {
            return Invalid(table, "a header row group may only be the first group.");
        }

        var rows = ImmutableArray.CreateBuilder<Row>();
        var cells = ImmutableArray.CreateBuilder<Cell>();
        var slots = new List<int[]>();
        foreach (var group in groups)
        {
            var groupRows = group.Children.OfType<Box>().ToImmutableArray();
            if (groupRows.Length != group.Children.Count || groupRows.Any(row => row.Layout != LayoutAlgorithmId.TableRow))
            {
                return Invalid(group, "the children of a row group must be rows.");
            }

            var firstRow = rows.Count;
            slots.AddRange(groupRows.Select(_ => Enumerable.Repeat(-1, columnCount).ToArray()));
            for (var index = 0; index < groupRows.Length; index++)
            {
                var rowBox = groupRows[index];
                rows.Add(new Row(rowBox, group, index == 0, index == groupRows.Length - 1));
                if (PlaceCells(rowBox, firstRow + index, firstRow + groupRows.Length - 1, columnCount, slots, cells) is { } problem)
                {
                    return Result<TableGrid, Error>.Failure(problem);
                }
            }
        }

        var headerRows = groups.FirstOrDefault()?.Layout == LayoutAlgorithmId.TableHeaderGroup ? groups[0].Children.Count : 0;
        var builtCells = cells.ToImmutable();
        return Result<TableGrid, Error>.Success(new TableGrid(
            rows.ToImmutable(), builtCells, columnCount, headerRows, BuildUnits(rows.ToImmutable(), builtCells), [.. slots.Select(row => row.ToImmutableArray())]));
    }

    /// <summary>The slot-filling rule for one row: each cell goes to the first column not yet covered.</summary>
    private static Error? PlaceCells(Box rowBox, int row, int lastRowOfGroup, int columnCount, List<int[]> slots, ImmutableArray<Cell>.Builder cells)
    {
        var column = 0;
        foreach (var child in rowBox.Children)
        {
            if (child is not Box cellBox)
            {
                return LayoutErrors.InvalidContent(rowBox, "the children of a row must be cell boxes.");
            }

            while (column < columnCount && slots[row][column] >= 0)
            {
                column++;
            }

            var span = cellBox.Style.Span;
            if (span.Columns < 1 || span.Rows < 1 || column + span.Columns > columnCount)
            {
                return LayoutErrors.InvalidContent(cellBox, $"the cell (span {span}) does not fit in the {columnCount} columns from column {column + 1}.");
            }

            if (row + span.Rows - 1 > lastRowOfGroup)
            {
                return LayoutErrors.InvalidContent(cellBox, $"the cell's row span of {span.Rows} runs past the end of its row group.");
            }

            for (var covered = row; covered < row + span.Rows; covered++)
            {
                for (var spanned = column; spanned < column + span.Columns; spanned++)
                {
                    if (slots[covered][spanned] >= 0)
                    {
                        return LayoutErrors.InvalidContent(cellBox, "the cell overlaps a cell that spans rows from above.");
                    }

                    slots[covered][spanned] = cells.Count;
                }
            }

            cells.Add(new Cell(cellBox, row, column, span.Rows, span.Columns));
            column += span.Columns;
        }

        return null;
    }

    /// <summary>Joins rows into units: a unit grows while a cell that starts inside it reaches a later row.</summary>
    private static ImmutableArray<Unit> BuildUnits(ImmutableArray<Row> rows, ImmutableArray<Cell> cells)
    {
        var units = ImmutableArray.CreateBuilder<Unit>();
        var first = 0;
        while (first < rows.Length)
        {
            var last = first;
            for (var row = first; row <= last; row++)
            {
                last = cells.Where(cell => cell.Row == row).Select(cell => cell.LastRow).Append(last).Max();
            }

            units.Add(new Unit(first, last));
            first = last + 1;
        }

        return units.ToImmutable();
    }

    private static Result<TableGrid, Error> Invalid(Box box, string message) =>
        Result<TableGrid, Error>.Failure(LayoutErrors.InvalidContent(box, message));
}
