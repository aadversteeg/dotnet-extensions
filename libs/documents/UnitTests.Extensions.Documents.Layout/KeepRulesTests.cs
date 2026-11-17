using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Keep rules in block flow (CSS Fragmentation 3 §3.1, §3.2) resolved by break appeal: a break an <c>avoid</c> is
/// against is taken only when no better one exists, so pagination always ends. Boxes are 20 pt wide, so every word is
/// a 12 pt line; the page area holds 6 lines.
/// </summary>
public class KeepRulesTests
{
    private static readonly BoxStyle Narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) };

    private static ValueList<PageFragment> Paginate(params Node[] children)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(children));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Per page: the ids of the root's children and how many lines each holds there.</summary>
    private static IEnumerable<string> Contents(ValueList<PageFragment> pages) =>
        pages.Select(page => string.Join(" ", ((BoxFragment)page.Root.Fragment).Children
            .Select(child => (BoxFragment)child.Fragment)
            .Select(box => $"{box.Box.Id}:{box.Children.Count}")));

    [Fact]
    public void Without_keep_rules_the_page_breaks_at_the_last_box_that_fits()
    {
        var pages = Paginate(Paragraph(1, Words(5), Narrow), Paragraph(2, "Heading", Narrow), Paragraph(3, Words(2), Narrow));

        Contents(pages).Should().Equal("#1:5 #2:1", "#3:2");
    }

    [Fact]
    public void A_box_kept_with_the_next_one_moves_with_it_to_the_next_page()
    {
        var heading = Narrow with { BreakAfter = BreakBetween.Avoid };

        var pages = Paginate(Paragraph(1, Words(5), Narrow), Paragraph(2, "Heading", heading), Paragraph(3, Words(2), Narrow));

        Contents(pages).Should().Equal(["#1:5", "#2:1 #3:2"], "the break before #3 violates break-after: avoid; the break before #2 is perfect");
    }

    [Fact]
    public void A_box_kept_with_the_previous_one_takes_it_along()
    {
        var pages = Paginate(
            Paragraph(1, Words(5), Narrow), Paragraph(2, "Heading", Narrow), Paragraph(3, Words(2), Narrow with { BreakBefore = BreakBetween.Avoid }));

        Contents(pages).Should().Equal("#1:5", "#2:1 #3:2");
    }

    [Fact]
    public void A_box_kept_together_moves_whole_when_it_fits_on_a_page()
    {
        var pages = Paginate(Paragraph(1, Words(3), Narrow), Paragraph(2, Words(4), Narrow with { BreakInside = BreakInside.Avoid }));

        Contents(pages).Should().Equal(["#1:3", "#2:4"], "without the rule three of its lines would stay on page 1");
    }

    [Fact]
    public void A_box_kept_together_but_taller_than_a_page_still_breaks_and_pagination_ends()
    {
        // It moves off page 1 (the perfect break before it), then breaks at the start of page 2, where no better
        // break exists (§4.4: rules are relaxed when there are not enough breaks).
        var pages = Paginate(Paragraph(1, "One", Narrow), Paragraph(2, Words(10), Narrow with { BreakInside = BreakInside.Avoid }));

        Contents(pages).Should().Equal("#1:1", "#2:6", "#2:4");
    }

    [Fact]
    public void A_chain_of_boxes_all_kept_with_the_next_one_breaks_at_the_latest_violating_break()
    {
        // Every break between them violates an avoid; none is better, so the latest that fits is used: no loop.
        var kept = Narrow with { BreakAfter = BreakBetween.Avoid };

        var pages = Paginate([.. Enumerable.Range(1, 10).Select(id => (Node)Paragraph(id, $"w{id}", kept))]);

        Contents(pages).Should().Equal("#1:1 #2:1 #3:1 #4:1 #5:1 #6:1", "#7:1 #8:1 #9:1 #10:1");
    }

    [Fact]
    public void A_keep_rule_on_a_first_child_is_seen_at_its_parent_s_edge()
    {
        // break-before: avoid on the first child of block #3 applies to the break before #3 (§3.1.1: values on a first
        // child propagate to the parent's edge), so #2 goes with #3.
        var inner = Flow(3, [Paragraph(4, Words(2), Narrow with { BreakBefore = BreakBetween.Avoid })]);

        var pages = Paginate(Paragraph(1, Words(5), Narrow), Paragraph(2, "Heading", Narrow), inner);

        Contents(pages).Should().Equal("#1:5", "#2:1 #3:1");
    }
}
