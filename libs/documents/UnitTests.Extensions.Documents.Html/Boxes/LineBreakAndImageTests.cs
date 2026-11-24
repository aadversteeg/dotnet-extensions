using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>br</c> (the model's forced line break), of <c>white-space</c> (how text collapses and
/// wraps) and of <c>img</c> (an image box, or an image in a line); the reference cases <c>hb-*</c>, <c>hw-*</c> and
/// <c>hi-*</c> hold Chromium's prints.</summary>
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

    /// <summary>The white-space shorthand and its longhands to the text style (CSS Text 4 §3, §3.1, §6.1), inherited
    /// into a span.</summary>
    [Theory]
    [InlineData("", WhiteSpaceCollapse.Collapse, TextWrapMode.Wrap)]
    [InlineData("white-space:normal", WhiteSpaceCollapse.Collapse, TextWrapMode.Wrap)]
    [InlineData("white-space:nowrap", WhiteSpaceCollapse.Collapse, TextWrapMode.NoWrap)]
    [InlineData("white-space:pre", WhiteSpaceCollapse.Preserve, TextWrapMode.NoWrap)]
    [InlineData("white-space:pre-wrap", WhiteSpaceCollapse.Preserve, TextWrapMode.Wrap)]
    [InlineData("white-space:pre-line", WhiteSpaceCollapse.PreserveBreaks, TextWrapMode.Wrap)]
    [InlineData("white-space:break-spaces", WhiteSpaceCollapse.BreakSpaces, TextWrapMode.Wrap)]
    [InlineData("white-space-collapse:collapse", WhiteSpaceCollapse.Collapse, TextWrapMode.Wrap)]
    [InlineData("white-space-collapse:preserve", WhiteSpaceCollapse.Preserve, TextWrapMode.Wrap)]
    [InlineData("white-space-collapse:preserve-breaks", WhiteSpaceCollapse.PreserveBreaks, TextWrapMode.Wrap)]
    [InlineData("white-space-collapse:break-spaces", WhiteSpaceCollapse.BreakSpaces, TextWrapMode.Wrap)]
    [InlineData("text-wrap-mode:wrap", WhiteSpaceCollapse.Collapse, TextWrapMode.Wrap)]
    [InlineData("text-wrap-mode:nowrap", WhiteSpaceCollapse.Collapse, TextWrapMode.NoWrap)]
    [InlineData("white-space:preserve nowrap", WhiteSpaceCollapse.Preserve, TextWrapMode.NoWrap)]
    [InlineData("white-space:collapse nowrap", WhiteSpaceCollapse.Collapse, TextWrapMode.NoWrap)]
    [InlineData("white-space:preserve-breaks wrap", WhiteSpaceCollapse.PreserveBreaks, TextWrapMode.Wrap)]
    [InlineData("white-space:nowrap break-spaces", WhiteSpaceCollapse.BreakSpaces, TextWrapMode.NoWrap)]
    public void White_space_maps_to_how_the_text_collapses_and_wraps(string style, WhiteSpaceCollapse collapse, TextWrapMode wrap)
    {
        var p = (Box)Body(Doc($"<p style=\"{style}\">a  b<span>c</span></p>")).Children.Single();

        p.Children.OfType<TextRun>().Should().AllSatisfy(run => (run.Style.WhiteSpaceCollapse, run.Style.TextWrapMode).Should().Be((collapse, wrap)));
        (p.Style.Text!.WhiteSpaceCollapse, p.Style.Text.TextWrapMode).Should().Be((collapse, wrap), "the strut's style is the block's");
    }

    [Theory]
    [InlineData("preserve-spaces")]
    [InlineData("discard")]
    public void The_white_space_collapse_values_that_are_not_built_are_refused_with_their_place(string value)
    {
        var refusal = Refusal(Doc($"<p>a</p>\n<p style=\"white-space-collapse:{value}\">a  b</p>"));

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.Value]).Should().Be((CssErrorCodes.Unsupported.Value, "white-space-collapse", value));
        refusal.Metadata[ErrorMetadataKeys.SourceLine].Should().Be(2);
    }

    [Fact]
    public void Text_goes_to_the_model_as_written_and_layout_collapses_it_by_its_mode()
    {
        var p = (Box)Body(Doc("<p style=\"white-space:pre-line\">a  b\n  c<span>d\ne</span></p>")).Children[0];

        p.Children.OfType<TextRun>().Select(run => run.Text).Should().Equal("a  b\n  c", string.Empty, "d\ne");
    }

    [Theory]
    [InlineData("white-space:pre", "preserve")]
    [InlineData("white-space:pre-wrap", "preserve")]
    [InlineData("white-space:break-spaces", "break-spaces")]
    [InlineData("white-space-collapse:preserve", "preserve")]
    public void A_tab_where_spaces_are_preserved_is_refused_at_its_text(string style, string value)
    {
        var refusal = Refusal(Doc($"<p>a</p>\n<p style=\"{style}\">ab\tc</p>"));

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.Value], refusal.Metadata[ErrorMetadataKeys.Offset]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "white-space-collapse", value, 2));
        (refusal.Metadata[ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((2, 13 + style.Length), "the start of the text");
    }

    [Theory]
    [InlineData("white-space:pre-line")]
    [InlineData("white-space:nowrap")]
    [InlineData("")]
    public void A_tab_where_spaces_collapse_is_a_space(string style) =>
        ((Box)Body(Doc($"<p style=\"{style}\">a\tb</p>")).Children.Single()).Children.OfType<TextRun>().Single().Text.Should().Be("a\tb");

    [Theory]
    [InlineData("white-space:pre-line", "\n", "preserve-breaks")]
    [InlineData("white-space:pre", " ", "preserve")]
    [InlineData("white-space:pre-wrap", "\n", "preserve")]
    [InlineData("white-space:break-spaces", " ", "break-spaces")]
    public void White_space_between_block_boxes_that_its_mode_preserves_is_refused(string style, string between, string value)
    {
        var refusal = Refusal(Doc($"<div style=\"{style}\"><p>a</p>{between}<p>b</p></div>"));

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "white-space-collapse", value));
    }

    [Theory]
    [InlineData("white-space:pre-line", " ")]
    [InlineData("white-space:nowrap", "\n")]
    [InlineData("", "\n")]
    public void White_space_between_block_boxes_that_collapses_away_is_dropped(string style, string between) =>
        ((Box)Body(Doc($"<div style=\"{style}\"><p>a</p>{between}<p>b</p></div>")).Children.Single()).Children.Should().HaveCount(2);

    [Theory]
    [InlineData("white-space:pre", " ", true)]
    [InlineData("white-space:break-spaces", " ", true)]
    [InlineData("white-space:pre-line", "\n", true)]
    [InlineData("white-space:pre-line", " ", false)]
    [InlineData("white-space:nowrap", " \n", false)]
    public void A_block_of_white_space_only_has_lines_where_its_mode_preserves_any_of_it(string style, string text, bool lines) =>
        ((Box)Body(Doc($"<p style=\"{style}\">{text}</p><p>z</p>")).Children[0]).Layout.Should().Be(lines ? LayoutAlgorithmId.Inline : LayoutAlgorithmId.Flow);

    [Fact]
    public void A_block_level_img_is_an_image_box_with_its_computed_size_and_its_alt_text_as_the_figure_s()
    {
        var body = Body(Doc("<img src=\"figure.png\" alt=\"chart\" width=\"35\" style=\"height:37.5pt;margin-top:3pt\"><img src=\"figure.png\" alt=\"\"><img src=\"figure.png\">"));

        var boxes = body.Children.Cast<Box>().ToList();
        boxes.Select(box => box.Layout).Should().AllBeEquivalentTo(LayoutAlgorithmId.Image);
        var image = (ImageNode)boxes[0].Children.Single();
        (image.Width?.Points, image.Height?.Points, image.Image.PixelWidth, image.Image.PixelHeight).Should().Be(((double?)26.25, (double?)37.5, 7, 10));
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
    public void An_img_inside_a_line_of_text_is_an_image_box_among_the_runs_with_the_text_style_of_the_element_it_lies_in()
    {
        var p = (Box)Body(Doc("<p>a <img src=\"figure.png\" alt=\"F\" width=\"14\" style=\"display:inline;margin:1pt 2pt;padding:3pt;border:1pt solid red;background-color:blue\"> b" +
            "<span style=\"white-space:nowrap\"> c<img src=\"figure.png\" alt=\"\" style=\"display:inline\"></span></p>")).Children.Single();

        p.Layout.Should().Be(LayoutAlgorithmId.Inline);
        p.Children.Select(node => node is TextRun run ? run.Text : "[img]").Should().Equal("a ", "[img]", " b", string.Empty, " c", "[img]");
        var images = p.Children.OfType<Box>().ToList();
        images.Should().AllSatisfy(box => box.Layout.Should().Be(LayoutAlgorithmId.Image));
        (images[0].Role, images[1].Role).Should().Be((SemanticRole.Figure("F"), SemanticRole.Decorative));

        var style = images[0].Style;
        (style.Margin.Top.Points, style.Margin.Right.Points, style.Padding.Left.Points, style.Border.Top.Width.Points).Should().Be((85 * 0.75 / 64, 170 * 0.75 / 64, 3, 0.75),
            "margins and padding are in Chromium's layout unit of 1/64 px (1 pt is 85.33 units, laid out as 85), a border width is floored to whole pixels");
        (style.Border.Top.Color, style.Background).Should().Be((new Color(255, 0, 0), new Color(0, 0, 255)));
        ((ImageNode)images[0].Children.Single()).Width.Should().Be(Length.FromPoints(10.5), "the width attribute is a hint for width, 14 px");

        style.Text.Should().Be(p.Style.Text, "the image lies in the paragraph");
        images[1].Style.Text!.TextWrapMode.Should().Be(TextWrapMode.NoWrap, "the image lies in the span that does not wrap");
        p.Style.Text!.TextWrapMode.Should().Be(TextWrapMode.Wrap);
    }

    [Theory]
    [InlineData("vertical-align:middle", "vertical-align", "middle", false)]
    [InlineData("vertical-align:top", "vertical-align", "top", false)]
    [InlineData("display:inline-block", "display", "inline-block", false)]
    [InlineData("break-before:page", "break-before", "page", false)]
    [InlineData("break-after:avoid", "break-after", "avoid", false)]
    [InlineData("break-inside:avoid", "break-inside", "avoid", false)]
    public void An_img_inside_a_line_of_text_refuses_what_is_not_built_with_its_value_and_start_tag(string style, string property, string value, bool byRegistry)
    {
        var refusal = Refusal(Doc($"<p>a\n<img src=\"figure.png\" alt=\"a\" style=\"display:inline;{style}\"> b</p>"));

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.Value]).Should().Be((byRegistry ? CssErrorCodes.Unsupported.Value : HtmlStyleErrorCodes.Unsupported.Value, property, value));
        refusal.Metadata[ErrorMetadataKeys.SourceLine].Should().Be(2);
    }

    [Fact]
    public void An_img_inside_a_line_of_text_with_the_initial_break_and_alignment_values_is_drawn() =>
        ((Box)Body(Doc("<p>a <img src=\"figure.png\" alt=\"a\" style=\"display:inline;vertical-align:baseline;break-before:auto;break-after:auto;break-inside:auto\"> b</p>")).Children.Single())
            .Children.OfType<Box>().Single().Style.BreakBefore.Should().Be(BreakBetween.Auto);

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
    [Theory]
    [InlineData("<img src=\"figure.png\" style=\"display:block;width:40pt;height:10pt\" alt=\"f\">", "40pt 10pt")]
    [InlineData("<img src=\"figure.png\" width=\"20\" height=\"60\" style=\"display:block\" alt=\"f\">", "15pt 45pt")]
    [InlineData("<p>a <img src=\"figure.png\" style=\"width:40pt;height:10pt\" alt=\"f\"></p>", "40pt 10pt")]
    public void An_img_given_a_width_and_a_height_in_another_ratio_than_its_own_is_refused_with_both_at_its_start_tag(string body, string sizes)
    {
        var refusal = Refusal(Doc("<p>a</p>\n" + body));

        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "width", sizes));
        refusal.Metadata[ErrorMetadataKeys.SourceLine].Should().Be(2);
    }
    [Theory]
    [InlineData("width:14pt;height:20pt")]
    [InlineData("width:8.4pt;height:12pt")]
    [InlineData("width:14pt")]
    [InlineData("height:20pt")]
    public void An_img_given_sizes_in_its_own_ratio_or_one_size_is_drawn(string style)
    {
        var image = (Box)Body(Doc($"<img src=\"figure.png\" style=\"display:block;{style}\" alt=\"f\">")).Children.Single();
        image.Layout.Should().Be(LayoutAlgorithmId.Image);
    }
}
