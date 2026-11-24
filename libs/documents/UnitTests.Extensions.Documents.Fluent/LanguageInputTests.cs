using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>The code front-end's languages (<see cref="ParagraphElement.Language"/>, <see cref="Span.Language"/>): on the
/// paragraph's box, a span group with the language around a span's run, a link group's own; refused when not a
/// well-formed tag, and on an image span.</summary>
public class LanguageInputTests
{
    private static Document With(params Element[] body) => Document([.. body], title: "Talen", language: "nl").FontFamily("Lato");

    [Fact]
    public void A_paragraph_s_language_is_on_its_box_and_a_span_s_on_a_span_group_around_its_run()
    {
        var model = DocumentTranslator.ToModel(With(Paragraph([Text("Dit is "), Text("English").Language("en"), Text(".")]).Language("nl-BE"))).Value;

        var paragraph = (Box)((Box)model.Root).Children[0];
        paragraph.Language.Should().Be("nl-BE");
        var group = paragraph.Children[1].Should().BeOfType<InlineGroup>().Which;
        group.Role.Should().Be(SemanticRole.Span);
        group.Language.Should().Be("en");
        group.Children.Should().ContainSingle().Which.Should().BeOfType<TextRun>().Which.Text.Should().Be("English");
        ((TextRun)paragraph.Children[2]).Id.Value.Should().Be(group.Id.Value + 2, "ids stay in pre-order");
    }

    [Fact]
    public void A_link_s_language_is_on_its_link_group()
    {
        var model = DocumentTranslator.ToModel(With(Paragraph([Link("https://example.com", "the site").Language("en")]))).Value;

        var group = (InlineGroup)((Box)((Box)model.Root).Children[0]).Children[0];
        group.Role.Should().BeOfType<SemanticRole.LinkRole>();
        group.Language.Should().Be("en");
    }

    [Theory]
    [InlineData("en_GB")]
    [InlineData("")]
    public void A_language_that_is_not_a_well_formed_tag_is_refused(string tag)
    {
        DocumentTranslator.ToModel(With(Paragraph("x").Language(tag))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Language);
        DocumentTranslator.ToModel(With(Paragraph([Text("x").Language(tag)]))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Language);
    }
}
