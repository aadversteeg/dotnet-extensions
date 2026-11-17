using Ave.Documents.Fonts.OpenType;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// The reader under every layout-table parser: its range checks, its stop after the first problem, and its work
/// budget, tested on the reader itself so that each rule is pinned by exactly one test.
/// </summary>
public class TableReaderTests
{
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MaxValue - 3)]
    public void A_read_near_the_largest_offset_is_out_of_range_and_does_not_wrap_around(int offset)
    {
        // offset + size is added in 64-bit arithmetic: in 32 bits int.MaxValue - 1 + 2 wraps to a negative number,
        // which is "inside" any table, and the read would index far outside the bytes.
        var reader = new TableReader(new byte[64]);

        (reader.UInt16(offset), reader.Int16(offset), reader.UInt32(offset), reader.Tag(offset)).Should().Be(((ushort)0, (short)0, 0u, "    "));
        reader.FirstOutOfRange.Should().NotBeNull();
    }

    [Fact]
    public void After_a_problem_no_further_value_is_read()
    {
        var reader = new TableReader(new byte[] { 0x12, 0x34 });
        reader.Invalid("coverage format 3");

        reader.UInt16(0).Should().Be(0, "the table is known to be malformed");
    }

    [Fact]
    public void A_table_may_spend_eight_units_per_byte_and_no_more()
    {
        // 4096 bytes: a budget of 8 x 4096 = 32 768 reads, above the minimum of 16 384.
        var reader = new TableReader(new byte[4096]);
        var budget = TableReader.WorkPerByte * 4096;

        reader.Spend(budget).Should().BeTrue();
        reader.HasProblem.Should().BeFalse();
        reader.Spend(1).Should().BeFalse();
        reader.FirstInvalid.Should().NotBeNull();
    }

    [Fact]
    public void A_small_table_has_the_minimum_budget()
    {
        var reader = new TableReader(new byte[16]);

        reader.Spend(TableReader.MinimumWork).Should().BeTrue();
        reader.Spend(1).Should().BeFalse();
    }
}
