using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
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

/// <summary>Box generation of an element with <c>display: inline-block</c> (THE RULE FOR AN INLINE-BLOCK IN HTML): a box
/// among the runs of its paragraph, made as a block box is made, and the values refused on it at its start tag; the
/// reference cases <c>iblk-*</c> hold Chromium's prints.</summary>
public class InlineBlockMappingTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static Box Paragraph(string body)
    {
        var result = Boxes(Doc(body));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return (Box)((Box)result.Value.Document.Root.Children.Single()).Children.Single();
    }

    private static Error Refusal(string body, string css = "") => Boxes(Doc(body, css)).Error.Parts().First();

    private static string? Property(Error error) => error.Metadata?[ErrorMetadataKeys.Property]?.ToString();

    [Fact]
    public void An_inline_block_with_text_is_a_box_of_lines_among_the_runs_with_its_block_values()
    {
        const string body = "<p>Before <span style=\"display: inline-block; padding: 3pt 6pt; width: 60pt; background-color: #ff0000\">Badge</span> after.</p>";
        var paragraph = Paragraph(body);

        paragraph.Layout.Should().Be(LayoutAlgorithmId.Inline);
        var box = paragraph.Children.OfType<Box>().Should().ContainSingle().Which;
        box.Layout.Should().Be(LayoutAlgorithmId.Inline);
        box.Style.Padding.Left.Should().Be(6.Pt());
        box.Style.SpecifiedWidth.Should().Be(new SizeValue.Fixed(60.Pt()), "a block box's width");
        box.Style.Text.Should().NotBeNull("its lines have their own strut");
        box.Source.Column.Should().Be(Doc(body).IndexOf("<span", StringComparison.Ordinal) + 1, "layout refuses it at its start tag");
        box.Children.OfType<TextRun>().Single().Text.Should().Be("Badge");
        paragraph.Children.OfType<TextRun>().Select(run => run.Text).Should().Equal("Before ", " after.");
    }

    [Fact]
    public void An_inline_block_with_a_list_is_a_flow_box_among_the_runs()
    {
        // In a div: a ul start tag closes an open p (HTML Standard §13.2.6.4.7).
        var paragraph = Paragraph("<div>List <span style=\"display: inline-block\"><ul><li>one</li></ul></span> end.</div>");

        paragraph.Children.OfType<Box>().Single().Layout.Should().Be(LayoutAlgorithmId.Flow);
    }

    [Theory]
    [InlineData("vertical-align: top", "vertical-align")]
    [InlineData("vertical-align: middle", "vertical-align")]
    [InlineData("width: 50%", "width")]
    [InlineData("max-width: 50%", "max-width")]
    [InlineData("break-inside: avoid", "break-inside")]
    public void An_inline_block_refuses_what_is_not_built_at_its_start_tag(string style, string property)
    {
        var refusal = Refusal($"<p>a\n<span style=\"display: inline-block; {style}\">b</span> c</p>");

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, property, 2));
        refusal.Message.Should().Contain("inline-block");
    }

    [Theory]
    [InlineData("<a href=\"https://example.com/\">x <span style=\"display: inline-block\">b</span></a>")]
    [InlineData("<span style=\"background-color: #ff0000\">x <span style=\"display: inline-block\">b</span></span>")]
    [InlineData("<u>x <span style=\"display: inline-block\">b</span></u>")]
    [InlineData("<sup>x <span style=\"display: inline-block\">b</span></sup>")]
    public void An_inline_block_inside_a_link_a_box_underlined_or_raised_text_is_refused(string inner)
    {
        var refusal = Refusal($"<p>a {inner} c</p>");

        (refusal.Code, Property(refusal)).Should().Be((HtmlStyleErrorCodes.Unsupported.InlineBox, "display"));
    }

    [Fact]
    public void A_table_inside_an_inline_block_is_refused_at_its_start_tag()
    {
        var refusal = Refusal("<div>a <span style=\"display: inline-block\">x\n<table><tr><td>b</td></tr></table></span> c</div>");

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.InlineBox, 2, "table"));
    }

    [Fact]
    public void Inline_blocks_nest_two_deep_and_a_third_is_refused()
    {
        const string two = "<span style=\"display: inline-block\">x <span style=\"display: inline-block\">y</span></span>";
        Paragraph($"<p>a {two} c</p>").Children.OfType<Box>().Single().Children.OfType<Box>().Should().ContainSingle();

        var refusal = Refusal("<p>a <span style=\"display: inline-block\">x <span style=\"display: inline-block\">y\n<span style=\"display: inline-block\">z</span></span></span> c</p>");

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((HtmlStyleErrorCodes.Unsupported.InlineBox, 2));
        refusal.Message.Should().Contain($"nest {HtmlBoxGenerator.MaxInlineBlockNesting} deep");
    }

    [Fact]
    public void An_inline_block_s_overflow_other_than_visible_is_refused_at_its_start_tag()
    {
        var refusal = Refusal("<p>a\n<span style=\"display: inline-block; overflow: hidden\">b</span> c</p>");

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "overflow-x", 2));
    }

    [Fact]
    public void An_inline_block_made_by_a_pseudo_element_is_refused_at_its_display_declaration()
    {
        var refusal = Refusal("<p>a</p>", "p::before { content: \"x\";\n  display: inline-block }");

        (refusal.Code, Property(refusal)).Should().Be((Ave.Extensions.Documents.Css.CssErrorCodes.Unsupported.GeneratedContent, "display"));
    }

    [Theory]
    [InlineData("inline-flex")]
    [InlineData("inline-grid")]
    [InlineData("inline-table")]
    public void The_other_inline_level_display_types_stay_refused_at_their_declaration(string display)
    {
        var refusal = Refusal($"<p>a <span style=\"display: {display}\">b</span> c</p>");

        (refusal.Code, Property(refusal)).Should().Be((Ave.Extensions.Documents.Css.CssErrorCodes.Unsupported.Value, "display"));
    }

    /// <summary>The inline-block cases through the HTML path into a tagged PDF (PDF/A-3a and PDF/UA-1 together, each case
    /// with a title and the images' alternative text added): boxes with text, wrapped lines and sizes, margins, rows of boxes,
    /// empty swatches, a list, a table, an image, a nested box, a styled span, a link and generated text inside one, a box that
    /// moves to the next page, and boxes in a cell and a list item. veraPDF passes both.</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void The_inline_block_cases_as_tagged_PDF_pass_veraPDF(string flavour)
    {
        string[] names = ["iblk-badge", "iblk-context", "iblk-inside", "iblk-margin", "iblk-page", "iblk-row", "iblk-sizes", "iblk-swatch", "iblk-wrap"];
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-inline-blocks-").FullName;
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(HtmlCases.Folder(), name + ".html");
                var html = File.ReadAllText(path).Replace("<head>", "<head><title>Inline blocks</title>", StringComparison.Ordinal)
                    .Replace("alt=\"figure\"", "alt=\"a figure\"", StringComparison.Ordinal);
                var baseUrl = new System.Uri(path).AbsoluteUri;
                var pdf = HtmlParser.Parse(html, new HtmlParseOptions(name + ".html"))
                    .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, AllowedResourceSchemes = HtmlCases.FileSchemes, BaseUrl = baseUrl }))
                    .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = HtmlCases.Resource, AllowedResourceSchemes = HtmlCases.FileSchemes, BaseUrl = baseUrl }))
                    .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts))
                    .OnSuccessBind(pages => pages.ToPdf(new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a.AndPdfUa1() }));
                pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? $"{name}: {pdf.Error}" : string.Empty);
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. pdf.Value]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(names.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
