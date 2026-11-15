using Ave.Documents.Core;
using Ave.Documents.Layout;
using Ave.Documents.DocumentModel;
using Ave.Documents.TestSupport;
using static UnitTests.Ave.Documents.Layout.Models;

namespace UnitTests.Ave.Documents.Layout;

public class PaginationTests
{
    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static BoxFragment Root(PageFragment page) => (BoxFragment)page.Root.Fragment;

    private static IEnumerable<int> LineCounts(PageFragment page) =>
        Root(page).Children.Select(child => ((BoxFragment)child.Fragment).Children.Count(line => line.Fragment is LineFragment));

    [Fact]
    public void Stacks_blocks_with_their_margins_in_the_page_area()
    {
        var pages = Paginate(Document(
            Paragraph(1, "One", BoxStyle.Initial with { Margin = new Edges(2.Pt(), 0.Pt(), 3.Pt(), 5.Pt()) }),
            Paragraph(2, "Two")));

        pages.Should().HaveCount(1);
        pages[0].Root.Offset.Should().Be(new Point(10, 10), "the page area starts inside the page margins");
        var children = Root(pages[0]).Children;
        children[0].Offset.Should().Be(new Point(5, 2), "left and top margin");
        children[0].Fragment.Size.Should().Be(new Size(175, LineHeight), "the width is the page area minus the left margin");
        children[1].Offset.Should().Be(new Point(0, 2 + LineHeight + 3), "below the first block's bottom margin; margins do not collapse");
    }

    [Fact]
    public void Padding_moves_the_content_and_is_part_of_the_box()
    {
        var pages = Paginate(Document(Paragraph(1, "One", BoxStyle.Initial with { Padding = new Edges(4.Pt(), 0.Pt(), 6.Pt(), 8.Pt()) })));

        var paragraph = (BoxFragment)Root(pages[0]).Children[0].Fragment;
        paragraph.Size.Height.Should().Be(4 + LineHeight + 6);
        paragraph.Children[0].Offset.Should().Be(new Point(8, 4));
    }

    [Fact]
    public void A_paragraph_too_long_for_the_page_continues_on_the_next_page_between_lines()
    {
        // 20 pt wide boxes force one word per line: 14 lines at 12 pt, 6 per page → 6 + 6 + 2.
        var narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) };
        var pages = Paginate(Document(Paragraph(1, Words(14), narrow)));

        pages.Select(page => LineCounts(page).Single()).Should().Equal(6, 6, 2);
        pages.Select(page => Root(page).Children[0].Fragment.Size.Height).Should().Equal(72, 72, 24);
    }

    [Fact]
    public void A_block_whose_padding_does_not_fit_moves_whole_to_the_next_page()
    {
        // Five lines use 60 pt of the 80 pt page area; the next block needs 25 pt of padding before any content.
        var narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) };
        var pages = Paginate(Document(
            Paragraph(1, Words(5), narrow),
            Paragraph(2, "Next", BoxStyle.Initial with { Padding = new Edges(25.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) })));

        pages.Should().HaveCount(2);
        Root(pages[0]).Children.Should().HaveCount(1);
        ((BoxFragment)Root(pages[1]).Children[0].Fragment).Box.Id.Should().Be(new NodeId(2));
        Root(pages[1]).Children[0].Offset.Y.Should().Be(0);
    }

    [Fact]
    public void A_continued_box_has_no_top_padding_on_the_next_page_and_bottom_padding_only_at_its_end()
    {
        var style = new BoxStyle(new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()), new Edges(4.Pt(), 0.Pt(), 4.Pt(), 0.Pt()), null, BreakBefore.Auto);
        var pages = Paginate(Document(Paragraph(1, Words(8), style)));

        // Page 1: 4 pt top padding, then 6 lines (72 pt); the bottom padding stays reserved, so 4 + 72 + 4 = 80.
        // Page 2: no top padding, 2 lines, then the bottom padding.
        pages.Select(page => Root(page).Children[0].Fragment.Size.Height).Should().Equal(4 + 72, 24 + 4);
        ((BoxFragment)Root(pages[1]).Children[0].Fragment).Children[0].Offset.Y.Should().Be(0);
    }

    [Fact]
    public void A_forced_break_starts_the_box_on_a_new_page_even_when_it_would_fit()
    {
        var pages = Paginate(Document(
            Paragraph(1, "One"),
            Paragraph(2, "Two", BoxStyle.Initial with { BreakBefore = BreakBefore.Page })));

        pages.Should().HaveCount(2);
    }

    [Fact]
    public void A_forced_break_before_the_first_box_of_the_document_makes_no_empty_page()
    {
        var pages = Paginate(Document(Paragraph(1, "One", BoxStyle.Initial with { BreakBefore = BreakBefore.Page })));

        pages.Should().HaveCount(1);
    }

    [Fact]
    public void A_forced_break_on_a_first_child_propagates_to_its_parent()
    {
        var nested = Flow(2, [Paragraph(3, "Inner", BoxStyle.Initial with { BreakBefore = BreakBefore.Page })]);
        var pages = Paginate(Document(Paragraph(1, "One"), nested));

        pages.Should().HaveCount(2);
        ((BoxFragment)Root(pages[1]).Children[0].Fragment).Box.Id.Should().Be(new NodeId(2), "the break falls before the parent");
    }

    [Fact]
    public void Painting_puts_the_background_under_the_box_and_the_baseline_one_ascent_below_its_top()
    {
        var model = Document(Paragraph(1, "Hi", BoxStyle.Initial with { Background = Color.White }));

        var page = DocumentLayout.Run(model, TestFonts.Set).Value.Pages.Single();

        var fill = page.Items.OfType<global::Ave.Documents.PageModel.FillRectangle>().Single();
        fill.Area.Should().Be(new Rect(new Point(10, 10), new Size(180, LineHeight)));
        var run = page.Items.OfType<global::Ave.Documents.PageModel.GlyphRun>().Single();
        run.Origin.X.Should().Be(10);
        run.Origin.Y.Should().BeApproximately(10 + Ascent, 1e-9);
        run.Glyphs.Select(glyph => glyph.X).Should().Equal(0, 1527 * 10 / 2000.0);
    }

    [Fact]
    public void A_box_split_over_pages_gets_a_structure_start_and_end_on_each_page()
    {
        var narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) };
        var document = DocumentLayout.Run(Document(Paragraph(1, Words(8), narrow)), TestFonts.Set).Value;

        document.Pages.Should().HaveCount(2).And.AllSatisfy(page =>
        {
            page.Items.OfType<global::Ave.Documents.PageModel.StructureStart>().Select(start => start.Node.Value).Should().Equal(0, 1);
            page.Items.OfType<global::Ave.Documents.PageModel.StructureEnd>().Select(end => end.Node.Value).Should().Equal(1, 0);
        });
    }

    [Fact]
    public void Fragment_tree_of_a_two_page_document_matches_the_golden_file()
    {
        var narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 140.Pt(), 0.Pt(), 0.Pt()), Background = Color.White };
        var pages = Paginate(Document(
            Paragraph(1, "A short first paragraph."),
            Paragraph(2, Words(7), narrow),
            Paragraph(3, "Last", BoxStyle.Initial with { Padding = Edges.All(2.Pt()) })));

        Golden.Compare("two-pages.fragments.txt", FragmentText.Write(pages)).Should().BeNull();
    }
}
