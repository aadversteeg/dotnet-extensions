using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>lang</c> (THE RULE FOR AN ELEMENT'S LANGUAGE IN HTML): the language of a block on its box
/// and of an inline element on a span group (or its link group), where it differs from its parent's; a tag that is not
/// well-formed, and the empty tag, refused at the start tag; and a Dutch document with an English span and a French
/// paragraph through a tagged PDF that veraPDF passes.</summary>
public class LanguageMappingTests
{
    private const string Dutch = "<!doctype html><html lang=\"nl-NL\"><head><title>Talen</title><style>body { font-family: Lato; margin: 0 }</style></head><body>";

    private static Result<HtmlBoxes, Error> Boxes(string body, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(Dutch + body + "</body></html>", new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy }));

    private static List<Node> All(Result<HtmlBoxes, Error> result)
    {
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var nodes = new List<Node>();
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            nodes.Add(node);
            foreach (var child in node switch { Box box => box.Children, InlineGroup group => group.Children, _ => [] })
            {
                pending.Push(child);
            }
        }

        return nodes;
    }

    private static string? LanguageOf(Node node) => node switch { Box box => box.Language, InlineGroup group => group.Language, _ => null };

    [Fact]
    public void A_block_in_another_language_carries_it_on_its_box_and_the_document_s_stays_in_the_metadata()
    {
        var result = Boxes("<p>Nederlands.</p><p lang=\"fr\">Français.</p><p lang=\"NL-nl\">Weer Nederlands.</p>");

        All(result).Where(node => LanguageOf(node) is not null).Should().ContainSingle()
            .Which.Should().BeOfType<Box>().Which.Role.Should().Be(SemanticRole.Paragraph);
        All(result).Select(LanguageOf).OfType<string>().Should().Equal("fr");
        result.Value.Document.Metadata.Language.Should().Be("nl-NL");
    }

    [Fact]
    public void An_inline_element_in_another_language_becomes_a_span_group_with_it_and_its_inline_children_add_none()
    {
        var groups = All(Boxes("<p>Dit is <span lang=\"en\">an <b>English</b> phrase</span> en <b>niet</b> meer.</p>")).OfType<InlineGroup>().ToList();

        var span = groups.Should().ContainSingle().Which;
        span.Role.Should().Be(SemanticRole.Span);
        span.Language.Should().Be("en");
    }

    [Fact]
    public void A_link_in_another_language_carries_it_on_its_link_group()
    {
        var group = All(Boxes("<p>Zie <a lang=\"en\" href=\"https://example.com/\">the site</a>.</p>")).OfType<InlineGroup>().Should().ContainSingle().Which;

        group.Role.Should().BeOfType<SemanticRole.LinkRole>();
        group.Language.Should().Be("en");
    }

    [Theory]
    [InlineData("en_GB", "not a well-formed language tag")]
    [InlineData("en-toolongsubtag", "not a well-formed language tag")]
    [InlineData("", "the language is unknown")]
    public void A_language_that_is_not_a_well_formed_tag_or_empty_is_refused_at_the_start_tag_and_not_guessed(string tag, string reason)
    {
        var error = Boxes($"<p>Tekst <span lang=\"{tag}\">text</span>.</p>").Error.Parts().Single();

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Language);
        error.Message.Should().Contain(reason);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("lang");
        All(Boxes($"<p>Tekst <span lang=\"{tag}\">text</span>.</p>", UnsupportedPolicy.Warn)).Select(LanguageOf).OfType<string>().Should().BeEmpty("under Warn the element keeps its parent's language");
    }

    [Fact]
    public void The_painter_carries_the_languages_into_the_structure_markers()
    {
        var result = Boxes("<p>Dit is <span lang=\"en\">English</span>.</p><p lang=\"fr\">Français.</p>");
        var pages = DocumentLayout.Run(result.Value.Document, result.Value.Fonts).Value;

        pages.Pages.SelectMany(page => page.Items).OfType<StructureStart>().Select(start => start.Language).OfType<string>().Should().Equal("en", "fr");
    }

    /// <summary>A Dutch document with an English span and a French paragraph, through the HTML path into a tagged PDF:
    /// veraPDF passes PDF/A-3a and PDF/UA-1 (among its rules 7.2-29 on the form of every <c>/Lang</c>).</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_Dutch_document_with_an_English_span_and_a_French_paragraph_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-languages-").FullName;
        try
        {
            var path = Path.Combine(HtmlCases.Folder(), "languages.html");
            var html = Dutch.Replace("font-family: Lato;", "font-family: Lato; font-size: 10.5pt;", StringComparison.Ordinal)
                + "<p>Dit is Nederlands, met <span lang=\"en\">an English phrase</span> erin.</p><p lang=\"fr\">Ceci est un paragraphe en français.</p></body></html>";
            var baseUrl = new System.Uri(path).AbsoluteUri;
            var pdf = HtmlParser.Parse(html, new HtmlParseOptions("languages.html"))
                .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, AllowedResourceSchemes = HtmlCases.FileSchemes, BaseUrl = baseUrl }))
                .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Resources = HtmlCases.Resource, AllowedResourceSchemes = HtmlCases.FileSchemes, BaseUrl = baseUrl }))
                .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts))
                .OnSuccessBind(pages => pages.ToPdf(new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a.AndPdfUa1() }));
            pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
            File.WriteAllBytes(Path.Combine(folder, "languages.pdf"), [.. pdf.Value]);

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().ContainSingle().Which.IsCompliant.Should().BeTrue(VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
