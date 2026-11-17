using System.Collections.Immutable;
using Ave.Documents.Core;

namespace UnitTests.Ave.Documents.Core;

public class ValueListTests
{
    private sealed record Holder(string Name, ValueList<int> Items);

    [Fact]
    public void Lists_with_equal_items_in_the_same_order_are_equal()
    {
        ValueList<int> first = [1, 2, 3];
        ValueList<int> second = [.. new[] { 1, 2, 3 }];

        (first == second).Should().BeTrue();
        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Theory]
    [InlineData(new[] { 1, 2 })]
    [InlineData(new[] { 1, 2, 4 })]
    [InlineData(new[] { 3, 2, 1 })]
    public void A_different_length_item_or_order_makes_lists_unequal(int[] other)
    {
        ValueList<int> list = [1, 2, 3];

        (list == other.ToValueList()).Should().BeFalse();
        (list != other.ToValueList()).Should().BeTrue();
    }

    [Fact]
    public void A_record_holding_a_value_list_compares_by_content()
    {
        var first = new Holder("a", [1, 2]);
        var second = new Holder("a", [1, 2]);

        first.Should().Be(second);
        (first with { Items = [1, 3] }).Should().NotBe(second);
    }

    [Fact]
    public void An_immutable_array_with_equal_items_is_not_equal_by_itself_which_is_why_the_type_exists()
    {
        ImmutableArray<int> first = [1, 2];
        ImmutableArray<int> second = [1, 2];

        (first == second).Should().BeFalse();
        ((ValueList<int>)first == second).Should().BeTrue();
    }

    [Fact]
    public void The_default_value_is_the_empty_list()
    {
        var empty = default(ValueList<string>);

        empty.IsEmpty.Should().BeTrue();
        empty.Count.Should().Be(0);
        (empty == ValueList<string>.Empty).Should().BeTrue();
        (empty == []).Should().BeTrue();
        empty.ToString().Should().Be("[]");
    }

    [Fact]
    public void It_reads_like_a_list()
    {
        ValueList<string> list = ["a", "b"];

        list[1].Should().Be("b");
        list.Should().Equal("a", "b");
        list.ToString().Should().Be("[a, b]");
    }
}
