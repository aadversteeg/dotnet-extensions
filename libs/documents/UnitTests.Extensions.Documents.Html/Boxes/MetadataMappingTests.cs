using System.Collections.Immutable;
using System.IO;
using System.Xml.Linq;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>meta name=author</c> and <c>meta name=description</c> (THE RULE FOR A DOCUMENT'S METADATA IN
/// HTML): the first non-empty content in tree order, the name in any ASCII case, white space stripped; a content too
/// long or with a control character refused at its start tag; no other name read; and both through a tagged PDF into its
/// document information and its XMP metadata, which veraPDF passes.</summary>
public class MetadataMappingTests
{
    private const string Head = "<!doctype html><html lang=\"en\"><head><title>Hours</title><style>body { font-family: Lato; font-size: 10.5pt; margin: 0 }</style>";

    private const string Both = "<meta name=\"author\" content=\"Ann Example\"><meta name=\"description\" content=\"The hours of a week.\">";

    private static Result<HtmlBoxes, Error> Boxes(string head, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(Head + head + "</head><body><p>Hours.</p></body></html>", new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter, Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy }));

    private static DocumentMetadata Metadata(string head)
    {
        var result = Boxes(head);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Document.Metadata;
    }

    private static ImmutableArray<byte> TaggedPdf(string head) =>
        Boxes(head)
            .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts))
            .OnSuccessBind(pages => pages.ToPdf(new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a.AndPdfUa1() }))
            .Value;

    [Fact]
    public void The_author_and_the_description_come_from_their_meta_elements()
    {
        var metadata = Metadata(Both);

        metadata.Author.Should().Be("Ann Example");
        metadata.Description.Should().Be("The hours of a week.");
        metadata.Title.Should().Be("Hours");
    }

    [Fact]
    public void The_first_meta_in_tree_order_with_a_content_that_is_not_empty_wins_its_name_in_any_ASCII_case_and_stripped()
    {
        var metadata = Metadata(
            "<meta name=\"AUTHOR\" content=\" \t\n \"><meta name=\"Author\" content=\"\n  Ann Example \">"
            + "<meta name=\"author\" content=\"Bert Example\"><meta name=\"Description\" content=\" Line one\nline two \">");

        metadata.Author.Should().Be("Ann Example");
        metadata.Description.Should().Be("Line one\nline two", "white space inside the content stays as written");
    }

    [Fact]
    public void A_meta_in_the_body_counts_in_tree_order()
    {
        var result = HtmlParser.Parse(Head + "</head><body><meta name=\"author\" content=\"Ann Example\"><p>Hours.</p></body></html>", new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

        result.Value.Document.Metadata.Author.Should().Be("Ann Example");
    }

    [Theory]
    [InlineData("<meta name=\"keywords\" content=\"hours, week\">")]
    [InlineData("<meta name=\"generator\" content=\"Example Writer\">")]
    [InlineData("<meta name=\"date\" content=\"2026-11-24\">")]
    [InlineData("<meta name=\"application-name\" content=\"Hours\">")]
    [InlineData("<meta property=\"author\" content=\"Ann Example\">")]
    [InlineData("<meta http-equiv=\"author\" content=\"Ann Example\">")]
    [InlineData("<meta name=\" author\" content=\"Ann Example\">")]
    [InlineData("<meta name=\"author\">")]
    public void No_other_meta_is_read(string head)
    {
        var metadata = Metadata(head);

        metadata.Author.Should().BeNull();
        metadata.Description.Should().BeNull();
    }

    [Theory]
    [InlineData("author", "Ann\u0001Example", "U+0001")]
    [InlineData("author", "Ann\u007FExample", "U+007F")]
    [InlineData("description", "A week\u0085of hours.", "U+0085")]
    [InlineData("description", "A week\u009Fof hours.", "U+009F")]
    [InlineData("description", "A week￾of hours.", "U+FFFE")]
    public void A_content_with_a_control_character_or_one_XML_does_not_allow_is_refused_at_the_meta_and_left_out(string name, string content, string character)
    {
        var head = $"<meta name=\"{name}\" content=\"{content}\">";

        var error = Boxes(head).Error.Parts().Single();

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Metadata);
        error.Message.Should().Contain(character);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("content");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(name);
        var warned = Boxes(head, UnsupportedPolicy.Warn).Value.Document.Metadata;
        (warned.Author, warned.Description).Should().Be((null, null), "under Warn the document has no author or description");
    }

    [Fact]
    public void A_tab_and_a_line_break_inside_a_content_are_kept()
    {
        Metadata("<meta name=\"description\" content=\"One\ttwo\r\nthree\">").Description.Should().Be("One\ttwo\nthree", "the parser makes a CR LF one line feed");
    }

    [Fact]
    public void A_content_up_to_the_limit_is_taken_and_a_longer_one_refused()
    {
        var longest = new string('a', HtmlMetadata.MaxLength);

        Metadata($"<meta name=\"description\" content=\"  {longest}  \">").Description.Should().Be(longest, "the limit counts the stripped content");
        var error = Boxes($"<meta name=\"description\" content=\"{longest}a\">").Error.Parts().Single();
        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Metadata);
        error.Message.Should().Contain("4097 characters long; at most 4096");
    }

    [Fact]
    public void A_tagged_PDF_has_the_author_and_the_description_in_its_document_information_and_its_XMP_metadata()
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(TaggedPdf(Both).ToArray());

        document.Information.Author.Should().Be("Ann Example");
        document.Information.Subject.Should().Be("The hours of a week.");
        document.TryGetXmpMetadata(out var xmp).Should().BeTrue();
        var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        var rdf = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        var xml = xmp!.GetXDocument();
        xml.Descendants(dc + "creator").Single().Descendants(rdf + "li").Select(li => li.Value).Should().Equal("Ann Example");
        var description = xml.Descendants(dc + "description").Single().Descendants(rdf + "li").Should().ContainSingle().Which;
        description.Value.Should().Be("The hours of a week.");
        description.Attribute(XNamespace.Xml + "lang")!.Value.Should().Be("x-default");
    }

    /// <summary>A document with an author and a description, through the HTML path into a tagged PDF: veraPDF passes
    /// PDF/A-3a and PDF/UA-1.</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_document_with_an_author_and_a_description_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-metadata-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "metadata.pdf"), [.. TaggedPdf(Both)]);

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().ContainSingle().Which.IsCompliant.Should().BeTrue(VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
