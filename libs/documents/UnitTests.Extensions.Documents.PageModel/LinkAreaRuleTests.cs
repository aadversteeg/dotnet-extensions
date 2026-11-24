using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>
/// The page-model rules for links (<see cref="LinkArea"/>, <see cref="SemanticRole.LinkRole"/>), for every back-end: a link
/// area right inside its own link's structure marker and outside every artifact, at most
/// <see cref="PageModelRules.MaxLinkAreasPerPage"/> per page, its rectangle in range; a link role whose target THE RULE
/// FOR A LINK'S URI refuses, or whose description is blank, is refused in a page model from any producer.
/// </summary>
public class LinkAreaRuleTests
{
    private static readonly ErrorCode Code = DocumentErrorCodes._ / "Test" / "Invalid";

    private static readonly SourceLocation Here = new("LinkAreaRuleTests.cs", 1);

    private static readonly NodeId LinkNode = new(2);

    private static readonly Rect Area = new(new Point(10, 10), new Size(30, 12));

    private static StructureStart Link(string uri = "https://example.com", string description = "Example") =>
        new(LinkNode, SemanticRole.Link(LinkTarget.Uri(uri), description), Here);

    private static Result<PageDocument, Error> Checked(params DrawItem[] items) =>
        PageModelRules.Check(new PageDocument(new DocumentMetadata("Links", null), [new Page(new Size(100, 100), [.. items])]), Code);

    private static readonly StructureStart Paragraph = new(new NodeId(1), SemanticRole.Paragraph, Here);

    [Fact]
    public void A_link_area_inside_its_link_s_marker_passes()
    {
        Checked(Paragraph, Link(), new LinkArea(LinkNode, Area), new StructureEnd(LinkNode), new StructureEnd(new NodeId(1))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_link_area_outside_its_link_s_marker_or_inside_an_artifact_is_a_breach()
    {
        Checked(Paragraph, new LinkArea(LinkNode, Area), new StructureEnd(new NodeId(1))).Error.Message.Should().Contain("outside the structure marker of its link");
        Checked(Paragraph, Link(), new StructureStart(new NodeId(3), SemanticRole.Span, Here), new LinkArea(LinkNode, Area), new StructureEnd(new NodeId(3)), new StructureEnd(LinkNode), new StructureEnd(new NodeId(1)))
            .Error.Message.Should().Contain("outside the structure marker of its link", "its link must be the innermost open node");
        Checked(new ArtifactStart(ArtifactKind.PageHeader), new LinkArea(LinkNode, Area), ArtifactEnd.Instance).Error.Message.Should().Contain("inside an artifact");
    }

    [Fact]
    public void More_link_areas_on_a_page_than_the_limit_is_a_breach_and_the_limit_itself_passes()
    {
        DrawItem[] Page(int areas) => [Link(), .. Enumerable.Repeat<DrawItem>(new LinkArea(LinkNode, Area), areas), new StructureEnd(LinkNode)];

        Checked(Page(PageModelRules.MaxLinkAreasPerPage)).IsSuccess.Should().BeTrue();
        var refused = Checked(Page(PageModelRules.MaxLinkAreasPerPage + 1));
        refused.Error.Code.Should().Be(Code);
        refused.Error.Message.Should().Contain("more than 8191 link areas");
    }

    [Theory]
    [InlineData(double.NaN, 5.0)]
    [InlineData(0.0, 3.5e38)]
    [InlineData(0.0, -1.0)]
    public void A_link_area_whose_rectangle_is_not_in_range_is_a_breach(double x, double width)
    {
        Checked(Link(), new LinkArea(LinkNode, new Rect(new Point(x, 0), new Size(width, 5))), new StructureEnd(LinkNode))
            .Error.Message.Should().Contain("A link area on page 1");
    }

    [Theory]
    [InlineData("javascript:alert(1)", "Example", "'javascript'")]
    [InlineData(" https://example.com", "Example", "printable ASCII")]
    [InlineData("https://example.com", " ", "description")]
    public void A_link_role_whose_target_or_description_is_refused_is_a_breach(string uri, string description, string reason)
    {
        var refused = Checked(Link(uri, description), new LinkArea(LinkNode, Area), new StructureEnd(LinkNode));

        refused.Error.Code.Should().Be(Code);
        refused.Error.Message.Should().Contain(reason);
    }
}
