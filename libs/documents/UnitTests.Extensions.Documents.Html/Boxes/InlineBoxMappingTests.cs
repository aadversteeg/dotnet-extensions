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

/// <summary>Box generation of an inline element with a box of its own (THE RULE FOR AN INLINE BOX IN HTML): a group with
/// <see cref="InlineGroup.Box"/> and no role, the values the rows lift and those they still refuse at the start tag; the
/// reference cases <c>ib-*</c> hold Chromium's prints.</summary>
public class InlineBoxMappingTests
{
    private static string Doc(string body) =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string body, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(Doc(body), new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = Resources }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy, Resources = Resources }));

    private static readonly byte[] Figure = File.ReadAllBytes(Path.Combine(HtmlCases.Folder(), "figure.png"));

    private static HtmlResource? Resources(string url) => url.EndsWith("figure.png", StringComparison.Ordinal) ? new HtmlResource(Figure) : null;

    private static Box Paragraph(string body)
    {
        var result = Boxes(body);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pending = new Stack<Box>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            var box = pending.Pop();
            if (box.Layout == LayoutAlgorithmId.Inline)
            {
                return box;
            }

            foreach (var child in box.Children.OfType<Box>())
            {
                pending.Push(child);
            }
        }

        throw new InvalidOperationException("no paragraph");
    }

    private static IEnumerable<Error> Refusals(string body) => Boxes(body).Error.Parts();

    private static string? Property(Error error) => error.Metadata?[ErrorMetadataKeys.Property]?.ToString();

    [Fact]
    public void A_span_with_a_background_padding_border_and_margin_is_a_group_without_a_role_that_holds_its_box()
    {
        var paragraph = Paragraph("<p>Plain <span style=\"background-color: #ff0000; padding: 3pt 6pt; border: 1.5pt solid #0000ff; margin: 0 9pt\">box</span> text.</p>");

        var group = paragraph.Children.OfType<InlineGroup>().Should().ContainSingle().Which;
        group.Role.Should().BeNull("a span without a language and not a link's target opens no structure element");
        var box = group.Box!;
        box.Background.Should().Be(new Color(255, 0, 0));
        box.Padding.Should().Be(new Edges(3.Pt(), 6.Pt(), 3.Pt(), 6.Pt()));
        box.Border.Should().Be(Border.All(new BorderSide(1.5.Pt(), new Color(0, 0, 255))));
        (box.MarginLeft, box.MarginRight).Should().Be((9.Pt(), 9.Pt()));
        group.Children[0].Should().BeOfType<TextRun>().Which.Text.Should().BeEmpty("the element's empty run comes first, for its line height");
    }

    [Fact]
    public void A_span_without_a_box_a_language_or_a_link_to_it_makes_no_group_as_before()
    {
        Paragraph("<p>Plain <span style=\"color: #ff0000\">red</span> <b>bold</b> text.</p>").Children.OfType<InlineGroup>().Should().BeEmpty();
    }

    [Fact]
    public void A_mark_is_a_group_with_the_user_agent_s_yellow_background()
    {
        var group = Paragraph("<p>Marked <mark>words</mark> here.</p>").Children.OfType<InlineGroup>().Should().ContainSingle().Which;

        group.Box!.Background.Should().Be(new Color(255, 255, 0));
        group.Box.Start.Should().Be(0);
    }

    [Fact]
    public void An_empty_span_with_a_box_is_a_group_too()
    {
        Paragraph("<p>Empty <span style=\"background-color: #0000ff; padding: 0 6pt\"></span> box.</p>").Children.OfType<InlineGroup>().Should().ContainSingle();
    }

    [Fact]
    public void A_span_with_a_box_and_a_language_is_a_span_element_with_its_box()
    {
        var group = Paragraph("<p>Plain <span lang=\"fr\" style=\"background-color: #ff0000\">mot</span> text.</p>").Children.OfType<InlineGroup>().Should().ContainSingle().Which;

        group.Role.Should().Be(SemanticRole.Span);
        group.Language.Should().Be("fr");
        group.Box.Should().NotBeNull();
    }

    [Theory]
    [InlineData("box-decoration-break: clone; background-color: #ff0000", "box-decoration-break")]
    [InlineData("box-decoration-break: clone; margin-left: 3pt", "box-decoration-break")]
    [InlineData("border: 1.5pt inset #000000", "border-top-style")]
    [InlineData("border: 1.5pt outset #000000", "border-top-style")]
    [InlineData("background-color: rgba(255, 0, 0, 0.5)", "background-color")]
    [InlineData("padding: 0 10%", "padding-right")]
    [InlineData("margin: 0 5%", "margin-right")]
    public void An_inline_box_value_that_is_not_drawn_is_refused_at_the_start_tag(string style, string property)
    {
        Refusals($"<p>Plain <span style=\"{style}\">box</span> text.</p>")
            .Should().Contain(error => error.Code == HtmlStyleErrorCodes.Unsupported.Value && Property(error) == property);
    }

    /// <summary>The style sheet itself refuses these: a rounded corner and a negative margin are taken only at their initial
    /// value, a dashed border not at all.</summary>
    [Theory]
    [InlineData("background-color: #ff0000; border-radius: 3pt", "border-radius")]
    [InlineData("margin-left: -3pt", "margin-left")]
    [InlineData("border: 1.5pt dashed #000000", "border-top-style")]
    public void An_inline_box_value_the_style_sheet_does_not_take_is_refused_there(string style, string property)
    {
        Refusals($"<p>Plain <span style=\"{style}\">box</span> text.</p>")
            .Should().Contain(error => error.Message.Contains(property, StringComparison.Ordinal));
    }

    [Fact]
    public void A_border_of_a_fraction_of_a_pixel_is_drawn_as_the_style_sheet_snaps_it()
    {
        var group = Paragraph("<p>Plain <span style=\"border: 0.5pt solid #000000\">box</span> text.</p>").Children.OfType<InlineGroup>().Single();

        group.Box!.Border.Top.Width.Should().Be(0.75.Pt(), "a width below one pixel is one pixel");
    }

    [Fact]
    public void A_box_inside_a_link_is_refused_at_the_inner_element_and_drawn_without_its_box()
    {
        const string body = "<p>See <a href=\"https://example.com/\">the <span style=\"background-color: #ff0000\">site</span></a>.</p>";
        Refusals(body).Should().ContainSingle().Which.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.InlineBox);

        var warned = Boxes(body, UnsupportedPolicy.Warn);
        warned.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("<p>See <span style=\"background-color: #ff0000\">the <a href=\"https://example.com/\">site</a></span>.</p>")]
    [InlineData("<p>See <a href=\"https://example.com/\" style=\"background-color: #ff0000\">the site</a>.</p>")]
    public void A_link_inside_a_box_or_with_a_box_of_its_own_is_refused_at_the_link(string body)
    {
        Refusals(body).Should().ContainSingle().Which.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Link);
    }

    [Fact]
    public void An_image_inside_a_box_is_refused_at_the_image()
    {
        Refusals("<p>See <span style=\"background-color: #ff0000\">the <img src=\"figure.png\" alt=\"a figure\"></span>.</p>")
            .Should().ContainSingle().Which.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.InlineBox);
    }

    [Fact]
    public void A_box_over_two_lines_of_a_justified_paragraph_is_refused_by_layout_at_the_element()
    {
        var boxes = Boxes($"<p style=\"text-align: justify\">Start <span style=\"background-color: #ff0000\">{string.Join(" ", Enumerable.Repeat("word", 200))}</span>.</p>");

        var laid = boxes.OnSuccessBind(value => DocumentLayout.Run(value.Document, value.Fonts));

        laid.IsFailure.Should().BeTrue();
        laid.Error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
        laid.Error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(1);
    }

    /// <summary>The inline box cases through the HTML path into a tagged PDF (PDF/A-3a and PDF/UA-1 together, each case with
    /// a title and a language added): a badge, a mark, code and a key inside paragraphs, a box with a language, boxes alone,
    /// boxes over lines and over a page break, and a box with a language over two lines. veraPDF passes both.</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void The_inline_box_cases_as_tagged_PDF_pass_veraPDF(string flavour)
    {
        string[] names = ["ib-align", "ib-alone", "ib-background", "ib-border", "ib-break-align", "ib-break-fraction", "ib-break-nested", "ib-break-page", "ib-break-space", "ib-code-kbd",
            "ib-empty-alone", "ib-fraction", "ib-margin", "ib-nested", "ib-only-boxes", "ib-origin-cell", "ib-origin-margin", "ib-origin-page", "ib-padding", "ib-space"];
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-inline-boxes-").FullName;
        try
        {
            foreach (var name in names.Append("badge-language").Append("broken-language"))
            {
                var path = Path.Combine(HtmlCases.Folder(), (name switch { "badge-language" => "ib-background", "broken-language" => "ib-break-nested", _ => name }) + ".html");
                var html = File.ReadAllText(path).Replace("<html>", "<html lang=\"en\">", StringComparison.Ordinal).Replace("<head>", "<head><title>Inline boxes</title>", StringComparison.Ordinal);
                if (name == "badge-language")
                {
                    html = html.Replace("<span style=\"background-color: #ff0000\">red</span>", "<span lang=\"fr\" style=\"background-color: #ff0000\">rouge</span>", StringComparison.Ordinal);
                }

                if (name == "broken-language")
                {
                    html = html.Replace("<p>Nested <span style=", "<p>Nested <span lang=\"fr\" style=", StringComparison.Ordinal);
                    html.Should().Contain("lang=\"fr\"");
                }

                var baseUrl = new System.Uri(path).AbsoluteUri;
                var pdf = HtmlParser.Parse(html, new HtmlParseOptions(name + ".html"))
                    .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = baseUrl }))
                    .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = HtmlCases.Resource, BaseUrl = baseUrl }))
                    .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts))
                    .OnSuccessBind(pages => pages.ToPdf(new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a.AndPdfUa1() }));
                pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? $"{name}: {pdf.Error}" : string.Empty);
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. pdf.Value]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(names.Length + 2);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
