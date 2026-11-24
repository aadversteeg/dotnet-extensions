using System.IO;
using System.Xml.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.TestSupport;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The author and the description of a document made in code through the PDF back-end: the document information's
/// <c>/Author</c> and <c>/Subject</c>, and XMP <c>dc:creator</c> and <c>dc:description</c> with the same text; a description
/// with a character XML does not allow refused; veraPDF passes PDF/A-3a and PDF/UA-1 on a document with both.
/// </summary>
public class MetadataOutputTests
{
    private static Document Described() =>
        Document(
            [Paragraph("The hours of a week.")],
            Page(300.Pt(), 200.Pt()).Margin(20.Pt()),
            title: "Hours",
            author: "Ann Example",
            language: "en",
            description: "The hours of a week, per day.").FontFamily("Lato");

    [Fact]
    public void The_author_and_the_description_are_in_the_document_information_and_in_the_XMP_metadata()
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(SampleFiles.Pdf(Described()).ToArray());

        document.Information.Author.Should().Be("Ann Example");
        document.Information.Subject.Should().Be("The hours of a week, per day.");
        document.TryGetXmpMetadata(out var xmp).Should().BeTrue();
        var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        var rdf = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        var xml = xmp!.GetXDocument();
        xml.Descendants(dc + "creator").Single().Descendants(rdf + "li").Select(li => li.Value).Should().Equal("Ann Example");
        var description = xml.Descendants(dc + "description").Single().Descendants(rdf + "li").Should().ContainSingle().Which;
        description.Value.Should().Be("The hours of a week, per day.");
        description.Attribute(XNamespace.Xml + "lang")!.Value.Should().Be("x-default");
    }

    [Fact]
    public void A_document_without_a_description_has_no_subject_and_no_XMP_description()
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(SampleFiles.Pdf(Described() with { Description = null }).ToArray());

        document.Information.Subject.Should().BeNull();
        document.TryGetXmpMetadata(out var xmp).Should().BeTrue();
        xmp!.GetXDocument().Descendants(XNamespace.Get("http://purl.org/dc/elements/1.1/") + "description").Should().BeEmpty();
    }

    [Fact]
    public void A_description_with_a_character_XML_does_not_allow_is_refused()
    {
        var error = DocumentTranslator.ToModel(Described() with { Description = "Hours\u0001" }).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.MetadataText);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("description");
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_document_with_an_author_and_a_description_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-metadata-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "metadata.pdf"), [.. SampleFiles.Pdf(Described())]);

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().ContainSingle().Which.IsCompliant.Should().BeTrue(VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
