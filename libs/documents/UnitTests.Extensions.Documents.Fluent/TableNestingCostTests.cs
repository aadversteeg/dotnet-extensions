using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The work of nested tables that break across a page: a paragraph of 12 forced lines on a page
/// that holds 10, inside <c>t</c> one-cell tables. With repeated grid lines (the default) every level laid its cut cell
/// out twice, once in the whole slice and once clear of the line at the cut, so the work doubled with every level:
/// 2^t layout calls, and a document of a few hundred bytes did not finish. A cut cell is now laid out once
/// (<c>TableSegments.Slice</c>), so the count is the same with and without grid lines: 2 (t + 1)(t + 2) calls in all,
/// and a box inside <c>k</c> tables is laid out at most 2k + 1 times on a page — each table around it measures it at
/// unlimited height, its own table lays it out in the cut and once more for what is left of it. Counted, not timed.
/// </summary>
public class TableNestingCostTests
{
    private static (long Calls, int MostPerBox) Count(int tables, TableLines lines)
    {
        Element inner = Paragraph([.. Enumerable.Range(0, 12).SelectMany(index => index == 0 ? new[] { Text($"p{index}") } : [LineBreak(), Text($"p{index}")])]).Orphans(1).Widows(1);
        for (var level = 0; level < tables; level++)
        {
            var table = Table([Fill()], [TableRow([Cell([inner])])]);
            inner = lines switch
            {
                TableLines.Repeated => table.GridLines(0.75.Pt(), Color.Black).RepeatLinesAtBreaks(),
                TableLines.Split => table.GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks(),
                _ => table,
            };
        }

        var model = DocumentTranslator.ToModel(Document([inner], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt())).Value;
        long calls = 0;
        var perBox = new Dictionary<(int Page, NodeId Box), int>();
        var environment = LayoutEnvironment.Create(TestFonts.SetWithItalic);
        var counted = environment.Algorithms;
        foreach (var (id, algorithm) in environment.Algorithms)
        {
            counted = counted.SetItem(id, new RegisteredAlgorithm((env, box, space, incoming) =>
            {
                calls++;
                perBox[(env.Page.Number, box.Id)] = perBox.GetValueOrDefault((env.Page.Number, box.Id)) + 1;
                return algorithm.Layout(env, box, space, incoming);
            }, algorithm.Measure));
        }

        var result = DocumentLayout.Run(model, environment with { Algorithms = counted });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Pages.Should().HaveCount(2);
        return (calls, perBox.Values.Max());
    }

    public enum TableLines
    {
        None,
        Split,
        Repeated,
    }

    [Theory]
    [InlineData(9, 220)]
    [InlineData(11, 312)]
    [InlineData(13, 420)]
    [InlineData(15, 544)]
    public void Nested_tables_that_break_cost_the_same_with_repeated_grid_lines_as_without(int tables, long calls)
    {
        // 2 (t + 1)(t + 2): the base before this rule counted 4,093 calls at 9 tables and 262,141 at 15, with repeated lines. The
        // count is quadratic in the depth, not exponential: each table measures every table inside it at unlimited height
        // (TableSegments.Measure) and lays a cut cell out once in the cut and once for what is left (CutShows, Rest). A
        // change that makes it worse fails here; one that makes it better (a layout result kept within the page, planned
        // for the next stage 1 pull request) is a deliberate edit of these numbers.
        var because = $"2 (t + 1)(t + 2) layout calls for t = {tables} nested tables that break across a page";
        calls.Should().Be(2L * (tables + 1) * (tables + 2), because);
        Count(tables, TableLines.Repeated).Calls.Should().Be(calls, because + ", with repeated grid lines");
        Count(tables, TableLines.Split).Calls.Should().Be(calls, because + ", with split grid lines");
        Count(tables, TableLines.None).Calls.Should().Be(calls, because + ", without grid lines");
    }

    [Theory]
    [InlineData(9)]
    [InlineData(15)]
    public void A_box_inside_k_tables_is_laid_out_at_most_2k_plus_1_times_on_a_page(int tables)
    {
        Count(tables, TableLines.Repeated).MostPerBox.Should().Be((2 * tables) + 1, "2k + 1 layouts of a box inside k tables on one page");
    }
}
