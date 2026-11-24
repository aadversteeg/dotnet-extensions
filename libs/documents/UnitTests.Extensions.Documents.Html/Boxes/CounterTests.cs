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
/// Counters in the text of <c>::before</c> and <c>::after</c>: the order of reset, increment and set on one element
/// (case <c>gc-counter-order</c>), counters made without a reset (<c>gc-counter-implicit</c>), which siblings see a
/// counter (<c>gc-counter-scope</c>), nesting with <c>counters()</c> (<c>gc-counters-nested</c>), hidden elements
/// (<c>gc-counter-hidden</c>), <c>counter-set</c> (<c>gc-counter-set</c>), the styles (<c>gc-counter-styles</c>), the
/// <c>list-item</c> counter (<c>gc-counter-list-item</c>), the numbered headings of a report
/// (<c>gc-counter-headings</c>), and what is refused, at its line and column.
/// </summary>
public class CounterTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>Counters</title><style>body {{ font-family: Lato; margin: 0 }}\n{css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse, ResourceResolver? resources = null) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = resources ?? (_ => null), BaseUrl = "https://example.org/" }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy }));

    /// <summary>The generated texts of the document in order, joined by "|": the runs of one pseudo-element's items, which
    /// follow each other, as one text.</summary>
    private static string Generated(string body, string css)
    {
        var result = Boxes(Doc(body, css));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Diagnostics.Should().BeEmpty();
        return GeneratedTexts(result.Value.Document.Root);
    }

    private static string GeneratedTexts(Node root)
    {
        var texts = new List<string>();
        var joining = false;
        foreach (var run in Runs(root))
        {
            if (run.Generated && joining)
            {
                texts[^1] += run.Text;
            }
            else if (run.Generated)
            {
                texts.Add(run.Text);
            }

            joining = run.Generated;
        }

        return string.Join("|", texts);
    }

    /// <summary>The text runs of a box tree in document order.</summary>
    private static IEnumerable<TextRun> Runs(Node root)
    {
        var pending = new Stack<Node>([root]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is TextRun run)
            {
                yield return run;
            }
            else if (node is Box box)
            {
                foreach (var child in box.Children.Reverse())
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().First();

    [Theory]
    // gc-counter-order: reset, then increment, then set on one element, then the pseudo-element reads it.
    [InlineData("<div>a</div>", "div { counter-reset: n 5; counter-increment: n 2 } div::before { content: counter(n) }", "7")]
    [InlineData("<div>a</div>", "div { counter-set: a 10; counter-increment: a 1; counter-reset: a 1 } div::before { content: counter(a) }", "10")]
    [InlineData("<div>a</div>", "div { counter-reset: x 1 y 2 z; counter-increment: x y 5 z } div::before { content: counter(x) \",\" counter(y) \",\" counter(z) }", "2,7,1")]
    [InlineData("<div>a</div>", "div { counter-reset: d 2 q 1 q 5; counter-increment: d 3 d 4 } div::before { content: counter(d) \",\" counter(q) }", "9,5")]
    [InlineData("<div>a</div>", "div { counter-reset: c; counter-increment: c } div::before { content: counter(c) }", "1")]
    [InlineData("<p>a</p><p class=\"no\">b</p><p>c</p>", "body { counter-reset: h } p { counter-increment: h } p.no { counter-increment: none } p::before { content: counter(h) }", "1|1|2")]
    // The pseudo-element's own counter properties come before its content and count for the elements after it.
    [InlineData("<p>a</p><p class=\"s\">b</p><p>c</p>", "body { counter-reset: h } p { counter-increment: h } p::before { content: counter(h) } p.s::before { counter-increment: h 10 }", "1|12|13")]
    public void An_element_resets_then_increments_then_sets_and_its_pseudo_element_reads_after_its_own(string body, string css, string generated) =>
        Generated(body, css).Should().Be(generated);

    [Theory]
    // gc-counter-implicit: an increment without a counter makes one at 0 on the element, seen by its following siblings.
    [InlineData("<div><h4>a</h4></div><h4>b</h4><div><h4>c</h4></div>", "h4 { counter-increment: un } h4::after { content: counter(un) }", "1|1|2")]
    [InlineData("<div><div><p>a</p></div></div><p>b</p>", "p { counter-increment: w } p::after { content: counter(w) }", "1|1")]
    // Reading a counter that does not exist makes one at 0 on the pseudo-element: the next sibling does not see it.
    [InlineData("<p>a</p><p class=\"i\">b</p><p>c</p>", "p::after { content: counter(q) } p.i { counter-increment: q }", "0|1|1")]
    [InlineData("<p class=\"m\">a</p><p>b</p>", "p.m::after { counter-increment: v } p::after { content: counter(v) }", "1|0")]
    // gc-counter-scope: a sibling sees a counter only when the parent has none of its name.
    [InlineData("<div class=\"f\"><p>a</p><div><p>b</p></div><p>c</p></div>", ".f { counter-reset: n } p { counter-increment: n } .f div { counter-reset: n 10 } p::before { content: counters(n, \".\") }", "1|1.11|2")]
    [InlineData("<div class=\"g\"><p class=\"r5\">a</p><p class=\"r7\">b</p><p class=\"i\">c</p></div>", ".g { counter-reset: m } .r5 { counter-reset: m 5 } .r7 { counter-reset: m 7 } .i { counter-increment: m } p::before { content: counters(m, \".\") }", "0.5|0.7|1")]
    [InlineData("<div class=\"i\">a</div><div class=\"j\">b</div>", ".i { counter-reset: z 40 } .j::before { content: counter(z) }", "40")]
    [InlineData("<div><p>a</p></div><p>b</p>", "p { counter-increment: o } div { counter-increment: o } p::before { content: counters(o, \".\") }", "2|3")]
    public void A_counter_is_seen_by_the_element_s_descendants_and_by_siblings_whose_parent_has_none_of_its_name(string body, string css, string generated) =>
        Generated(body, css).Should().Be(generated);

    [Theory]
    // gc-counters-nested: counters() joins every level, outermost first; ::after after a nested list sees the item's level.
    [InlineData("<ol><li>A<ol><li>B</li><li>C<ol><li>D</li></ol></li></ol></li><li>E</li></ol>", "ol { list-style: none; counter-reset: item } li { counter-increment: item } li::before { content: counters(item, \".\") }", "1|1.1|1.2|1.2.1|2")]
    [InlineData("<ol><li>A<ol><li>B</li></ol></li></ol>", "ol { list-style: none; counter-reset: item } li { counter-increment: item } li::after { content: counters(item, \"/\", upper-roman) }", "I/I|I")]
    // gc-counter-list-item: the list's numbers, read; a nested list's are not seen after it.
    [InlineData("<ol start=\"3\"><li>a</li><li>b<ol><li>c</li></ol></li></ol>", "li::before { content: counters(list-item, \".\") } li::after { content: counter(list-item, upper-roman) }", "3|III|4|4.1|I|IV")]
    public void Counters_joins_the_levels_and_list_item_reads_the_list_s_numbers(string body, string css, string generated) =>
        Generated(body, css).Should().Be(generated);

    [Theory]
    // gc-counter-hidden: display: none and its subtree count nothing, also a pseudo-element of display: none.
    [InlineData("<p>a</p><p class=\"g\">b</p><div class=\"g\"><p>c</p></div><p>d</p>", "body { counter-reset: h } p { counter-increment: h } .g { display: none } p::before { content: counter(h) }", "1|2")]
    [InlineData("<p class=\"q\">a</p><p>b</p>", "body { counter-reset: h } p { counter-increment: h } p::before { content: counter(h) } p.q::before { content: \"x\"; display: none; counter-increment: h 100 }", "2")]
    public void Hidden_elements_do_not_count(string body, string css, string generated) =>
        Generated(body, css).Should().Be(generated);

    [Theory]
    // gc-counter-styles: the 12 styles of list-style-type; roman and alphabetic fall back to decimal outside their range.
    [InlineData("disc", "•|•|•|•")]
    [InlineData("circle", "◦|◦|◦|◦")]
    [InlineData("square", "■|■|■|■")]
    [InlineData("none", "|||")]
    [InlineData("decimal", "-1|0|1|2")]
    [InlineData("decimal-leading-zero", "-1|00|01|02")]
    [InlineData("lower-roman", "-1|0|i|ii")]
    [InlineData("upper-roman", "-1|0|I|II")]
    [InlineData("lower-alpha", "-1|0|a|b")]
    [InlineData("upper-latin", "-1|0|A|B")]
    public void A_counter_is_drawn_in_the_style_it_names(string style, string generated) =>
        string.Join("|", Generated("<p>a</p><p>b</p><p>c</p><p>d</p>", $"body {{ counter-reset: s -2 }} p {{ counter-increment: s }} p::before {{ content: \"[\" counter(s, {style}) \"]\" }}").Split('|').Select(text => text.Trim('[', ']')))
            .Should().Be(generated);

    [Fact]
    public void The_numbered_headings_of_a_report_print_on_two_levels()
    {
        // gc-counter-headings.
        var css = "body { counter-reset: section } h2 { counter-increment: section; counter-reset: sub } h3 { counter-increment: sub }"
            + " h2::before { content: counter(section) \". \" } h3::before { content: counter(section) \".\" counter(sub) \" \" }";

        Generated("<h2>A</h2><h3>a</h3><h3>b</h3><h2>B</h2><h3>c</h3>", css).Should().Be("1. |1.1 |1.2 |2. |2.1 ");
    }

    [Theory]
    [InlineData("div { counter-reset: list-item 4 }", "'counter-reset' of list-item is not drawn yet")]
    [InlineData("div { counter-increment: list-item 2 }", "'counter-increment' of list-item is not drawn yet")]
    [InlineData("div { counter-set: list-item 1 }", "'counter-set' of list-item is not drawn yet")]
    [InlineData("div { counter-reset: n 2147483648 }", "beyond a 32-bit integer")]
    [InlineData("div { counter-increment: n -2147483649 }", "beyond a 32-bit integer")]
    [InlineData("div { counter-reset: a b c d e f g h i j k l m n o p q }", "more than 16 counters")]
    [InlineData("div { counter-reset: reversed(n) }", "a counter that counts down is not")]
    public void Counter_properties_the_engine_does_not_count_are_refused_at_their_declaration(string css, string message)
    {
        var refusal = Refusal(Doc("<div>a</div>", css));

        refusal.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        refusal.ToString().Should().Contain(message);
        refusal.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
    }

    [Theory]
    [InlineData("p::before { content: counter(n, thai) }", "@counter-style is not")]
    [InlineData("p::before { content: counter(n, symbols(cyclic \"*\")) }", "symbols() is not drawn yet")]
    [InlineData("p::before { content: counters(n, \".\", \"-\") }", "a string as the counter style is not drawn yet")]
    [InlineData("p::before { content: counters(n) }", "it is not a valid counters()")]
    [InlineData("p::before { content: counter(n, decimal, x) }", "it is not a valid counter()")]
    [InlineData("p::before { content: counter(page) }", "page numbers exist only in page-margin boxes")]
    public void Counter_reads_the_engine_does_not_draw_are_refused_at_their_declaration(string css, string message)
    {
        var refusal = Refusal(Doc("<p>a</p>", css));

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((CssErrorCodes.Unsupported.GeneratedContent, 2));
        refusal.ToString().Should().Contain(message);
    }

    [Fact]
    public void A_counter_taken_past_32_bits_is_refused_at_the_declaration_and_kept_at_the_bound()
    {
        // gc-counter-bound: Chromium prints 2147483647 twice.
        var html = Doc("<div><p>a</p><p>b</p></div>", "div { counter-reset: n 2147483647 }\np {\n  counter-increment: n }\np::before { content: counter(n) }");
        var refusal = Refusal(html);

        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn], refusal.Metadata[ErrorMetadataKeys.Property])
            .Should().Be((HtmlStyleErrorCodes.Unsupported.Counter, 4, 3, "counter-increment"));
        refusal.ToString().Should().Contain("beyond a 32-bit integer");
        var warned = Boxes(html, UnsupportedPolicy.Warn);
        GeneratedTexts(warned.Value.Document.Root).Should().Be("2147483647|2147483647");
    }

    [Fact]
    public void Counters_joins_at_most_32_levels()
    {
        string Nested(int levels) => string.Concat(Enumerable.Repeat("<div>", levels)) + "x" + string.Concat(Enumerable.Repeat("</div>", levels));
        const string css = "div { counter-reset: n } div::before { content: counters(n, \".\") }";

        Boxes(Doc(Nested(32), css)).IsSuccess.Should().BeTrue();
        var refusal = Refusal(Doc(Nested(33), css));
        (refusal.Code, refusal.Metadata![ErrorMetadataKeys.SourceLine]).Should().Be((HtmlStyleErrorCodes.Unsupported.Counter, 2));
        refusal.ToString().Should().Contain("joins 33 levels, and at most 32 are drawn");
    }

    [Fact]
    public void More_than_a_thousand_live_counters_fail_under_every_policy()
    {
        var names = string.Join(" ", Enumerable.Range(0, 16).Select(i => "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var css = $"div {{ counter-reset: {names} }}";
        string Nested(int levels) => string.Concat(Enumerable.Repeat("<div>", levels)) + "x" + string.Concat(Enumerable.Repeat("</div>", levels));

        Boxes(Doc(Nested(62), css)).IsSuccess.Should().BeTrue("62 levels of 16 counters are 992");
        foreach (var policy in new[] { UnsupportedPolicy.Refuse, UnsupportedPolicy.Warn })
        {
            var result = Boxes(Doc(Nested(63), css), policy);
            result.IsFailure.Should().BeTrue();
            result.Error.Parts().Should().Contain(error => error.Code == HtmlStyleErrorCodes.Unsupported.Counter && error.Message.Contains("At most 1000 counters", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_refusal_inside_generated_text_names_the_style_sheet_of_its_content_declaration()
    {
        var sheet = "p::before {\n  content: \"a\\1 b\" }\nspan::after { content: counter(n); font-family: Nowhere }";
        HtmlResource? Resolve(string url) => url == "https://example.org/s.css" ? new HtmlResource(System.Text.Encoding.UTF8.GetBytes(sheet)) : null;
        var html = "<!doctype html><html lang=\"en\"><head><title>t</title><link rel=\"stylesheet\" href=\"s.css\"><style>body { font-family: Lato }</style></head><body><p>x <span>y</span></p></body></html>";

        var refusals = Boxes(html, UnsupportedPolicy.Refuse, Resolve).Error.Parts().ToList();

        var character = refusals.Single(error => error.Code == HtmlStyleErrorCodes.Unsupported.Character);
        (character.Metadata![ErrorMetadataKeys.SourceFile], character.Metadata[ErrorMetadataKeys.SourceLine]).Should().Be(("https://example.org/s.css", 2));
        var font = refusals.Single(error => error.Code == HtmlStyleErrorCodes.Unsupported.Font);
        (font.Metadata![ErrorMetadataKeys.SourceFile], font.Metadata[ErrorMetadataKeys.SourceLine]).Should().Be(("https://example.org/s.css", 3));
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_tagged_PDF_with_numbered_headings_passes_veraPDF(string flavour)
    {
        var html = "<!doctype html><html lang=\"en\"><head><title>Report</title><style>@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Regular.ttf) } @font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Bold.ttf); font-weight: 700 } body { font-family: Lato; counter-reset: section }"
            + " h2 { counter-increment: section; counter-reset: sub } h3 { counter-increment: sub } h2::before { content: counter(section) \". \" } h3::before { content: counter(section) \".\" counter(sub) \" \" }</style></head>"
            + "<body><h1>Report</h1><h2>Summary</h2><p>Text.</p><h3>Scope</h3><p>More text.</p><h2>Results</h2><p>The end.</p></body></html>";
        var options = new HtmlRenderOptions { Style = HtmlStyleOptions.Default with { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = new Uri(Path.GetFullPath(Path.Combine(HtmlCases.Folder(), "gc.html"))).AbsoluteUri } };
        var profile = flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1;
        var pdf = HtmlRendering.ToPdf(html, options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = profile });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        PdfStructureReadBack.Read(pdf.Value.Output).SelectMany(element => element.DepthFirst()).Where(element => element.Type is "H2" or "H3").Select(element => element.Text)
            .Should().Equal("1. Summary", "1.1 Scope", "2. Results");
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-counters-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "counters.pdf"), [.. pdf.Value.Output]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
