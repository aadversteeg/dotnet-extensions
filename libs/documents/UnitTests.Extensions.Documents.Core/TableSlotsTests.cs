using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

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

    [Theory]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void A_span_of_any_size_is_a_problem_value_and_never_overflows(int columns, int rows)
    {
        // Row 0 of three rows: a, the huge cell, c, in a 3-column table. The span arithmetic is done in long, so the
        // cell after it is still placed (at the end of the grid) and nothing indexes outside a row. The huge cell
        // itself has the one problem SpanTooLarge; a row span cut to its group still covers the rows below it.
        var grid = TableSlots.Fill(Groups([[One, new CellSpan(columns, rows), One], [One, One, One], [One, One, One]]), columnCount: 3);

        grid.Problems.Where(problem => problem.Row == 0 && problem.Cell == 1).Should().ContainSingle()
            .Which.Kind.Should().Be(TableSlots.ProblemKind.SpanTooLarge);
        grid.Slots.Should().OnlyContain(row => row.Length == 3);
        grid.Cells.Should().OnlyContain(cell => cell.Column <= 3 && cell.Span.Columns <= 3 && cell.Span.Rows <= 3);
    }

    [Fact]
    public void A_row_span_past_the_group_is_found_however_large_it_is()
    {
        // row + rows − 1 overflowed int and the span counted as fitting.
        var grid = TableSlots.Fill(Groups([[One], [One], [new CellSpan(1, int.MaxValue)]]), columnCount: 1);

        grid.Problems.Should().ContainSingle().Which.Should().Be(new TableSlots.Problem(TableSlots.ProblemKind.SpanTooLarge, 2, 0));
        grid.Cells[2].Span.Should().Be(new CellSpan(1, 1), "cut back to its group's last row");
    }

    [Fact]
    public void A_row_span_past_the_group_within_the_html_limit_is_found_in_long_arithmetic()
    {
        var grid = TableSlots.Fill(Groups([[One], [One], [new CellSpan(1, TableSlots.MaxRowSpan)]]), columnCount: 1);

        grid.Problems.Should().ContainSingle().Which.Should().Be(new TableSlots.Problem(TableSlots.ProblemKind.SpanPastGroup, 2, 0));
    }

    [Fact]
    public void Without_a_column_count_a_span_above_the_html_limit_is_a_problem_and_takes_no_memory()
    {
        // HTML caps colspan at 1000 and rowspan at 65534; without a column count, those are the bound on how far one
        // cell can widen the grid. Five million columns would be 5 million slots per row.
        var grid = TableSlots.Fill(Groups([[new CellSpan(5_000_000, 1)], [One]]));

        grid.Problems.Should().ContainSingle().Which.Kind.Should().Be(TableSlots.ProblemKind.SpanTooLarge);
        grid.ColumnCount.Should().Be(TableSlots.MaxColumnSpan);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1000, 65534, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(-3, 1, false)]
    [InlineData(1001, 1, false)]
    [InlineData(1, 65535, false)]
    public void The_span_rule_is_one_to_the_table_model_s_limits(int columns, int rows, bool valid)
    {
        TableSlots.IsValidSpan(new CellSpan(columns, rows)).Should().Be(valid);
    }

    [Fact]
    public void A_span_below_one_is_its_own_problem()
    {
        var grid = TableSlots.Fill(Groups([[new CellSpan(0, 1)]]), columnCount: 1);

        grid.Problems.Should().ContainSingle().Which.Kind.Should().Be(TableSlots.ProblemKind.SpanBelowOne);
    }

    [Fact]
    public void A_grid_at_the_PDF_UA_check_s_cap_fills_with_the_memory_its_comment_states()
    {
        // The PDF/UA-1 check fills at most 4 194 304 slots (TableRegularity.MaxGridSlots): here 1 000 columns of 4 194
        // rows, each row one cell spanning them all. Its comment states about 51 MiB (16 MiB of slots, plus the slot
        // lists growing as cells are placed and the grid copied from them): pinned below 64 MiB. Measured, not timed.
        IReadOnlyList<IReadOnlyList<IReadOnlyList<CellSpan>>> groups = [[.. Enumerable.Range(0, 4_194).Select(_ => (IReadOnlyList<CellSpan>)[new CellSpan(1_000, 1)])]];
        var before = GC.GetAllocatedBytesForCurrentThread();

        var grid = TableSlots.Fill(groups, 1_000);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        grid.Slots.Length.Should().Be(4_194);
        allocated.Should().BeLessThan(64L * 1024 * 1024, $"{allocated / (1024.0 * 1024):0.0} MiB allocated");
    }

    [Fact]
    public void A_grid_of_the_most_columns_and_slots_fills_and_one_column_or_row_more_is_too_large()
    {
        IReadOnlyList<IReadOnlyList<CellSpan>> Rows(int count) => [.. Enumerable.Range(0, count).Select(_ => (IReadOnlyList<CellSpan>)[One])];

        TableSlots.Fill([Rows(1)], TableSlots.MaxColumns).Problems.Should().BeEmpty();
        TableSlots.Fill([Rows(1)], TableSlots.MaxColumns + 1).Problems.Should().Equal(new TableSlots.Problem(TableSlots.ProblemKind.TooLarge, 0, -1));
        var rows = TableSlots.MaxSlots / TableSlots.MaxColumns;
        TableSlots.Fill([Rows(rows)], TableSlots.MaxColumns).Problems.Should().BeEmpty();
        var tooMany = TableSlots.Fill([Rows(rows + 1)], TableSlots.MaxColumns);
        tooMany.Problems.Should().Equal(new TableSlots.Problem(TableSlots.ProblemKind.TooLarge, 0, -1));
        tooMany.Slots.Should().BeEmpty("nothing is filled");
    }

    [Fact]
    public void Without_a_column_count_the_cell_that_would_pass_the_limits_stops_the_filling()
    {
        // Cells of 1 000 columns side by side: 4 of them and one of 96 are the most columns; one column more is refused at
        // that cell. Under that width, 1 024 rows are the most slots; with a 1 025th row the same cell passes them.
        IReadOnlyList<CellSpan> Wide(int last) => [new(1_000, 1), new(1_000, 1), new(1_000, 1), new(1_000, 1), new(last, 1)];
        IReadOnlyList<IReadOnlyList<CellSpan>> Group(int last, int rows) => [Wide(last), .. Enumerable.Range(1, rows - 1).Select(_ => (IReadOnlyList<CellSpan>)[One])];

        TableSlots.Fill([Group(96, 1)]).ColumnCount.Should().Be(TableSlots.MaxColumns);
        TableSlots.Fill([Group(97, 1)]).Problems.Should().Equal(new TableSlots.Problem(TableSlots.ProblemKind.TooLarge, 0, 4));
        TableSlots.Fill([Group(96, 1_024)]).Problems.Should().BeEmpty();
        TableSlots.Fill([Group(96, 1_025)]).Problems.Should().Equal(new TableSlots.Problem(TableSlots.ProblemKind.TooLarge, 0, 4));
    }

    [Fact]
    public void A_staircase_of_spans_is_stopped_before_its_grid_is_made()
    {
        // Each row's cell starts after the columns the rows above still cover, so 1 000 rows of rowspan=1000
        // colspan=1000 would make a grid of 1 000 000 columns. The fifth cell passes the slots and stops the filling,
        // after about 4 million slots, not 10^9: pinned below 64 MiB. Measured, not timed.
        IReadOnlyList<IReadOnlyList<IReadOnlyList<CellSpan>>> groups = [[.. Enumerable.Range(0, 1_000).Select(_ => (IReadOnlyList<CellSpan>)[new CellSpan(1_000, 1_000)])]];
        var before = GC.GetAllocatedBytesForCurrentThread();

        var grid = TableSlots.Fill(groups);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        grid.Problems[^1].Should().Be(new TableSlots.Problem(TableSlots.ProblemKind.TooLarge, 4, 0), "the rows before it run past their group, the fifth stops the filling");
        allocated.Should().BeLessThan(64L * 1024 * 1024, $"{allocated / (1024.0 * 1024):0.0} MiB allocated");
    }

    [Fact]
    public void The_slots_of_a_document_are_added_up_to_their_limit()
    {
        var slots = new TableSlots.DocumentSlots();

        slots.TryAdd(TableSlots.MaxDocumentSlots - 1).Should().BeTrue();
        slots.TryAdd(1).Should().BeTrue();
        slots.TryAdd(1).Should().BeFalse();
        slots.Count.Should().Be(TableSlots.MaxDocumentSlots);
        TableSlots.SlotsOf(int.MaxValue, int.MaxValue).Should().Be((long)int.MaxValue * int.MaxValue, "the product does not wrap");
    }
}
