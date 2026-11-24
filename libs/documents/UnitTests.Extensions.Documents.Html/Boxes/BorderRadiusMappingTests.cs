using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
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

/// <summary>Box generation of <c>border-radius</c> (CSS Backgrounds 3 §5): the corners a block box takes into
/// <see cref="BoxStyle.BorderRadius"/>, and the places and forms still refused, each with what to do; the reference cases
/// <c>br-*</c> hold Chromium's prints.</summary>
public class BorderRadiusMappingTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static List<Error> Refusals(string html) => Boxes(html) is { IsFailure: true } failed ? [.. failed.Error.Parts()] : [];

    private static string? Property(Error error) => error.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Property, out var value) ? (string)value : null;

    [Fact]
    public void A_block_box_takes_one_radius_per_corner_as_a_length_or_a_percentage()
    {
        var result = Boxes(Doc("<div style=\"border-radius: 4px 8px 50% 0; background: #ff0000\">x</div>"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);

        var div = (Box)((Box)result.Value.Document.Root.Children.Single()).Children.Single();
        div.Style.BorderRadius.Should().Be(new BorderRadius(new CornerRadius(3.Pt()), new CornerRadius(6.Pt()), CornerRadius.OfPercent(50), CornerRadius.Zero));
    }

    [Theory]
    [InlineData("<p>a <span style=\"border-radius: 3px; background: #ff0000\">b</span> c</p>", "an inline element")]
    [InlineData("<p>a <span style=\"display: inline-block; border-radius: 3px\">b</span> c</p>", "an inline-block")]
    [InlineData("<table><tr><td style=\"border-radius: 3px\">b</td></tr></table>", "a table or a part of a table")]
    [InlineData("<table style=\"border-collapse: separate; border-radius: 3px\"><tr><td>b</td></tr></table>", "a table or a part of a table")]
    [InlineData("<ul><li style=\"border-radius: 3px\">b</li></ul>", "a list item")]
    [InlineData("<img alt=\"b\" width=\"10\" height=\"10\" style=\"display: block; border-radius: 3px\" src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==\">", "an image")]
    [InlineData("<div style=\"border-radius: 3px; overflow: hidden\">b<p>c</p></div>", "overflow other than visible")]
    [InlineData("<div style=\"border-radius: 3px; border: 1px solid #000000; border-left-width: 2px\">b</div>", "different widths")]
    [InlineData("<div style=\"border-radius: 3px; border: 1px solid #000000; border-top-color: #ff0000\">b</div>", "different widths, colours")]
    [InlineData("<div style=\"border-radius: 3px; border-bottom: 1px solid #000000\">b</div>", "give all four sides one solid border")]
    [InlineData("<div style=\"border-radius: 3px; border: 2px outset #000000\">b</div>", "or not solid")]
    public void A_radius_where_it_is_not_drawn_is_one_refusal_at_the_start_tag(string body, string reason)
    {
        var radius = Refusals(Doc(body)).Where(error => Property(error)?.EndsWith("-radius", StringComparison.Ordinal) == true).ToList();

        radius.Should().ContainSingle();
        radius[0].Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Value);
        radius[0].Message.Should().Contain("border-radius").And.Contain(reason);
    }

    [Fact]
    public void Elliptical_corners_are_refused_where_they_are_written()
    {
        var shorthand = Refusals(Doc("<div style=\"border-radius: 4px / 2px\">b</div>")).Single();
        shorthand.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        shorthand.Message.Should().Contain("'border-radius'").And.Contain("give one radius per corner");

        var longhand = Refusals(Doc("<div style=\"border-top-left-radius: 4px 2px\">b</div>")).Single();
        longhand.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        longhand.Message.Should().Contain("elliptical corner");
    }

    [Fact]
    public void A_radius_on_a_list_marker_or_a_page_margin_box_is_refused_by_the_style_sheet()
    {
        Refusals(Doc("<ul><li>b</li></ul>", "li::marker { border-radius: 3px }")).Should().Contain(error => error.Code == CssErrorCodes.Unsupported.GeneratedContent && error.Message.Contains("radius", StringComparison.Ordinal));
        Refusals(Doc("<p>b</p>", "@page { margin: 20pt; @top-center { content: 'x'; border-radius: 3px } }")).Should().Contain(error => error.Code == CssErrorCodes.Unsupported.PageRule && error.Message.Contains("'border-radius'", StringComparison.Ordinal));
    }

    /// <summary>The rounded box cases through the HTML path into a tagged PDF (PDF/A-3a and PDF/UA-1 together, each case with
    /// a title and a language added): backgrounds, rings, rounded boxes in a table cell and a list item, text in a corner,
    /// and a box over a page break. The rounded paths are layout artifacts, as square backgrounds and borders are. veraPDF
    /// passes both.</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void The_rounded_box_cases_as_tagged_PDF_pass_veraPDF(string flavour)
    {
        string[] names = ["br-bg-clamp", "br-bg-corners", "br-bg-percent", "br-bg-radii", "br-border-1px", "br-border-3px", "br-boxes-inside", "br-break", "br-text-corner"];
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-rounded-boxes-").FullName;
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(HtmlCases.Folder(), name + ".html");
                var html = File.ReadAllText(path).Replace("<html>", "<html lang=\"en\">", StringComparison.Ordinal).Replace("<head>", "<head><title>Rounded boxes</title>", StringComparison.Ordinal);
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

            results.Should().HaveCount(names.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
