using Ave.Documents.Layout;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// A break token is a position in the content: two tokens are equal exactly when they resume at the same place. For a
/// table that is the next row, where each cut cell goes on, and whether the break fell inside the next row or before
/// it: a page that starts inside a row lays it out differently (no line above the cut cells) from one that starts with
/// it.
/// </summary>
public class BreakTokenTests
{
    [Fact]
    public void A_table_token_inside_a_row_differs_from_one_before_it()
    {
        var before = new TracksBreakToken(3, [], RowIsSplit: false);
        var inside = new TracksBreakToken(3, [], RowIsSplit: true);

        before.Should().NotBe(inside);
        before.Should().Be(new TracksBreakToken(3, [], RowIsSplit: false));
    }

    [Fact]
    public void Table_tokens_at_the_same_place_are_equal_whatever_they_carry()
    {
        var token = new TracksBreakToken(3, [new CellBreak(1, null, IsComplete: true)], RowIsSplit: true);

        token.Should().Be(token with { Cells = [new CellBreak(1, null, IsComplete: true)] });
        token.GetHashCode().Should().Be((token with { Cells = [new CellBreak(1, null, IsComplete: true)] }).GetHashCode());
        token.Should().NotBe(token with { NextRow = 4 });
        token.Should().NotBe(token with { Cells = [new CellBreak(1, null, IsComplete: false)] });
    }
}
