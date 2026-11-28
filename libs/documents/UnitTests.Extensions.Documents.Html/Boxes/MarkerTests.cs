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
/// <c>::marker</c>: its colour on every kind of marker (case <c>gc-marker-color</c>), its weight and style
/// (<c>gc-marker-weight</c>), a content of strings as its text (<c>gc-marker-content</c>), and the rest refused at its
/// declaration (<c>gc-marker-font</c>: a larger marker makes the item's first line taller in Chromium).
/// </summary>
public class MarkerTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>Markers</title><style>body {{ font-family: Lato; margin: 0 }}\n{css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    /// <summary>What every label of the document holds, in order: the shape or the text, its colour, weight and style.</summary>
    private static string Labels(string body, string css = "")
    {
        var result = Boxes(Doc(body, css));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Diagnostics.Should().BeEmpty();
        var labels = new List<string>();
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            if (pending.Pop() is not Box box)
            {
                continue;
            }

            if (box.Role is SemanticRole.ListLabelRole)
            {
                labels.Add(box.Children.Single() switch
                {
                    MarkerSymbol symbol => $"{symbol.Shape} {symbol.Style.Color} {symbol.Style.FontWeight.Value}",
                    TextRun run => $"'{run.Text}' {run.Style.Color} {run.Style.FontWeight.Value} {run.Style.FontStyle}",
                    var other => other.ToString()!,
                });
                continue;
            }

            foreach (var child in box.Children.Reverse())
            {
                pending.Push(child);
            }
        }

        return string.Join("|", labels);
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().First();

    private static readonly string Red = new Color(0xc0, 0, 0).ToString();

    [Fact]
    public void A_marker_rule_colours_every_kind_of_marker()
    {
        // gc-marker-color: disc, circle, square and the numbers.
        Labels(
                "<ul><li>a</li><li style=\"list-style-type: circle\">b</li><li style=\"list-style-type: square\">c</li></ul><ol><li>d</li><li style=\"list-style-type: upper-roman\">e</li></ol>",
                "li::marker { color: #c00000 }")
            .Should().Be($"Disc {Red} 400|Circle {Red} 400|Square {Red} 400|'1. ' {Red} 400 Normal|'II. ' {Red} 400 Normal");
    }

    [Fact]
    public void A_marker_takes_its_weight_and_style_and_the_cascade_chooses_among_its_rules()
    {
        // gc-marker-weight; gc-marker-color: ol.k > li::marker (0,1,3) wins over li.c::marker (0,1,2).
        var black = new Color(0, 0, 0).ToString();
        Labels("<ol><li>a</li></ol><ol class=\"i\"><li>b</li></ol>", "ol li::marker { font-weight: bold } .i li::marker { font-weight: normal; font-style: italic }")
            .Should().Be($"'1. ' {black} 700 Normal|'1. ' {black} 400 Italic");
        Labels("<ol class=\"k\"><li class=\"c\">a</li></ol>", "li.c::marker { color: #008000 } ol.k > li::marker { color: #c00000 }")
            .Should().Be($"'1. ' {Red} 400 Normal");
    }

    [Fact]
    public void A_content_of_strings_is_the_text_of_the_marker_also_for_a_bullet()
    {
        // gc-marker-content: "-> " in a ul, "Step: " in an ol, right-aligned as a number.
        Labels("<ul><li>a</li></ul><ol><li>b</li></ol>", "ul li::marker { content: \"-> \" } ol li::marker { content: \"Step\" \": \"; color: #c00000 }")
            .Should().Be($"'-> ' {new Color(0, 0, 0)} 400 Normal|'Step: ' {Red} 400 Normal");
    }

    [Fact]
    public void A_marker_rule_on_an_item_without_a_marker_or_on_an_element_that_is_not_an_item_draws_nothing()
    {
        // gc-marker-color: list-style none with a ::marker rule.
        Labels("<ul style=\"list-style: none\"><li>a</li></ul><p>b</p>", "li::marker, p::marker { color: #c00000 }").Should().BeEmpty();
    }

    [Theory]
    // gc-marker-font: a 16 pt marker makes the item's first line 18 pt high in Chromium.
    [InlineData("li::marker {\n  font-size: 16pt }", "'font-size' on ::marker is not drawn yet")]
    [InlineData("li::marker {\n  font-family: serif }", "'font-family' on ::marker is not drawn yet")]
    [InlineData("li::marker {\n  background-color: #eee }", "'background-color' on ::marker is not drawn yet")]
    [InlineData("li::marker {\n  content: counter(list-item) }", "A counter in the content of ::marker is not drawn yet")]
    [InlineData("li::marker {\n  content: none }", "'content: none' on ::marker is not drawn yet")]
    [InlineData("li::marker {\n  all: unset }", "'all' on ::marker is not drawn yet")]
    public void Any_other_declaration_of_a_marker_rule_is_refused_at_its_place(string css, string message)
    {
        var refusal = Refusal(Doc("<ol><li>a</li></ol>", css));

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata.ContainsKey(ErrorMetadataKeys.SourceColumn)).Should().Be((CssErrorCodes.Unsupported.GeneratedContent, 3, true));
        refusal.ToString().Should().Contain(message);
    }

    [Fact]
    public void A_content_on_the_marker_of_an_item_without_a_marker_is_refused()
    {
        var refusal = Refusal(Doc("<ul style=\"list-style-type: none\"><li>a</li></ul>", "li::marker { content: \"x\" }"));

        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.List);
        refusal.ToString().Should().Contain("whose list-style-type is none");
    }

    private const string TaggedHtml = "<!doctype html><html lang=\"en\"><head><title>Markers</title><style>@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Regular.ttf) } @font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Bold.ttf); font-weight: 700 } body { font-family: Lato }"
        + " h1::after { content: \"\"; display: block; border-bottom: 1px solid #993333 } p::before { content: \"Note\"; display: block; font-weight: bold }"
        + " ul li::marker { color: #c00000 } ol li::marker { color: #0000c0; font-weight: bold } .s li::marker { content: \"-> \" }</style></head>"
        + "<body><h1>Lists</h1><p>A paragraph.</p><ul><li>Red disc</li></ul><ol><li>Blue number</li></ol><ul class=\"s\"><li>Arrow</li></ul></body></html>";

    private static HtmlRenderOptions TaggedOptions { get; } = new() { Style = HtmlStyleOptions.Default with { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, AllowedResourceSchemes = HtmlCases.FileSchemes, BaseUrl = new Uri(Path.GetFullPath(Path.Combine(HtmlCases.Folder(), "gc.html"))).AbsoluteUri } };

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_tagged_PDF_with_block_pseudo_elements_and_coloured_markers_passes_veraPDF(string flavour)
    {
        var profile = flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1;
        var pdf = HtmlRendering.ToPdf(TaggedHtml, TaggedOptions, new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = profile });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-markers-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "markers.pdf"), [.. pdf.Value.Output]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
