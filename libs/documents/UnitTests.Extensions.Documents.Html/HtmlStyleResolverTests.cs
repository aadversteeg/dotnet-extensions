using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

public class HtmlStyleResolverTests
{
    private const string Doctype = "<!DOCTYPE html>";

    private static IEnumerable<Error> Parts(Error error) => error.Code == ErrorCodes.Aggregate ? error.Parts() : [error];

    [Fact]
    public void Refusals_fail_the_document_by_default_and_are_diagnostics_when_only_warned()
    {
        const string html = Doctype + "<p style=\"float: left; color: red\">x</p><div style=\"display: flex\"></div>";

        var refused = Refused(html);
        Parts(refused).Select(error => error.Code).Should().Equal(CssErrorCodes.Unsupported.Value, CssErrorCodes.Unsupported.Value);
        var styled = Styled(html, Warn);
        styled.ByName("p").Value("color").Should().Be("rgb(255, 0, 0)");
        styled.Diagnostics.Count(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal).Should().Be(2);
    }

    [Fact]
    public void A_document_without_an_element_fails_instead_of_throwing()
    {
        var parsed = new Ave.Extensions.Documents.Html.Parsing.HtmlParseResult(new Ave.Extensions.Documents.Html.Parsing.HtmlDocument(Ave.Extensions.Documents.Html.Parsing.DocumentMode.NoQuirks, []), [], "made.html");

        HtmlStyleResolver.Resolve(parsed, Warn).Match(_ => HtmlStyleErrorCodes._, error => error.Code).Should().Be(HtmlStyleErrorCodes.NoDocumentElement);
    }

    [Fact]
    public void A_cell_with_no_table_around_it_is_refused()
    {
        // Only a tree made in code has one. Chromium 159 gives it a padding of 1px that an author's revert keeps
        // (measured through script: td and th made by createElement in a div), which no presentational hint gives.
        static Ave.Extensions.Documents.Html.Parsing.HtmlElement Element(string name, params Ave.Extensions.Documents.Html.Parsing.HtmlNode[] children) =>
            new(Ave.Extensions.Documents.Html.Parsing.ElementNamespace.Html, name, [], [.. children], null, TextPosition.Start);
        var document = new Ave.Extensions.Documents.Html.Parsing.HtmlDocument(Ave.Extensions.Documents.Html.Parsing.DocumentMode.NoQuirks,
            [Element("html", Element("head"), Element("body", Element("div", Element("td"), Element("th")), Element("table", Element("td"))))]);
        var parsed = new Ave.Extensions.Documents.Html.Parsing.HtmlParseResult(document, [], "made.html");

        var styled = HtmlStyleResolver.Resolve(parsed, Warn).Match(value => value, error => throw new InvalidOperationException(error.Message));
        styled.Diagnostics.Where(error => error.Code == HtmlStyleErrorCodes.Unsupported.Element).Select(error => error.Metadata![ErrorMetadataKeys.Value])
            .Should().Equal("td", "th");
        Parts(HtmlStyleResolver.Resolve(parsed, Warn with { Unsupported = UnsupportedPolicy.Refuse }).Match(_ => throw new InvalidOperationException("accepted"), error => error))
            .Select(error => error.Code).Should().Equal(HtmlStyleErrorCodes.Unsupported.Element, HtmlStyleErrorCodes.Unsupported.Element);
    }

    [Fact]
    public void A_document_in_quirks_mode_is_refused()
    {
        Parts(Refused("<p>no doctype</p>")).Should().ContainSingle(error => error.Code == HtmlStyleErrorCodes.Unsupported.QuirksMode);
        Resolve(Doctype + "<p>x</p>", Warn with { Unsupported = UnsupportedPolicy.Refuse }).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Linked_style_sheets_and_imports_come_through_the_resolver_once_per_absolute_url()
    {
        var asked = new List<string>();
        var files = new Dictionary<string, string>
        {
            ["https://example.org/css/site.css"] = "@import 'parts/colours.css'; p { margin-top: 3px }",
            ["https://example.org/css/parts/colours.css"] = "p { color: rgb(1, 2, 3) }",
            ["https://example.org/other.css"] = "@import url(css/parts/colours.css); p { margin-left: 4px }",
        };
        HtmlResource? Resolve(string url)
        {
            asked.Add(url);
            return files.TryGetValue(url, out var text) ? new HtmlResource(Encoding.UTF8.GetBytes(text)) : null;
        }

        var html = Doctype + "<head><base href=\"https://example.org/doc/\"><link rel=stylesheet href=\"../css/site.css\">"
            + "<link rel=\"stylesheet\" href=\"/other.css\"><link rel=\"alternate stylesheet\" href=\"alt.css\"><link rel=stylesheet href=\"print.css\" media=\"screen\"></head><p>x</p>";
        var styled = Styled(html, Warn with { Resources = Resolve, BaseUrl = "https://example.org/index.html", AllowBaseElement = true });

        var p = styled.ByName("p");
        p.Value("color").Should().Be("rgb(1, 2, 3)");
        p.Value("margin-top").Should().Be("3px");
        p.Value("margin-left").Should().Be("4px");
        asked.Should().BeEquivalentTo(["https://example.org/css/site.css", "https://example.org/css/parts/colours.css", "https://example.org/other.css"]).And.OnlyHaveUniqueItems();
        // The two sheets that do not apply say why, as class Ignored.
        styled.Diagnostics.Select(error => error.Message).Should().Equal(
            "The style sheet of this link element does not apply: it is an alternate style sheet.",
            "The style sheet of this link element does not apply: its media 'screen' do not match.");
        styled.Diagnostics.Should().OnlyContain(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Ignored);
    }

    [Fact]
    public void A_style_sheet_the_resolver_does_not_give_is_refused_where_it_is_named()
    {
        var error = Parts(Refused(Doctype + "<head>\n<link rel=stylesheet href=missing.css></head>")).Single();

        error.Code.Should().Be(HtmlStyleErrorCodes.Resource.NotFound);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("missing.css");
    }

    [Fact]
    public void Style_elements_apply_in_tree_order_with_media_and_the_preferred_title()
    {
        var html = Doctype + "<style>p { color: red }</style><style media=print>p { margin-top: 1px }</style>"
            + "<style media=screen>p { margin-top: 2px }</style><style type=text/x-other>p { color: blue }</style>"
            + "<style title=A>p { margin-left: 3px }</style><style title=B>p { margin-left: 4px }</style><p>x</p><style>p { color: green }</style>";
        var p = Styled(html).ByName("p");

        p.Value("color").Should().Be("rgb(0, 128, 0)");
        p.Value("margin-top").Should().Be("1px");
        p.Value("margin-left").Should().Be("3px");
    }

    [Fact]
    public void Diagnostics_point_into_the_document()
    {
        var styled = Styled(Doctype + "\n<style>\np { color: 12px }\n</style>\n<p style=\"margin-top: nope\">x</p>");

        var places = styled.Diagnostics.Where(error => error.Code == CssErrorCodes.Unsupported.Value)
            .Select(error => ((int)error.Metadata![ErrorMetadataKeys.SourceLine]!, (int)error.Metadata![ErrorMetadataKeys.SourceColumn]!)).Order().ToList();
        places.Should().Equal((3, 5), (5, 11));
        styled.Diagnostics.Should().OnlyContain(error => (string)error.Metadata![ErrorMetadataKeys.SourceFile]! == "doc.html");
    }

    [Theory]
    [InlineData("<p style=\"bogus:1\">x", 2, 11)]
    [InlineData("<p style=\"color:red&#59; bogus:1\">x", 2, 26)]
    [InlineData("<p style=\"color: red;&#10;  bogus: 1\">x", 2, 29)]
    [InlineData("<p style=\"color: red;\n  bogus: 1\">x", 3, 3)]
    [InlineData("<p style='&#x62;ogus: 1'>x", 2, 11)]
    [InlineData("<p style=bogus:1>x", 2, 10)]
    [InlineData("<style media=\"(bogus-feature)\"></style>", 2, 16)]
    public void Diagnostics_of_style_and_media_attributes_point_at_what_the_author_wrote(string markup, int line, int column)
    {
        var styled = Styled(Doctype + "\n" + markup, Warn);

        // The place of the character in the attribute value, through character references and line feeds.
        var diagnostic = styled.Diagnostics.First(error => error.Code != HtmlStyleErrorCodes.Ignored.StyleSheet);
        ((int)diagnostic.Metadata![ErrorMetadataKeys.SourceLine]!, (int)diagnostic.Metadata![ErrorMetadataKeys.SourceColumn]!).Should().Be((line, column));
    }

    [Fact]
    public void Diagnostics_of_a_style_element_with_several_texts_point_into_each_text()
    {
        // The text after a child element of an svg style element keeps its own place.
        var styled = Styled(Doctype + "\n<p id=t>x</p><svg><style>p{}<g/>\n#t{bogus:1}</style></svg>", Warn);

        var diagnostic = styled.Diagnostics.Single(error => error.Message.Contains("bogus", StringComparison.Ordinal));
        ((int)diagnostic.Metadata![ErrorMetadataKeys.SourceLine]!, (int)diagnostic.Metadata![ErrorMetadataKeys.SourceColumn]!).Should().Be((3, 4));
    }

    [Fact]
    public void The_language_is_inherited_from_lang_and_xml_lang_and_the_meta_default()
    {
        var styled = Styled(Doctype + "<html><head><meta http-equiv=content-language content=\"de, en\"></head><body><p id=a>x</p>"
            + "<div lang=fr><p id=b>x</p><p id=c lang=\"\">x</p><svg xml:lang=nl lang=it><text id=d>x</text></svg></div></body></html>");

        styled.ById("a").Language.Should().Be("de");
        styled.ById("b").Language.Should().Be("fr");
        styled.ById("c").Language.Should().Be(string.Empty);
        styled.Root.Elements().Single(element => element.Element.LocalName == "svg").Language.Should().Be("nl");
    }

    [Fact]
    public void Text_takes_its_parents_style_and_template_contents_are_not_styled()
    {
        var styled = Styled(Doctype + "<p style=\"color: red\">text<!-- comment --></p><template><p>inside</p></template>");

        var p = styled.ByName("p");
        p.Children.Should().ContainSingle().Which.Should().BeOfType<StyledText>().Which.Style.Should().BeSameAs(p.Style);
        styled.Root.Elements().Count(element => element.Element.LocalName == "p").Should().Be(1);
        styled.ByName("template").Children.Should().BeEmpty();
    }

    [Fact]
    public void Form_controls_are_refused_unless_they_are_not_drawn()
    {
        var errors = Parts(Refused(Doctype + "<input type=hidden><input><div hidden><button>b</button></div><select></select>")).ToList();

        errors.Where(error => error.Code == HtmlStyleErrorCodes.Unsupported.Element).Select(error => (string)error.Metadata![ErrorMetadataKeys.Value]!)
            .Should().Equal("input", "select");
    }

    [Theory]
    [InlineData("<img src=x alt=x align=left>", "align")]
    [InlineData("<img src=x alt=x align=texttop>", "align")]
    [InlineData("<table align=right></table>", "align")]
    [InlineData("<table frame=box></table>", "frame")]
    [InlineData("<table rules=all></table>", "rules")]
    [InlineData("<body background=x.png>", "background")]
    [InlineData("<br clear=all>", "clear")]
    [InlineData("<iframe width=10></iframe>", "width")]
    public void Presentational_attributes_the_engine_cannot_map_are_refused_at_the_attribute(string markup, string attribute)
    {
        var error = Parts(Refused(Doctype + markup)).First(item => item.Code == HtmlStyleErrorCodes.Unsupported.Attribute);

        error.Metadata![ErrorMetadataKeys.Value].Should().Be(attribute);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(1);
    }

    [Fact]
    public void Presentational_hints_lose_to_author_rules_and_to_the_style_attribute()
    {
        var styled = Styled(Doctype + "<style>#a { width: 10px }</style><table id=a width=50 bgcolor=red style=\"background-color: blue\"><tr><td>x</td></tr></table>");

        styled.ById("a").Value("width").Should().Be("10px");
        styled.ById("a").Value("background-color").Should().Be("rgb(0, 0, 255)");
    }

    [Theory]
    [InlineData("chucknorris", "rgb(192, 0, 0)")]
    [InlineData("#abc", "rgb(170, 187, 204)")]
    [InlineData("abc", "rgb(10, 11, 12)")]
    [InlineData("RED", "rgb(255, 0, 0)")]
    [InlineData(" #0f0 ", "rgb(0, 255, 0)")]
    [InlineData("#123456789", "rgb(18, 69, 120)")]
    [InlineData("transparent", null)]
    [InlineData("", null)]
    public void Legacy_colour_values_follow_the_HTML_Standard(string value, string? expected) =>
        AttributeValues.LegacyColor(value)?.ToString().Should().Be(expected);

    [Theory]
    [InlineData("100", "100px")]
    [InlineData(" 50% ", "50%")]
    [InlineData("12.5px", "12.5px")]
    [InlineData("3.", "3px")]
    [InlineData("x", null)]
    [InlineData("-5", null)]
    public void Dimension_values_follow_the_HTML_Standard(string value, string? expected) =>
        AttributeValues.Dimension(value)?.ToString().Should().Be(expected);

    [Theory]
    [InlineData("1", "x-small")]
    [InlineData("+1", "large")]
    [InlineData("-2", "x-small")]
    [InlineData("9", "xxx-large")]
    [InlineData("x", null)]
    public void Legacy_font_sizes_follow_the_HTML_Standard(string value, string? expected) =>
        AttributeValues.LegacyFontSize(value).Should().Be(expected);
}
