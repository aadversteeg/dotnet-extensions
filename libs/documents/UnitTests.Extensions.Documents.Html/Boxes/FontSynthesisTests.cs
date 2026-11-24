using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Under <c>font-synthesis: none</c> (or its longhands) a weight or style the family has no face of prints in the nearest
/// face, plainly, as Chromium 159 prints it (cases <c>fs-none-regular-only</c>, <c>fs-none-regular-bold</c>); without it
/// the face is refused, and the message says how to print it.
/// </summary>
public class FontSynthesisTests
{
    private static HtmlRenderOptions Options(params TrueTypeFont[] fonts) => new() { Fonts = new FontSet([.. fonts]), SourceName = "doc.html" };

    private static string Document(string style) =>
        $"<!doctype html><html><head><style>@page {{ size: 300pt 200pt; margin: 12pt }} body {{ margin: 0; font-family: Lato }} p {{ {style} }}</style></head><body><p>alpha</p></body></html>";

    private static string FaceOf(string style, params TrueTypeFont[] fonts)
    {
        var pages = HtmlRendering.ToPages(Document(style), Options(fonts));
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        return PrintedPages.Engine(pages.Value.Output).Pages.Single().Words.Single().Face!;
    }

    [Theory]
    [InlineData("font-synthesis: none; font-weight: 700", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-weight: 600", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-weight: 300", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-style: italic", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-weight: 700; font-style: italic", "Lato-Regular")]
    [InlineData("font-synthesis-weight: none; font-weight: 700", "Lato-Regular")]
    [InlineData("font-synthesis-style: none; font-style: italic", "Lato-Regular")]
    public void With_synthesis_off_a_family_of_one_face_prints_in_it(string style, string face) =>
        FaceOf(style, TestFonts.Regular).Should().Be(face);

    [Theory]
    [InlineData("font-synthesis: none; font-style: italic", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-weight: 700; font-style: italic", "Lato-Bold")]
    [InlineData("font-synthesis: none; font-weight: 600", "Lato-Bold")]
    [InlineData("font-synthesis: none; font-weight: 500", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-weight: 800", "Lato-Bold")]
    [InlineData("font-synthesis: none; font-weight: 300", "Lato-Regular")]
    [InlineData("font-synthesis: none; font-weight: 900", "Lato-Bold")]
    public void With_synthesis_off_the_nearest_face_is_taken(string style, string face) =>
        FaceOf(style, TestFonts.Regular, TestFonts.Bold).Should().Be(face);

    [Theory]
    [InlineData("font-weight: 700")]
    [InlineData("font-style: italic")]
    [InlineData("font-synthesis-weight: none; font-style: italic")]
    [InlineData("font-synthesis-style: none; font-weight: 700")]
    public void Where_a_browser_would_synthesise_the_face_is_refused_with_the_way_out(string style)
    {
        var result = HtmlRendering.ToPages(Document(style), Options(TestFonts.Regular));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().EndWith(" Give the face, or write font-synthesis: none to print in the nearest face.");
    }

    /// <summary>The order of CSS Fonts 4 §5.2 among several faces on one side of the weight, with <c>@font-face</c> rules
    /// that declare the files at other weights: Lato-Bold at 700, Lato-Regular at 900 and at 200, Lato-Italic (as upright)
    /// at 300.</summary>
    [Theory]
    [InlineData(600, "Lato-Bold")]
    [InlineData(800, "Lato-Regular")]
    [InlineData(950, "Lato-Regular")]
    [InlineData(350, "Lato-Italic")]
    [InlineData(250, "Lato-Regular")]
    [InlineData(100, "Lato-Regular")]
    [InlineData(450, "Lato-Italic")]
    [InlineData(500, "Lato-Italic")]
    public void The_nearest_weight_follows_the_order_of_the_font_matching_rule(int weight, string face)
    {
        var html = "<!doctype html><html><head><style>" +
                   "@font-face { font-family: Body; src: url(bold.ttf); font-weight: 700 }" +
                   "@font-face { font-family: Body; src: url(regular.ttf); font-weight: 900 }" +
                   "@font-face { font-family: Body; src: url(regular.ttf); font-weight: 200 }" +
                   "@font-face { font-family: Body; src: url(italic.ttf); font-weight: 300 }" +
                   $" @page {{ size: 300pt 200pt; margin: 12pt }} body {{ margin: 0; font-family: Body; font-synthesis: none }} p {{ font-weight: {weight} }}</style></head><body><p>alpha</p></body></html>";
        var files = new Dictionary<string, string> { ["bold.ttf"] = "Lato-Bold.ttf", ["regular.ttf"] = "Lato-Regular.ttf", ["italic.ttf"] = "Lato-Italic.ttf" };
        var options = new HtmlRenderOptions
        {
            Style = HtmlStyleOptions.Default with { Resources = url => files.TryGetValue(url, out var file) ? new HtmlResource(TestFonts.Bytes(file)) : null },
            SourceName = "doc.html",
        };

        var pages = HtmlRendering.ToPages(html, options);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value.Output).Pages.Single().Words.Single().Face.Should().Be(face);
    }

    [Fact]
    public void A_face_of_a_font_face_rule_is_matched_the_same_way()
    {
        var html = "<!doctype html><html><head><style>@font-face { font-family: Body; src: url(lato.ttf); font-weight: 400 }" +
                   " @page { size: 300pt 200pt; margin: 12pt } body { margin: 0; font-family: Body } p { font-weight: 700 }" +
                   " .none { font-synthesis: none }</style></head><body><p class=\"none\">alpha</p></body></html>";
        var options = new HtmlRenderOptions
        {
            Style = HtmlStyleOptions.Default with { Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null },
            SourceName = "doc.html",
        };

        var pages = HtmlRendering.ToPages(html, options);
        var refused = HtmlRendering.ToPages(html.Replace(" class=\"none\"", string.Empty, StringComparison.Ordinal), options);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value.Output).Pages.Single().Words.Single().Face.Should().Be("Lato-Regular");
        refused.Error.Message.Should().Be("The @font-face rules of 'Body' have no face of weight 700; the nearest face is not taken and none is synthesised." +
            " Give the face, or write font-synthesis: none to print in the nearest face.");
    }
}
