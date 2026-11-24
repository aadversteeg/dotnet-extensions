using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// A <c>::before</c> or <c>::after</c> of <c>display: block</c>: a block box, the first or the last child of its element,
/// with the element's own text in anonymous boxes around it (cases <c>gc-block-margins</c>, <c>gc-block-box</c>,
/// <c>gc-block-anonymous</c>, <c>gc-block-break</c>); one without text is decoration; one on an element that is not a block
/// is refused (<c>gc-block-span</c>).
/// </summary>
public class BlockPseudoElementTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>Blocks</title><style>body {{ font-family: Lato; margin: 0 }}\n{css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static Box Body(string html)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Diagnostics.Should().BeEmpty();
        return (Box)result.Value.Document.Root.Children.Single();
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().First();

    /// <summary>A box's children as "role:text", the text of its runs joined, a star before generated text.</summary>
    private static string Shape(Box box) => string.Join(" | ", box.Children.OfType<Box>().Select(child =>
        $"{child.Role.GetType().Name.Replace("Role", string.Empty, StringComparison.Ordinal)}:{string.Concat(child.Children.OfType<TextRun>().Select(run => run.Generated ? "*" + run.Text : run.Text))}"));

    [Fact]
    public void A_block_pseudo_element_is_the_first_or_last_child_box_and_the_element_text_lies_in_an_anonymous_box()
    {
        // gc-block-anonymous: an element with inline content only.
        var p = (Box)Body(Doc("<p>Inline <b>bold</b> words</p>", "p::before { content: \"Opening\"; display: block } p::after { content: \"Closing\"; display: block }")).Children.Single();

        p.Role.Should().Be(SemanticRole.Paragraph);
        p.Layout.Should().Be(LayoutAlgorithmId.Flow);
        Shape(p).Should().Be("Generic:*Opening | Generic:Inline bold words | Generic:*Closing");
    }

    [Fact]
    public void A_block_pseudo_element_has_the_margins_padding_border_background_and_alignment_of_a_block()
    {
        // gc-block-box and gc-block-margins.
        var p = (Box)Body(Doc("<p>Body</p>", "p::before { content: \"Boxed\"; display: block; margin: 9pt 0 3pt; padding: 3pt 6pt; border: 1.5pt solid #336699; background: #e0e8f0; text-align: center }")).Children.Single();
        var before = (Box)p.Children[0];

        (before.Style.Margin.Top.Points, before.Style.Margin.Bottom.Points, before.Style.Padding.Left.Points).Should().Be((9, 3, 6));
        before.Style.Border.Top.Width.Points.Should().Be(1.5);
        before.Style.Background.Should().Be(new Color(0xe0, 0xe8, 0xf0));
        before.Style.TextAlign.Should().Be(TextAlign.Center);
    }

    [Fact]
    public void A_block_pseudo_element_without_text_is_decoration()
    {
        // gc-block-box: the rule under a heading.
        var h2 = (Box)Body(Doc("<h2>Results</h2>", "h2::after { content: \"\"; display: block; border-bottom: 0.75pt solid #993333 }")).Children.Single();
        var after = (Box)h2.Children[^1];

        after.Role.Should().Be(SemanticRole.Decorative);
        after.Children.Should().BeEmpty();
        after.Style.Border.Bottom.Width.Points.Should().Be(0.75);
    }

    [Theory]
    // gc-block-span: Chromium breaks the span's lines around the block.
    [InlineData("<p>Text <span>inner</span> after</p>", "span::before { content: \"x\";\n  display: block }", "(display: inline) is not drawn: a block pseudo-element is drawn on a block element only")]
    [InlineData("<ul><li>Item</li></ul>", "li::before { content: \"x\";\n  display: block }", "(display: list-item) is not drawn")]
    public void A_block_pseudo_element_on_an_element_that_is_not_a_block_is_refused_at_its_display(string body, string css, string message)
    {
        var refusal = Refusal(Doc(body, css));

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((CssErrorCodes.Unsupported.GeneratedContent, 3, 3));
        refusal.ToString().Should().Contain(message);
    }

    [Theory]
    // Block sizes are not drawn in this version: refused as on any block, at the pseudo-element.
    [InlineData("height")]
    [InlineData("width")]
    public void A_size_on_a_block_pseudo_element_is_refused_as_on_any_block(string property)
    {
        var refusal = Refusal(Doc("<p>a</p>", $"p::before {{ content: \"x\"; display: block; {property}: 20pt }}"));

        refusal.Metadata![ErrorMetadataKeys.Property].Should().Be(property);
    }

    private const string TaggedHtml = "<!doctype html><html lang=\"en\"><head><title>Blocks</title><style>@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Regular.ttf) } @font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Bold.ttf); font-weight: 700 } body { font-family: Lato }"
        + " h1 { counter-increment: sec } h1::before { content: \"Section \" counter(sec); display: block; font-size: 10.5pt } h1::after { content: \"\"; display: block; border-bottom: 1px solid #993333 }"
        + " p::after { content: \"End of note\"; display: block; margin-top: 6pt; padding: 3pt; background: #eeeeee }</style></head>"
        + "<body><h1>Results</h1><p>A paragraph.</p></body></html>";

    private static HtmlRenderOptions TaggedOptions { get; } = new() { Style = HtmlStyleOptions.Default with { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = new Uri(Path.GetFullPath(Path.Combine(HtmlCases.Folder(), "gc.html"))).AbsoluteUri } };

    [Fact]
    public void In_the_structure_tree_the_text_of_a_block_pseudo_element_is_a_div_of_its_element_and_one_without_text_is_not_there()
    {
        var pdf = HtmlRendering.ToPdf(TaggedHtml, TaggedOptions, new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfUa1 });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);

        var elements = PdfStructureReadBack.Read(pdf.Value.Output).SelectMany(element => element.DepthFirst()).ToList();

        // As the anonymous box around the element's own text, a block pseudo-element is a Div of its element; the rule
        // under the heading is an artifact, so not in the tree.
        string.Join(" ", elements.Select(element => $"{element.Type}[{element.Text}]"))
            .Should().Be("Document[] Div[] H1[] Div[Section 1] Div[Results] P[] Div[A paragraph.] Div[End of note]");
        PdfStructureReadBack.ParentTreeDifferences(pdf.Value.Output).Should().BeEmpty();
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_tagged_PDF_with_block_pseudo_elements_passes_veraPDF(string flavour)
    {
        var profile = flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1;
        var pdf = HtmlRendering.ToPdf(TaggedHtml, TaggedOptions, new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = profile });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-blocks-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "blocks.pdf"), [.. pdf.Value.Output]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
