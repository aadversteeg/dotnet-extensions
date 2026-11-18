using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// CSS 2.1 §8.3.1 collapsing margins inside Flow, and CSS Fragmentation 3 §5.2 margins at page breaks. Expected
/// positions are computed by hand from the rule named in each test (page area 180 × 80 pt, one line = 12 pt).
/// </summary>
public class MarginCollapsingTests
{
    private static BoxStyle Margins(double top, double bottom) =>
        BoxStyle.Initial with { Margin = new Edges(top.Pt(), 0.Pt(), bottom.Pt(), 0.Pt()) };

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static ValueList<PlacedFragment> RootChildren(PageFragment page) => ((BoxFragment)page.Root.Fragment).Children;

    [Fact]
    public void Adjacent_siblings_are_separated_by_the_larger_margin_not_the_sum()
    {
        var page = Paginate(Document(Paragraph(1, "One", Margins(0, 10)), Paragraph(2, "Two", Margins(6, 0))))[0];

        RootChildren(page)[1].Offset.Y.Should().Be(LineBoxHeight + 10, "max(10, 6) = 10");
    }

    [Fact]
    public void A_parent_without_top_padding_shares_its_top_margin_with_its_first_child()
    {
        var page = Paginate(Document(
            Paragraph(1, "One"),
            Flow(2, [Paragraph(3, "Two", Margins(12, 0))], Margins(5, 0))))[0];

        var parent = RootChildren(page)[1];
        parent.Offset.Y.Should().Be(LineBoxHeight + 12, "the parent's border box moves down by max(5, 12)");
        ((BoxFragment)parent.Fragment).Children[0].Offset.Y.Should().Be(0, "the child's margin collapsed out of the parent");
    }

    [Fact]
    public void Margins_of_nested_first_children_all_collapse_into_one()
    {
        var page = Paginate(Document(
            Paragraph(1, "One"),
            Flow(2, [Flow(3, [Paragraph(4, "Two", Margins(4, 0))], Margins(9, 0))], Margins(2, 0))))[0];

        RootChildren(page)[1].Offset.Y.Should().Be(LineBoxHeight + 9, "max(2, 9, 4)");
    }

    [Fact]
    public void Top_padding_separates_the_parent_and_child_margins()
    {
        var parentStyle = Margins(5, 0) with { Padding = new Edges(1.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) };
        var page = Paginate(Document(Paragraph(1, "One"), Flow(2, [Paragraph(3, "Two", Margins(12, 0))], parentStyle)))[0];

        var parent = RootChildren(page)[1];
        parent.Offset.Y.Should().Be(LineBoxHeight + 5);
        ((BoxFragment)parent.Fragment).Children[0].Offset.Y.Should().Be(1 + 12, "padding, then the child's own margin");
    }

    [Fact]
    public void The_last_childs_bottom_margin_leaves_through_a_parent_without_bottom_padding()
    {
        var page = Paginate(Document(
            Flow(1, [Paragraph(2, "One", Margins(0, 9))], Margins(0, 4)),
            Paragraph(3, "Two")))[0];

        RootChildren(page)[0].Fragment.Size.Height.Should().Be(LineBoxHeight, "the child's bottom margin is not inside the parent");
        RootChildren(page)[1].Offset.Y.Should().Be(LineBoxHeight + 9, "max(4, 9) below the parent");
    }

    [Fact]
    public void Bottom_padding_keeps_the_last_childs_bottom_margin_inside_the_parent()
    {
        var parentStyle = Margins(0, 4) with { Padding = new Edges(0.Pt(), 0.Pt(), 2.Pt(), 0.Pt()) };
        var page = Paginate(Document(Flow(1, [Paragraph(2, "One", Margins(0, 9))], parentStyle), Paragraph(3, "Two")))[0];

        RootChildren(page)[0].Fragment.Size.Height.Should().Be(LineBoxHeight + 9 + 2);
        RootChildren(page)[1].Offset.Y.Should().Be(LineBoxHeight + 9 + 2 + 4);
    }

    [Fact]
    public void An_empty_box_lets_the_margins_on_both_sides_collapse_through_it()
    {
        var page = Paginate(Document(
            Paragraph(1, "One", Margins(0, 5)),
            Paragraph(2, "   ", Margins(20, 3)),
            Paragraph(3, "Two", Margins(7, 0))))[0];

        RootChildren(page)[1].Fragment.Size.Height.Should().Be(0, "a paragraph of only spaces makes no line box");
        RootChildren(page)[2].Offset.Y.Should().Be(LineBoxHeight + 20, "max(5, 20, 3, 7)");
    }

    [Fact]
    public void Margins_of_the_root_box_do_not_collapse_with_its_children()
    {
        var root = Flow(0, [Paragraph(1, "One", Margins(6, 0))], Margins(10, 0));
        var document = Document() with { Root = root };

        var page = Paginate(document)[0];

        page.Root.Offset.Y.Should().Be(10 + 10, "page margin, then the root's own top margin");
        RootChildren(page)[0].Offset.Y.Should().Be(6, "the first child's margin stays inside the root");
    }

    [Fact]
    public void Margins_adjoining_an_unforced_break_are_truncated()
    {
        // Six lines fill 72 of the 80 pt; the next paragraph does not fit and moves to page 2.
        var pages = Paginate(Document(
            Paragraph(1, Words(6), BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) }),
            Paragraph(2, "Two", Margins(6, 0))));

        pages.Should().HaveCount(2);
        RootChildren(pages[1])[0].Offset.Y.Should().Be(0, "its top margin adjoins the break and is truncated to zero");
    }

    [Fact]
    public void Margins_after_a_forced_break_are_preserved()
    {
        var pages = Paginate(Document(
            Paragraph(1, "One"),
            Paragraph(2, "Two", Margins(6, 0) with { BreakBefore = BreakBetween.Page })));

        pages.Should().HaveCount(2);
        RootChildren(pages[1])[0].Offset.Y.Should().Be(6);
    }
}
