using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The code front-end's link (<see cref="Elements.Link"/>): a span that becomes an inline group with the link's role around
/// one run of its text, described by its text (else its URI), and refused at the author's line when THE RULE FOR A LINK'S
/// URI refuses its URI.
/// </summary>
public class LinkInputTests
{
    private static Document With(params Span[] spans) =>
        Document([Paragraph([.. spans])], title: "Links", language: "en").FontFamily("Lato");

    [Fact]
    public void A_link_becomes_a_group_with_the_link_s_role_around_a_run_of_its_text_in_the_span_s_style()
    {
        var model = DocumentTranslator.ToModel(With(Text("See "), Link("https://example.com/a", "  our   site ").Bold(), Text("."))).Value;

        var paragraph = (Box)((Box)model.Root).Children[0];
        var group = paragraph.Children[1].Should().BeOfType<InlineGroup>().Which;
        group.Role.Should().Be(SemanticRole.Link(LinkTarget.Uri("https://example.com/a"), "our site"), "the description is the text with its white space collapsed");
        group.Style.FontWeight.Should().Be(FontWeight.Bold);
        var run = group.Children.Should().ContainSingle().Which.Should().BeOfType<TextRun>().Which;
        run.Text.Should().Be("  our   site ");
        run.Style.Should().Be(group.Style);
        run.Id.Value.Should().Be(group.Id.Value + 1);
        ((TextRun)paragraph.Children[2]).Id.Value.Should().Be(group.Id.Value + 2, "ids stay in pre-order");
        DocumentModelText.Write(model).Should().Contain($"group {group.Id} link uri=\"https://example.com/a\" description=\"our site\" Lato");
    }

    [Theory]
    [InlineData("javascript:alert(1)", "'javascript'")]
    [InlineData("file:///etc/hosts", "'file'")]
    [InlineData("data:text/html,<b>x</b>", "'data'")]
    [InlineData(" https://example.com", "printable ASCII")]
    [InlineData("java\u0000script:x", "printable ASCII")]
    [InlineData("terms.html", "no scheme")]
    public void A_link_the_rule_refuses_is_reported_at_its_line_with_the_reason(string uri, string reason)
    {
        var line = new System.Diagnostics.StackFrame(0, true).GetFileLineNumber() + 1;
        var link = Link(uri, "click");

        var error = DocumentTranslator.ToModel(With(link)).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
        error.Message.Should().Contain(reason).And.Contain("http, https, mailto and tel");
        link.Source.Line.Should().Be(line);
        error.Message.Should().Contain(link.Source.ToString());
    }
}
