using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Css;

public class CssParserTests
{
    private static readonly CssSourceOptions Options = new("sheet.css");

    private static string FirstSelectorRuleColor(StyleSheet sheet) =>
        string.Concat(((StyleRule)sheet.Rules[0]).Declarations[0].Value.OfType<CssToken>().Select(token => token.Text));

    [Fact]
    public void Bytes_with_a_byte_order_mark_are_decoded_by_it()
    {
        byte[] utf8 = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("p { color: \"é\" }")];
        byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("p { color: \"é\" }")];

        FirstSelectorRuleColor(CssParser.ParseStyleSheet(utf8, Options).Value.Value).Should().Be("é");
        FirstSelectorRuleColor(CssParser.ParseStyleSheet(utf16, Options).Value.Value).Should().Be("é");
    }

    [Fact]
    public void Without_a_mark_the_protocol_then_charset_then_the_document_encoding_decide()
    {
        var latin = Encoding.Latin1.GetBytes("@charset \"iso-8859-1\"; p { color: \"é\" }");
        var ascii = Encoding.ASCII.GetBytes("@charset \"windows-1252\"; p { color: red }");
        var utf16 = Encoding.Unicode.GetBytes("p { color: red }");

        // An encoding the engine does not decode: no style sheet at all, a failure (review 39 API-1).
        var refused = CssParser.ParseStyleSheet(latin, Options).Match(_ => throw new InvalidOperationException("expected a failure"), error => error);
        refused.Code.Should().Be(CssErrorCodes.Unsupported.Encoding);
        CssDiagnostics.ClassOf(refused.Code).Should().Be(DiagnosticClass.Refusal);

        CssParser.ParseStyleSheet(ascii, Options).Value.Value.Rules.Should().ContainSingle();
        CssParser.ParseStyleSheet(utf16, Options with { ProtocolEncoding = "UTF-16LE" }).Value.Value.Rules.Should().ContainSingle();
        CssParser.ParseStyleSheet(Encoding.UTF8.GetBytes("p { color: \"é\" }"), Options with { EnvironmentEncoding = "utf-8" }).Value.Value.Rules.Should().ContainSingle();
    }

    [Fact]
    public void Invalid_utf8_becomes_replacement_characters()
    {
        byte[] bytes = [.. Encoding.ASCII.GetBytes("p { color: \""), 0xFF, .. Encoding.ASCII.GetBytes("\" }")];

        FirstSelectorRuleColor(CssParser.ParseStyleSheet(bytes, Options).Value.Value).Should().Be("�");
    }

    [Fact]
    public void Nesting_past_the_limit_fails_the_reading()
    {
        var css = "p { color: " + new string('(', CssInputLimits.MaxDepth + 1) + " }";

        var error = CssParser.ParseStyleSheet(css, Options).Match(_ => throw new InvalidOperationException("expected a failure"), failure => failure);

        error.Code.Should().Be(CssErrorCodes.TooDeep);
        CssDiagnostics.ClassOf(error.Code).Should().Be(DiagnosticClass.Refusal);
        error.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(11 + CssInputLimits.MaxDepth, "the { is the first level, so the last ( is past the limit");
    }

    [Fact]
    public void A_sheet_past_the_length_limit_fails_the_reading()
    {
        var result = CssParser.ParseStyleSheet(new string(' ', CssInputLimits.MaxCharacters + 1), Options);

        result.Match(_ => CssErrorCodes.Parse.Syntax, error => error.Code).Should().Be(CssErrorCodes.TooLarge);
    }

    [Fact]
    public void A_media_query_list_past_the_limit_fails_the_reading_instead_of_matching_every_medium()
    {
        // Review 39 API-1: it came back as the empty list, which matches every medium.
        var deep = new string('(', CssInputLimits.MaxDepth + 1) + "print" + new string(')', CssInputLimits.MaxDepth + 1);

        CssParser.ParseMediaQueryList(deep, Options).Match(_ => CssErrorCodes.Parse.Syntax, error => error.Code).Should().Be(CssErrorCodes.TooDeep);
    }

    [Fact]
    public void A_style_attribute_keeps_the_declarations_that_parse_and_reports_where_the_others_were()
    {
        var result = CssParser.ParseDeclarations("color: red; : nope; margin: 0", Options with { Origin = new TextPosition(3, 10) }).Value;

        result.Value.Select(declaration => declaration.Name).Should().Equal("color", "margin");
        result.Value[1].Position.Should().Be(new TextPosition(3, 30));
        var error = result.Diagnostics.Should().ContainSingle(item => item.Code == CssErrorCodes.Parse.Syntax).Subject;
        error.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(22);
    }

    [Fact]
    public void Every_diagnostic_class_comes_from_the_code()
    {
        CssDiagnostics.ClassOf(CssErrorCodes.Invalid.Value).Should().Be(DiagnosticClass.Warning);
        CssDiagnostics.ClassOf(CssErrorCodes.Parse.Syntax).Should().Be(DiagnosticClass.Warning);
        CssDiagnostics.ClassOf(CssErrorCodes.Ignored.Property).Should().Be(DiagnosticClass.Ignored);
        CssDiagnostics.ClassOf(CssErrorCodes.Unsupported.AtRule).Should().Be(DiagnosticClass.Refusal);
        CssDiagnostics.ClassOf(CssErrorCodes.TooLarge).Should().Be(DiagnosticClass.Refusal);
    }

    [Fact]
    public void Unsupported_and_unknown_at_rules_are_refused_and_keyframes_ignored()
    {
        var result = CssParser.ParseStyleSheet("@container (width > 1px) { p {} } @keyframes k { from {} } @property --x { syntax: '*' } @foo;", Options).Value;

        result.Diagnostics.Select(error => error.Code).Should().Equal(
            CssErrorCodes.Unsupported.AtRule, CssErrorCodes.Ignored.AtRule, CssErrorCodes.Unsupported.AtRule, CssErrorCodes.Unsupported.AtRule);
        result.Diagnostics[^1].Message.Should().Be("'@foo' is not valid or not supported in this profile; the rule is skipped.");
    }

    [Fact]
    public void Font_face_and_page_rules_are_read()
    {
        var sheet = CssParser.ParseStyleSheet("@font-face { font-family: Body; src: url(body.woff2) format('woff2'), local(x); font-weight: 300 700 } @page :first { margin: 1cm; @top-center { content: 'x' } }", Options).Value;

        var face = sheet.Value.Rules.OfType<FontFaceRule>().Single();
        face.Family.Should().Be("Body");
        face.Sources.Should().Equal(new FontFaceSource("body.woff2", "woff2"));
        face.WeightRange.Should().Be((300, 700));
        sheet.Diagnostics.Should().ContainSingle(error => error.Code == CssErrorCodes.Unsupported.Value);
        var page = sheet.Value.Rules.OfType<PageRule>().Single();
        page.Selectors.Single().PseudoClasses.Should().Equal("first");
        page.Margins.Single().Name.Should().Be("top-center");
    }

    [Fact]
    public void Serialized_values_read_back_the_same()
    {
        foreach (var text in new[] { "a/**/b", "1px -2.5e3% #id url(x\\)y) \"q\\\"\" 1\\65 3", "calc(1px + 2px) [a] {b}", "--x - -->" })
        {
            var values = SyntaxParser.ParseComponentValues(CssSource.Of(text), (_, _) => { }).Values;
            var again = SyntaxParser.ParseComponentValues(CssSource.Of(CssSerializer.Serialize(values)), (_, _) => { }).Values;
            CssSerializer.Serialize(again).Should().Be(CssSerializer.Serialize(values), text);
            again.Count.Should().Be(values.Count, text);
        }
    }
}
