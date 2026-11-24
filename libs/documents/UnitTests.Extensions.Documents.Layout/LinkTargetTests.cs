using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Links inside the document (<see cref="LinkTarget.Node"/>) through the model check and painting: THE RULE FOR A LINK
/// INSIDE THE DOCUMENT (a box or an inline group of the body, outside every artifact) and THE RULE FOR A LINK TARGET'S
/// PLACE (one <see cref="Anchor"/> per target, at its first fragment). Pages of 200 × 100 pt with 10 pt margins
/// (<see cref="Models"/>).
/// </summary>
public class LinkTargetTests
{
    private static InlineGroup LinkTo(int id, int target) =>
        new(new NodeId(id), SemanticRole.Link(LinkTarget.Node(new NodeId(target)), "to the target"), Text(), [Run(id + 1, "link")], At(0));

    private static TextRun Run(int id, string text) => new(new NodeId(id), text, Text(), At(0));

    private static Box Line(int id, ValueList<Node> children) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, children, At(0));

    private static PageDocument Laid(StyledDocument document)
    {
        var laid = DocumentLayout.Run(document, TestFonts.Set);
        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        return laid.Value;
    }

    private static List<(int Page, Anchor Anchor)> Anchors(PageDocument pages) =>
        [.. pages.Pages.SelectMany((page, index) => page.Items.OfType<Anchor>().Select(anchor => (index + 1, anchor)))];

    [Fact]
    public void A_box_target_gets_one_anchor_at_the_top_left_of_its_border_box_inside_its_structure_marker()
    {
        var pages = Laid(Document(Line(1, [LinkTo(2, 10)]), Paragraph(10, "Target")));

        var (page, anchor) = Anchors(pages).Should().ContainSingle().Which;
        page.Should().Be(1);
        anchor.Node.Should().Be(new NodeId(10));
        var items = pages.Pages[0].Items.ToList();
        items[items.IndexOf(anchor) - 1].Should().BeOfType<StructureStart>().Which.Node.Should().Be(new NodeId(10));
        var run = pages.Pages[0].Items.OfType<GlyphRun>().Single(glyphs => glyphs.Glyphs[0].Text == "T");
        anchor.At.X.Should().Be(10);
        anchor.At.Y.Should().BeApproximately(run.Origin.Y - Ascent, 1e-9, "the paragraph's box starts at its first line's top");
    }

    [Fact]
    public void A_target_that_starts_on_a_later_page_gets_its_anchor_on_that_page_once_though_it_runs_on()
    {
        var filler = Paragraph(10, Words(120));
        var pages = Laid(Document(Line(1, [LinkTo(2, 20)]), filler, Paragraph(20, Words(60))));

        var (page, anchor) = Anchors(pages).Should().ContainSingle().Which;
        var first = pages.Pages.Select((each, index) => (each, index)).First(each => each.each.Items.OfType<StructureStart>().Any(start => start.Node == new NodeId(20)));
        page.Should().Be(first.index + 1);
        page.Should().BeGreaterThan(1);
        pages.Pages.Count(each => each.Items.OfType<StructureStart>().Any(start => start.Node == new NodeId(20))).Should().BeGreaterThan(1, "the target runs over a page break, and its anchor stays at its start");
        anchor.At.Y.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void An_inline_group_target_gets_its_anchor_at_the_top_of_its_first_line_part()
    {
        var span = new InlineGroup(new NodeId(12), SemanticRole.Span, Text(), [Run(13, "marked")], At(0));
        var pages = Laid(Document(Line(1, [LinkTo(2, 12)]), Line(10, [Run(11, "Some "), span, Run(14, " words")])));

        var anchor = Anchors(pages).Should().ContainSingle().Which.Anchor;
        var marked = pages.Pages[0].Items.OfType<GlyphRun>().Single(run => string.Concat(run.Glyphs.Select(glyph => glyph.Text)) == "marked");
        anchor.At.Should().Be(new Point(marked.Origin.X, marked.Origin.Y - Ascent));
    }

    [Fact]
    public void A_link_to_the_root_leads_to_the_top_of_the_first_page_s_page_area()
    {
        var anchor = Anchors(Laid(Document(Paragraph(10, "First"), Line(1, [LinkTo(2, 0)])))).Should().ContainSingle().Which;

        anchor.Page.Should().Be(1);
        anchor.Anchor.At.Should().Be(new Point(10, 10));
    }

    [Fact]
    public void Two_links_to_one_node_make_one_anchor_and_a_document_without_links_inside_it_makes_none()
    {
        Anchors(Laid(Document(Line(1, [LinkTo(2, 10), Run(4, " "), LinkTo(5, 10)]), Paragraph(10, "Target")))).Should().ContainSingle();
        Anchors(Laid(Document(Paragraph(10, "Target")))).Should().BeEmpty();
    }

    [Theory]
    [InlineData(99, "node #99")]
    [InlineData(11, "node #11")]
    public void A_link_to_a_node_that_is_no_box_or_group_of_the_body_is_refused(int target, string named)
    {
        var error = DocumentLayout.Run(Document(Line(1, [LinkTo(2, target)]), Line(10, [Run(11, "a run")])), TestFonts.Set).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
        error.Message.Should().Contain(named).And.Contain("no box or inline group of the body");
    }

    [Fact]
    public void A_link_to_a_node_inside_a_decorative_box_or_a_page_area_is_refused()
    {
        var decorative = new Box(new NodeId(10), SemanticRole.Decorative, LayoutAlgorithmId.Flow, BoxStyle.Initial, [Paragraph(11, "hidden")], At(0));
        var header = new PageArea(Flow(30, [Paragraph(31, "Header")]), 20.Pt());
        var plain = Document(Line(1, [LinkTo(2, 31)]));
        var withHeader = plain with { Page = plain.Page with { Template = new PageTemplate(header, null) } };

        DocumentLayout.Run(Document(Line(1, [LinkTo(2, 11)]), decorative), TestFonts.Set).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
        DocumentLayout.Run(withHeader, TestFonts.Set).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
    }

    [Fact]
    public void Resolving_the_targets_sets_them_and_a_link_without_one_gives_its_place_to_its_content()
    {
        var root = Flow(0, [Line(1, [Run(5, "See "), LinkTo(2, -1), Run(6, " and "), LinkTo(7, -1)])]);

        var resolved = LinkTargets.Resolve(root, new Dictionary<NodeId, LinkTarget?> { [new NodeId(2)] = LinkTarget.Node(new NodeId(1)), [new NodeId(7)] = null });

        var line = (Box)resolved.Children.Single();
        line.Children.Select(child => child.Id.Value).Should().Equal(5, 2, 6, 8);
        ((SemanticRole.LinkRole)((InlineGroup)line.Children[1]).Role).Target.Should().Be(LinkTarget.Node(new NodeId(1)));
        LinkTargets.Resolve(root, new Dictionary<NodeId, LinkTarget?>()).Should().BeSameAs(root);
    }
}
