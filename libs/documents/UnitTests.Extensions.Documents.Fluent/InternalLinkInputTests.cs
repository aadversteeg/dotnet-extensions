using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The code front-end's links inside the document (<see cref="Elements.LinkTo"/>, <see cref="ParagraphElement.Anchor"/>):
/// a link group whose target is the node of the paragraph with that anchor, before or after the link, and the refusals
/// at the author's line: a name no paragraph has, a name given twice, a name of the wrong length, an anchor or a link to
/// one in a page area.
/// </summary>
public class InternalLinkInputTests
{
    private static Document With(params Element[] body) => Document([.. body], title: "Links", language: "en").FontFamily("Lato");

    private static IEnumerable<Node> All(Node node) =>
        [node, .. (node switch { Box box => box.Children, InlineGroup group => group.Children, _ => [] }).SelectMany(All)];

    private static (InlineGroup Link, Box Target) Linked(StyledDocument model, string text)
    {
        var link = All(model.Root).OfType<InlineGroup>().Single();
        var target = ((LinkTarget.NodeTarget)((SemanticRole.LinkRole)link.Role!).Target).Value;
        var box = All(model.Root).OfType<Box>().Single(box => box.Id == target);
        box.Children.OfType<TextRun>().First().Text.Should().Be(text);
        return (link, box);
    }

    [Fact]
    public void A_link_to_an_anchor_leads_to_the_node_of_the_heading_after_it()
    {
        var model = DocumentTranslator.ToModel(With(Paragraph([Text("See "), LinkTo("results", "the results")]), Heading(2, "Results").Anchor("results"))).Value;

        var (link, target) = Linked(model, "Results");
        target.Role.Should().Be(SemanticRole.Heading(2));
        ((SemanticRole.LinkRole)link.Role!).Description.Should().Be("the results");
        DocumentModelText.Write(model).Should().Contain($"group {link.Id} link node={target.Id} description=\"the results\"");
    }

    [Fact]
    public void A_link_may_follow_its_anchor_and_a_blank_text_is_described_by_the_name()
    {
        var model = DocumentTranslator.ToModel(With(Paragraph("Intro").Anchor("intro"), Paragraph([LinkTo("intro", " ")]))).Value;

        var (link, _) = Linked(model, "Intro");
        ((SemanticRole.LinkRole)link.Role!).Description.Should().Be("intro");
    }

    [Fact]
    public void A_link_to_a_name_no_paragraph_has_is_refused_at_the_link_s_line()
    {
        var line = new System.Diagnostics.StackFrame(0, true).GetFileLineNumber() + 1;
        var link = LinkTo("nowhere", "click");

        var error = DocumentTranslator.ToModel(With(Paragraph([link]), Paragraph("Other").Anchor("other"))).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
        error.Message.Should().Contain("'nowhere'").And.Contain("no paragraph or heading of the body has");
        link.Source.Line.Should().Be(line);
        error.Message.Should().Contain(link.Source.ToString());
    }

    [Fact]
    public void A_name_given_twice_is_refused_at_the_second_anchor()
    {
        var error = DocumentTranslator.ToModel(With(Paragraph("One").Anchor("same"), Paragraph("Two").Anchor("same"))).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
        error.Message.Should().Contain("'same' is given twice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Elements.MaxAnchorLength + 1)]
    public void A_name_of_no_characters_or_beyond_the_limit_is_refused(int length)
    {
        var name = new string('a', length);

        DocumentTranslator.ToModel(With(Paragraph("Target").Anchor(name))).Error.Message.Should().Contain("1 to 1024 characters");
        DocumentTranslator.ToModel(With(Paragraph([LinkTo(name, "x")]))).Error.Message.Should().Contain("1 to 1024 characters");
    }

    [Fact]
    public void A_name_at_the_limit_is_accepted()
    {
        var name = new string('a', Elements.MaxAnchorLength);

        DocumentTranslator.ToModel(With(Paragraph([LinkTo(name, "x")]), Paragraph("Target").Anchor(name))).IsSuccess.Should().BeTrue();
    }
}
