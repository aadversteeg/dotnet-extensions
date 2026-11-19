using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>br</c> (the model's forced line break) and of a block-level <c>img</c> (an image box);
/// the reference cases <c>hb-*</c> and <c>hi-*</c> hold Chromium's prints.</summary>
public class LineBreakAndImageTests
{
    private const string LineSeparator = "\u2028";

    private static readonly byte[] Figure = File.ReadAllBytes(Path.Combine(HtmlCases.Folder(), "figure.png"));

    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} img {{ display: block }} {css}</style></head><body>{body}</body></html>";

    /// <summary>Serves figure.png (7 x 10 px) at every URL ending in "figure.png" but those under "/wrong/", and bytes that start as a PNG and do not
    /// decode at "broken.png".</summary>
    private static HtmlResource? Resources(string url) =>
        url.Contains("/wrong/", StringComparison.Ordinal) ? null
        : url.EndsWith("figure.png", StringComparison.Ordinal) ? new HtmlResource(Figure)
        : url.EndsWith("broken.png", StringComparison.Ordinal) ? new HtmlResource(Figure.AsMemory(0, 40))
        : null;

    private static Result<HtmlBoxes, Error> Boxes(string html, string? baseUrl = "file:///docs/t.html") =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Resources = Resources, BaseUrl = baseUrl }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Resources = Resources, BaseUrl = baseUrl }));

    private static Box Body(string html, string? baseUrl = "file:///docs/t.html")
    {
        var result = Boxes(html, baseUrl);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return (Box)result.Value.Document.Root.Children.Single();
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().Single();

    private static string? Property(Error error) => error.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Property, out var value) ? (string)value : null;

    [Fact]
    public void A_br_is_a_forced_line_break_in_its_own_text_style()
    {
        var p = (Box)Body(Doc("<p>a<span style=\"font-size:20pt\">b<br>c</span><br>d<span><br style=\"font-size:30pt\"></span></p>")).Children.Single();

        var runs = p.Children.OfType<TextRun>().ToList();
        runs.Select(run => run.Text).Should().Equal("a", string.Empty, "b", LineSeparator, "c", LineSeparator, "d", string.Empty, LineSeparator);
        runs.Where(run => run.Text == LineSeparator).Select(run => run.Style.FontSize.Points).Should().Equal(20, 12, 30);
        p.Layout.Should().Be(LayoutAlgorithmId.Inline);
    }

    [Fact]
    public void A_br_with_display_none_is_nothing_and_a_block_level_br_is_refused_at_its_start_tag()
    {
        var p = (Box)Body(Doc("<p>a<br style=\"display:none\">b<span><br style=\"display:none\"></span></p>")).Children.Single();
        p.Children.OfType<TextRun>().Select(run => run.Text).Should().Equal("a", "b", string.Empty);

        var refusal = Refusal(Doc("<p>a</p>\n<br style=\"display:block\">"));
        (refusal.Code, Property(refusal)).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "display"));
        refusal.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
    }

    [Fact]
    public void A_block_with_only_a_br_has_a_line_and_one_with_only_white_space_has_none()
    {
        var body = Body(Doc("<p> <br> </p><p> </p>"));

        ((Box)body.Children[0]).Layout.Should().Be(LayoutAlgorithmId.Inline, "Chromium gives it one line (case hb-empty-block)");
        ((Box)body.Children[1]).Layout.Should().Be(LayoutAlgorithmId.Flow);
    }

    [Fact]
    public void A_br_between_block_boxes_goes_into_an_anonymous_block_box()
    {
        var body = Body(Doc("<div>a<br><p>b</p><br></div>"));
        var div = (Box)body.Children.Single();

        div.Children.Should().HaveCount(3);
        ((Box)div.Children[2]).Children.OfType<TextRun>().Select(run => run.Text).Should().Equal(LineSeparator);
    }

    [Fact]
    public void Under_pre_line_each_line_feed_of_the_text_is_a_forced_line_break_and_the_spaces_still_collapse_in_layout()
    {
        var p = (Box)Body(Doc("<p style=\"white-space:pre-line\">a  b\n  c<span>d\ne</span></p><p>f\ng</p>")).Children[0];

        p.Children.OfType<TextRun>().Select(run => run.Text).Should().Equal("a  b\u2028  c", string.Empty, "d\u2028e");
        ((Box)Body(Doc("<p>f\ng</p>")).Children.Single()).Children.OfType<TextRun>().Single().Text.Should().Be("f\ng", "white-space: normal keeps the line feed for collapsing");
    }

    [Fact]
    public void Under_pre_line_a_line_feed_between_block_boxes_is_refused_and_white_space_without_one_is_dropped()
    {
        var refusal = Refusal(Doc("<div style=\"white-space:pre-line\"><p>a</p>\n<p>b</p></div>"));
        (refusal.Code, Property(refusal)).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "white-space-collapse"));

        ((Box)Body(Doc("<div style=\"white-space:pre-line\"><p>a</p> <p>b</p></div>")).Children.Single()).Children.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("white-space:pre", "white-space-collapse")]
    [InlineData("white-space:pre-wrap", "white-space-collapse")]
    [InlineData("white-space:nowrap", "text-wrap-mode")]
    [InlineData("text-wrap-mode:nowrap", "text-wrap-mode")]
    [InlineData("white-space-collapse:preserve", "white-space-collapse")]
    public void Preserved_spaces_and_text_that_does_not_wrap_are_refused_where_there_is_text(string style, string property) =>
        Boxes(Doc($"<p style=\"{style}\">a  b</p>")).Error.Parts().Select(Property).Should().Contain(property);

    [Fact]
    public void A_block_level_img_is_an_image_box_with_its_computed_size_and_its_alt_text_as_the_figure_s()
    {
        var body = Body(Doc("<img src=\"figure.png\" alt=\"chart\" width=\"35\" style=\"height:60pt;margin-top:3pt\"><img src=\"figure.png\" alt=\"\"><img src=\"figure.png\">"));

        var boxes = body.Children.Cast<Box>().ToList();
        boxes.Select(box => box.Layout).Should().AllBeEquivalentTo(LayoutAlgorithmId.Image);
        var image = (ImageNode)boxes[0].Children.Single();
        (image.Width?.Points, image.Height?.Points, image.Image.PixelWidth, image.Image.PixelHeight).Should().Be(((double?)26.25, (double?)60.0, 7, 10));
        boxes[0].Style.Margin.Top.Points.Should().Be(3);
        boxes[0].Role.Should().Be(SemanticRole.Figure("chart"));
        boxes[1].Role.Should().Be(SemanticRole.Decorative, "an empty alt says the image is decorative");
        boxes[2].Role.Should().Be(SemanticRole.Figure(null), "no alt: a figure without a description, which PDF/UA refuses later");
        ((ImageNode)boxes[1].Children.Single()).Width.Should().BeNull("auto: the natural size and ratio fill it in");
        boxes.SelectMany(box => new[] { box.Id.Value, box.Children.Single().Id.Value }).Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_image_file_comes_from_a_data_url_or_through_the_resolver_against_the_base_element()
    {
        var data = "data:image/png;base64," + Convert.ToBase64String(Figure);
        Body(Doc($"<img src=\"{data}\" alt=\"a\">"), baseUrl: null).Children.Should().ContainSingle();

        // The base element's href, not the document's URL, is the base of the img's URL (the resolver serves nothing
        // under "/wrong/").
        var img = Doc("<img src=\"figure.png\" alt=\"a\">");
        Body(img.Replace("<title>", "<base href=\"https://example.com/x/\"><title>", StringComparison.Ordinal), baseUrl: "file:///wrong/t.html")
            .Children.Should().ContainSingle();
        Boxes(img, baseUrl: "file:///wrong/t.html").IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("<img alt=\"a\">")]
    [InlineData("<img src=\"missing.png\" alt=\"a\">")]
    [InlineData("<img src=\"broken.png\" alt=\"a\">")]
    public void An_image_that_does_not_come_or_does_not_decode_is_refused_at_its_start_tag(string img)
    {
        var refusal = Refusal(Doc("<p>a</p>\n" + img));

        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Rendering);
        (refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((2, 1));
    }

    [Fact]
    public void An_img_inside_a_line_of_text_is_refused()
    {
        var refusal = Refusal(Doc("<p>a <img src=\"figure.png\" alt=\"a\" style=\"display:inline\"> b</p>"));

        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Rendering);
        refusal.Message.Should().Contain("inside a line");
    }

    [Theory]
    [InlineData("width:50%", "width")]
    [InlineData("aspect-ratio:2/1", "aspect-ratio")]
    [InlineData("max-height:10pt", "max-height")]
    [InlineData("overflow:hidden", null)]
    [InlineData("aspect-ratio:auto 2/1", null)]
    public void An_image_takes_lengths_its_natural_ratio_and_clipping_and_refuses_the_other_sizes(string style, string? refused)
    {
        var result = Boxes(Doc($"<img src=\"figure.png\" alt=\"a\" style=\"{style}\">"));

        (result.IsFailure ? Property(result.Error.Parts().Single()) : null).Should().Be(refused);
    }

    [Fact]
    public void A_forced_break_after_an_image_is_a_forced_break_before_the_next_box()
    {
        var body = Body(Doc("<p style=\"break-after:page\">a</p><img src=\"figure.png\" alt=\"a\" style=\"break-after:page\"><p>b</p>"));

        body.Children.Cast<Box>().Select(box => box.Style.BreakBefore).Should().Equal(BreakBetween.Auto, BreakBetween.Page, BreakBetween.Page);
    }

    [Fact]
    public void The_images_of_case_hi_attributes_are_where_and_as_large_as_Chromium_prints_them()
    {
        var boxes = HtmlCases.Boxes("hi-attributes");
        var pages = DocumentLayout.Run(boxes.Value.Document, boxes.Value.Fonts);
        var engine = PrintedPages.Engine(pages.Value).Pages.Single().Images;
        var chromium = PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", "hi-attributes.print.txt"))).Pages.Single().Images;

        engine.Should().HaveCount(2);
        foreach (var (ours, theirs) in engine.Zip(chromium))
        {
            new[] { ours.X0 - theirs.X0, ours.Y0 - theirs.Y0, ours.X1 - theirs.X1, ours.Y1 - theirs.Y1 }.Should().AllSatisfy(delta => Math.Abs(delta).Should().BeLessThanOrEqualTo(HtmlCases.Bound));
        }
    }
}
