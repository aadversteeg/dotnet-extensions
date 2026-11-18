using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// Small documents the engine accepted with another computed value than Chromium 159's. Each is now either accepted
/// with Chromium's value, or refused. Every value below is Chromium's on the same markup, measured in the pinned
/// container (tools/reference/chromium, Chromium 159.0.8154.6, print media, 794 × 1123 px); the measured pages add a
/// title, ids and classes, or a wrapping p, which change none of the values.
/// </summary>
public class FaithfulSubsetTests
{
    private const string Doctype = "<!DOCTYPE html>";

    private static HtmlStyleOptions Refuse { get; } = MeasuredPage with { Unsupported = UnsupportedPolicy.Refuse };

    /// <summary>The document resolved in the default mode, which must accept it.</summary>
    private static StyledHtmlDocument Accepted(string html, HtmlStyleOptions? options = null) =>
        HtmlStyleResolver.Resolve(Parsed(html), options ?? Refuse).Match(styled => styled, error => throw new InvalidOperationException("Refused: " + error));

    private static StyledElement Element(StyledHtmlDocument styled, string id) => id == "html" ? styled.ByName("html") : styled.ById(id);

    private static IEnumerable<Error> Parts(Error error) => error.Code == ErrorCodes.Aggregate ? error.Parts() : [error];

    [Theory]
    // A ';' in the block of a group rule starts the prelude of the next rule, which is dropped; before it, the rule applies.
    [InlineData("<style>@media print{ .a{color:red}; .b{margin-top:5px} }</style><p class=a id=a>a</p><p class=b id=b>b</p>", "a", "color", "rgb(255, 0, 0)")]
    [InlineData("<style>@media print{ .a{color:red}; .b{margin-top:5px} }</style><p class=a id=a>a</p><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    [InlineData("<style>@media print{ margin-top:5px; .b{margin-top:5px} }</style><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    [InlineData("<style>@media print{ ; .b{margin-top:5px} }</style><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    [InlineData("<style>@media print{ --x: 5px; .b{margin-top:5px} }</style><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    [InlineData("<style>@supports (color:red){ .a{color:red}; .b{margin-top:5px} }</style><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    [InlineData("<style>@layer x{ .a{color:red}; .b{margin-top:5px} }</style><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    [InlineData("<style>@media print{ .a{color:red} .b{margin-top:5px}; }</style><p class=b id=b>b</p>", "b", "margin-top", "5px")]
    [InlineData("<style>.a{color:red}; .b{margin-top:5px}</style><p class=b id=b>b</p>", "b", "margin-top", "16px")]
    // Inside a style rule the same at-rule holds declarations, and a stray ';' is nothing.
    [InlineData("<style>.a{ @media print{ margin-top:5px; ; color:red } }</style><p class=a id=a>a</p>", "a", "margin-top", "5px")]
    [InlineData("<style>.a{ @media print{ margin-top:5px; ; color:red } }</style><p class=a id=a>a</p>", "a", "color", "rgb(255, 0, 0)")]
    // A var() in a fallback that is not used makes no dependency, so no cycle.
    [InlineData("<style>#t{--a: a1 var(--b, var(--c)); --b: b1; --c: c1 var(--a)} #t i{font-family: z var(--a, fb)}</style><div id=t><i id=i>i</i></div>", "i", "font-family", "\"z a1 b1\"")]
    [InlineData("<style>#t{--a: a1 var(--b, var(--c)); --b: b1; --c: c1 var(--a)} #t b{font-family: z var(--c, fb)}</style><div id=t><b id=b>b</b></div>", "b", "font-family", "\"z c1 a1 b1\"")]
    [InlineData("<style>#t{--b: b1; --a: a1 var(--b, var(--a))} #t i{font-family: z var(--a, fb)}</style><div id=t><i id=i>i</i></div>", "i", "font-family", "\"z a1 b1\"")]
    // '--' alone is no custom property name: the declaration that names it, or uses it in var(), is dropped.
    [InlineData("<style>#t{--: dd} #t i{font-family: z var(--, fb)}</style><div id=t style=\"font-family: tfam\"><i id=i>i</i></div>", "i", "font-family", "tfam")]
    [InlineData("<style>#t{--y: y1 var(--, yf)} #t b{font-family: z var(--y, fb)}</style><div id=t><b id=b>b</b></div>", "b", "font-family", "\"z fb\"")]
    // A CSS-wide keyword that substitution makes is that keyword.
    [InlineData("<style>#p{--x: px} #t{--e:; --x: var(--e) inherit} #t i{font-family: z var(--x, fb)}</style><div id=p><div id=t><i id=i>i</i></div></div>", "i", "font-family", "\"z px\"")]
    [InlineData("<style>#p{--w: pw} #t{--w: var(--nope, initial)} #t b{font-family: z var(--w, fb)}</style><div id=p><div id=t><b id=b>b</b></div></div>", "b", "font-family", "\"z fb\"")]
    [InlineData("<style>#p{color: rgb(2, 2, 2)} #t{--i: var(--nope, initial); color: var(--i)}</style><div id=p><div id=t>t</div></div>", "t", "color", "rgb(2, 2, 2)")]
    [InlineData("<style>#p{--x: px} #t{--x: var(--nope, unset)} #t i{font-family: z var(--x, fb)}</style><div id=p><div id=t><i id=i>i</i></div></div>", "i", "font-family", "\"z px\"")]
    // revert and revert-layer made by var() roll the cascade back.
    [InlineData("<p id=a style=\"display: var(--nope, revert)\">a</p>", "a", "display", "block")]
    [InlineData("<p id=b style=\"margin-top: var(--nope, revert)\">b</p>", "b", "margin-top", "16px")]
    [InlineData("<table><tr><td id=c style=\"display: var(--nope, revert)\">c</td></tr></table>", "c", "display", "table-cell")]
    [InlineData("<style>@layer l{ #a{margin-top: 7px} } @layer m{ #a{margin-top: var(--nope, revert-layer)} }</style><p id=a>a</p>", "a", "margin-top", "7px")]
    // @supports is false for a declaration Chromium does not accept.
    [InlineData("<style>@supports (--x: var()){.a{margin-top:5px}}</style><p class=a id=a>a</p>", "a", "margin-top", "16px")]
    [InlineData("<style>@supports (text-align: -internal-center){.a{margin-top:5px}}</style><p class=a id=a>a</p>", "a", "margin-top", "16px")]
    [InlineData("<style>@supports not (--x: var()){.a{margin-top:5px}}</style><p class=a id=a>a</p>", "a", "margin-top", "5px")]
    // & at the top level matches the root with no specificity.
    [InlineData("<style>& { min-width:101px } html { min-width:100px }</style><p>a</p>", "html", "min-width", "100px")]
    [InlineData("<style>& { min-width:101px } :root{ min-width:100px }</style><p>a</p>", "html", "min-width", "100px")]
    [InlineData("<style>& { min-width:101px }</style><p>a</p>", "html", "min-width", "101px")]
    [InlineData("<style>&.x{min-width:101px}</style><p>a</p>", "html", "min-width", "0px")]
    [InlineData("<style>p:not(&){ margin-top:3px }</style><p id=a>a</p>", "a", "margin-top", "3px")]
    // An+B up to 2^30 - 1 in size matches as in Chromium.
    [InlineData("<style>p:nth-child(-n+1073741823){margin-top:7px}</style><p id=a>a</p><p>b</p><p id=c>c</p>", "c", "margin-top", "7px")]
    [InlineData("<style>p:nth-child(n-1000000000){margin-top:7px}</style><p id=a>a</p>", "a", "margin-top", "7px")]
    [InlineData("<style>p:nth-child(0n+999999999){margin-top:7px}</style><p id=a>a</p>", "a", "margin-top", "16px")]
    // background: one box sets the origin and the clip, two set them in order.
    [InlineData("<style>.a{background: red padding-box}</style><p class=a id=a>a</p>", "a", "background-clip", "padding-box")]
    [InlineData("<style>.a{background: padding-box}</style><p class=a id=a>a</p>", "a", "background-clip", "padding-box")]
    [InlineData("<style>.a{background: none padding-box}</style><p class=a id=a>a</p>", "a", "background-clip", "padding-box")]
    [InlineData("<style>.a{background: red border-box padding-box}</style><p class=a id=a>a</p>", "a", "background-origin", "border-box")]
    [InlineData("<style>.a{background: red border-box padding-box}</style><p class=a id=a>a</p>", "a", "background-clip", "padding-box")]
    [InlineData("<style>.a{background: padding-box red border-box}</style><p class=a id=a>a</p>", "a", "background-clip", "border-box")]
    [InlineData("<style>.a{background: red repeat repeat}</style><p class=a id=a>a</p>", "a", "background-color", "rgb(255, 0, 0)")]
    [InlineData("<style>.a{font-synthesis: weight style}</style><p class=a id=a>a</p>", "a", "font-synthesis-small-caps", "none")]
    // A family name of identifiers that does not start with a generic family; '--a' is written quoted.
    [InlineData("<style>.a{font-family: foo serif}</style><p class=a id=a>a</p>", "a", "font-family", "\"foo serif\"")]
    [InlineData("<style>.a{font-family: --a, serif}</style><p class=a id=a>a</p>", "a", "font-family", "\"--a\", serif")]
    [InlineData("<style>.a{font: 12px serif, sans-serif}</style><p class=a id=a>a</p>", "a", "font-size", "12px")]
    // Small font sizes Chromium keeps.
    [InlineData("<style>.a{font-size: 2e-7px}</style><p class=a id=a>a</p>", "a", "font-size", "2e-07px")]
    [InlineData("<style>.a{font-size: 1e-3pt}</style><p class=a id=a>a</p>", "a", "font-size", "0.00133333px")]
    // aspect-ratio: auto and a ratio once each; numbers to six digits, past the float range the largest float.
    [InlineData("<style>.a{aspect-ratio: 16/9 auto}</style><p class=a id=a>a</p>", "a", "aspect-ratio", "auto 16 / 9")]
    [InlineData("<style>.a{aspect-ratio: auto 1.123456789/3}</style><p class=a id=a>a</p>", "a", "aspect-ratio", "auto 1.12346 / 3")]
    [InlineData("<style>.a{aspect-ratio: auto 1e999/1}</style><p class=a id=a>a</p>", "a", "aspect-ratio", "auto 3.40282e+38 / 1")]
    // align and face are read as one CSS value: a ';' outside a string or comment is no value; the end of the
    // attribute ends a string or a comment.
    [InlineData("<p id=a align=\"justify;\">a</p>", "a", "text-align", "start")]
    [InlineData("<p id=a align=\"center;\">a</p>", "a", "text-align", "start")]
    [InlineData("<p id=a align=\"justify !important\">a</p>", "a", "text-align", "start")]
    [InlineData("<p id=a align=\" justify\">a</p>", "a", "text-align", "justify")]
    [InlineData("<p id=a align=\"justify /*x\">a</p>", "a", "text-align", "justify")]
    [InlineData("<p id=a align=\"justify/**/;\">a</p>", "a", "text-align", "start")]
    [InlineData("<font id=a face=\"'unterminated\">a</font>", "a", "font-family", "unterminated")]
    [InlineData("<font id=a face=\"a;b\">a</font>", "a", "font-family", "\"Times New Roman\"")]
    [InlineData("<font id=a face=\"a\\;b\">a</font>", "a", "font-family", "\"a;b\"")]
    [InlineData("<font id=a face='\"x;y\"'>a</font>", "a", "font-family", "\"x;y\"")]
    // A Content-Security-Policy outside head, and a report-only one, do nothing.
    [InlineData("<title>t</title><style>p{color:red}</style><body><meta http-equiv=\"Content-Security-Policy\" content=\"style-src 'none'\"><p id=a style=\"margin-top: 5px\">x</p>", "a", "color", "rgb(255, 0, 0)")]
    [InlineData("<title>t</title><style>p{color:red}</style><body><meta http-equiv=\"Content-Security-Policy\" content=\"style-src 'none'\"><p id=a style=\"margin-top: 5px\">x</p>", "a", "margin-top", "5px")]
    [InlineData("<title>t</title><meta http-equiv=\"Content-Security-Policy-Report-Only\" content=\"style-src 'none'\"><style>p{color:red}</style><p id=a>x</p>", "a", "color", "rgb(255, 0, 0)")]
    // An image with alt text keeps its size when it is drawn (display block), or has no alt text.
    [InlineData("<style>img{display:block}</style><img id=a src=missing.png alt=logo width=100 height=40>", "a", "width", "100px")]
    [InlineData("<img id=a src=missing.png alt=\"\" width=100 height=40>", "a", "width", "100px")]
    [InlineData("<img id=a src=missing.png width=100 height=40>", "a", "height", "40px")]
    // A data: URL holds the image itself (measured in the corpus case hints).
    [InlineData("<img id=a src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=\" alt=x width=100 height=50>", "a", "width", "100px")]
    [InlineData("<img id=a src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=\" alt=x width=100 height=50>", "a", "height", "50px")]
    public void The_value_is_Chromiums_and_the_document_is_accepted(string body, string id, string property, string chromium) =>
        Element(Accepted(Doctype + body), id).Value(property).Should().Be(chromium);

    [Fact]
    public void A_line_feed_after_a_dropped_nul_is_still_the_first_character_of_a_pre()
    {
        var pre = Accepted(Doctype + "<pre id=a>\0\ny</pre>z").ById("a");

        pre.Children.OfType<StyledText>().Single().Text.Data.Should().Be("y");
    }

    [Theory]
    // @import: a layer statement after an import ends the place for imports; an invalid rule before one does not.
    [InlineData("@import \"a.css\"; @layer x; @import \"b.css\";", "3px", "16px")]
    [InlineData("@layer x; @import \"a.css\"; @layer y; @import \"b.css\";", "3px", "16px")]
    [InlineData("@import \"a.css\"; @layer x, y; @import \"b.css\";", "3px", "16px")]
    [InlineData("@layer x; @import \"b.css\";", "0px", "9px")]
    [InlineData("@keyframes {} @import \"b.css\";", "0px", "9px")]
    [InlineData("@keyframes k{} @import \"b.css\";", "0px", "16px")]
    public void An_import_applies_where_Chromium_applies_it(string sheet, string marginLeftOfA, string marginTopOfB)
    {
        var sheets = new Dictionary<string, string> { ["https://example.org/a.css"] = "#a{margin-left:3px}", ["https://example.org/b.css"] = "#b{margin-top:9px}" };
        HtmlResource? Resolve(string url) => sheets.TryGetValue(url, out var text) ? new HtmlResource(System.Text.Encoding.UTF8.GetBytes(text)) : null;

        var styled = Accepted(Doctype + $"<style>{sheet}</style><p id=a>a</p><p id=b>b</p>", Refuse with { Resources = Resolve, BaseUrl = "https://example.org/" });

        (styled.ById("a").Value("margin-left"), styled.ById("b").Value("margin-top")).Should().Be((marginLeftOfA, marginTopOfB));
    }

    [Theory]
    // A cycle of var() references: Chromium does not make every member invalid.
    [InlineData("<style>#t{--a: var(--b); --b: var(--a)}</style><p id=t>x", "Documents.Css.Unsupported.Value", "cycle")]
    // A '!' inside a var() fallback: Chromium drops some such declarations and unsets the property for others.
    [InlineData("<style>.a{padding-top:4px; padding-top: var(--x, !important)}</style><p class=a>a", "Documents.Css.Unsupported.Value", "'!'")]
    [InlineData("<style>.a{padding-top:4px; padding-top: var(--x, 5px !important)}</style><p class=a>a", "Documents.Css.Unsupported.Value", "'!'")]
    // Selectors Chromium matches by another rule: an upper-case OF, An+B of 2^30 or more, an escaped '*'.
    [InlineData("<style>div, :nth-child(1 OF .a) { padding-top:37px }</style><div class=a></div>", "Documents.Css.Unsupported.Selector", "OF")]
    [InlineData("<style>p:nth-child(-n+1073741824){margin-top:7px}</style><p>a", "Documents.Css.Unsupported.Selector", "2^30")]
    [InlineData("<style>p:nth-child(2147483647n+1){margin-top:7px}</style><p>a", "Documents.Css.Unsupported.Selector", "2^30")]
    [InlineData("<style>\\*{margin-top:5px}</style><p>a", "Documents.Css.Unsupported.Selector", "'*'")]
    // An animation the engine cannot see the name of: through var() or env(), a keyword named twice, a keyword that
    // is a name in Chromium's shorthand.
    [InlineData("<style>p{--a:k; animation: var(--a) 100s}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s env(x, k)}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s ease ease}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s Ease ease}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: linear 100s linear infinite}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: infinite infinite 100s}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s both both}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s add}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s replace}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s accumulate}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s auto}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<style>p{animation: 100s none, 100s ease ease}</style><p>a", "Documents.Css.Unsupported.Value", "animation")]
    [InlineData("<p style=\"--a:k; animation: var(--a) 100s\">a", "Documents.Css.Unsupported.Value", "animation")]
    // Values Chromium drops as invalid, which the engine refuses (not valid or not supported in this profile): a
    // component named twice, a generic family that starts a name of several identifiers.
    [InlineData("<style>.a{aspect-ratio: auto 16/9 auto}</style><p class=a>a", "Documents.Css.Unsupported.Value", "not a value of aspect-ratio")]
    [InlineData("<style>.a{font-synthesis: weight weight}</style><p class=a>a", "Documents.Css.Unsupported.Value", "not a value of font-synthesis")]
    [InlineData("<style>.a{font-synthesis: style style}</style><p class=a>a", "Documents.Css.Unsupported.Value", "not a value of font-synthesis")]
    [InlineData("<style>.a{font-synthesis: weight style weight}</style><p class=a>a", "Documents.Css.Unsupported.Value", "not a value of font-synthesis")]
    [InlineData("<style>.a{font: 12px sans-serif serif}</style><p class=a>a", "Documents.Css.Unsupported.Value", "generic family 'sans-serif'")]
    [InlineData("<style>.a{font-family: sans-serif serif}</style><p class=a>a", "Documents.Css.Unsupported.Value", "generic family 'sans-serif'")]
    [InlineData("<style>.a{font-family: monospace foo, serif}</style><p class=a>a", "Documents.Css.Unsupported.Value", "generic family 'monospace'")]
    [InlineData("<style>.a{background: red scroll scroll}</style><p class=a>a", "Documents.Css.Unsupported.Value", "not a value of background")]
    [InlineData("<style>.a{background: red padding-box border-box padding-box}</style><p class=a>a", "Documents.Css.Unsupported.Value", "not a value of background")]
    // Values Chromium computes by a rule the engine does not keep.
    [InlineData("<style>.a{font-size: 1e-7px}</style><p class=a>a", "Documents.Css.Unsupported.Value", "2e-7px")]
    [InlineData("<style>.a{-webkit-user-modify: read-write-plaintext-only}</style><p class=a>a", "Documents.Css.Unsupported.Value", "-webkit-user-modify")]
    // align on html, head and body aligns the text in Chromium.
    [InlineData("<body align=center><p>a", "Documents.Html.Unsupported.Attribute", "align")]
    [InlineData("<body align=right><p>a", "Documents.Html.Unsupported.Attribute", "align")]
    [InlineData("<html align=center><p>a", "Documents.Html.Unsupported.Attribute", "align")]
    [InlineData("<head align=center></head><p>a", "Documents.Html.Unsupported.Attribute", "align")]
    // A Content-Security-Policy in head drops style sheets and style attributes in Chromium.
    [InlineData("<meta http-equiv=\"Content-Security-Policy\" content=\"style-src 'none'\"><style>p{color:red}</style><p>a", "Documents.Html.Unsupported.Attribute", "Content-Security-Policy")]
    [InlineData("<meta http-equiv=\"content-security-policy\" content=\"default-src 'self'\"><p style=\"color:red\">a", "Documents.Html.Unsupported.Attribute", "Content-Security-Policy")]
    [InlineData("<meta http-equiv=\"Content-Security-Policy\" content=\"style-src-attr 'none'\"><p style=\"margin-left:3px\">a", "Documents.Html.Unsupported.Attribute", "Content-Security-Policy")]
    // A color-scheme meta other than normal or light.
    [InlineData("<meta name=color-scheme content=dark><p>a", "Documents.Html.Unsupported.Attribute", "color scheme")]
    [InlineData("<meta name=color-scheme content=\"light dark\"><p>a", "Documents.Html.Unsupported.Attribute", "color scheme")]
    [InlineData("<p>a</p><meta name=color-scheme content=dark>", "Documents.Html.Unsupported.Attribute", "color scheme")]
    // autofocus: Chromium focuses the element.
    [InlineData("<a href=\"#y\" autofocus>l</a>", "Documents.Html.Unsupported.Attribute", "autofocus")]
    [InlineData("<style>div:focus{color:red}</style><div tabindex=0 autofocus>x</div>", "Documents.Html.Unsupported.Attribute", "autofocus")]
    // The charset of a style sheet link: Chromium decodes the sheet by it.
    [InlineData("<link rel=stylesheet charset=utf-16 href=x.css><p>a", "Documents.Html.Unsupported.Attribute", "charset")]
    // Elements Chromium gives a layout of its own.
    [InlineData("<fencedframe></fencedframe><p>a", "Documents.Html.Unsupported.Element", "fencedframe")]
    [InlineData("<geolocation></geolocation><p>a", "Documents.Html.Unsupported.Element", "geolocation")]
    // An image with alt text that is not there: Chromium shows the alt text inline, and its size computes to auto.
    [InlineData("<img src=missing.png alt=logo width=100 height=40>", "Documents.Html.Unsupported.Attribute", "alt text")]
    [InlineData("<img src=missing.png alt=logo style=\"width:100px;height:40px\">", "Documents.Html.Unsupported.Attribute", "alt text")]
    [InlineData("<img src=missing.png alt=logo width=100>", "Documents.Html.Unsupported.Attribute", "alt text")]
    [InlineData("<img src=\"\" alt=logo width=100 height=40>", "Documents.Html.Unsupported.Attribute", "alt text")]
    [InlineData("<img alt=logo width=100 height=40>", "Documents.Html.Unsupported.Attribute", "alt text")]
    [InlineData("<style>img{width:100px;height:40px;border:1px solid}</style><img src=missing.png alt=logo>", "Documents.Html.Unsupported.Attribute", "alt text")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\" alt=logo width=100 height=40>", "Documents.Html.Unsupported.Attribute", "alt text")]
    public void What_Chromium_computes_by_a_rule_the_engine_does_not_keep_is_refused(string body, string code, string named)
    {
        var refusals = Parts(Refused(Doctype + body, Refuse)).ToList();

        refusals.Should().Contain(error => error.Code.ToString() == code && error.Message.Contains(named, StringComparison.Ordinal));
    }

    [Fact]
    public void An_image_the_resolver_gives_keeps_its_size_and_one_that_is_no_image_is_refused()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var resources = new Dictionary<string, byte[]> { ["https://example.org/x.png"] = png, ["https://example.org/notanimage.txt"] = "text"u8.ToArray() };
        HtmlResource? Resolve(string url) => resources.TryGetValue(url, out var bytes) ? new HtmlResource(bytes) : null;
        var options = Refuse with { Resources = Resolve, BaseUrl = "https://example.org/" };

        var image = Accepted(Doctype + "<img id=a src=x.png alt=logo width=100 height=40>", options).ById("a");
        (image.Value("width"), image.Value("height"), image.Value("aspect-ratio")).Should().Be(("100px", "40px", "auto 100 / 40"));
        Parts(Refused(Doctype + "<img src=notanimage.txt alt=logo width=100 height=40>", options)).Should().ContainSingle()
            .Which.Metadata![ErrorMetadataKeys.Value].Should().Be("alt");
    }

    [Fact]
    public void A_form_control_that_is_not_drawn_is_accepted_with_the_engines_values_a_listed_limit()
    {
        // Chromium 159 gives the hidden textarea its user-agent style (a 1px solid border in rgb(118, 118, 118), a white
        // background); the engine has no style for form controls, and accepts one only when it is not drawn.
        var styled = Accepted(Doctype + "<p>x</p><textarea id=t hidden>text</textarea>");
        var textarea = styled.ById("t");

        (textarea.Value("display"), textarea.Value("border-top-style"), textarea.Value("border-top-width"), textarea.Value("background-color"))
            .Should().Be(("none", "none", "0px", "rgba(0, 0, 0, 0)"));
    }
}
