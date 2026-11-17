using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// The grid of a tracks box: its rows in order, and every cell with the slots it covers. Built from the box tree
/// (row groups → rows → cells) with <see cref="TableSlots"/>, the one implementation of the slot-filling rule: each
/// cell takes the first free column of its row, skipping slots that a row span from above already covers. A row
/// without cells is allowed here (its slots are empty); the front-end refuses it as input.
/// </summary>
/// <param name="Rows">Every row, header rows first, in document order.</param>
/// <param name="Cells">Every cell, in document order (row by row).</param>
/// <param name="ColumnCount">The number of columns (from the column definitions).</param>
/// <param name="HeaderRowCount">How many rows at the start belong to the header group.</param>
/// <param name="Units">The row units: runs of rows that row spans join (see <see cref="Unit"/>).</param>
/// <param name="Slots">For every row, the cell index in each column, or −1 for an empty slot.</param>
/// <param name="CellsStartingIn">For every row, the indexes of the cells whose first row it is, in document order —
/// so a page finds the cells of its rows without scanning the whole table.</param>
/// <param name="UnitOfRow">For every row, the index of its unit in <paramref name="Units"/>.</param>
internal sealed record TableGrid(
    ImmutableArray<TableGrid.Row> Rows,
    ImmutableArray<TableGrid.Cell> Cells,
    int ColumnCount,
    int HeaderRowCount,
    ImmutableArray<TableGrid.Unit> Units,
    ImmutableArray<ImmutableArray<int>> Slots,
    ImmutableArray<ImmutableArray<int>> CellsStartingIn,
    ImmutableArray<int> UnitOfRow)
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
    public Unit UnitOf(int row) => Units[UnitOfRow[row]];

    /// <summary>The indexes of the cells whose first row lies in <paramref name="first"/>…<paramref name="last"/>.</summary>
    /// <param name="first">The first row.</param>
    /// <param name="last">The last row.</param>
    /// <returns>The cell indexes, in document order.</returns>
    public IEnumerable<int> CellsStartingInRows(int first, int last) =>
        Enumerable.Range(first, last - first + 1).SelectMany(row => CellsStartingIn[row]);

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
            || groups.Any(group => group.Style.TableDisplay is not (TableDisplay.HeaderGroup or TableDisplay.RowGroup)))
        {
            return Invalid(table, "the children of a tracks box must be row groups.");
        }

        if (groups.Skip(1).Any(group => group.Style.TableDisplay == TableDisplay.HeaderGroup))
        {
            return Invalid(table, "a header row group may only be the first group.");
        }

        var groupRows = groups.Select(group => group.Children.OfType<Box>().ToImmutableArray()).ToImmutableArray();
        for (var index = 0; index < groups.Length; index++)
        {
            if (groupRows[index].Length != groups[index].Children.Count || groupRows[index].Any(row => row.Style.TableDisplay != TableDisplay.Row))
            {
                return Invalid(groups[index], "the children of a row group must be rows.");
            }
        }

        var rowBoxes = groupRows.SelectMany(rows => rows).ToImmutableArray();
        if (rowBoxes.FirstOrDefault(row => row.Children.Any(child => child is not Box)) is { } mixed)
        {
            return Invalid(mixed, "the children of a row must be cell boxes.");
        }

        var cellBoxes = rowBoxes.Select(row => row.Children.Cast<Box>().ToImmutableArray()).ToImmutableArray();
        var grid = TableSlots.Fill([.. groupRows.Select(rows => (IReadOnlyList<IReadOnlyList<CellSpan>>)[.. rows.Select(row => (IReadOnlyList<CellSpan>)[.. row.Children.Cast<Box>().Select(cell => cell.Style.Span)])])], columnCount);
        if (grid.Problems.Where(problem => problem.Kind != TableSlots.ProblemKind.EmptyRow).Select(problem => (TableSlots.Problem?)problem).FirstOrDefault() is { } problem)
        {
            var box = cellBoxes[problem.Row][problem.Cell];
            return Invalid(box, problem.Kind switch
            {
                TableSlots.ProblemKind.PastLastColumn => $"the cell (span {box.Style.Span}) does not fit in the table's {columnCount} columns.",
                TableSlots.ProblemKind.SpanPastGroup => $"the cell's row span of {box.Style.Span.Rows} runs past the end of its row group.",
                TableSlots.ProblemKind.SpanBelowOne => $"the cell's span {box.Style.Span} is less than one column or one row.",
                TableSlots.ProblemKind.SpanTooLarge => $"the cell's span {box.Style.Span} is larger than {TableSlots.MaxColumnSpan} columns or {TableSlots.MaxRowSpan} rows.",
                _ => "the cell overlaps a cell that spans rows from above.",
            });
        }

        var rows = groupRows.SelectMany((rows, group) => rows.Select((row, index) => new Row(row, groups[group], index == 0, index == rows.Length - 1))).ToImmutableArray();
        var flatCells = cellBoxes.SelectMany(cells => cells).ToImmutableArray();
        var cells = grid.Cells.Select((placement, index) => new Cell(flatCells[index], placement.Row, placement.Column, placement.Span.Rows, placement.Span.Columns)).ToImmutableArray();
        var headerRows = groups.FirstOrDefault()?.Style.TableDisplay == TableDisplay.HeaderGroup ? groupRows[0].Length : 0;
        var startingIn = rows.Select(_ => ImmutableArray.CreateBuilder<int>()).ToArray();
        for (var index = 0; index < cells.Length; index++)
        {
            startingIn[cells[index].Row].Add(index);
        }

        ImmutableArray<ImmutableArray<int>> cellsStartingIn = [.. startingIn.Select(list => list.ToImmutable())];
        var units = BuildUnits(cells, cellsStartingIn);
        var unitOfRow = new int[rows.Length];
        for (var unit = 0; unit < units.Length; unit++)
        {
            for (var row = units[unit].FirstRow; row <= units[unit].LastRow; row++)
            {
                unitOfRow[row] = unit;
            }
        }

        return Result<TableGrid, Error>.Success(new TableGrid(rows, cells, columnCount, headerRows, units, grid.Slots, cellsStartingIn, [.. unitOfRow]));
    }

    /// <summary>Joins rows into units: a unit grows while a cell that starts inside it reaches a later row.</summary>
    private static ImmutableArray<Unit> BuildUnits(ImmutableArray<Cell> cells, ImmutableArray<ImmutableArray<int>> cellsStartingIn)
    {
        var units = ImmutableArray.CreateBuilder<Unit>();
        var first = 0;
        while (first < cellsStartingIn.Length)
        {
            var last = first;
            for (var row = first; row <= last; row++)
            {
                last = cellsStartingIn[row].Select(cell => cells[cell].LastRow).Append(last).Max();
            }

            units.Add(new Unit(first, last));
            first = last + 1;
        }

        return units.ToImmutable();
    }

    private static Result<TableGrid, Error> Invalid(Box box, string message) =>
        Result<TableGrid, Error>.Failure(LayoutErrors.InvalidContent(box, message));
}
