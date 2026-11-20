using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
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

/// <summary>
/// The page-margin boxes of <c>@page</c> rules as box generation draws them (<see cref="HtmlBoxGenerator"/>): the header
/// and footer areas they make, where their rows start, page 1's <c>:first</c> boxes, their content and their refusals.
/// The page is the one of the measured cases <c>pm-*</c>: 400 × 300 pt, <c>margin: 60pt 50pt</c>, so the page area
/// starts at 50.25 pt (67 px) and the rows at 49.992 pt (66.65625 px).
/// </summary>
public class MarginBoxGenerationTests
{
    private const string Page = "size: 400pt 300pt; margin: 60pt 50pt; font-family: Lato; font-size: 10pt;";

    private static readonly HtmlRenderOptions Options = new()
    {
        Style = HtmlStyleOptions.Default with
        {
            Resources = url => url switch
            {
                "lato.ttf" => new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")),
                "badge.png" => new HtmlResource(Badge),
                "text.png" => new HtmlResource("not an image"u8.ToArray()),
                _ => null,
            },
        },
        SourceName = "t.html",
    };

    /// <summary>The image of the cases <c>pm-image-*</c>: 40 × 24 px of one colour.</summary>
    private static byte[] Badge => System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "chromium", "badge.png"));

    private static string Doc(string css, string body = "<p>one</p>") =>
        "<!doctype html><html lang=\"en\"><head><title>T</title><style>@font-face { font-family: Lato; src: url(lato.ttf) }\n"
        + $"html, body {{ margin: 0; font-family: Lato; font-size: 10pt }}\n{css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = Options.Style.Resources }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy, Resources = Options.Style.Resources }));

    private static PageStyle Model(string html)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Document.Page;
    }

    private static PrintedPages Printed(string html)
    {
        var pages = HtmlRendering.ToPages(html, Options);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        return PrintedPages.Engine(pages.Value.Output);
    }

    private static PrintedWord Word(PrintedPages pages, int page, string text) => pages.Pages[page - 1].Words.Single(word => word.Text == text);

    private static string Texts(PageArea? area) =>
        string.Join("|", ((Box)area!.Content).Children.OfType<Box>().Select(box => string.Concat(box.Children.OfType<TextRun>().Select(run => run.Text))));

    [Fact]
    public void A_generated_margin_box_makes_a_header_and_a_footer_as_high_as_the_page_margins_and_the_body_stays_where_it_was()
    {
        var page = Model(Doc($"@page {{ {Page} @top-left {{ content: \"Aa\" }} }}"));

        page.Margins.Top.Should().Be(Length.Zero);
        page.Margins.Bottom.Should().Be(Length.Zero);
        page.Template.Header!.Height.Should().Be(60.Pt(), "the top of Chromium's page area, 80 px");
        page.Template.Footer!.Height.Should().Be(60.Pt(), "the sheet below the page area, which ends at 320 px");
        ((Box)page.Template.Footer.Content).Children.Should().BeEmpty("a page with boxes at one edge only still has both areas");

        var printed = Printed(Doc($"@page {{ {Page} @top-left {{ content: \"Aa\" }} }}"));
        Word(printed, 1, "Aa").Baseline.Should().Be(33.75, "one 10pt line in the middle of the 60pt box (case pm-inherit)");
        Word(printed, 1, "one").Baseline.Should().Be(Word(Printed(Doc($"@page {{ {Page} }}")), 1, "one").Baseline, "the body does not move");
    }

    [Fact]
    public void The_rows_start_at_the_left_margin_cut_to_1_64_px_and_end_at_the_page_area_s_end()
    {
        var page = Model(Doc($"@page {{ {Page} @top-left {{ content: \"Aa\" }} @top-right {{ content: \"Bb\" }} }}"));
        page.Template.Header!.StartInMargin.Points.Should().BeApproximately(50.25 - (66.65625 * 0.75), 1e-9);
        page.Template.Footer!.StartInMargin.Should().Be(page.Template.Header.StartInMargin);

        var printed = Printed(Doc($"@page {{ {Page} @top-left {{ content: \"Aa\" }} @top-right {{ content: \"Bb\" }} }}"));
        Word(printed, 1, "Aa").X0.Should().BeApproximately(49.992, 0.001, "where Chromium starts the left box (cases pm-*)");
        Word(printed, 1, "Bb").X1.Should().BeApproximately(351.0, 0.01, "the right box ends at the page area's end, 468 px");
        Word(printed, 1, "one").X0.Should().Be(50.25, "the body starts at the page area");
    }

    [Fact]
    public void Page_1_takes_the_first_page_context_and_the_other_pages_the_rules_without_a_selector()
    {
        var html = Doc($"@page {{ {Page} @top-left {{ content: \"Every\" }} }} @page :first {{ @top-left {{ content: \"First\" }} }}",
            "<p>one</p><p style=\"break-before: page\">two</p>");

        var page = Model(html);
        Texts(page.FirstPage!.Header).Should().Be("First");
        Texts(page.Template.Header).Should().Be("Every");

        var printed = Printed(html);
        printed.PageTexts.Should().Equal("First one", "Every two");
    }

    [Fact]
    public void Without_a_generated_margin_box_the_page_is_as_its_rules_give_it()
    {
        var page = Model(Doc($"@page {{ {Page} @top-left {{ content: none }} }}"));

        page.Template.Should().Be(PageTemplate.Empty);
        page.FirstPage.Should().BeNull();
        page.Margins.Top.Should().Be(60.Pt());
    }

    [Fact]
    public void Each_margin_box_is_a_block_box_with_its_computed_style_and_its_content_as_text_and_page_counters()
    {
        var page = Model(Doc($"@page {{ {Page} @bottom-right {{ content: \"Page \" counter(page) \" of \" counter(pages); padding-top: 6pt; border-top: 3pt solid #0000ff;"
            + " background-color: #00ff00; vertical-align: top; text-align: left; color: #ff0000 } }"));

        var box = (Box)((Box)page.Template.Footer!.Content).Children.Single();
        box.Style.MarginBoxPlace.Should().Be(MarginBoxPlace.End);
        box.Style.Padding.Top.Should().Be(6.Pt());
        box.Style.Border.Top.Width.Should().Be(3.Pt());
        box.Style.Background.Should().Be(new Color(0, 255, 0));
        box.Style.VerticalAlign.Should().Be(VerticalAlign.Top);
        box.Style.TextAlign.Should().Be(TextAlign.Left);
        box.Style.Text!.Color.Should().Be(new Color(255, 0, 0));
        box.Children.Select(node => node is CounterRun counter ? counter.Counter.ToString() : ((TextRun)node).Text)
            .Should().Equal("Page ", nameof(PageCounter.PageNumber), " of ", nameof(PageCounter.PageCount));
        box.Source.Line.Should().Be(3);
        box.Source.Column.Should().BeGreaterThan(1, "the content declaration's column");
    }

    [Fact]
    public void The_page_count_in_a_margin_box_is_the_total_on_every_page()
    {
        var printed = Printed(Doc($"@page {{ {Page} @bottom-right {{ content: \"Page \" counter(page) \" of \" counter(pages) }} }}",
            "<p>one</p><p style=\"break-before: page\">two</p><p style=\"break-before: page\">three</p>"));

        printed.PageTexts.Should().Equal("one Page 1 of 3", "two Page 2 of 3", "three Page 3 of 3");
    }

    /// <summary>The images of the margin boxes of a page: each one's size in the model.</summary>
    private static List<ImageNode> Images(PageArea? area) =>
        ((Box)area!.Content).Children.OfType<Box>().SelectMany(box => box.Children.OfType<Box>()).SelectMany(box => box.Children.OfType<ImageNode>()).ToList();

    /// <summary>Measured (cases <c>pm-image-url</c>, <c>pm-image-set</c>): the 40 × 24 px image at its natural size
    /// divided by the resolution.</summary>
    [Theory]
    [InlineData("url(badge.png)", 30.0, 18.0)]
    [InlineData("image-set(url(badge.png) 2x)", 15.0, 9.0)]
    [InlineData("image-set(url(badge.png) 144dpi)", 20.0, 12.0)]
    [InlineData("image-set(\"badge.png\" 60dpcm)", 30 * 96 / (60 * 2.54), 18 * 96 / (60 * 2.54))]
    public void An_image_in_the_content_is_an_inline_image_of_its_natural_size_divided_by_its_resolution(string image, double width, double height)
    {
        var page = Model(Doc($"@page {{ {Page} @top-left {{ content: \"Aa\" {image} \"Bb\" }} }}"));

        var node = Images(page.Template.Header).Single();
        node.Width!.Value.Points.Should().BeApproximately(width, 1e-9);
        node.Height!.Value.Points.Should().BeApproximately(height, 1e-9);
        var box = ((Box)page.Template.Header!.Content).Children.OfType<Box>().Single();
        box.Children.Select(child => child.GetType().Name).Should().Equal(new[] { nameof(TextRun), nameof(Box), nameof(TextRun) }, "an atomic inline between the strings");
    }

    /// <summary>Measured (case <c>pm-image-middle</c>): the image on the baseline after "Aa", "Bb" after it, painted at
    /// whole pixels.</summary>
    [Fact]
    public void An_image_between_two_strings_is_drawn_where_Chromium_draws_it()
    {
        var html = Doc($"@page {{ {Page} @top-left {{ content: \"Aa\" image-set(url(badge.png) 1.5x) \"Bb\" }} }}", "<p>Ee</p>");

        var pages = HtmlRendering.ToPages(html, Options);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        var printed = PrintedPages.Engine(pages.Value.Output);
        var chromium = PrintedPages.Chromium("pm-image-middle").Pages[0].Images.Single();
        var image = printed.Pages[0].Images.Single();
        static double Pixel(double points) => Math.Round(points / 0.75, MidpointRounding.AwayFromZero) * 0.75;
        (Pixel(image.X0), Pixel(image.Y0), Pixel(image.X1), Pixel(image.Y1)).Should().Be((chromium.X0, chromium.Y0, chromium.X1, chromium.Y1), "Chromium paints it at whole pixels: 61.5, 23.25, 81.75, 35.25");
        Pixel(Word(printed, 1, "Aa").Baseline).Should().Be(35.25, "the image's bottom edge is on the baseline, the line in the middle of the box, at a whole pixel");
        Word(printed, 1, "Bb").X0.Should().BeApproximately(81.727, 0.75 / 64, "20pt after the end of 'Aa', within Chromium's layout unit");
    }

    /// <summary>Measured (case <c>pm-snap-pitch</c>: a line height of 20.5 px, two lines in each box, the alignment leaving
    /// the content's top a quarter, a half or three quarters of a pixel past a whole one): Chromium paints each baseline
    /// on the nearest whole pixel, half up, and leaves the lines where they are laid out. A top at 19.5 px gives baselines
    /// at 35 and 55 px (34.5 and 55.0 rounded), where a top put on 20 px would give 35 and 56. The engine lays the lines
    /// out unsnapped and paints the baseline where it is laid out (a documented limit,
    /// <see cref="FontMetricRounding.WholePixels"/>), so its baselines rounded to the pixel are Chromium's.</summary>
    [Fact]
    public void Each_baseline_of_a_margin_box_is_laid_out_unsnapped_and_rounds_to_the_pixel_Chromium_paints_it_on()
    {
        var boxes = HtmlCases.Boxes("pm-snap-pitch").Value;
        var engine = PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value).Pages[0].Words.Where(word => word.Text != "Body").ToList();
        var chromium = PrintedPages.Chromium("pm-snap-pitch").Pages[0].Words.Where(word => word.Text != "Body").ToList();

        engine.Should().HaveCount(12);
        foreach (var word in engine)
        {
            var measured = chromium.Single(other => other.Text == word.Text && Math.Abs(other.X0 - word.X0) < 0.1 && Math.Abs(other.Baseline - word.Baseline) < 0.75);
            Math.Round(word.Baseline / 0.75, MidpointRounding.AwayFromZero).Should().Be(measured.Baseline / 0.75, $"'{word.Text}' at x {word.X0} is laid out at {word.Baseline / 0.75} px");
        }

        var right = engine.Where(word => word.X0 > 300 && word.Baseline < 60).Select(word => word.Baseline / 0.75).ToList();
        right.Should().HaveCount(2);
        right[0].Should().BeApproximately(34.5, 1e-9, "the top-right box's content starts 19.5 px down and its first baseline is 15 px below that");
        right[1].Should().BeApproximately(55.0, 1e-9, "one line of 20.5 px further down");
    }

    /// <summary>Measured (case <c>pm-len-sum</c>: a margin and a padding above a line of "Aa" and an image, which sets the
    /// line's baseline at the image's height below its top): Chromium lays a margin box's margins and padding out in its
    /// unit of 1/64 px, as in the body (<c>pm-len-body</c>), and puts none of them on a whole pixel; it paints the line's
    /// top on the nearest whole pixel, half up, and the baseline at its distance from that top. 5pt + 5pt is 13.3125 px
    /// and the line's top is painted at 13 px, where whole-pixel lengths would give 7 + 7 = 14; 10.25px + 10.25px is
    /// painted at 21, where they would give 20. The engine lays the lengths out in the same unit.</summary>
    [Fact]
    public void A_margin_box_s_margin_and_padding_are_laid_out_in_Chromium_s_unit_and_only_the_line_s_top_is_painted_on_a_whole_pixel()
    {
        var boxes = HtmlCases.Boxes("pm-len-sum").Value;
        var engine = PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value).Pages[0].Words.Where(word => word.Text == "Aa").ToList();
        var chromium = PrintedPages.Chromium("pm-len-sum").Pages[0].Words.Where(word => word.Text == "Aa").ToList();

        engine.Should().HaveCount(6);
        var tops = new List<double>();
        foreach (var word in engine)
        {
            // The image is 24 px high, 24 / 0.7 px in the top row; its bottom is on the baseline.
            var image = word.Baseline < 150 ? 24 / 0.7 : 24;
            var top = (word.Baseline / 0.75) - image;
            tops.Add(top);
            var measured = chromium.Single(other => Math.Abs(other.X0 - word.X0) < 0.1 && Math.Abs(other.Baseline - word.Baseline) < 1.5);
            (Math.Round(top, MidpointRounding.AwayFromZero) + image).Should().BeApproximately(measured.Baseline / 0.75, 1.0 / 64, $"the line at x {word.X0} is laid out with its top at {top} px");
        }

        // 5pt is 6.65625 px and 10.4px is 10.390625 px in Chromium's unit; 10.25px is exact.
        foreach (var (ours, laidOut) in tops.Order().Zip(new[] { 13.3125, 20.5, 20.78125, 333.3125, 340.5, 340.78125 }))
        {
            ours.Should().BeApproximately(laidOut, 1e-9);
        }
    }

    /// <summary>Measured (case <c>pm-paint-order</c>, whose rules come in neither order): Chromium paints the top row from
    /// left to right and the bottom row from right to left, backgrounds and images alike.</summary>
    [Fact]
    public void The_boxes_of_a_row_are_painted_in_Chromium_s_order_whatever_the_order_of_their_rules()
    {
        var boxes = HtmlCases.Boxes("pm-paint-order").Value;
        var engine = PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value).Pages[0];
        var chromium = PrintedPages.Chromium("pm-paint-order").Pages[0];
        static double Pixel(double points) => Math.Round(points / 0.75, MidpointRounding.AwayFromZero) * 0.75;

        engine.Images.Select(image => (Pixel(image.X0), Pixel(image.Y0))).Should().Equal(chromium.Images.Select(image => (image.X0, image.Y0)));
        engine.Rectangles.Select(rectangle => (Pixel(rectangle.Y0), rectangle.Colour)).Should().Equal(chromium.Rectangles.Select(rectangle => (rectangle.Y0, rectangle.Colour)),
            "top row red, green, blue; bottom row blue, green, red");
    }

    /// <summary>CSS Values 4 §4.5.1: a URL in a style sheet is resolved against that sheet's URL, a URL in the document's
    /// own style element against the document's.</summary>
    [Fact]
    public void The_url_of_an_image_is_resolved_against_the_style_sheet_that_holds_the_content_declaration()
    {
        var asked = new List<string>();
        HtmlResource? Resolver(string url)
        {
            asked.Add(url);
            return url switch
            {
                "file:///doc/sheets/page.css" => new HtmlResource(System.Text.Encoding.UTF8.GetBytes("@page { @top-left { content: url(../images/badge.png) } }")),
                "file:///doc/images/badge.png" or "file:///doc/own/badge.png" => new HtmlResource(Badge),
                "file:///doc/lato.ttf" => new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")),
                _ => null,
            };
        }

        var html = "<!doctype html><html lang=\"en\"><head><title>T</title><link rel=\"stylesheet\" href=\"sheets/page.css\"><style>@font-face { font-family: Lato; src: url(lato.ttf) }"
                   + $" html, body {{ margin: 0; font-family: Lato; font-size: 10pt }} @page {{ {Page} @bottom-left {{ content: url(own/badge.png) }} }}</style></head><body><p>one</p></body></html>";
        var result = HtmlParser.Parse(html, new HtmlParseOptions("doc.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Resources = Resolver, BaseUrl = "file:///doc/doc.html" }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Resources = Resolver, BaseUrl = "file:///doc/doc.html" }));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        asked.Should().Contain(["file:///doc/images/badge.png", "file:///doc/own/badge.png"]);
        asked.Should().NotContain(["file:///doc/sheets/images/badge.png", "file:///images/badge.png"]);
        Images(result.Value.Document.Page.Template.Header).Should().ContainSingle();
        Images(result.Value.Document.Page.Template.Footer).Should().ContainSingle();
    }

    [Theory]
    [InlineData("url(nowhere.png)", "is not there")]
    [InlineData("image-set(url(text.png) 2x)", "does not decode")]
    public void An_image_that_does_not_come_or_does_not_decode_is_refused_at_the_content_declaration_and_left_out_under_warn(string image, string reason)
    {
        var html = Doc($"@page {{ {Page}\n@top-left {{ content: \"Logo\" {image} }} }}");

        var refusal = Boxes(html).Error.Parts().Single();
        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Rendering);
        refusal.Message.Should().Contain(reason);
        (refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((4, 13));
        refusal.Metadata[ErrorMetadataKeys.Property].Should().Be("content");

        var warned = Boxes(html, UnsupportedPolicy.Warn);
        warned.Value.Diagnostics.Should().ContainSingle(error => error.Code == HtmlStyleErrorCodes.Unsupported.Rendering);
        Texts(warned.Value.Document.Page.Template.Header).Should().Be("Logo");
    }

    [Fact]
    public void The_page_context_cascade_s_refusals_are_reported_as_they_are_in_both_modes()
    {
        var html = Doc($"@page {{ {Page} @top-left {{ content: \"Aa\"; text-transform: uppercase }} @left-top {{ content: \"x\" }} }}");

        Boxes(html).Error.Parts().Select(error => error.Code).Should().Equal(CssErrorCodes.Unsupported.PageRule, CssErrorCodes.Unsupported.PageRule);
        var warned = Boxes(html, UnsupportedPolicy.Warn);
        warned.Value.Diagnostics.Count(error => error.Code == CssErrorCodes.Unsupported.PageRule).Should().Be(2);
        Texts(warned.Value.Document.Page.Template.Header).Should().Be("Aa", "what is left is drawn");
    }

    [Fact]
    public void Content_wider_than_its_margin_box_is_refused_at_its_content_declaration()
    {
        var html = Doc($"@page {{ {Page}\n  @bottom-left {{ content: \"Left\" }}\n  @bottom-right {{ content: \"Averyveryverylongunbreakablewordthatisfarwiderthanthewholepagearea\" }} }}");

        var error = HtmlRendering.ToPages(html, Options).Error;
        error.Code.Should().Be(LayoutErrorCodes.MarginBoxOverflow);
        (error.Metadata![ErrorMetadataKeys.SourceLine], error.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((4, 18), "the left box's content declaration: it is pressed below its min-content width");
    }

    /// <summary>Measured (case <c>pm-inherit-family</c>): with no font in <c>@page</c> the margin boxes print in the
    /// root's family.</summary>
    [Fact]
    public void A_margin_box_draws_in_the_roots_family_when_page_names_none()
    {
        var printed = Printed(Doc("@page { size: 400pt 300pt; margin: 60pt 50pt; @top-left { content: \"Aa\" } }"));

        Word(printed, 1, "Aa").Face.Should().Be("Lato-Regular");
    }

    /// <summary>A margin box in a family the caller did not supply is refused as an element in it is: the same code and
    /// message, at the margin box's <c>content</c> declaration, once although page 1 draws the box of every page.</summary>
    [Fact]
    public void A_margin_box_in_a_family_the_caller_did_not_supply_is_refused_as_an_element_is()
    {
        var box = Boxes(Doc($"@page {{ {Page}\n@top-left {{ content: \"Aa\"; font-family: Nowhere }} }}")).Error.Parts().Single();
        var element = Boxes(Doc(string.Empty, "<p style=\"font-family: Nowhere\">one</p>")).Error.Parts().Single();

        box.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
        (box.Code, box.Message).Should().Be((element.Code, element.Message));
        (box.Metadata![ErrorMetadataKeys.SourceLine], box.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((4, 13));
    }

    /// <summary>Measured (cases <c>pm-start-25mm</c>, <c>pm-start-12pt</c>): beside a left margin that rounds down to a
    /// whole pixel Chromium starts the rows at the page area's start, not at the margin cut to 1/64 px, which lies after
    /// it.</summary>
    [Theory]
    [InlineData("margin: 60pt 50pt 60pt 12.2pt", 12.0)]
    [InlineData("margin: 25mm", 70.5)]
    public void Beside_a_left_margin_that_rounds_down_the_rows_start_at_the_page_area(string margin, double start)
    {
        var html = Doc($"@page {{ size: 400pt 300pt; {margin}; font-family: Lato; @top-left {{ content: \"Aa\" }} }}", "<p>Ee</p>");

        Model(html).Template.Header!.StartInMargin.Should().Be(Length.Zero);
        var printed = Printed(html);
        Word(printed, 1, "Aa").X0.Should().Be(start);
        Word(printed, 1, "Ee").X0.Should().Be(start, "the body starts there too");
    }

    [Fact]
    public void Margin_box_text_is_a_pagination_artifact_not_structure_content()
    {
        var html = Doc($"@page {{ {Page} @top-left {{ content: \"Heading\" }} @bottom-right {{ content: \"Footing\" }} }}");

        var page = HtmlRendering.ToPages(html, Options).Value.Output.Pages.Single();
        var kinds = new List<ArtifactKind?>();
        ArtifactKind? open = null;
        foreach (var item in page.Items)
        {
            if (item is ArtifactStart start)
            {
                open = start.Kind;
            }
            else if (item is ArtifactEnd)
            {
                open = null;
            }
            else if (item is GlyphRun)
            {
                kinds.Add(open);
            }
        }

        kinds.Should().BeEquivalentTo(new ArtifactKind?[] { ArtifactKind.PageHeader, ArtifactKind.PageFooter, null }, "one run in the header, one in the footer, the body's outside both");
        var pdf = HtmlRendering.ToPdf(html, Options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var outline = PdfStructureReadBack.Outline(PdfStructureReadBack.Read(pdf.Value.Output));
        outline.Should().Contain("one").And.NotContain("Heading").And.NotContain("Footing");
    }

    [Fact]
    public void An_image_in_a_margin_box_is_part_of_the_pagination_artifact_and_asks_no_alternative_text()
    {
        var html = Doc($"@page {{ {Page} @top-left {{ content: url(badge.png) \"Heading\" }} }}");

        var page = HtmlRendering.ToPages(html, Options).Value.Output.Pages.Single();
        ArtifactKind? open = null;
        var kinds = new List<ArtifactKind?>();
        foreach (var item in page.Items)
        {
            if (item is ArtifactStart start)
            {
                open = start.Kind;
            }
            else if (item is ArtifactEnd)
            {
                open = null;
            }
            else if (item is DrawImage)
            {
                kinds.Add(open);
            }
        }

        kinds.Should().Equal(ArtifactKind.PageHeader);
        var pdf = HtmlRendering.ToPdf(html, Options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        PdfStructureReadBack.Outline(PdfStructureReadBack.Read(pdf.Value.Output)).Should().NotContain("Figure");
    }
}
