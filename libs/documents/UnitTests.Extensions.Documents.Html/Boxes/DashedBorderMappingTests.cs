using System.IO;
using Ave.Extensions.Documents;
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

/// <summary>Box generation of <c>border-style: dashed</c> (CSS Backgrounds 3 §3.2): the sides a block box takes into
/// <see cref="BorderStyle.Dashed"/>, and the places and styles still refused, each with what to do; the reference cases
/// <c>bd-*</c> hold Chromium's prints.</summary>
public class DashedBorderMappingTests
{
    private static string Doc(string body) =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static List<Error> Refusals(string html) => Boxes(html) is { IsFailure: true } failed ? [.. failed.Error.Parts()] : [];

    [Fact]
    public void A_block_box_takes_its_dashed_sides_and_is_drawn()
    {
        var boxes = Boxes(Doc("<div style=\"border: 1px dashed #666666; border-left-style: solid; padding: 3mm\">x</div>"));
        boxes.IsSuccess.Should().BeTrue(boxes.IsFailure ? boxes.Error.ToString() : string.Empty);

        var div = (Box)((Box)boxes.Value.Document.Root.Children.Single()).Children.Single();
        div.Style.Border.Top.Style.Should().Be(BorderStyle.Dashed);
        div.Style.Border.Left.Style.Should().Be(BorderStyle.Solid);
        DocumentLayout.Run(boxes.Value.Document, boxes.Value.Fonts).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("<p>a <span style=\"border: 1px dashed #000000\">b</span> c</p>", "an inline box")]
    [InlineData("<p>a <span style=\"display: inline-block; border: 1px dashed #000000\">b</span> c</p>", "an inline-block")]
    [InlineData("<ul><li style=\"border: 1px dashed #000000\">b</li></ul>", "a list item")]
    [InlineData("<table><tr><td style=\"border: 1px dashed #000000\">b</td></tr></table>", "a table or a part of a table")]
    [InlineData("<div style=\"border: 1px dashed #000000; border-top-color: #ff0000\">b</div>", "one width and one colour")]
    [InlineData("<div style=\"border: 1px dashed #000000; border-top-width: 2px\">b</div>", "one width and one colour")]
    [InlineData("<div style=\"border: 2px dashed #000000; border-top-style: inset\">b</div>", "solid or dashed")]
    public void A_dashed_side_where_it_is_not_drawn_is_refused_with_what_to_do(string body, string reason)
    {
        var refusals = Refusals(Doc(body));

        refusals.Should().NotBeEmpty();
        refusals.Should().OnlyContain(error => error.Code == HtmlStyleErrorCodes.Unsupported.Value);
        refusals[0].Message.Should().Contain("dashed").And.Contain(reason);
    }

    [Theory]
    [InlineData("dotted")]
    [InlineData("double")]
    [InlineData("groove")]
    [InlineData("ridge")]
    public void Dotted_double_groove_and_ridge_are_refused_as_later_work(string style)
    {
        var refusal = Refusals(Doc($"<div style=\"border-bottom: 1px {style} #000000\">b</div>")).Single();

        refusal.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        refusal.Message.Should().Contain($"'border-bottom-style: {style}' is not drawn in this version").And.Contain("come later").And.Contain("use solid or dashed");
    }

    /// <summary>Rounded badges, pills, cells and list items and dashed boxes (cases <c>bri-*</c>, <c>bd-*</c>) as tagged PDF/A-3a
    /// with PDF/UA-1: the rounded and dashed paths are layout artifacts, as square backgrounds and borders are. veraPDF
    /// passes both.</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void Rounded_inline_boxes_and_dashed_boxes_as_tagged_PDF_pass_veraPDF(string flavour)
    {
        string[] names = ["bri-pill", "bri-iblk", "bri-cell", "bri-broken", "bri-list", "bd-widths", "bd-sides", "bd-break"];
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-dashed-boxes-").FullName;
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(HtmlCases.Folder(), name + ".html");
                var html = File.ReadAllText(path).Replace("<html>", "<html lang=\"en\">", StringComparison.Ordinal).Replace("<head>", "<head><title>Badges and dashes</title>", StringComparison.Ordinal);
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
