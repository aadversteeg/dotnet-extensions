using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The slot-filling rule of a table, in one place for every layer that needs it: the front-end checks an author's
/// table with it, layout builds its grid with it, and the PDF/UA check finds rows that do not fill every column with
/// it. The rule is the HTML table model's (HTML Living Standard §4.9.12.1, "Forming a table"; CSS 2.1 §17.5 places cells
/// the same way and says "A cell box cannot extend beyond the last row box of a table or row group"):
/// rows are filled top to bottom; each cell takes the first column of its row that no cell from above covers yet,
/// and covers as many columns and rows as its span says; a row span ends with its row group.
/// </summary>
public static class TableSlots
{
    /// <summary>What is wrong with a table's shape.</summary>
    public enum ProblemKind
    {
        /// <summary>A cell reaches past the last column (more cells than columns, or too wide a column span).</summary>
        PastLastColumn,

        /// <summary>A cell's row span runs past the last row of its row group.</summary>
        SpanPastGroup,

        /// <summary>A cell covers a slot that a cell spanning rows from above already covers.</summary>
        Overlap,

        /// <summary>A row without any cell.</summary>
        EmptyRow,

        /// <summary>A span larger than the HTML table model allows (<see cref="MaxColumnSpan"/>,
        /// <see cref="MaxRowSpan"/>).</summary>
        SpanTooLarge,

        /// <summary>A span of less than one column or one row.</summary>
        SpanBelowOne,
    }

    /// <summary>
    /// The largest column span: HTML's limit for <c>colspan</c> ("greater than zero and less than or equal to 1000",
    /// HTML Living Standard §4.9.11, attributes common to <c>td</c> and <c>th</c> elements). It bounds how far one cell can widen a grid
    /// whose column count is not given.
    /// </summary>
    public const int MaxColumnSpan = 1000;

    /// <summary>The largest row span: HTML's limit for <c>rowspan</c> ("less than or equal to 65534", same section).</summary>
    public const int MaxRowSpan = 65534;

    /// <summary>The rule for a span, in one place for every layer: at least one column and one row, at most
    /// <see cref="MaxColumnSpan"/> columns and <see cref="MaxRowSpan"/> rows.</summary>
    /// <param name="span">The span.</param>
    /// <returns>True when the span keeps the rule.</returns>
    public static bool IsValidSpan(CellSpan span) =>
        span.Columns is >= 1 and <= MaxColumnSpan && span.Rows is >= 1 and <= MaxRowSpan;

    /// <summary>One problem: its kind, and the row and the cell (index within its row; −1 for the row itself).</summary>
    /// <param name="Kind">What is wrong.</param>
    /// <param name="Row">The row, counted over the whole table.</param>
    /// <param name="Cell">The cell's index within its row, or −1 when the problem is the row.</param>
    public readonly record struct Problem(ProblemKind Kind, int Row, int Cell);

    /// <summary>Where one cell went.</summary>
    /// <param name="Row">Its first row, counted over the whole table.</param>
    /// <param name="Column">Its first column.</param>
    /// <param name="Span">Its span; a row span cut back to the end of its row group when it ran past it.</param>
    public readonly record struct Placement(int Row, int Column, CellSpan Span);

    /// <summary>The filled grid.</summary>
    /// <param name="Cells">Every cell, row by row, in document order.</param>
    /// <param name="Slots">Per row, the index into <paramref name="Cells"/> of the cell in each column, or −1 for an empty
    /// slot; as many columns as the widest row reaches when the column count is not given.</param>
    /// <param name="Problems">Everything that breaks the rule; empty for a valid table.</param>
    public sealed record Grid(ImmutableArray<Placement> Cells, ImmutableArray<ImmutableArray<int>> Slots, ImmutableArray<Problem> Problems)
    {
        /// <summary>The number of columns of the grid.</summary>
        public int ColumnCount => Slots.IsEmpty ? 0 : Slots[0].Length;

        /// <summary>The rows with an empty slot: they do not fill every column.</summary>
        public IEnumerable<int> IncompleteRows => Enumerable.Range(0, Slots.Length).Where(row => Slots[row].Contains(-1));
    }

    /// <summary>
    /// Fills the slots. Every problem is reported, not only the first; a cell with a problem is placed as far as it
    /// fits, so that later cells are still placed where the author meant them and their own problems are found.
    /// </summary>
    /// <param name="groups">The table's row groups in order; per group its rows; per row the spans of its cells.</param>
    /// <param name="columnCount">The table's column count, or null to take as many as the widest row reaches.</param>
    /// <returns>The grid with its problems.</returns>
    public static Grid Fill(IReadOnlyList<IReadOnlyList<IReadOnlyList<CellSpan>>> groups, int? columnCount = null)
    {
        var limit = columnCount ?? int.MaxValue;
        var slots = groups.SelectMany(group => group).Select(_ => new List<int>()).ToArray();
        var cells = ImmutableArray.CreateBuilder<Placement>();
        var problems = ImmutableArray.CreateBuilder<Problem>();
        var firstRow = 0;
        foreach (var group in groups)
        {
            var lastRowOfGroup = firstRow + group.Count - 1;
            for (var index = 0; index < group.Count; index++)
            {
                var row = firstRow + index;
                if (group[index].Count == 0)
                {
                    problems.Add(new Problem(ProblemKind.EmptyRow, row, -1));
                }

                var column = 0;
                for (var cell = 0; cell < group[index].Count; cell++)
                {
                    while (column < limit && IsCovered(slots[row], column))
                    {
                        column++;
                    }

                    // All span arithmetic in long: a span is the author's number and may be anything up to
                    // int.MaxValue. A span above HTML's limits is cut to them, so no single number decides how many
                    // slots are made.
                    var span = group[index][cell];
                    var columns = System.Math.Clamp(span.Columns, 1, MaxColumnSpan);
                    var rows = System.Math.Clamp(span.Rows, 1, MaxRowSpan);
                    if (!IsValidSpan(span))
                    {
                        // The one problem of this cell: whether the cut span still runs past the columns or the
                        // group follows from the author's number, not from where the cell is.
                        problems.Add(new Problem(span.Columns < 1 || span.Rows < 1 ? ProblemKind.SpanBelowOne : ProblemKind.SpanTooLarge, row, cell));
                    }
                    else
                    {
                        if ((long)column + columns > limit)
                        {
                            problems.Add(new Problem(ProblemKind.PastLastColumn, row, cell));
                        }

                        if ((long)row + rows - 1 > lastRowOfGroup)
                        {
                            problems.Add(new Problem(ProblemKind.SpanPastGroup, row, cell));
                        }
                    }

                    var placed = new CellSpan(
                        System.Math.Max(1, (int)System.Math.Min(columns, (long)limit - column)),
                        System.Math.Min(rows, lastRowOfGroup - row + 1));
                    if (column < limit && Cover(slots, row, column, placed, cells.Count))
                    {
                        problems.Add(new Problem(ProblemKind.Overlap, row, cell));
                    }

                    cells.Add(new Placement(row, column, placed));
                    column = (int)System.Math.Min((long)column + columns, limit);
                }
            }

            firstRow += group.Count;
        }

        var width = columnCount ?? slots.Select(row => row.Count).DefaultIfEmpty(0).Max();
        ImmutableArray<ImmutableArray<int>> grid = [.. slots.Select(row => Enumerable.Range(0, width).Select(column => column < row.Count ? row[column] : -1).ToImmutableArray())];
        return new Grid(cells.ToImmutable(), grid, problems.ToImmutable());
    }

    private static bool IsCovered(List<int> row, int column) => column < row.Count && row[column] >= 0;

    /// <summary>Marks the slots a cell covers, growing the rows as needed; true when one of them was already covered
    /// (the cell that covered it first keeps it).</summary>
    private static bool Cover(List<int>[] slots, int row, int column, CellSpan span, int cell)
    {
        var overlaps = false;
        for (var covered = row; covered < row + span.Rows; covered++)
        {
            for (var spanned = column; spanned < column + span.Columns; spanned++)
            {
                while (slots[covered].Count <= spanned)
                {
                    slots[covered].Add(-1);
                }

                overlaps |= slots[covered][spanned] >= 0;
                slots[covered][spanned] = slots[covered][spanned] >= 0 ? slots[covered][spanned] : cell;
            }
        }

        return overlaps;
    }
}
