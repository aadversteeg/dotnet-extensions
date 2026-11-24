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
/// The text of <c>::before</c> and <c>::after</c> through style resolution and box generation: where its runs go in the
/// element's inline content (cases <c>gc-string-block</c>, <c>gc-string-children</c>, <c>gc-list-item</c>,
/// <c>gc-cell</c>), its style (<c>gc-string-style</c>, <c>gc-string-inline</c>), <c>attr()</c> (<c>gc-attr</c>), what
/// draws nothing and is not reported (<c>gc-nothing</c>), and what is refused, at its line and column.
/// </summary>
public class GeneratedContentTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>Generated</title><style>body {{ font-family: Lato; margin: 0 }}\n{css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static Box Body(string html)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Diagnostics.Should().BeEmpty();
        return (Box)result.Value.Document.Root.Children.Single();
    }

    /// <summary>The text runs of the first box with inline content, in order, each as its text with a star when it is
    /// generated; empty runs (an inline element's line height) left out.</summary>
    private static string Runs(string body, string css = "") => string.Join("|", FirstLines(Body(Doc(body, css)), skipLabels: true)
        .Children.OfType<TextRun>().Where(run => run.Text.Length > 0).Select(run => run.Generated ? "*" + run.Text : run.Text));

    /// <summary>The first box of lines in pre-order, a list item's label left out when asked.</summary>
    private static Box FirstLines(Box root, bool skipLabels = false)
    {
        var pending = new Stack<Box>([root]);
        while (pending.Count > 0)
        {
            var box = pending.Pop();
            if (skipLabels && box.Role is SemanticRole.ListLabelRole)
            {
                continue;
            }

            if (box.Layout == LayoutAlgorithmId.Inline)
            {
                return box;
            }

            foreach (var child in box.Children.OfType<Box>().Reverse())
            {
                pending.Push(child);
            }
        }

        throw new InvalidOperationException("no lines");
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().First();

    [Theory]
    // gc-string-block, gc-string-inline, gc-string-children: first and last inline content of the element, inside it.
    [InlineData("<p>text after</p>", "p::before { content: \"Note: \" }", "*Note: |text after")]
    [InlineData("<p>inline <span>span</span> tail</p>", "span::after { content: \" (end)\" }", "inline |span|* (end)| tail")]
    [InlineData("<p><b>bold</b> mid <i>it</i></p>", "p::before { content: \"[\" } p::after { content: \"]\" }", "*[|bold| mid |it|*]")]
    // The one-colon forms (Selectors 4 §16) and several strings joined.
    [InlineData("<p>x</p>", "p:before { content: \"a\" \"b\" } p:after { content: \"c\" }", "*ab|x|*c")]
    // gc-attr: an attribute's value, the empty string for a missing one; the name of an HTML element's attribute in any case.
    [InlineData("<p data-x=\"seven\">days</p>", "p::before { content: attr(DATA-X) \" \" }", "*seven |days")]
    [InlineData("<p>missing</p>", "p::before { content: \"[\" attr(data-missing) \"]\" }", "*[]|missing")]
    // gc-list-item, gc-cell: inside the item's body after the marker, inside the cell.
    [InlineData("<ol><li>item</li></ol>", "li::before { content: \"LI-\" }", "*LI-|item")]
    [InlineData("<table><tr><td>one</td></tr></table>", "table { border-collapse: collapse } td::before { content: \"Cell: \" }", "*Cell: |one")]
    // An empty element still generates its text (Chromium 159, measured: an empty div with ::before draws it).
    [InlineData("<div></div>", "div::before { content: \"EMPTY\" }", "*EMPTY")]
    // The cascade: specificity counts the pseudo-element, the later rule of the same specificity wins, !important wins.
    [InlineData("<p class=\"c\">x</p>", ".c::before { content: \"class\" } p::before { content: \"type\" }", "*class|x")]
    [InlineData("<p>x</p>", "p::before { content: \"first\" } p::before { content: \"second\" }", "*second|x")]
    [InlineData("<p class=\"c\">x</p>", "p::before { content: \"type\" !important } .c::before { content: \"class\" }", "*type|x")]
    public void Generated_text_is_the_first_or_last_inline_content_of_its_element(string body, string css, string runs) =>
        Runs(body, css).Should().Be(runs);

    [Fact]
    public void A_pseudo_element_rule_never_applies_to_the_element_itself()
    {
        var lines = FirstLines(Body(Doc("<p>own text</p>", "p::before { content: \"G\"; color: #c00000; font-weight: 700; } p::after { color: #00c000 }")));
        var runs = lines.Children.OfType<TextRun>().Where(run => run.Text.Length > 0).ToList();

        runs.Select(run => (run.Text, run.Style.Color, run.Style.FontWeight.Value)).Should().Equal(
            ("G", new Color(0xc0, 0, 0), 700), ("own text", new Color(0, 0, 0), 400));
    }

    [Fact]
    public void Generated_text_inherits_from_its_element_and_takes_its_own_declarations()
    {
        // gc-string-style: bold, upright, in the element's colour, over an italic element.
        var lines = FirstLines(Body(Doc("<p>Before <span class=\"d\">board</span></p>",
            ".d { font-style: italic; color: #1b5e20 } .d::before { content: \"Decision: \"; font-weight: 700; font-style: normal }")));
        var generated = lines.Children.OfType<TextRun>().Single(run => run.Generated);

        (generated.Text, generated.Style.FontWeight.Value, generated.Style.FontStyle, generated.Style.Color).Should().Be(
            ("Decision: ", 700, Ave.Extensions.Documents.Core.FontStyle.Normal, new Color(0x1b, 0x5e, 0x20)));
        lines.Children.OfType<TextRun>().Single(run => run.Text == "board").Style.FontStyle.Should().Be(Ave.Extensions.Documents.Core.FontStyle.Italic);
    }

    [Fact]
    public void Generated_text_carries_the_place_of_its_content_declaration()
    {
        var html = Doc("<p>x</p>", "p::before {\n  content: \"a\" }");
        var run = FirstLines(Body(html)).Children.OfType<TextRun>().Single(run => run.Generated);

        // The declaration is on the style element's third line, line 3 of the document.
        run.Source.Should().Be(new SourceLocation("t.html", 3));
    }

    [Fact]
    public void Text_decoration_applies_as_an_inline_element_s()
    {
        var lines = FirstLines(Body(Doc("<p>x</p>", "p { text-decoration: underline } p::before { content: \"g\"; text-decoration: line-through }")));
        var generated = lines.Children.OfType<TextRun>().Single(run => run.Generated);

        generated.Style.Decorations.Select(line => line.Line).Should().Equal(TextDecorationLine.Underline, TextDecorationLine.LineThrough);
    }

    [Theory]
    // gc-nothing: no content, none, normal, a CSS-wide keyword, display none; img and br; box properties with them.
    [InlineData("<p>a</p>", "*, *::before, *::after { box-sizing: border-box }")]
    [InlineData("<p>a</p>", "p::before { content: none; padding: 6pt; background: red } p::after { content: normal; display: block }")]
    [InlineData("<p>a</p>", "p::before { content: \"x\"; display: none; margin: 6pt }")]
    [InlineData("<p>a</p>", "p::before { content: \"x\" } p::before { content: initial } p::after { content: \"y\"; all: unset }")]
    [InlineData("<p>a<br>b <img src=\"data:,x\" alt=\"\" style=\"display: none\"></p>", "br::after { content: \"BR\"; position: absolute } img::before { content: \"IMG\" }")]
    [InlineData("<p>a</p>", "p::before { padding: 6pt; float: left; position: absolute; display: block }")]
    public void What_Chromium_does_not_draw_is_left_out_without_a_word(string body, string css)
    {
        var runs = FirstLines(Body(Doc(body, css))).Children.OfType<TextRun>();

        runs.Should().NotContain(run => run.Generated);
    }

    [Theory]
    [InlineData("p::before { content: counter(x) }", "counter() and counters() come in a later version")]
    [InlineData("p::before { content: counters(x, \".\") }", "counter() and counters() come in a later version")]
    [InlineData("p::after { content: \" \" counter(page) }", "page numbers exist only in page-margin boxes; a browser prints 0 here")]
    [InlineData("p::after { content: counter(pages) }", "page numbers exist only in page-margin boxes; a browser prints 0 here")]
    [InlineData("p::before { content: open-quote }", "write the quotation marks as strings")]
    [InlineData("p::before { content: no-close-quote }", "write the quotation marks as strings")]
    [InlineData("p::before { content: url(a.png) }", "put an img in the HTML")]
    [InlineData("p::before { content: linear-gradient(red, blue) }", "put an img in the HTML")]
    [InlineData("p::before { content: attr(data-x, \"none\") }", "write attr(name) alone")]
    [InlineData("p::before { content: attr(data-x type(<number>)) }", "write attr(name) alone")]
    [InlineData("p::before { content: \"✓\" / \"\" }", "the alternative text after '/' is not drawn yet")]
    [InlineData("p::before { content: \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" \"a\" }", "it has 17 items, and at most 16 are drawn")]
    [InlineData("p::before { content: var(--x) }", "write the value")]
    [InlineData("p::before { content: revert }", "write the value")]
    [InlineData("p::before { content: \"x\";\n  display: block }", "a block pseudo-element comes in a later version")]
    [InlineData("p::before { content: \"x\";\n  padding-left: 6pt }", "it needs inline boxes")]
    [InlineData("p::before { content: \"x\";\n  background: #ccc }", "it needs inline boxes")]
    [InlineData("p::before { content: \"x\";\n  border: 1px solid }", "it needs inline boxes")]
    [InlineData("p::before { content: \"x\";\n  margin-right: 12pt }", "it needs inline boxes")]
    public void Generated_content_the_engine_does_not_draw_is_refused_at_its_declaration(string css, string message)
    {
        var refusal = Refusal(Doc("<p data-x=\"1\">a</p>", css));

        refusal.Code.Should().Be(CssErrorCodes.Unsupported.GeneratedContent);
        refusal.ToString().Should().Contain(message);
        // The declaration is on the style element's second line (content) or third line (the box property).
        refusal.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(css.Contains('\n', StringComparison.Ordinal) ? 3 : 2);
        refusal.Metadata.ContainsKey(ErrorMetadataKeys.SourceColumn).Should().BeTrue();
    }

    [Theory]
    // Values the registry refuses for every element are refused as for an element, at the declaration.
    [InlineData("p::before { content: \"x\";\n  display: inline-block }", "display")]
    [InlineData("p::before { content: \"x\";\n  position: absolute }", "position")]
    [InlineData("p::before { content: \"x\";\n  float: left }", "float")]
    public void Values_refused_for_every_element_are_refused_on_a_pseudo_element_too(string css, string property)
    {
        var refusal = Refusal(Doc("<p>a</p>", css));

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.Property]).Should().Be((CssErrorCodes.Unsupported.Value, 3, property));
    }

    [Theory]
    [InlineData("<table><tr><td>a</td></tr></table>", "tr::before { content: \"TR\" }", "an anonymous table cell")]
    [InlineData("<table><tr><td>a</td></tr></table>", "table::after { content: \"T\" }", "an anonymous table cell")]
    [InlineData("<ul><li>a</li></ul>", "ul::before { content: \"U\" }", "put the text in an li")]
    [InlineData("<ol><li>a</li></ol>", "ol::after { content: \"O\" }", "put the text in an li")]
    [InlineData("<p>a</p><hr>", "hr::before { content: \"H\" }", "is not measured")]
    public void Generated_text_on_an_element_that_cannot_hold_it_is_refused_at_its_declaration(string body, string css, string message)
    {
        var refusal = Refusal(Doc(body, css));

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((CssErrorCodes.Unsupported.GeneratedContent, 2));
        refusal.ToString().Should().Contain(message);
    }

    [Theory]
    [InlineData("li::marker { color: red }", "a list marker is drawn from list-style-type alone")]
    [InlineData("p::first-line { color: red }", "put the first line's text in a span")]
    [InlineData("p:first-letter { color: red }", "put the first letter in a span")]
    public void The_other_tree_abiding_pseudo_elements_stay_refused_with_what_to_write(string css, string message)
    {
        var refusal = Refusal(Doc("<p>a</p>", css));

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((CssErrorCodes.Unsupported.Selector, 2));
        refusal.ToString().Should().Contain(message);
    }

    [Theory]
    [InlineData("<h2>Chapter</h2>", "h2 { string-set: chapter content() }", "Documents.Css.Unsupported.Property")]
    [InlineData("<div>Header</div>", "div { position: running(header) }", "Documents.Css.Unsupported.Value")]
    public void Running_headers_stay_refused_and_say_that_a_browser_does_not_print_them_either(string body, string css, string code)
    {
        var refusal = Refusal(Doc(body, css));

        (refusal.Code.ToString(), refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((code, 2));
        refusal.ToString().Should().Contain("running headers are not printed by a browser either; write the header text in the margin box's content");
    }

    [Fact]
    public void Generated_text_past_its_limit_is_refused_not_cut()
    {
        var value = new string('a', 4097);
        var refusal = Refusal(Doc($"<p data-x=\"{value}\">a</p>", "p::before { content: attr(data-x) }"));

        refusal.Code.Should().Be(CssErrorCodes.Unsupported.GeneratedContent);
        refusal.ToString().Should().Contain("more than 4096 characters");
        Runs("<p data-x=\"" + new string('a', 4096) + "\">a</p>", "p::before { content: attr(data-x) }").Should().StartWith("*aaaa");
    }

    [Fact]
    public void Generated_text_of_a_hidden_element_is_not_made()
    {
        Runs("<p>a<span style=\"display: none\">b</span></p>", "span::before { content: \"x\"; position: absolute }").Should().Be("a");
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_tagged_PDF_with_generated_text_in_a_heading_and_a_paragraph_passes_veraPDF(string flavour)
    {
        var html = "<!doctype html><html lang=\"en\"><head><title>Generated</title><style>@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Regular.ttf) } @font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Bold.ttf); font-weight: 700 } body { font-family: Lato }"
            + " h1::before { content: \"Part \" } p::before { content: \"Note: \" } p::after { content: \" (\" attr(data-x) \")\" }</style></head>"
            + "<body><h1>One</h1><p data-x=\"seven\">A paragraph with generated text.</p></body></html>";
        var options = new HtmlRenderOptions { Style = HtmlStyleOptions.Default with { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = new Uri(Path.GetFullPath(Path.Combine(HtmlCases.Folder(), "gc.html"))).AbsoluteUri } };
        var profile = flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1;
        var pdf = HtmlRendering.ToPdf(html, options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = profile });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-generated-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.pdf"), [.. pdf.Value.Output]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
