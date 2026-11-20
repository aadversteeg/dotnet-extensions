using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using UnitTests.Extensions.Documents.Html.Boxes;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// The page contexts of a document and their page-margin boxes (CSS Paged Media 3 §5.3): the cascade of the
/// <c>@page</c> rules for every page and for page 1, the inheritance Chromium 159 prints, the value of <c>content</c>,
/// and each refusal at its line and column.
/// </summary>
public class PageContextTests
{
    private const string Doctype = "<!DOCTYPE html>";

    private static string Html(string css, string root = "") => Doctype + "<html><head><style>\n" + css + "\n</style></head><body " + root + ">x</body></html>";

    private static PageContexts Pages(string css) => Styled(Html(css)).PageContexts;

    private static MarginBoxStyle Box(PageContextStyle context, PageMarginBox box) => context.MarginBoxes.Single(item => item.Box == box);

    private static string Value(MarginBoxStyle box, string property) => ComputedText.Of(property, box.Style);

    /// <summary>The line and column (1-based) of the first <paramref name="text"/> in a document.</summary>
    private static (int Line, int Column) At(string document, string text, int occurrence = 0)
    {
        var index = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            index = document.IndexOf(text, index + 1, StringComparison.Ordinal);
        }

        index.Should().BeGreaterThanOrEqualTo(0);
        var before = document[..index];
        return (before.Count(c => c == '\n') + 1, index - (before.LastIndexOf('\n') + 1) + 1);
    }

    private static (int Line, int Column) Place(Error error) =>
        ((int)error.Metadata![ErrorMetadataKeys.SourceLine]!, (int)error.Metadata![ErrorMetadataKeys.SourceColumn]!);

    /// <summary>The one refusal of a style sheet: its code, place and message.</summary>
    private static Error Refusal(string css)
    {
        var refusals = Pages(css).Diagnostics.Where(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal).ToList();
        refusals.Should().ContainSingle();
        return refusals[0];
    }

    private static void RefusedAt(string css, string at, string reason, int occurrence = 0)
    {
        var refusal = Refusal(css);
        refusal.Code.Should().Be(CssErrorCodes.Unsupported.PageRule);
        Place(refusal).Should().Be(At(Html(css), at, occurrence));
        refusal.Message.Should().Contain(reason);
    }

    // ---- inheritance (measured: Chromium 159 prints) ----

    /// <summary>Measured: <c>html { color: green; font-size: 20pt; font-weight: bold }</c> and a <c>@top-left</c> with a
    /// string and no style of its own print the string green, 20pt, bold.</summary>
    [Fact]
    public void The_roots_colour_size_and_weight_reach_the_margin_boxes()
    {
        var styled = Styled(Doctype + "<html style=\"color: green; font-size: 20pt; font-weight: bold\"><head><style>@page { font-family: Lato; @top-left { content: \"Text\" } }</style></head><body>x</body></html>");

        var box = Box(styled.PageContexts.EveryPage, PageMarginBox.TopLeft);
        Value(box, "color").Should().Be("rgb(0, 128, 0)");
        Value(box, "font-size").Should().Be("26.6667px");
        Value(box, "font-weight").Should().Be("700");
        styled.PageContexts.Diagnostics.Should().BeEmpty();
    }

    /// <summary>Measured (case <c>pm-inherit-family</c>): <c>html { font-family: Lato }</c> and no font in <c>@page</c>
    /// print the margin boxes in Lato, and <c>body { color: red; font-size: 8pt; font-weight: bold }</c> does not reach
    /// them.</summary>
    [Fact]
    public void The_roots_family_reaches_the_margin_boxes_and_the_body_does_not()
    {
        var styled = Styled(Doctype + "<html style=\"font-family: Lato\"><head><style>@page { font-size: 10pt; @top-left { content: \"Text\" } }</style></head><body style=\"color: red; font-size: 8pt; font-weight: bold; font-family: Other\">x</body></html>");

        var context = styled.PageContexts.EveryPage;
        ComputedText.Of("font-family", context.Style).Should().Be("Lato");
        var box = Box(context, PageMarginBox.TopLeft);
        Value(box, "font-family").Should().Be("Lato");
        Value(box, "color").Should().Be("rgb(0, 0, 0)");
        Value(box, "font-size").Should().Be("13.3333px");
        Value(box, "font-weight").Should().Be("400");
        styled.PageContexts.Diagnostics.Should().BeEmpty();
    }

    /// <summary>Measured: <c>font-size: 2em</c> in a margin rule under <c>@page { font-size: 10pt }</c> prints 20pt, while
    /// <c>html</c> is 20pt; a percentage is the page context's size too.</summary>
    [Fact]
    public void Em_and_a_percentage_font_size_are_the_page_contexts_size()
    {
        var styled = Styled(Doctype + "<html style=\"font-size: 20pt\"><head><style>@page { font-family: Lato; font-size: 10pt; @top-left { content: \"a\"; font-size: 2em } @top-right { content: \"b\"; font-size: 150% } @bottom-left { content: \"c\"; line-height: 2 } }</style></head><body>x</body></html>");

        var context = styled.PageContexts.EveryPage;
        ComputedText.Of("font-size", context.Style).Should().Be("13.3333px");
        Value(Box(context, PageMarginBox.TopLeft), "font-size").Should().Be("26.6667px");
        Value(Box(context, PageMarginBox.TopRight), "font-size").Should().Be("20px");
        Value(Box(context, PageMarginBox.BottomLeft), "line-height").Should().Be("2");
    }

    [Fact]
    public void The_page_context_inherits_the_roots_size_for_its_own_em()
    {
        var styled = Styled(Doctype + "<html style=\"font-size: 20pt\"><head><style>@page { font-family: Lato; font-size: 0.5em; @top-left { content: \"a\" } }</style></head><body>x</body></html>");

        Value(Box(styled.PageContexts.EveryPage, PageMarginBox.TopLeft), "font-size").Should().Be("13.3333px");
    }

    // ---- the cascade of the page rules ----

    /// <summary>Measured: <c>@page :first</c> applies on page 1 only, over the rule without a selector; of two
    /// <c>:first</c> rules the later wins per margin box.</summary>
    [Fact]
    public void First_cascades_over_the_plain_rules_on_page_1_only()
    {
        var pages = Pages("@page { font-family: Lato; @top-right { content: \"every\"; margin-top: 1cm; color: blue } }\n"
            + "@page :first { @top-right { content: \"\" } }\n@page :first { @top-right { color: red } }\n@page { @top-right { color: green } }");

        var every = Box(pages.EveryPage, PageMarginBox.TopRight);
        every.Content.Items.Should().Equal(new CssContentString("every"));
        Value(every, "color").Should().Be("rgb(0, 128, 0)");
        var first = Box(pages.FirstPage, PageMarginBox.TopRight);
        first.Content.Items.Should().Equal(new CssContentString(""));
        first.Content.Generates.Should().BeTrue();
        Value(first, "margin-top").Should().Be("37.7953px");
        Value(first, "color").Should().Be("rgb(255, 0, 0)");
        pages.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void An_important_declaration_wins_over_a_later_and_more_specific_one()
    {
        var pages = Pages("@page { font-family: Lato; @top-left { content: \"a\" !important; color: blue !important } }\n@page :first { @top-left { content: \"b\"; color: red } }");

        var first = Box(pages.FirstPage, PageMarginBox.TopLeft);
        first.Content.Items.Should().Equal(new CssContentString("a"));
        Value(first, "color").Should().Be("rgb(0, 0, 255)");
    }

    /// <summary>Measured: <c>content: ""</c> generates the box; <c>none</c>, <c>normal</c> and no <c>content</c> generate
    /// none.</summary>
    [Fact]
    public void Only_a_content_list_generates_a_margin_box()
    {
        var pages = Pages("@page { font-family: Lato; @top-left { content: none } @top-center { content: normal } @top-right { color: red } @bottom-left { content: \"\" } @bottom-right { content: \"x\" } }\n"
            + "@page :first { @bottom-right { content: none } }");

        pages.EveryPage.MarginBoxes.Select(box => box.Box).Should().Equal(PageMarginBox.BottomLeft, PageMarginBox.BottomRight);
        pages.FirstPage.MarginBoxes.Select(box => box.Box).Should().Equal(PageMarginBox.BottomLeft);
        pages.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_wide_keyword_in_content_is_normal()
    {
        Pages("@page { font-family: Lato; @top-left { content: initial } @top-right { content: inherit } }").EveryPage.MarginBoxes.Should().BeEmpty();
    }

    /// <summary>Measured: with no alignment of their own the left, centre and right boxes align their text left, centred
    /// and right, and the top and bottom rows centre their content vertically.</summary>
    [Fact]
    public void A_margin_box_aligns_by_its_place_unless_its_rule_says_otherwise()
    {
        var pages = Pages("@page { font-family: Lato; @top-left { content: \"a\" } @top-center { content: \"b\" } @bottom-right { content: \"c\" } @bottom-left { content: \"d\"; text-align: right; vertical-align: top } }");

        var boxes = pages.EveryPage.MarginBoxes;
        boxes.Select(box => Value(box, "text-align")).Should().Equal("left", "center", "right", "right");
        boxes.Select(box => Value(box, "vertical-align")).Should().Equal("middle", "middle", "top", "middle");
    }

    [Fact]
    public void The_margin_box_declarations_are_computed_as_an_elements_are()
    {
        var pages = Pages("@page { font-family: Lato; @top-left { content: \"x\"; margin: 1cm 0 0.4cm; border-bottom: 3pt solid #ffa500; padding-top: 6pt; white-space: pre; background-color: #eee } }");

        var box = Box(pages.EveryPage, PageMarginBox.TopLeft);
        Value(box, "margin-top").Should().Be("37.7953px");
        Value(box, "margin-bottom").Should().Be("15.1181px");
        Value(box, "border-bottom-width").Should().Be("4px");
        Value(box, "border-bottom-style").Should().Be("solid");
        Value(box, "border-bottom-color").Should().Be("rgb(255, 165, 0)");
        Value(box, "padding-top").Should().Be("8px");
        Value(box, "white-space-collapse").Should().Be("preserve");
        Value(box, "background-color").Should().Be("rgb(238, 238, 238)");
        pages.Diagnostics.Should().BeEmpty();
    }

    // ---- the value of content ----

    [Fact]
    public void Content_reads_strings_with_their_escapes_and_the_page_counters()
    {
        var pages = Pages("@page { font-family: Lato; @bottom-right { content: \"Pagina \" counter(page) \" van \" counter(pages, decimal) \"\\A b\\2003 c\" } }");

        Box(pages.EveryPage, PageMarginBox.BottomRight).Content.Items.Should().Equal(
            new CssContentString("Pagina "), new CssContentCounter(PageCounterName.Page), new CssContentString(" van "),
            new CssContentCounter(PageCounterName.Pages), new CssContentString("\nb\u2003c"));
    }

    [Fact]
    public void Content_reads_an_image_set_of_one_url_and_a_bare_url_with_the_style_sheets_base()
    {
        var styled = Styled(Html("@page { font-family: Lato; @top-left { content: image-set(url(logo.png) 160dpcm) \"\\2003Title\" } @top-right { content: url(\"b.png\") } @bottom-left { content: image-set(\"c.png\" 2x) } @bottom-right { content: image-set(url(d.png) 192dpi) } }"), Warn with { BaseUrl = "file:///docs/a.html" });

        var every = styled.PageContexts.EveryPage;
        var logo = (CssContentImage)Box(every, PageMarginBox.TopLeft).Content.Items[0];
        logo.Url.Should().Be("logo.png");
        logo.Resolution.Should().BeApproximately(160 * 2.54 / 96, 1e-12);
        Box(every, PageMarginBox.TopLeft).Content.Items[1].Should().Be(new CssContentString("\u2003Title"));
        Box(every, PageMarginBox.TopLeft).BaseUrl.Should().Be("file:///docs/a.html");
        Box(every, PageMarginBox.TopRight).Content.Items.Should().Equal(new CssContentImage("b.png", 1));
        Box(every, PageMarginBox.BottomLeft).Content.Items.Should().Equal(new CssContentImage("c.png", 2));
        Box(every, PageMarginBox.BottomRight).Content.Items.Should().Equal(new CssContentImage("d.png", 2));
        styled.PageContexts.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Content_on_an_element_is_refused_as_before()
    {
        var styled = Styled(Doctype + "<p style=\"content: 'x'\">x</p>");

        styled.Diagnostics.Should().Contain(error => error.Code == CssErrorCodes.Unsupported.Value);
    }

    [Theory]
    [InlineData("attr(title)", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("counter(chapter)", "only counter(page) and counter(pages), in decimal, are")]
    [InlineData("counter(page, upper-roman)", "only counter(page) and counter(pages), in decimal, are")]
    [InlineData("counter(Page)", "only counter(page) and counter(pages), in decimal, are")]
    [InlineData("counters(page, \".\")", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("open-quote", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("\"a\" leader(dotted)", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("string(title)", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("target-counter(url(#a), page)", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("element(header)", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("linear-gradient(red, blue)", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("image-set(url(a.png) 1x, url(b.png) 2x)", "image-set() takes one url() and its resolution here")]
    [InlineData("image-set(url(a.png))", "image-set() takes one url() and its resolution here")]
    [InlineData("image-set(url(a.png) 0x)", "image-set() takes one url() and its resolution here")]
    [InlineData("image-set(url(a.png) type(\"image/png\") 1x)", "image-set() takes one url() and its resolution here")]
    [InlineData("\"a\" / \"alt\"", "only strings, counter(page), counter(pages), url() and image-set() are")]
    [InlineData("revert", "write the value")]
    public void A_content_item_the_engine_does_not_draw_is_refused_at_its_declaration(string value, string reason)
    {
        var css = $"@page {{ font-family: Lato; @top-left {{ content: \"kept\"; content: {value} }} }}";

        RefusedAt(css, "content", reason, occurrence: 1);
        Box(Pages(css).EveryPage, PageMarginBox.TopLeft).Content.Items.Should().Equal(new CssContentString("kept"));
    }

    [Fact]
    public void A_refused_content_declaration_is_reported_once_for_both_page_contexts()
    {
        Pages("@page { font-family: Lato; @top-left { content: attr(x) } }\n@page :first { }").Diagnostics.Should().ContainSingle();
    }

    // ---- refusals, each at its line and column ----

    [Theory]
    [InlineData("@page :left { @top-left { content: \"a\" } }")]
    [InlineData("@page :right { color: red }")]
    [InlineData("@page :blank { }")]
    [InlineData("@page cover { }")]
    [InlineData("@page cover:first { }")]
    [InlineData("@page :first:left { }")]
    [InlineData("@page :first, :left { }")]
    public void A_page_selector_other_than_none_and_first_is_refused_at_the_rule(string css)
    {
        RefusedAt("@page { font-family: Lato }\n" + css, "@page", "Only @page and @page :first are drawn", occurrence: 1);
    }

    [Theory]
    [InlineData("top-left-corner")]
    [InlineData("top-right-corner")]
    [InlineData("bottom-left-corner")]
    [InlineData("bottom-right-corner")]
    [InlineData("left-top")]
    [InlineData("left-middle")]
    [InlineData("left-bottom")]
    [InlineData("right-top")]
    [InlineData("right-middle")]
    [InlineData("right-bottom")]
    public void A_margin_box_outside_the_top_and_bottom_rows_is_refused_at_its_rule(string name)
    {
        var css = $"@page {{ font-family: Lato; @{name} {{ content: \"a\" }} }}";

        RefusedAt(css, "@" + name, "only the top and bottom rows");
        Pages(css).EveryPage.MarginBoxes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("width: 3cm", "the row sets the boxes' widths and heights")]
    [InlineData("height: 1cm", "the row sets the boxes' widths and heights")]
    [InlineData("min-width: 1cm", "the row sets the boxes' widths and heights")]
    [InlineData("max-height: 1cm", "the row sets the boxes' widths and heights")]
    [InlineData("background-image: url(a.png)", "only background-color is")]
    [InlineData("background: red", "a margin box takes content, font")]
    [InlineData("background-size: 2cm", "only background-color is")]
    [InlineData("text-transform: uppercase", "a margin box takes content, font")]
    [InlineData("display: block", "a margin box takes content, font")]
    [InlineData("counter-increment: page", "a margin box takes content, font")]
    [InlineData("vertical-align: baseline", "it takes top, middle or bottom")]
    [InlineData("vertical-align: inherit", "it takes top, middle or bottom")]
    [InlineData("padding-left: 10%", "A percentage in 'padding-left'")]
    [InlineData("margin: 1cm 5%", "A percentage in 'margin'")]
    [InlineData("--x: 1pt", "Custom properties and var()")]
    [InlineData("color: var(--c, red)", "Custom properties and var()")]
    [InlineData("font-size: 1.5rem", "'rem' in 'font-size' is the page context's font size")]
    [InlineData("margin-top: calc(1rem + 2pt)", "'rem' in 'margin-top'")]
    public void A_declaration_a_margin_box_does_not_draw_is_refused_at_it(string declaration, string reason)
    {
        var name = declaration[..declaration.IndexOf(':')];
        RefusedAt($"@page {{ font-family: Lato; @top-left {{ content: \"a\"; {declaration} }} }}", name + ":", reason);
    }

    [Theory]
    [InlineData("counter-reset: page 3", "page counters start at 1 and count every page")]
    [InlineData("counter-increment: page", "page counters start at 1 and count every page")]
    [InlineData("counter-set: page 2", "page counters start at 1 and count every page")]
    [InlineData("text-align: right", "set it in the margin rule")]
    [InlineData("text-transform: uppercase", "reaches the margin boxes, which do not draw it")]
    [InlineData("font-size: 2rem", "'rem' in 'font-size'")]
    [InlineData("color: var(--c)", "Custom properties and var()")]
    public void A_declaration_of_page_that_reaches_the_margin_boxes_and_is_not_drawn_is_refused_at_it(string declaration, string reason)
    {
        var name = declaration[..declaration.IndexOf(':')];
        RefusedAt($"@page {{ font-family: Lato; {declaration}; @top-left {{ content: \"a\" }} }}", name + ":", reason);
    }

    /// <summary>The page box's declarations and those that reach no margin box are left to box generation, which reads
    /// the page box (and refuses what it does not draw, as before).</summary>
    [Fact]
    public void The_page_boxs_declarations_are_not_checked_here()
    {
        Pages("@page { size: A4; margin: 4cm 1cm 2cm; margin-top: 2cm; background-color: red; --x: 1; display: block; font-family: Lato }").Diagnostics.Should().BeEmpty();
    }

    /// <summary>Without <c>font-family</c> in the root, <c>@page</c> or the margin rule, a margin box has the initial
    /// family, as an element without one: the cascade does not refuse it (box generation matches its face as an
    /// element's).</summary>
    [Fact]
    public void A_generated_margin_box_without_a_font_family_has_the_initial_family()
    {
        var pages = Pages("@page { @top-left { content: \"a\" } }");

        pages.Diagnostics.Should().BeEmpty();
        Value(Box(pages.EveryPage, PageMarginBox.TopLeft), "font-family").Should().Be(ComputedText.Of("font-family", Ave.Extensions.Documents.Css.Cascade.ComputedStyle.Initial));
    }

    /// <summary>A value is read by the property's own parser, as on an element, with the same refusal.</summary>
    [Fact]
    public void A_value_the_element_parser_refuses_is_refused_on_a_margin_box_too()
    {
        var css = "@page { font-family: Lato; @top-left { content: \"a\"; border-top: 1pt dashed red } }";

        var refusal = Refusal(css);
        refusal.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        Place(refusal).Should().Be(At(Html(css), "border-top"));
    }

    [Fact]
    public void The_page_context_diagnostics_do_not_refuse_the_styled_document()
    {
        var html = Html("@page { font-family: Lato; @top-left { content: attr(x); width: 2cm } }");

        Resolve(html, Warn with { Unsupported = UnsupportedPolicy.Refuse }).IsSuccess.Should().BeTrue();
        Styled(html).Diagnostics.Should().NotContain(error => error.Code == CssErrorCodes.Unsupported.PageRule);
    }

    [Fact]
    public void A_document_without_page_rules_has_no_page_contexts()
    {
        Styled(Doctype + "<p>x</p>").PageContexts.Should().BeSameAs(PageContexts.None);
    }

    /// <summary>Every name of the margin box's property list is a longhand or a shorthand of the registry.</summary>
    [Fact]
    public void The_margin_box_property_list_names_registered_properties()
    {
        PageContextCascade.MarginBoxLonghands.Should().OnlyContain(name => Ave.Extensions.Documents.Css.Properties.PropertyRegistry.Find(name) != null);
        PageContextCascade.MarginBoxShorthands.Should().OnlyContain(name => Ave.Extensions.Documents.Css.Properties.PropertyRegistry.FindShorthand(name) != null);
    }

    // ---- the documents ----

    /// <summary>The timesheets' page rules give no refusal in the page-context cascade: page 1 has the empty
    /// <c>@top-right</c> of <c>:first</c>, every page five boxes; the logo is an image item.</summary>
    [Theory]
    [InlineData("timesheet-normal")]
    [InlineData("timesheet-one-page")]
    [InlineData("timesheet-pages")]
    public void The_timesheets_page_rules_cascade_without_a_refusal(string name)
    {
        var path = Path.Combine(HtmlCases.Documents.Html, name + ".html");
        var parsed = HtmlParser.Parse(File.ReadAllText(path), new HtmlParseOptions(name + ".html")).Match(result => result, error => throw new InvalidOperationException(error.ToString()));
        var styled = HtmlStyleResolver.Resolve(parsed, Warn with { BaseUrl = new Uri(path).AbsoluteUri }).Match(result => result, error => throw new InvalidOperationException(error.ToString()));

        var pages = styled.PageContexts;
        pages.Diagnostics.Should().BeEmpty();
        var every = pages.EveryPage.MarginBoxes;
        every.Select(box => box.Box).Should().Equal(PageMarginBox.TopLeft, PageMarginBox.TopRight, PageMarginBox.BottomLeft, PageMarginBox.BottomCenter, PageMarginBox.BottomRight);
        every[0].Content.Items[0].Should().BeOfType<CssContentImage>().Which.Resolution.Should().BeApproximately(160 * 2.54 / 96, 1e-12);
        every[0].Content.Items[1].Should().Be(new CssContentString("\u2003Urenstaat"));
        every.Select(box => Value(box, "font-family")).Distinct().Should().ContainSingle().Which.Should().Contain("Lato");
        Value(every[1], "white-space-collapse").Should().Be("preserve");
        Value(every[4], "vertical-align").Should().Be("top");
        every[4].Content.Items.Should().Contain(new CssContentCounter(PageCounterName.Pages));
        var first = pages.FirstPage.MarginBoxes;
        first.Select(box => box.Box).Should().Equal(every.Select(box => box.Box));
        first[1].Content.Items.Should().Equal(new CssContentString(""));
        Value(first[1], "border-bottom-width").Should().Be("4px");
    }
}
