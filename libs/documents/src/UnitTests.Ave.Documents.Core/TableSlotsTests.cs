using Ave.Documents.Core;

namespace UnitTests.Ave.Documents.Core;

/// <summary>The slot-filling rule of the HTML table model, shared by the front-end, layout and the PDF/UA check.</summary>
public class TableSlotsTests
{
    private static readonly CellSpan One = CellSpan.Single;

    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<CellSpan>>> Groups(params CellSpan[][][] groups) => groups;

    [Fact]
    public void A_cell_takes_the_first_column_a_row_span_from_above_leaves_free()
    {
        // Row 0: A spans two rows, B. Row 1: C goes to column 1, beside A.
        var grid = TableSlots.Fill(Groups([[new CellSpan(1, 2), One], [One]]), columnCount: 2);

        grid.Problems.Should().BeEmpty();
        grid.Cells.Select(cell => (cell.Row, cell.Column)).Should().Equal((0, 0), (0, 1), (1, 1));
        grid.Slots[1].Should().Equal(0, 2);
    }

    [Fact]
    public void Without_a_column_count_the_grid_is_as_wide_as_its_widest_row()
    {
        var grid = TableSlots.Fill(Groups([[new CellSpan(3, 1)], [One]]));

        grid.ColumnCount.Should().Be(3);
        grid.IncompleteRows.Should().Equal(1);
    }

    [Fact]
    public void Every_problem_is_reported_with_its_row_and_cell()
    {
        var grid = TableSlots.Fill(Groups([[One, One, One], [], [new CellSpan(1, 2)]], [[One]]), columnCount: 2);

        grid.Problems.Should().Equal(
            new TableSlots.Problem(TableSlots.ProblemKind.PastLastColumn, 0, 2),
            new TableSlots.Problem(TableSlots.ProblemKind.EmptyRow, 1, -1),
            new TableSlots.Problem(TableSlots.ProblemKind.SpanPastGroup, 2, 0));
    }

    [Fact]
    public void A_row_span_ends_with_its_row_group()
    {
        var grid = TableSlots.Fill(Groups([[new CellSpan(1, 3)], [One]], [[One, One]]), columnCount: 2);

        grid.Cells[0].Span.Should().Be(new CellSpan(1, 2), "cut back to the group's last row");
        grid.Slots[2].Should().Equal([2, 3], "the next group starts with free slots");
    }

    [Fact]
    public void A_cell_spanning_columns_over_a_slot_covered_from_above_overlaps()
    {
        // Row 0: A, B spans two rows in column 1. Row 1: C spans two columns from column 0 and runs into B.
        var grid = TableSlots.Fill(Groups([[One, new CellSpan(1, 2)], [new CellSpan(2, 1)]]), columnCount: 2);

        grid.Problems.Should().ContainSingle().Which.Should().Be(new TableSlots.Problem(TableSlots.ProblemKind.Overlap, 1, 0));
        grid.Slots[1].Should().Equal([2, 1], "the cell that covered the slot first keeps it");
    }
}
