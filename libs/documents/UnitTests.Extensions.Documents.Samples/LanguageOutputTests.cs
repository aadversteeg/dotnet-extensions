using System.IO;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.TestSupport;
using UglyToad.PdfPig.Tokens;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The language of a paragraph and of a span through the PDF back-end: the document's language in the catalog, and
/// <c>/Lang</c> on the structure element of a paragraph or a span whose language differs from its parent's, none where it
/// is the same; veraPDF passes PDF/A-3a and PDF/UA-1 on a Dutch document with an English span and a French paragraph.
/// </summary>
public class LanguageOutputTests
{
    /// <summary>A Dutch document: a paragraph with an English span, a French paragraph, and a paragraph set to Dutch again.</summary>
    private static Document ThreeLanguages() =>
        Document(
            [
                Paragraph([Text("Dit is Nederlands, met "), Text("an English phrase").Language("en"), Text(" erin.")]),
                Paragraph("Ceci est un paragraphe en français.").Language("fr"),
                Paragraph("Weer Nederlands, zoals het document.").Language("NL-nl"),
            ],
            Page(300.Pt(), 200.Pt()).Margin(20.Pt()),
            title: "Talen",
            language: "nl-NL").FontFamily("Lato");

    private static string? Language(LinkOutputTests.ReadBack read, DictionaryToken element) =>
        element.ContainsKey(NameToken.Create("Lang")) ? LinkOutputTests.StringOf(read, element, "Lang") : null;

    [Fact]
    public void A_span_and_a_paragraph_in_another_language_carry_it_on_their_structure_elements_and_the_catalog_the_document_s()
    {
        using var read = new LinkOutputTests.ReadBack(SampleFiles.Pdf(ThreeLanguages()));

        var elements = read.Elements().Select(each => each.Element).ToList();
        var paragraphs = elements.Where(element => element.Data["S"].Equals(NameToken.Create("P"))).ToList();
        var span = elements.Should().ContainSingle(element => element.Data["S"].Equals(NameToken.Create("Span"))).Which;

        Language(read, span).Should().Be("en");
        paragraphs.Select(paragraph => Language(read, paragraph)).Should().Equal(null, "fr", null);
        elements.Where(element => !element.Data["S"].Equals(NameToken.Create("Span")) && element != paragraphs[1]).Should().OnlyContain(element => Language(read, element) == null,
            "only an element whose language differs from its parent's writes one; nl-NL in any case is the document's");
        LinkOutputTests.StringOf(read, read.Catalog, "Lang").Should().Be("nl-NL");
    }

    [Theory]
    [InlineData("en_GB")]
    [InlineData("en-toolongsubtag")]
    [InlineData("")]
    public void A_language_that_is_not_a_well_formed_tag_is_refused_at_its_line(string tag)
    {
        var error = DocumentTranslator.ToModel(Document([Paragraph([Text("x").Language(tag)])], language: "nl").FontFamily("Lato")).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Language);
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_document_in_three_languages_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-languages-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "three-languages.pdf"), [.. SampleFiles.Pdf(ThreeLanguages())]);

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().ContainSingle().Which.IsCompliant.Should().BeTrue(VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
