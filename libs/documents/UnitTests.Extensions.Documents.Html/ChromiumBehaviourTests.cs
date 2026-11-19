using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// Silent differences from Chromium 159, one small document each: the value Chromium 159 computes (measured in the
/// browser or in tools/reference/chromium with the pinned container), which the engine must compute too; or the
/// refusal that stands for a value the engine does not keep. Before the fix each document was accepted with another
/// value.
/// </summary>
public class ChromiumBehaviourTests
{
    private const string Doctype = "<!DOCTYPE html><title>t</title>";

    private static IEnumerable<Error> Refusals(StyledHtmlDocument styled) =>
        styled.Diagnostics.Where(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal);

    [Theory]
    // A table does not take -webkit-center from around it.
    [InlineData("<center><table id=t><tr><td>x</td></tr></table></center>", "t", "text-align", "start")]
    [InlineData("<div align=center><table><tr><td id=t>x</td></tr></table></div>", "t", "text-align", "start")]
    // The link colour is for a elements.
    [InlineData("<map><area id=t href=x alt=x></map>", "t", "color", "rgb(0, 0, 0)")]
    // No nobr wbr rule.
    [InlineData("<nobr>a<wbr id=t>b</nobr>", "t", "text-wrap-mode", "nowrap")]
    // A stray ! makes the custom property invalid, so the fallback is used.
    [InlineData("<style>#t{--a: red !ie; color: var(--a, blue)}</style><p id=t>x", "t", "color", "rgb(0, 0, 255)")]
    // outline-offset is snapped like a border width.
    [InlineData("<p id=t style=\"outline-offset: 1.5px\">x", "t", "outline-offset", "1px")]
    [InlineData("<p id=t style=\"outline-offset: 0.4px\">x", "t", "outline-offset", "1px")]
    [InlineData("<p id=t style=\"outline-offset: -0.5px\">x", "t", "outline-offset", "-1px")]
    // A calc() of two huge numbers is 0, not NaN.
    [InlineData("<p id=t style=\"width: calc(1e999px - 1e999px)\">x", "t", "width", "0px")]
    // The inclusive media comparisons have a tolerance of 1/64 px.
    [InlineData("<style>@media (max-width: 793.99px){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(255, 0, 0)")]
    [InlineData("<style>@media (width: 794.01px){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(255, 0, 0)")]
    [InlineData("<style>@media (width: 794.02px){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(0, 0, 0)")]
    // selector() takes one complex selector.
    [InlineData("<style>@supports selector(p, q){#t{color:red}}</style><p id=t>x", "t", "color", "rgb(0, 0, 0)")]
    // Which style sheets apply.
    [InlineData("<style type=\" text/css \">#t{color:red}</style><p id=t>x", "t", "color", "rgb(0, 0, 0)")]
    // A face attribute is one font-family value, never CSS.
    [InlineData("<font id=t face=\"x; color: red\">a</font>", "t", "color", "rgb(0, 0, 0)")]
    // Presentational attributes as Chromium reads them.
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

    [Theory]
    // An important revert-layer rolls back as if no rules were in its layer or between the layer's normal and important
    // levels (CSS Cascade 5 §7.3.5): written, through var(), in a shorthand, in all, in a custom property; in the style
    // attribute it rolls back the attribute's declarations only. Measured in Chromium 159.0.8154.6.
    [InlineData("<style>p { color: red } @layer a { p { color: revert-layer !important } }</style><div id=d><p id=a>x</p></div>", "a", "color", "rgb(0, 0, 0)")]
    [InlineData("<style>@layer a { p { margin-left: 2px; margin-left: var(--x, revert-layer) !important } }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "0px")]
    [InlineData("<style>p { margin-left: 2px; margin-left: var(--x, revert-layer) !important }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "0px")]
    [InlineData("<style>@layer a { p { margin-left: 2px } } p { margin-left: 3px; margin-left: revert-layer !important }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "2px")]
    [InlineData("<style>@layer a { p { margin-left: 2px } } @layer b { p { margin-left: 5px; margin-left: revert-layer !important } } @layer c { p { margin-left: 7px } } p { margin-left: 3px }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "2px")]
    [InlineData("<style>@layer a { p { margin-left: 2px } } @layer b { p { margin-left: 5px; margin-left: revert-layer !important } } @layer c { p { margin-left: 7px !important } }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "2px")]
    [InlineData("<style>@layer a { p { margin-left: 2px } } @layer b { p { margin-left: revert-layer !important } } p { margin-left: 3px !important }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "2px")]
    [InlineData("<style>@layer a { p { color: green } } @layer b { p { color: revert-layer !important } }</style><div id=d><p id=a style=\"color: red\">x</p></div>", "a", "color", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { p { color: green } } p { color: blue }</style><div id=d><p id=a style=\"color: red; color: revert-layer !important\">x</p></div>", "a", "color", "rgb(0, 0, 255)")]
    [InlineData("<style>@layer a { @layer x { p { margin-left: 2px } } @layer y { p { margin-left: 4px; margin-left: revert-layer !important } } p { margin-left: 6px } }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "2px")]
    [InlineData("<style>@layer a { p { margin: 2px } } @layer b { p { margin: 5px; margin: revert-layer !important } } p { margin: 3px }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "2px")]
    [InlineData("<style>@layer a { p { margin-left: 2px; color: green } } @layer b { p { all: revert-layer !important } } p { margin-left: 3px; color: red }</style><div id=d><p id=a>x</p></div>", "a", "color", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { p { text-align: left } } @layer b { p { text-align: revert-layer !important } } p { text-align: right }</style><div id=d><p id=a align=center>x</p></div>", "a", "text-align", "left")]
    [InlineData("<style>@layer b { p { text-align: revert-layer !important } } p { text-align: right }</style><div id=d><p id=a align=center>x</p></div>", "a", "text-align", "-webkit-center")]
    [InlineData("<style>@layer a { div { color: green } } @layer b { div { color: blue; color: revert-layer !important } } div { color: red }</style><div id=d><p id=a>x</p></div>", "d", "color", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { p { margin-left: 2px } p { margin-left: revert-layer !important } } @layer a { p { margin-left: 8px } }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "0px")]
    [InlineData("<style>@layer b, a; @layer a { p { margin-left: 2px } } @layer b { p { margin-left: 4px } } @layer a { p { margin-left: revert-layer !important } }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "4px")]
    [InlineData("<style>@layer b, a; @layer a { p { margin-left: 2px } } @layer b { p { margin-left: 4px } } @layer b { p { margin-left: revert-layer !important } }</style><div id=d><p id=a>x</p></div>", "a", "margin-left", "0px")]
    [InlineData("<style>@layer a { p { --c: green } } @layer b { p { --c: red; --c: revert-layer !important; color: var(--c, blue) } }</style><div id=d><p id=a>x</p></div>", "a", "color", "rgb(0, 128, 0)")]
    // The engine's own -internal- keyword is the user agent's also when var() gives it: an author's text-align is then
    // invalid at computed-value time and unset (inherited).
    [InlineData("<style>p { --k: -internal-ave-th-center; text-align: right; text-align: var(--k) }</style><div id=d><p id=a>x</p></div>", "a", "text-align", "start")]
    [InlineData("<style>p { --k: -INTERNAL-AVE-TH-CENTER; text-align: right; text-align: var(--k) }</style><div id=d><p id=a>x</p></div>", "a", "text-align", "start")]
    [InlineData("<style>p { text-align: right; text-align: var(--nope, -internal-ave-th-center) }</style><div id=d><p id=a>x</p></div>", "a", "text-align", "start")]
    public void The_roll_back_of_an_important_revert_layer_and_a_substituted_internal_keyword_are_Chromiums(string body, string id, string property, string chromium) =>
        Styled(Doctype + body, MeasuredPage).ById(id).Value(property).Should().Be(chromium);

    private static HtmlStyleOptions Refusing { get; } = MeasuredPage with { Unsupported = UnsupportedPolicy.Refuse };

    [Theory]
    // An inline image that is not there (no resolver in these tests), with no alt text: its width, height and
    // aspect-ratio compute to auto unless it has both sizes, or an aspect-ratio and one size; whatever the author wrote,
    // important or in a layer. No src, or a src of white space only, is not an image that failed. Measured in Chromium
    // 159.0.8154.6; accepted in the default mode.
    [InlineData("<p><img id=t src=missing.png width=10></p>", "width", "auto")]
    [InlineData("<p><img id=t src=missing.png height=10></p>", "height", "auto")]
    [InlineData("<p><img id=t src=missing.png alt=\"\" width=10></p>", "width", "auto")]
    [InlineData("<p><img id=t src=\"\" width=10></p>", "width", "auto")]
    [InlineData("<p><img id=t src=\"data:text/plain,x\" width=10></p>", "width", "auto")]
    [InlineData("<p><img id=t src=missing.png width=\"50%\"></p>", "width", "auto")]
    [InlineData("<p><img id=t src=missing.png style=\"width: 10px\"></p>", "width", "auto")]
    [InlineData("<style>img { width: 10px !important }</style><p><img id=t src=missing.png alt=\"\"></p>", "width", "auto")]
    [InlineData("<style>@layer a { img { width: 10px !important } }</style><p><img id=t src=missing.png></p>", "width", "auto")]
    [InlineData("<p><img id=t src=missing.png style=\"width: revert-layer !important; width: 10px\"></p>", "width", "auto")]
    [InlineData("<p><img id=t src=missing.png style=\"aspect-ratio: auto 2 / 1\"></p>", "aspect-ratio", "auto")]
    [InlineData("<p><img id=t src=missing.png width=10 height=10></p>", "aspect-ratio", "auto 10 / 10")]
    [InlineData("<p><img id=t src=missing.png alt=\"\" width=10 height=10></p>", "width", "10px")]
    [InlineData("<p><img id=t src=missing.png width=10 style=\"aspect-ratio: auto 2 / 1\"></p>", "width", "10px")]
    [InlineData("<p><img id=t src=missing.png width=10 style=\"height: 10px !important\"></p>", "width", "10px")]
    [InlineData("<p><img id=t src=missing.png width=10 style=\"display: block\"></p>", "width", "10px")]
    [InlineData("<p><img id=t width=10></p>", "width", "10px")]
    [InlineData("<p><img id=t src=\"  \" width=10></p>", "width", "10px")]
    public void A_missing_image_keeps_or_loses_its_sizes_as_in_Chromium(string body, string property, string chromium) =>
        Styled(Doctype + body, Refusing).ById("t").Value(property).Should().Be(chromium);

    [Theory]
    // align on an element without a rule of its own: middle is center, any other value is read as text-align (no
    // hint when it is not one: absmiddle, "right;"), inside a div that aligns right. Measured in Chromium 159.0.8154.6.
    [InlineData("<ul id=t align=center><li>i</li></ul>", "center")]
    [InlineData("<ul><li id=t align=middle>i</li></ul>", "center")]
    [InlineData("<section id=t align=justify>x</section>", "justify")]
    [InlineData("<span id=t align=\" center\">x</span>", "center")]
    [InlineData("<x-custom id=t align=end>x</x-custom>", "end")]
    [InlineData("<foo id=t align=MIDDLE>x</foo>", "center")]
    [InlineData("<pre id=t align=-webkit-center>x</pre>", "-webkit-center")]
    [InlineData("<font id=t align=left>x</font>", "left")]
    [InlineData("<center id=t align=right>x</center>", "right")]
    [InlineData("<blockquote id=t align=absmiddle>x</blockquote>", "right")]
    [InlineData("<address id=t align=\"right;\">x</address>", "right")]
    [InlineData("<h3 id=t align=middle>x</h3>", "center")]
    public void Align_on_other_elements_is_Chromiums(string element, string textAlign) =>
        Styled(Doctype + "<div style=\"text-align: right\">" + element + "</div>", MeasuredPage).ById("t").Value("text-align").Should().Be(textAlign);

    [Theory]
    // @supports with var(): true when every var() has its grammar, whatever its fallback holds; false for a var()
    // without a name, or with a semicolon or a '!' at the top of its fallback. Measured in Chromium 159.0.8154.6.
    [InlineData("(color: var())", "1px")]
    [InlineData("(color: var(--x, var()))", "1px")]
    [InlineData("(color: var(x))", "1px")]
    [InlineData("(color: var(--))", "1px")]
    [InlineData("(margin: 1px var())", "1px")]
    [InlineData("(font: var())", "1px")]
    [InlineData("(all: var())", "1px")]
    [InlineData("(color: rgb(var()))", "1px")]
    [InlineData("(color: var(--x, a;b))", "1px")]
    [InlineData("(color: var(--x, var(--y, ;)))", "1px")]
    [InlineData("(color: var(--x, !))", "1px")]
    [InlineData("(color: var(--x, 5px !important))", "1px")]
    [InlineData("not (color: var())", "9px")]
    [InlineData("(text-align: var(--x, -internal-center))", "9px")]
    [InlineData("(text-align: -internal-center var(--x))", "9px")]
    [InlineData("(text-align: var(--x, -internal-ave-th-center))", "9px")]
    [InlineData("(color: var(--x, (;)))", "9px")]
    [InlineData("(color: var(--x,))", "9px")]
    public void A_supports_condition_with_var_is_answered_as_Chromium_answers_it(string condition, string marginLeft) =>
        Styled(Doctype + "<style>p { margin-left: 1px } @supports " + condition + " { p { margin-left: 9px } }</style><p id=t>x</p>", Refusing).ById("t").Value("margin-left").Should().Be(marginLeft);

    [Theory]
    // A var() with a semicolon at the top of its fallback is invalid when parsed: the declaration is dropped, also a
    // custom property's. Measured in Chromium 159.0.8154.6.
    [InlineData("p { color: green; color: var(--x, a;b) }", "rgb(0, 128, 0)")]
    [InlineData("p { color: green; color: rgb(var(--x, ;)) }", "rgb(0, 128, 0)")]
    [InlineData("p { --a: var(--x, ;); color: green; color: var(--a, blue) }", "rgb(0, 0, 255)")]
    public void A_var_with_a_semicolon_in_its_fallback_drops_the_declaration(string sheet, string color) =>
        Styled(Doctype + "<style>" + sheet + "</style><p id=t>x</p>", Refusing).ById("t").Value("color").Should().Be(color);

    [Theory]
    // Values Chromium keeps in a narrower form than a number: a ratio component as a float of more than 2^-20, else 0
    // (gfx::SizeF); a font weight in quarters, cut toward zero (FontSelectionValue). Other numbers keep their value.
    // Measured in Chromium 159.0.8154.6.
    [InlineData("aspect-ratio: auto 0.0000001/1", "aspect-ratio", "auto 0 / 1")]
    [InlineData("aspect-ratio: auto 1/0.0000001", "aspect-ratio", "auto 1 / 0")]
    [InlineData("aspect-ratio: auto 0.00000095367431640625/1", "aspect-ratio", "auto 0 / 1")]
    [InlineData("aspect-ratio: auto 0.000000954/1", "aspect-ratio", "auto 9.54e-07 / 1")]
    [InlineData("aspect-ratio: auto 1e-7/1e-7", "aspect-ratio", "auto 0 / 0")]
    [InlineData("aspect-ratio: auto calc(0.0000001)/1", "aspect-ratio", "auto 0 / 1")]
    [InlineData("aspect-ratio: auto 0.000001/1", "aspect-ratio", "auto 1e-06 / 1")]
    [InlineData("font-weight: 100.6", "font-weight", "100.5")]
    [InlineData("font-weight: 999.99", "font-weight", "999.75")]
    [InlineData("font-weight: 400.24", "font-weight", "400")]
    [InlineData("font-weight: 1.1", "font-weight", "1")]
    [InlineData("font-weight: calc(0.1234567 * 400)", "font-weight", "49.25")]
    public void A_number_Chromium_keeps_in_a_narrower_form_is_Chromiums(string declaration, string property, string chromium) =>
        Styled(Doctype + "<style>p { " + declaration + " }</style><p id=t>x</p>", Refusing).ById("t").Value(property).Should().Be(chromium);

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
        // svg itself is refused (its own rules); with warnings only, the sheet's rule applies, as in Chromium.
        var styled = Styled(Doctype + "<p id=t>x</p><svg><style>p{color:red}</style></svg>", MeasuredPage);

        styled.ById("t").Value("color").Should().Be("rgb(255, 0, 0)");
        Refusals(styled).Should().ContainSingle().Which.Message.Should().Contain("<svg>");
    }

    [Theory]
    // A script a browser would run, and event handler attributes.
    [InlineData("<p id=t>x</p><script>document.getElementById('t').style.color='red'</script>", "script")]
    [InlineData("<script type=module>1</script>", "script")]
    [InlineData("<script language=javascript>1</script>", "script")]
    [InlineData("<body onload=\"x()\"><p>x", "onload")]
    [InlineData("<img src=x alt=x onerror=\"x()\">", "onerror")]
    // Elements Chromium styles with rules the engine does not have.
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
    // What changes Chromium's printed rendering is refused, the rest of the ignored list stays ignored.
    [InlineData("<p style=\"zoom: 2\">x", "zoom")]
    [InlineData("<style>@keyframes k{from{color:red}} p{animation: k 1s forwards}</style><p>x", "animation")]
    [InlineData("<p style=\"resize: both; overflow: hidden\">x", "resize")]
    [InlineData("<p style=\"will-change: opacity\">x", "will-change")]
    // overflow-x hidden makes overflow-y auto, which the engine does not support.
    [InlineData("<p style=\"overflow-x: hidden\">x", "overflow-x")]
    // Values Chromium clamps or keeps in a way the engine does not.
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
        // Before the fix it applied with warnings only.
        var media = new string('(', 300) + "print" + new string(')', 300);
        // Past the limit resolution stops and fails, in warn mode too (no partly styled document).
        var result = Resolve(Doctype + $"<style media=\"{media}\">#t{{margin-bottom:4px}}</style><p id=t>x", MeasuredPage);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(CssErrorCodes.TooDeep);
    }
}
