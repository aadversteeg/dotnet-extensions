using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The case comparison reads the areas that paths which are not rectangles paint (<c>path</c> lines of <c>print.txt</c>,
/// a list marker's disc or circle): a print that paints one where the other does not, or another area, another colour or
/// another place, differs; the same area is no difference.
/// </summary>
public class PathComparisonTests
{
    private const string Page = "page 1 300.000 420.000\nword 1 42.000 50.000 50.250 10.500 one #000000 Lato-Regular\n";

    private const string Disc = "path 1 30.000 40.500 33.750 44.250 #000000 2cfc2/cfffc/fffff/cfffc/2cfc2\n";

    [Fact]
    public void The_same_painted_area_is_no_difference()
    {
        HtmlCases.Difference(PrintedPages.Parse(Page + Disc), PrintedPages.Parse(Page + Disc)).Should().BeNull();
        HtmlCases.Difference(PrintedPages.Parse(Page), PrintedPages.Parse(Page)).Should().BeNull();
    }

    [Fact]
    public void A_path_only_one_side_paints_is_a_difference()
    {
        HtmlCases.Difference(PrintedPages.Parse(Page), PrintedPages.Parse(Page + Disc)).Should().Be($"page 1 has 0 painted paths, Chromium 1; the engine's paths: digest {HtmlCases.Digest([])} (print.txt)");
        HtmlCases.Difference(PrintedPages.Parse(Page + Disc), PrintedPages.Parse(Page)).Should().MatchRegex(@"^page 1 has 1 painted paths, Chromium 0; the engine's paths: digest [0-9a-f]{16} \(print\.txt\)$")
            .And.NotContain(HtmlCases.Digest([]), "the digest is of the one path");
    }

    [Theory]
    [InlineData("path 1 30.000 40.500 33.750 44.250 #000000 2cfc2/cfffc/fffff/cfffc/2cfc3\n")]
    [InlineData("path 1 30.000 40.500 33.750 44.250 #000000 fffff/fffff/fffff/fffff/fffff\n")]
    [InlineData("path 1 30.750 40.500 34.500 44.250 #000000 2cfc2/cfffc/fffff/cfffc/2cfc2\n")]
    [InlineData("path 1 30.000 40.500 33.750 44.250 #ff0000 2cfc2/cfffc/fffff/cfffc/2cfc2\n")]
    public void Another_area_place_or_colour_is_a_difference(string chromium)
    {
        HtmlCases.Difference(PrintedPages.Parse(Page + Disc), PrintedPages.Parse(Page + chromium)).Should().StartWith("page 1 path at (30, 40.5) paints 2cfc2/cfffc/fffff/cfffc/2cfc2 #000000, Chromium");
    }

    [Fact]
    public void A_path_line_is_read_with_its_rows()
    {
        var path = PrintedPages.Parse(Page + Disc).Pages[0].Paths.Should().ContainSingle().Subject;
        path.Should().BeEquivalentTo(new { Page = 1, X0 = 30.0, Y0 = 40.5, X1 = 33.75, Y1 = 44.25, Colour = "#000000" });
        path.Rows.Should().Equal("2cfc2", "cfffc", "fffff", "cfffc", "2cfc2");
        PrintedPages.Parse(Page).Pages[0].Paths.Should().BeEmpty();
        FluentActions.Invoking(() => PrintedPages.Parse(Page + "path 1 30.000 40.500 33.750 44.250 #000000\n")).Should().Throw<FormatException>();
    }
}
