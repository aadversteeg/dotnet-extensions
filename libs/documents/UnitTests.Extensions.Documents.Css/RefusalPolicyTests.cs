using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Rules;
using static UnitTests.Extensions.Documents.Css.TestTree;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>
/// Input above CSS Syntax 3 that the engine cannot read is either invalid (Chromium drops it) or valid CSS beyond this
/// profile (Chromium uses it), and the engine cannot tell which: its one answer is a refusal, "not valid or not
/// supported in this profile" (the refusal policy in docs/architecture.md). Each input here is one Chromium 159 takes
/// and the engine once dropped with a warning, so the document was accepted with a different style.
/// </summary>
public class RefusalPolicyTests
{
    private static TestElement Doc(params object[] body) => E("html", E("body", body));

    private static List<Ave.Extensions.ErrorPaths.Error> Refusals(Computed computed) =>
        [.. computed.Diagnostics.Where(error => CssDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal)];

    [Theory]
    [InlineData("vertical-align: 5px")]
    [InlineData("vertical-align: -webkit-baseline-middle")]
    [InlineData("display: -webkit-flex")]
    [InlineData("color-scheme: light dark")]
    [InlineData("color: -webkit-link")]
    [InlineData("font-size: -webkit-xxx-large")]
    [InlineData("text-decoration-line: underline underline")]
    [InlineData("color: 12px")]
    public void A_value_the_grammar_does_not_read_is_refused_in_the_authors_words(string declaration)
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, $"p {{ {declaration} }}");

        var refusal = Refusals(computed).Should().ContainSingle().Subject;
        refusal.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        refusal.Message.Should().StartWith($"'{declaration}' is not valid or not supported in this profile");
    }

    [Fact]
    public void A_value_the_grammar_does_not_read_after_var_substitution_is_refused()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "p { --a: 5px; vertical-align: var(--a) }");

        Refusals(computed).Should().ContainSingle().Which.Message.Should().Contain("not valid or not supported in this profile");
    }

    [Fact]
    public void A_refused_value_that_loses_the_cascade_refuses_nothing()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "#x { vertical-align: top } p { vertical-align: 5px }");

        Refusals(computed).Should().BeEmpty();
        computed.Value(root["x"], "vertical-align").Should().Be("top");
    }

    [Fact]
    public void A_selector_that_does_not_parse_is_refused()
    {
        var sheet = CssParser.ParseStyleSheet("p:bogus-state { color: red }", new CssSourceOptions("a.css")).Value;

        var refusal = sheet.Diagnostics.Should().ContainSingle().Subject;
        refusal.Code.Should().Be(CssErrorCodes.Unsupported.Selector);
        refusal.Message.Should().StartWith("The selector 'p:bogus-state' is not valid or not supported in this profile");
    }

    [Fact]
    public void Host_with_a_forgiving_complex_argument_parses_so_the_rest_of_the_list_applies()
    {
        var root = Doc(E("p", A("id", "t")));
        var computed = Compute(root, ":host(:is(div .foo)), #t { margin-top: 1px }");

        // Chromium 159: the rule applies to #t. :host itself is outside the profile (shadow trees).
        computed.Value(root["t"], "margin-top").Should().Be("0.75pt");
        Refusals(computed).Should().ContainSingle().Which.Message.Should().Contain(":host()");
        CssParser.ParseStyleSheet(":host(:not(.a .b)) {}", new CssSourceOptions("a.css")).Value.Diagnostics
            .Should().ContainSingle().Which.Message.Should().Contain("not valid or not supported in this profile");
    }

    [Theory]
    [InlineData("(-webkit-min-device-pixel-ratio: 2)")]
    [InlineData("(-webkit-max-device-pixel-ratio: 0)")]
    [InlineData("(bogus-feature: 1)")]
    [InlineData("(width: red)")]
    [InlineData("screen and")]
    public void A_media_query_the_engine_cannot_evaluate_is_refused_never_false(string query)
    {
        var sheet = CssParser.ParseStyleSheet($"@media {query} {{ p {{ color: red }} }}", new CssSourceOptions("a.css")).Value;

        sheet.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(CssErrorCodes.Unsupported.MediaFeature);
    }

    [Theory]
    [InlineData("@bogus;")]
    [InlineData("@layer initial { p {} }")]
    [InlineData("@supports (display: block) and { p {} }")]
    public void An_at_rule_the_engine_cannot_read_is_refused(string rule)
    {
        var sheet = CssParser.ParseStyleSheet(rule, new CssSourceOptions("a.css")).Value;

        var refusal = sheet.Diagnostics.Should().ContainSingle().Subject;
        refusal.Code.Should().Be(CssErrorCodes.Unsupported.AtRule);
        refusal.Message.Should().Contain("is not valid or not supported in this profile");
    }

    [Fact]
    public void An_unreadable_rule_before_an_import_does_not_block_it()
    {
        var sheet = CssParser.ParseStyleSheet("@bogus; @import 'red.css'; @media bogus-query! {} @import 'late.css';", new CssSourceOptions("a.css")).Value;

        // Chromium 159 imports red.css: only a valid rule closes the place for @import.
        sheet.Value.Rules.OfType<ImportRule>().Select(import => import.Url).Should().Equal("red.css");
        sheet.Diagnostics.Should().Contain(error => error.Code == CssErrorCodes.Invalid.Rule && error.Message.Contains("@import after another rule"));
    }

    [Fact]
    public void Selection_and_placeholder_never_draw_on_paper_and_are_ignored_by_rule()
    {
        var sheet = CssParser.ParseStyleSheet("::selection { color: red } input::placeholder { color: red }", new CssSourceOptions("a.css")).Value;

        sheet.Diagnostics.Select(error => error.Code).Should().Equal(CssErrorCodes.Ignored.Selector, CssErrorCodes.Ignored.Selector);
    }
}
