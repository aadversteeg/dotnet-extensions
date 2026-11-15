using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

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
        WhiteSpaceCollapsing.Collapse(["a  b"]).Should().Equal("a  b");

    [Fact]
    public void Leading_and_trailing_spaces_stay_for_line_breaking_to_remove() =>
        WhiteSpaceCollapsing.Collapse(["  a  "]).Should().Equal(" a ");
}
