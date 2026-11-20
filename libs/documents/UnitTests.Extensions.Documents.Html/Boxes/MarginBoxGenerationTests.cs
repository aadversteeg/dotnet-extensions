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
        Style = HtmlStyleOptions.Default with { Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null },
        SourceName = "t.html",
    };

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

    [Fact]
    public void An_image_in_the_content_is_refused_at_the_content_declaration_and_left_out_under_warn()
    {
        var html = Doc($"@page {{ {Page}\n@top-left {{ content: \"Logo\" url(logo.png) }} }}");

        var refusal = Boxes(html).Error.Parts().Single();
        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.PageRule);
        (refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((4, 13));

        var warned = Boxes(html, UnsupportedPolicy.Warn);
        warned.Value.Diagnostics.Should().ContainSingle(error => error.Code == HtmlStyleErrorCodes.Unsupported.PageRule);
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
    /// whole pixel Chromium starts the rows at the page area's start, not at the margin cut to 1/64 px; refused.</summary>
    [Theory]
    [InlineData("margin: 60pt 50pt 60pt 12.2pt")]
    [InlineData("margin: 25mm")]
    public void A_left_margin_that_rounds_down_to_a_whole_pixel_refuses_the_margin_boxes(string margin)
    {
        var error = Boxes(Doc($"@page {{ size: 400pt 300pt; {margin}; font-family: Lato; @top-left {{ content: \"Aa\" }} }}")).Error.Parts().Single();

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.PageRule);
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
}
