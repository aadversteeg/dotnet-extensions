using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>
/// The page-model rules for anchors (<see cref="Anchor"/>), for every back-end: one anchor per node, outside every
/// artifact, its point in range, at most <see cref="PageModelRules.MaxAnchors"/> in the document, and one for the node of
/// every link inside the document (<see cref="LinkTarget.Node"/>) on any page.
/// </summary>
public class AnchorRuleTests
{
    private static readonly ErrorCode Code = DocumentErrorCodes._ / "Test" / "Invalid";

    private static readonly SourceLocation Here = new("AnchorRuleTests.cs", 1);

    private static readonly NodeId LinkNode = new(2);

    private static readonly NodeId TargetNode = new(5);

    private static DrawItem[] LinkTo(NodeId target) =>
    [
        new StructureStart(LinkNode, SemanticRole.Link(LinkTarget.Node(target), "to the target"), Here),
        new LinkArea(LinkNode, new Rect(new Point(10, 10), new Size(30, 12))),
        new StructureEnd(LinkNode),
    ];

    private static DrawItem[] Target(Point at) =>
        [new StructureStart(TargetNode, SemanticRole.Paragraph, Here), new Anchor(TargetNode, at), new StructureEnd(TargetNode)];

    private static Result<PageDocument, Error> Checked(params DrawItem[][] pages) =>
        PageModelRules.Check(new PageDocument(new DocumentMetadata("Anchors", null), [.. pages.Select(items => new Page(new Size(100, 100), [.. items]))]), Code);

    [Fact]
    public void A_link_and_its_target_s_anchor_pass_on_the_same_page_and_on_another_page()
    {
        Checked([.. LinkTo(TargetNode), .. Target(new Point(10, 40))]).IsSuccess.Should().BeTrue();
        Checked(LinkTo(TargetNode), Target(new Point(10, 10))).IsSuccess.Should().BeTrue();
        Checked(Target(new Point(10, 10)), LinkTo(TargetNode)).IsSuccess.Should().BeTrue("a link may come after its target");
    }

    [Fact]
    public void A_link_to_a_node_without_an_anchor_is_a_breach_at_the_link_s_source()
    {
        var error = Checked(LinkTo(new NodeId(9)), Target(new Point(10, 10))).Error;

        error.Code.Should().Be(Code);
        error.Message.Should().Contain("leads to node #9, which has no anchor");
    }

    [Fact]
    public void A_second_anchor_of_a_node_and_an_anchor_inside_an_artifact_are_breaches()
    {
        Checked(Target(new Point(10, 10)), Target(new Point(10, 20))).Error.Message.Should().Contain("has a second anchor, on page 2");
        Checked([new ArtifactStart(ArtifactKind.PageHeader), new Anchor(TargetNode, new Point(0, 0)), ArtifactEnd.Instance]).Error.Message.Should().Contain("inside an artifact");
    }

    [Theory]
    [InlineData(double.NaN, 5.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    [InlineData(0.0, 4e38)]
    public void An_anchor_whose_point_or_height_above_the_foot_of_the_page_is_out_of_range_is_a_breach(double x, double y)
    {
        Checked([new Anchor(TargetNode, new Point(x, y))]).Error.Message.Should().Contain("anchor of node #5");
    }

    [Fact]
    public void More_anchors_in_a_document_than_the_limit_is_a_breach()
    {
        DrawItem[] Anchors(int from, int count) => [.. Enumerable.Range(from, count).Select(node => (DrawItem)new Anchor(new NodeId(node), new Point(0, 0)))];

        Checked(Anchors(0, PageModelRules.MaxAnchors)).IsSuccess.Should().BeTrue();
        Checked(Anchors(0, PageModelRules.MaxAnchors / 2), Anchors(PageModelRules.MaxAnchors / 2, PageModelRules.MaxAnchors / 2 + 1)).Error.Message.Should().Contain("more than 65536 anchors");
    }
}
