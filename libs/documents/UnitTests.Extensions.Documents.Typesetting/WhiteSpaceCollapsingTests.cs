using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting;

public class WhiteSpaceCollapsingTests
{
    [Fact]
    public void Spaces_tabs_and_line_breaks_collapse_to_one_space() =>
        WhiteSpaceCollapsing.Collapse(["a  \t\r\n  b"]).Should().Equal("a b");

    [Fact]
    public void Collapsing_crosses_run_boundaries_and_keeps_the_first_space() =>
        WhiteSpaceCollapsing.Collapse(["a ", "  b", "\n", "c"]).Should().Equal("a ", "b", " ", "c");

    [Fact]
    public void The_no_break_space_is_not_collapsible() =>
        WhiteSpaceCollapsing.Collapse(["a\u00A0\u00A0b"]).Should().Equal("a\u00A0\u00A0b");

    [Fact]
    public void Leading_and_trailing_spaces_stay_for_line_breaking_to_remove() =>
        WhiteSpaceCollapsing.Collapse(["  a  "]).Should().Equal(" a ");

    [Fact]
    public void Preserved_spaces_stay_and_a_preserved_line_feed_becomes_the_forced_break() =>
        WhiteSpaceCollapsing.Collapse([("  a   b\n  c\r", WhiteSpaceCollapse.Preserve)]).Should().Equal("  a   b\u2028  c ");

    [Fact]
    public void Preserving_breaks_collapses_spaces_keeps_line_feeds_and_removes_the_spaces_after_them() =>
        WhiteSpaceCollapsing.Collapse([("a   b \n  c\n\nd", WhiteSpaceCollapse.PreserveBreaks)]).Should().Equal("a b \u2028c\u2028\u2028d");

    [Fact]
    public void A_preserved_space_neither_collapses_with_a_collapsible_space_beside_it_nor_stops_it_being_kept() =>
        WhiteSpaceCollapsing.Collapse([("a ", WhiteSpaceCollapse.Collapse), ("  ", WhiteSpaceCollapse.Preserve), (" b", WhiteSpaceCollapse.Collapse)])
            .Should().Equal("a ", "  ", " b");

    [Fact]
    public void Collapsing_still_crosses_a_run_whose_line_feeds_are_preserved() =>
        WhiteSpaceCollapsing.Collapse([("a ", WhiteSpaceCollapse.Collapse), (" b\n", WhiteSpaceCollapse.PreserveBreaks), (" c", WhiteSpaceCollapse.Collapse)])
            .Should().Equal("a ", "b\u2028", "c");

    [Theory]
    [InlineData("   ", WhiteSpaceCollapse.Collapse, true)]
    [InlineData("\n", WhiteSpaceCollapse.Collapse, true)]
    [InlineData("   ", WhiteSpaceCollapse.PreserveBreaks, true)]
    [InlineData(" \n ", WhiteSpaceCollapse.PreserveBreaks, false)]
    [InlineData(" ", WhiteSpaceCollapse.Preserve, false)]
    [InlineData("", WhiteSpaceCollapse.Preserve, true)]
    public void Only_white_space_that_collapses_leaves_no_line(string text, WhiteSpaceCollapse mode, bool allCollapsible) =>
        WhiteSpaceCollapsing.IsAllCollapsible(text, mode).Should().Be(allCollapsible);
}
