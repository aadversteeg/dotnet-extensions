using System.IO;
using System.Text;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.Html;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// docs/html-css-profile.md is generated from the code: the property registry, the selector catalog, the CSS reader's
/// lists of at-rules, units and functions, and the HTML front-end's tables. When the code changes, this test fails
/// until the document is generated again: set DOCUMENTS_UPDATE_PROFILE=1 and run it.
/// </summary>
public class ProfileDocumentTests
{
    [Fact]
    public void The_profile_document_is_the_code()
    {
        var path = Path.GetFullPath(Path.Combine(Styling.Reference(), "..", "..", "..", "..", "docs", "html-css-profile.md"));
        var generated = Generate();
        if (Environment.GetEnvironmentVariable("DOCUMENTS_UPDATE_PROFILE") == "1")
        {
            File.WriteAllText(path, generated);
        }

        File.Exists(path).Should().BeTrue("docs/html-css-profile.md is generated: run this test with DOCUMENTS_UPDATE_PROFILE=1");
        File.ReadAllText(path).Should().Be(generated, "the code changed: run this test with DOCUMENTS_UPDATE_PROFILE=1 and commit the document");
    }

    /// <summary>The generated document and the other committed text the tests compare byte for byte have LF line ends
    /// in every checkout (.gitattributes); without the rule a Windows checkout has CRLF and the comparison fails.</summary>
    [Fact]
    public void Committed_text_the_tests_compare_byte_for_byte_has_the_same_bytes_on_every_platform()
    {
        var root = Path.GetFullPath(Path.Combine(Styling.Reference(), "..", "..", "..", "..", "..", ".."));
        var attributes = File.ReadAllLines(Path.Combine(root, ".gitattributes")).Select(line => line.Trim()).ToList();

        attributes.Should().Contain([
            "libs/documents/docs/html-css-profile.md text eol=lf",
            "libs/documents/tools/reference/chromium/computed-style/** text eol=lf",
            "libs/documents/Ave.Extensions.Documents.Html/UserAgent/*.css text eol=lf",
            "libs/documents/Ave.Extensions.Documents.Html.Parsing/Tokenizing/*.txt text eol=lf",
        ]);
        File.ReadAllText(Path.Combine(root, "libs", "documents", "docs", "html-css-profile.md")).Should().NotContain("\r");
    }

    private static string Generate()
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');

        Line("# HTML and CSS profile (v0)");
        Line();
        Line("Generated from the code by `UnitTests.Extensions.Documents.Html/ProfileDocumentTests.cs`; do not edit. What the HTML");
        Line("front-end supports, what it ignores by rule (no effect in a static paged document) and what it refuses. Valid input");
        Line("that is not listed as supported or ignored is refused: a diagnostic of class Refusal with its line and column");
        Line("(`HtmlStyleDiagnostics.ClassOf`), which fails the document unless the caller asked for warnings only. The");
        Line("reference is Chromium 159; see `docs/architecture.md`, \"HTML/CSS front-end\".");
        Line();
        Line("## Properties");
        Line();
        Line("Longhands with every value the engine supports. A value CSS defines that is not listed is refused.");
        Line();
        Line("| Property | Inherited | Initial | Supported values | Defined in |");
        Line("| --- | --- | --- | --- | --- |");
        foreach (var property in PropertyRegistry.Longhands.Where(property => property.Support == PropertySupport.Supported).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Line($"| `{property.Name}` | {(property.Inherited ? "yes" : "no")} | `{property.Initial}` | {Code(property.Supported)} | {Escape(property.Specification)} |");
        }

        Line();
        Line("### Supported at the initial value only");
        Line();
        Line("What the layout always does: a declaration that sets one of these to its initial value is accepted (so resets and");
        Line("the user-agent style sheet are not refused); any other value is refused.");
        Line();
        Line("| Property | Initial | Defined in |");
        Line("| --- | --- | --- |");
        foreach (var property in PropertyRegistry.Longhands.Where(property => property.Support == PropertySupport.InitialOnly).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Line($"| `{property.Name}` | `{property.Initial}` | {Escape(property.Specification)} |");
        }

        Line();
        Line("### Shorthands");
        Line();
        Line("| Shorthand | Sets | Supported | Defined in |");
        Line("| --- | --- | --- | --- |");
        foreach (var shorthand in PropertyRegistry.Shorthands.OrderBy(shorthand => shorthand.Name, StringComparer.Ordinal))
        {
            Line($"| `{shorthand.Name}` | {string.Join(", ", shorthand.Longhands)} | {Code(shorthand.Supported)} | {Escape(shorthand.Specification)} |");
        }

        Line();
        Line("`all` takes only a CSS-wide keyword (initial, inherit, unset, revert, revert-layer).");
        Line();
        Line("### Ignored by rule");
        Line();
        Line("| Property | Why |");
        Line("| --- | --- |");
        foreach (var ignored in PropertyRegistry.Ignored.OrderBy(ignored => ignored.Name, StringComparer.Ordinal))
        {
            Line($"| `{ignored.Name}` | {Escape(ignored.Reason)} |");
        }

        Line();
        Line("## Values");
        Line();
        Line($"- Absolute lengths: {string.Join(", ", Units.Absolute.Keys.Order(StringComparer.Ordinal))}. Relative lengths the engine resolves: {string.Join(", ", Units.Relative.Order(StringComparer.Ordinal))} (ex and ch need the caller's font metrics, else they are refused). Refused: {string.Join(", ", Units.UnsupportedRelative.Order(StringComparer.Ordinal))}.");
        Line("- Math functions: calc(), min(), max(), clamp(); a result that still mixes a length and a percentage after computing is refused. Refused: "
            + string.Join(", ", Calc.OtherMathFunctions.Order(StringComparer.Ordinal).Select(name => name + "()")) + ", and typed arithmetic (multiplying or dividing dimensions).");
        Line("- Colours: the 148 named colours, transparent, currentcolor, #rgb, #rgba, #rrggbb, #rrggbbaa, rgb(), rgba(), hsl(), hsla(); system colours "
            + string.Join(", ", Colors.SystemColors.Keys.Order(StringComparer.Ordinal)) + ". Refused: "
            + string.Join(", ", Colors.OtherFunctions.Order(StringComparer.Ordinal).Select(name => name + "()")) + ", relative colour syntax.");
        Line("- Custom properties and var() (CSS Variables 1), with fallbacks; cycles and missing values make the property unset, as in a browser. Refused: env(), attr(), if().");
        Line();
        Line("## Selectors");
        Line();
        Line("Type, universal, ID, class, the attribute selectors with the i modifier, the four combinators, the nesting");
        Line("selector & (CSS Nesting 1). Pseudo-classes:");
        Line();
        Line("| Pseudo-class | Support | Note |");
        Line("| --- | --- | --- |");
        foreach (var entry in PseudoClasses.AllClasses.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            Line($"| `:{entry.Name}{(entry.Functional ? "()" : string.Empty)}` | {Support(entry.Support)} | {Escape(entry.Note)} |");
        }

        Line();
        Line("Pseudo-elements, all refused: " + string.Join(", ", PseudoClasses.AllElements.Select(entry => $"`::{entry.Name}{(entry.Functional ? "()" : string.Empty)}`").Distinct()) + ", and Chromium's `::-webkit-*`.");
        Line();
        Line("## At-rules");
        Line();
        Line("- Supported: @media (media types; the width and height features, other features refused), @supports (answered");
        Line("  as Chromium would), @import (through the caller's resolver, with layer() and supports()), @layer, @font-face and");
        Line("  @page (read as values for the layout), CSS Nesting 1 inside style rules.");
        Line("- Ignored by rule: " + string.Join(", ", RuleBuilder.IgnoredAtRules.Order(StringComparer.Ordinal).Select(name => "@" + name)) + ".");
        Line("- Refused: " + string.Join(", ", RuleBuilder.UnsupportedAtRules.Order(StringComparer.Ordinal).Select(name => "@" + name)) + ".");
        Line();
        Line("## HTML");
        Line();
        Line("- The user-agent style sheet: `Ave.Extensions.Documents.Html/UserAgent/html.css` (HTML Standard §15, as Chromium 159");
        Line("  applies it).");
        Line("- A document in quirks mode is refused (add `<!DOCTYPE html>`).");
        Line("- Refused when drawn (display not none): " + string.Join(", ", HtmlStyleResolver.FormControls) + ".");
        Line("- Style sheets: style elements, link rel=stylesheet (not alternate; the preferred title set), with media and type;");
        Line("  linked sheets and imports through the caller's `ResourceResolver`; a sheet it does not give is refused.");
        Line();
        Line("Presentational attributes (HTML Standard §15.2–§15.4):");
        Line();
        Line("| Element | Mapped | Refused |");
        Line("| --- | --- | --- |");
        foreach (var (elements, mapped, refused) in PresentationalHints.Documented)
        {
            Line($"| {elements} | {Escape(mapped)} | {Escape(refused)} |");
        }

        return text.ToString();
    }

    private static string Support(SelectorSupport support) => support switch
    {
        SelectorSupport.Supported => "supported",
        SelectorSupport.NeverMatches => "never matches (ignored by rule)",
        _ => "refused",
    };

    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);

    private static string Code(string text) => "`" + text.Replace("|", "\\|", StringComparison.Ordinal) + "`";
}
