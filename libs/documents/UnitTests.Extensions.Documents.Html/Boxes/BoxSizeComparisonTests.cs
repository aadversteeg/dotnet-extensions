using Ave.Extensions.Documents.DocumentModel;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The comparison of <c>boxes.txt</c> itself (<see cref="HtmlCases.BoxDifference(CaseFolder, string, IReadOnlyList{string})"/>):
/// how a box named by its id is found, and which size of the engine's is put against the one size the file holds per
/// element. The file is measured in a window, one layout without page breaks; a box the engine prints in one part
/// is compared on its page, one it cuts over pages in the engine's layout of the same boxes without breaks. Each test
/// changes one row of a measured file in memory and reads the difference that row gives.
/// </summary>
public class BoxSizeComparisonTests
{
    private static List<string> Rows(string name) => [.. HtmlCases.BoxRows(HtmlCases.Cases, name)];

    private static List<string> With(List<string> rows, string label, string height)
    {
        var index = rows.FindIndex(row => row.Split(' ') is { Length: > 3 } fields && string.Join(' ', fields[3..]) == label);
        index.Should().BeGreaterThanOrEqualTo(0, $"the file has a row '{label}'");
        var fields = rows[index].Split(' ');
        fields[2] = height;
        return [.. rows.Select((row, at) => at == index ? string.Join(' ', fields) : row)];
    }

    private static List<string> WithWidth(List<string> rows, string label, string width)
    {
        var index = rows.FindIndex(row => row.Split(' ') is { Length: > 3 } fields && string.Join(' ', fields[3..]) == label);
        index.Should().BeGreaterThanOrEqualTo(0, $"the file has a row '{label}'");
        var fields = rows[index].Split(' ');
        fields[1] = width;
        return [.. rows.Select((row, at) => at == index ? string.Join(' ', fields) : row)];
    }

    private static string? Difference(string name, List<string> rows) => HtmlCases.BoxDifference(HtmlCases.Cases, name, rows);

    [Theory]
    [InlineData("te-top")]
    [InlineData("tj-order")]
    [InlineData("te-table-border")]
    [InlineData("tl-fixed-width-auto")]
    [InlineData("fr-empty-span")]
    public void The_measured_rows_of_a_case_with_boxes_named_by_id_have_no_difference(string name) =>
        Difference(name, Rows(name)).Should().BeNull();

    [Fact]
    public void A_table_inside_a_div_on_the_same_line_is_found_as_the_line_s_table_box()
    {
        Difference("te-top", With(Rows("te-top"), "t", "68.203")).Should().Be("box table 't' height 67.453, Chromium 68.203 (boxes.txt)");
        Difference("tj-order", With(Rows("tj-order"), "t2", "43.219")).Should().Be("box table 't2' height 42.469, Chromium 43.219 (boxes.txt)");
    }

    [Fact]
    public void The_box_of_an_id_is_the_box_of_the_role_its_tag_gives_not_the_first_box_on_its_line()
    {
        const string Html = """
            <!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="common.css"><style>@page { size: 300pt 204pt; margin: 12pt; } body { font-size: 12pt; line-height: 15pt; }</style></head><body>
            <div id="d" style="padding: 6pt"><table id="t"><tbody><tr><td>a</td></tr></tbody></table></div>
            </body></html>
            """;

        var table = HtmlCases.BoxDifference(Html, ["table 0 0 t", "td 0 15 a"]);
        var div = HtmlCases.BoxDifference(Html, ["div 0 0 d", "td 0 15 a"]);

        table.Should().StartWith("box table 't' height ");
        div.Should().StartWith("box div 'd' height ");
        Height(div!).Should().BeApproximately(Height(table!) + 12, 0.001, "the div is the table and its padding");
        HtmlCases.BoxDifference(Html, ["p 0 0 t", "td 0 15 a"]).Should().Be("box p 't' is not one <p> with that id (boxes.txt)");
    }

    [Fact]
    public void An_element_of_a_line_whose_boxes_do_not_match_its_elements_is_a_difference()
    {
        // An inline span makes no box of its own: the line holds the div's box only.
        Difference("font-sizes", ["span 50.227 11.250 n10-9pt"]).Should().StartWith("box span 'n10-9pt' is not found: line 6 has 1 boxes of role GenericRole for 2 elements of that role");
    }

    [Fact]
    public void A_cell_the_engine_cuts_over_pages_is_compared_in_the_layout_without_breaks()
    {
        const string Cell = "b0b1b2b3b4b5b6b7b8b9b10b";
        var rows = Rows("a2-row-end");

        Difference("a2-row-end", rows).Should().BeNull();
        Difference("a2-row-end", With(rows, Cell, "331.500")).Should().Be($"box td '{Cell}' height without breaks (in 2 parts) 330.75, Chromium 331.5 (boxes.txt)");
        Difference("a2-row-end", With(rows, Cell, "164.625")).Should().Be($"box td '{Cell}' height without breaks (in 2 parts) 330.75, Chromium 164.625 (boxes.txt)");
    }

    [Fact]
    public void A_cell_the_engine_prints_in_one_part_is_compared_on_its_page()
    {
        Difference("te-top", With(Rows("te-top"), "a0", "23.484")).Should().Be("box td 'a0' height 22.734, Chromium 23.484 (boxes.txt)");
    }

    [Fact]
    public void The_width_of_a_box_is_compared_after_its_height_where_the_engine_prints_it_and_without_breaks_where_it_cuts_it()
    {
        Difference("te-top", WithWidth(Rows("te-top"), "a0", "99.996")).Should().Be("box td 'a0' width 99.252, Chromium 99.996 (boxes.txt)");
        Difference("te-top", WithWidth(Rows("te-top"), "t", "200.754")).Should().Be("box table 't' width 200.004, Chromium 200.754 (boxes.txt)");
        Difference("a2-row-end", WithWidth(Rows("a2-row-end"), "b0b1b2b3b4b5b6b7b8b9b10b", "276.000"))
            .Should().Be("box td 'b0b1b2b3b4b5b6b7b8b9b10b' width without breaks (in 2 parts) 275.25, Chromium 276 (boxes.txt)");
    }

    [Fact]
    public void A_header_cell_repeated_on_the_next_page_is_one_part_and_each_copy_is_compared()
    {
        Difference("a1-header-row", Rows("a1-header-row")).Should().BeNull();
        Difference("a1-header-row", With(Rows("a1-header-row"), "head", "15.000")).Should().Be("box th 'head' height 14.25, Chromium 15 (boxes.txt)");
    }

    [Fact]
    public void A_table_cut_over_pages_is_compared_whole_in_the_layout_without_breaks()
    {
        Difference("te-break", Rows("te-break")).Should().BeNull();
        Difference("te-break", With(Rows("te-break"), "t", "248.063")).Should().Be("box table 't' height without breaks (in 2 parts) 247.313, Chromium 248.063 (boxes.txt)");
    }

    [Fact]
    public void A_break_the_document_forces_makes_the_layout_without_breaks_a_difference()
    {
        const string Html = """
            <!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="common.css"><style>@page { size: 300pt 204pt; margin: 12pt; } body { font-size: 12pt; line-height: 15pt; } td { padding: 0; }</style></head><body>
            <table><tbody><tr><td>a0<br>a1<br>a2<br>a3<br>a4<br>a5<br>a6<br>a7<br>a8<br>a9<br>a10<br>a11<br>a12<br>a13</td></tr></tbody></table><p style="break-before: page">after</p>
            </body></html>
            """;

        HtmlCases.BoxDifference(Html, ["td 0 210 a0a1a2"]).Should().Be("box td 'a0a1a2': the layout without breaks has 2 pages (boxes.txt)");
    }

    [Fact]
    public void The_layout_without_breaks_is_the_same_page_as_tall_as_the_model_allows()
    {
        var document = HtmlCases.Boxes("pm-image-set").Value.Document;
        var unbroken = HtmlCases.Unbroken(document);

        unbroken.Page.Height.Should().Be(ModelLimits.MaxLength);
        unbroken.Page.Should().Be(document.Page with { Height = ModelLimits.MaxLength }, "only the height changes: the width, margins, clip and templates stay");
        unbroken.Root.Should().BeSameAs(document.Root);
    }

    private static double Height(string difference) =>
        double.Parse(difference.Split(" height ")[1].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
}
