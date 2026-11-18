using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// The silent differences of review 39, one small document each: the value Chromium 159 computes (measured by the review
/// or in tools/reference/chromium with the pinned container), which the engine must compute too; or the refusal that
/// stands for a value the engine does not keep. Before the fix each document was accepted with another value.
/// </summary>
public class ChromiumBehaviourTests
{
    private const string Doctype = "<!DOCTYPE html><title>t</title>";

    private static IEnumerable<Error> Refusals(StyledHtmlDocument styled) =>
        styled.Diagnostics.Where(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal);

    [Theory]
    // F4: a table does not take -webkit-center from around it.
    [InlineData("<center><table id=t><tr><td>x</td></tr></table></center>", "t", "text-align", "start")]
    [InlineData("<div align=center><table><tr><td id=t>x</td></tr></table></div>", "t", "text-align", "start")]
    // F2: the link colour is for a elements.
    [InlineData("<map><area id=t href=x alt=x></map>", "t", "color", "rgb(0, 0, 0)")]
    // F6: no nobr wbr rule.
    [InlineData("<nobr>a<wbr id=t>b</nobr>", "t", "text-wrap-mode", "nowrap")]
    // M8: a stray ! makes the custom property invalid, so the fallback is used.
    [InlineData("<style>#t{--a: red !ie; color: var(--a, blue)}</style><p id=t>x", "t", "color", "rgb(0, 0, 255)")]
    // F14: outline-offset is snapped like a border width.
    [InlineData("<p id=t style=\"outline-offset: 1.5px\">x", "t", "outline-offset", "1px")]
    [InlineData("<p id=t style=\"outline-offset: 0.4px\">x", "t", "outline-offset", "1px")]
    [InlineData("<p id=t style=\"outline-offset: -0.5px\">x", "t", "outline-offset", "-1px")]
    // M14: a calc() of two huge numbers is 0, not NaN.
    [InlineData("<p id=t style=\"width: calc(1e999px - 1e999px)\">x", "t", "width", "0px")]
    // F22: the inclusive media comparisons have a tolerance of 1/64 px.
    [InlineData("<style>@media (max-width: 793.99px){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(255, 0, 0)")]
    [InlineData("<style>@media (width: 794.01px){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(255, 0, 0)")]
    [InlineData("<style>@media (width: 794.02px){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(0, 0, 0)")]
    // F20: selector() takes one complex selector.
    [InlineData("<style>@supports selector(p, q){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(0, 0, 0)")]
    // F19: which style sheets apply.
    [InlineData("<style type=\" text/css \">#t{color:red}</style><p id=t>x", "t", "color", "rgb(0, 0, 0)")]
    // M15: a face attribute is one font-family value, never CSS.
    [InlineData("<font id=t face=\"x; color: red\">a</font>", "t", "color", "rgb(0, 0, 0)")]
    // F5 and the batch-5 rows: presentational attributes as Chromium reads them.
    [InlineData("<hr id=t size=abc>", "t", "border-bottom-width", "0px")]
    [InlineData("<hr id=t size=\"5abc\">", "t", "height", "3px")]
    [InlineData("<table cellpadding=-1><tr><td id=t>x</td></tr></table>", "t", "padding-left", "0px")]
    [InlineData("<table cellpadding=\"10%\"><tr><td id=t>x</td></tr></table>", "t", "padding-left", "10px")]
    [InlineData("<div id=t align=\" center\">x</div>", "t", "text-align", "center")]
    [InlineData("<div id=t align=end>x</div>", "t", "text-align", "end")]
    [InlineData("<h1 id=t align=middle>x</h1>", "t", "text-align", "center")]
    [InlineData("<table><tr><td id=t height=\"3*\">x</td></tr></table>", "t", "height", "auto")]
    [InlineData("<table><colgroup id=t align=right><col></colgroup><tr><td>x</td></tr></table>", "t", "text-align", "-webkit-right")]
    [InlineData("<table id=t border bordercolor=\"\"><tr><td>x</td></tr></table>", "t", "border-top-style", "outset")]
    [InlineData("<img id=t border=x alt=x src=\"data:,\">", "t", "border-top-style", "solid")]
    [InlineData("<hr id=t color=\"\">", "t", "border-top-style", "solid")]
    public void The_value_is_Chromiums(string body, string id, string property, string chromium) =>
        Styled(Doctype + body, MeasuredPage).ById(id).Value(property).Should().Be(chromium);

    [Fact]
    public void Body_margins_come_from_the_last_attribute_of_each_pair()
    {
        Styled(Doctype + "<body marginwidth=5 leftmargin=7><p>x", MeasuredPage).ByName("body").Value("margin-left").Should().Be("7px");
        Styled(Doctype + "<body leftmargin=7 marginwidth=5><p>x", MeasuredPage).ByName("body").Value("margin-left").Should().Be("5px");
    }

    [Fact]
    public void The_preferred_style_sheet_set_is_named_by_whichever_comes_first()
    {
        var sheets = new Dictionary<string, string> { ["https://example.org/a.css"] = "#t{color:red}", ["https://example.org/b.css"] = "#t{margin-top:1px}" };
        HtmlResource? Resolve(string url) => sheets.TryGetValue(url, out var text) ? new HtmlResource(System.Text.Encoding.UTF8.GetBytes(text)) : null;
        var options = MeasuredPage with { Resources = Resolve, BaseUrl = "https://example.org/" };

        var before = Styled(Doctype + "<meta http-equiv=default-style content=b><link rel=stylesheet title=a href=a.css><link rel=stylesheet title=b href=b.css><p id=t>x", options).ById("t");
        (before.Value("color"), before.Value("margin-top")).Should().Be(("rgb(0, 0, 0)", "1px"));
        var after = Styled(Doctype + "<link rel=stylesheet title=a href=a.css><meta http-equiv=default-style content=b><link rel=stylesheet title=b href=b.css><p id=t>x", options).ById("t");
        (after.Value("color"), after.Value("margin-top")).Should().Be(("rgb(255, 0, 0)", "16px"));
        Styled(Doctype + "<link rel=stylesheet href=a.css disabled><p id=t>x", options).ById("t").Value("color").Should().Be("rgb(0, 0, 0)");
        Styled(Doctype + "<link rel=stylesheet href=a.css type=\"text/css; charset=utf-8\"><p id=t>x", options).ById("t").Value("color").Should().Be("rgb(255, 0, 0)");
    }

    [Fact]
    public void A_style_element_inside_svg_applies()
    {
        // Review 39 M5. svg itself is refused (its own rules); with warnings only, the sheet's rule applies, as in Chromium.
        var styled = Styled(Doctype + "<p id=t>x</p><svg><style>p{color:red}</style></svg>", MeasuredPage);

        styled.ById("t").Value("color").Should().Be("rgb(255, 0, 0)");
        Refusals(styled).Should().ContainSingle().Which.Message.Should().Contain("<svg>");
    }

    [Theory]
    // M13: a script a browser would run, and event handler attributes.
    [InlineData("<p id=t>x</p><script>document.getElementById('t').style.color='red'</script>", "script")]
    [InlineData("<script type=module>1</script>", "script")]
    [InlineData("<script language=javascript>1</script>", "script")]
    [InlineData("<body onload=\"x()\"><p>x", "onload")]
    [InlineData("<img src=x alt=x onerror=\"x()\">", "onerror")]
    // M6: elements Chromium styles with rules the engine does not have.
    [InlineData("<details><summary>s</summary>x</details>", "details")]
    [InlineData("<p><math><mi>x</mi></math>", "math")]
    [InlineData("<noscript><p>x</p></noscript>", "noscript")]
    // The user-agent style of an element outside the profile names the element.
    [InlineData("<p>a<sub>b</sub>", "sub")]
    [InlineData("<div contenteditable>x</div>", "contenteditable")]
    public void What_the_engine_does_not_run_or_draw_is_refused_where_the_author_wrote_it(string body, string value)
    {
        var refusal = Refusals(Styled(Doctype + body, MeasuredPage)).First();

        refusal.Metadata![Ave.Extensions.Documents.Core.ErrorMetadataKeys.Value].Should().Be(value);
        refusal.Metadata![Ave.Extensions.Documents.Core.ErrorMetadataKeys.SourceLine].Should().Be(1);
    }

    [Fact]
    public void A_data_block_is_not_a_script()
    {
        Refusals(Styled(Doctype + "<script type=\"application/json\">{}</script><p>x", MeasuredPage)).Should().BeEmpty();
    }

    [Theory]
    // M9, M10: what changes Chromium's printed rendering is refused, the rest of the ignored list stays ignored.
    [InlineData("<p style=\"zoom: 2\">x", "zoom")]
    [InlineData("<style>@keyframes k{from{color:red}} p{animation: k 1s forwards}</style><p>x", "animation")]
    [InlineData("<p style=\"resize: both; overflow: hidden\">x", "resize")]
    [InlineData("<p style=\"will-change: opacity\">x", "will-change")]
    // M11: overflow-x hidden makes overflow-y auto, which the engine does not support.
    [InlineData("<p style=\"overflow-x: hidden\">x", "overflow-x")]
    // F13, F9, M14, CR-8, CR-13: values Chromium clamps or keeps in a way the engine does not.
    [InlineData("<div style=\"font-size: 0.2em\"><p>x</p></div>", "font-size")]
    [InlineData("<p style=\"font-size: 99999px\">x", "font-size")]
    [InlineData("<p style=\"widows: 2147483647\">x", "widows")]
    [InlineData("<p style=\"width: 1e999px\">x", "width")]
    [InlineData("<p style=\"color: hsl(1e999, 100%, 50%)\">x", "color")]
    [InlineData("<p style=\"color: rgb(none none none)\">x", "color")]
    [InlineData("<img src=x alt=x width=\"99999999999999999999\">", "width")]
    public void A_value_the_engine_does_not_keep_as_Chromium_is_refused(string body, string property)
    {
        var styled = Styled(Doctype + body, MeasuredPage);

        Refusals(styled).Should().Contain(error => error.Metadata != null && Equals(error.Metadata.GetValueOrDefault(Ave.Extensions.Documents.Core.ErrorMetadataKeys.Property), property));
    }

    [Theory]
    [InlineData("<p style=\"zoom: 1\">x")]
    [InlineData("<style>@keyframes k{from{color:red}} p{animation-duration: 1s}</style><p>x")]
    [InlineData("<p style=\"will-change: scroll-position; cursor: pointer; transition: color 1s\">x")]
    [InlineData("<style>::selection{color:red} ::-webkit-scrollbar{width:4px} input::placeholder{color:red}</style><p>x")]
    public void What_does_not_change_the_printed_page_is_ignored_not_refused(string body) =>
        Refusals(Styled(Doctype + body, MeasuredPage)).Should().BeEmpty();

    [Fact]
    public void A_supports_condition_the_engine_cannot_answer_for_Chromium_is_refused()
    {
        var styled = Styled(Doctype + "<style>@supports font-tech(variations){#t{color:red}}</style><p id=t>x", MeasuredPage);

        styled.ById("t").Value("color").Should().Be("rgb(0, 0, 0)");
        Refusals(styled).Should().ContainSingle().Which.Code.Should().Be(CssErrorCodes.Unsupported.AtRule);
    }

    [Fact]
    public void A_media_attribute_past_the_limit_ends_the_resolution()
    {
        // Review 39 CR-14: it applied with warnings only.
        var media = new string('(', 300) + "print" + new string(')', 300);
        // Past the limit resolution stops and fails, in warn mode too (no partly styled document).
        var result = Resolve(Doctype + $"<style media=\"{media}\">#t{{margin-bottom:4px}}</style><p id=t>x", MeasuredPage);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(CssErrorCodes.TooDeep);
    }
}
