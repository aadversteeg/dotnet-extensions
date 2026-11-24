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
            "libs/documents/tools/reference/chromium/html-tree/** text eol=lf",
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
        Line("The tables are generated from the code by `UnitTests.Extensions.Documents.Html/ProfileDocumentTests.cs`; do not edit.");
        Line("The prose around them and the table of presentational attributes are written by hand in that test and in");
        Line("`Html/PresentationalHints.cs` (`Documented`); the corpus checks the attributes against Chromium. What the HTML");
        Line("front-end supports, what it ignores by rule (no effect in a static paged document) and what it refuses. Valid input");
        Line("that is not listed as supported or ignored is refused: a diagnostic of class Refusal with its line and column");
        Line("(`HtmlStyleDiagnostics.ClassOf`), which fails the document unless the caller asked for warnings only. The");
        Line("reference is Chromium 159; see `docs/architecture.md`, \"HTML/CSS front-end\".");
        Line();
        Line("## Properties");
        Line();
        Line("Longhands with every value the engine supports. A value CSS defines that is not listed is refused. So is a value that");
        Line("does not match the engine's grammar of a known property: a typing error such as `color: 12px`, or valid CSS beyond this");
        Line("profile such as `vertical-align: 5px`. The engine cannot tell the two apart, and a browser drops the first and uses the");
        Line("second, so both get the one answer \"not valid or not supported in this profile\". The same holds for a selector or media");
        Line("query that does not parse and an unknown at-rule. Dropped with a warning, as in a browser, is only what CSS Syntax 3");
        Line("recovers from (an unclosed block, a declaration without a colon), a top-level `!` other than `!important`, a rule in a");
        Line("place CSS does not allow, another browser engine's property (`-moz-`, `-ms-`, `-o-`) and a `var()` that is cyclic or");
        Line("names nothing.");
        Line();
        Line("| Property | Inherited | Initial | Supported values | Defined in |");
        Line("| --- | --- | --- | --- | --- |");
        foreach (var property in PropertyRegistry.Longhands.Where(property => property.Support == PropertySupport.Supported).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Line($"| `{property.Name}` | {(property.Inherited ? "yes" : "no")} | `{property.Initial}` | {Code(property.Supported)} | {Escape(property.Specification)} |");
        }

        Line();
        Line("`overflow-x` and `overflow-y`: `hidden` on one axis while the other stays `visible` is refused, since Chromium");
        Line("computes the visible axis to auto then (Known limits); set both, as `overflow: hidden` does. `clip` does not scroll,");
        Line("so `clip` beside `visible` is accepted and kept, as in Chromium.");
        Line();
        Line("`width` on a table part: a table is drawn at `auto` or `100%`; a `col` takes a length, or a percentage in the fixed");
        Line("layout (`table-layout: fixed` on a table of width `100%`) when every other column is a percentage or has no width");
        Line("and, beside a column without a width, the percentages add up to less than 100. A percentage on a `col` of the");
        Line("automatic layout, a width on a cell (`td`, `th`) and on a `colgroup` are refused. A `tfoot` is drawn after the body");
        Line("rows, once; Chromium repeats a small one on every page of a table that breaks (Known limits).");
        Line();
        Line("### Supported at the initial value only");
        Line();
        Line("What the layout always does: a declaration that sets one of these to its initial value is accepted, so resets and");
        Line("the user-agent style sheet are not refused. So is a value in the Accepted column: aspect-ratio auto with a ratio,");
        Line("which the width and height attributes of img make; an image with a natural ratio uses its own, as in Chromium, and");
        Line("on any other element stage 4 applies the ratio or refuses it. A zero length where the initial value is a keyword or");
        Line("a percentage is the same value and accepted too: `letter-spacing: 0` (computed normal), `background-position-x: 0`");
        Line("and `background-position-y: 0` (computed 0px, as Chromium keeps it). Any other value is refused.");
        Line();
        Line("| Property | Initial | Accepted | Defined in |");
        Line("| --- | --- | --- | --- |");
        foreach (var property in PropertyRegistry.Longhands.Where(property => property.Support == PropertySupport.InitialOnly).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Line($"| `{property.Name}` | `{property.Initial}` | {Code(property.Supported)} | {Escape(property.Specification)} |");
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
        Line("Each was checked against Chromium 159's print rendering; the values that change it are refused instead.");
        Line();
        Line("| Property | Why | Refused instead |");
        Line("| --- | --- | --- |");
        foreach (var ignored in PropertyRegistry.Ignored.OrderBy(ignored => ignored.Name, StringComparer.Ordinal))
        {
            Line($"| `{ignored.Name}` | {Escape(ignored.Reason)} | {Escape(ignored.RefusedValues ?? string.Empty)} |");
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
        Line("- Custom properties and var() (CSS Variables 1), with fallbacks; missing values make the property unset, as in a browser; revert and revert-layer made by substitution roll the cascade back. Refused: a cycle of var() references (Chromium does not make every member invalid), a '!' inside a var(), env(), attr(), if().");
        Line("- CSS-wide keywords initial, inherit, unset, revert and revert-layer (CSS Cascade 5 §7.3). An important revert-layer rolls back the normal declarations of its own layer, of the later layers and the unlayered ones too, so the value comes from the layers below it (§7.3.5); in the style attribute it also rolls back the attribute's normal declarations, as in Chromium 159.");
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
        Line("Pseudo-elements supported: " + string.Join(", ", PseudoClasses.AllElements.Where(entry => entry.Support == SelectorSupport.Supported).Select(entry => $"`::{entry.Name}` ({entry.Note})")) + ".");
        Line();
        Line("Pseudo-elements never draw in an accepted document and are ignored by rule: " + string.Join(", ", PseudoClasses.AllElements.Where(entry => entry.Support == SelectorSupport.NeverMatches).Select(entry => $"`::{entry.Name}{(entry.Functional ? "()" : string.Empty)}`").Distinct()) + ". The others are refused: "
            + string.Join(", ", PseudoClasses.AllElements.Where(entry => entry.Support == SelectorSupport.Unsupported).Select(entry => $"`::{entry.Name}{(entry.Functional ? "()" : string.Empty)}`").Distinct()) + ", and Chromium's other `::-webkit-*`.");
        Line();
        Line("## At-rules");
        Line();
        Line("- Supported: @media (media types; the width and height features with Chromium's 1/64px tolerance, other features");
        Line("  refused), @supports (answered as Chromium 159 would where the engine can tell, refused where it cannot), @import (through the caller's resolver, with layer() and supports()), @layer, @font-face and");
        Line("  @page (read as values for the layout), CSS Nesting 1 inside style rules.");
        Line("- Ignored by rule: " + string.Join(", ", RuleBuilder.IgnoredAtRules.Order(StringComparer.Ordinal).Select(name => "@" + name)) + ".");
        Line("- Refused: " + string.Join(", ", RuleBuilder.UnsupportedAtRules.Order(StringComparer.Ordinal).Select(name => "@" + name)) + ", and every");
        Line("  other at-rule the engine does not know, a vendor-prefixed one too (`@-moz-document`); Chromium drops those.");
        Line("- Read without effect: @charset (the encoding is decided before the sheet is parsed).");
        Line();
        Line("Page-margin boxes (CSS Paged Media 3 §5) are drawn for `@page` and `@page :first`: the top and bottom rows");
        Line("(`@top-left`, `@top-center`, `@top-right`, `@bottom-left`, `@bottom-center`, `@bottom-right`), their widths as");
        Line("§5.3.2 shares them, each box with its margin, padding, solid borders, background colour, text alignment and");
        Line("vertical alignment (top, middle, bottom), and its content: strings, `counter(page)`, `counter(pages)` and images");
        Line("(`url()`, or `image-set()` of one URL and its resolution) on the baseline as an `img` in a line, at their natural");
        Line("size divided by the resolution, the URL resolved against the style sheet that holds the `content` declaration;");
        Line("an image that does not come or does not decode is refused there. The");
        Line("page context inherits from the root element, `font-family` too; a face that only a margin box uses is drawn,");
        Line("where Chromium prints its fallback serif face (it has not loaded the face then; case `pm-family-unused`). Refused at their");
        Line("place: the corner and side boxes, other page selectors, `width` and `height` on a margin box, the size and");
        Line("margins of `@page :first`, and content wider than its box (`Documents.Layout.MarginBoxOverflow`), which");
        Line("Chromium draws over its neighbours. Content taller than its box is drawn as Chromium draws it: from the top of");
        Line("the box whatever its vertical alignment, unclipped, under the body; the box keeps its height.");
        Line();
        Line("## HTML");
        Line();
        Line("- The user-agent style sheet: `Ave.Extensions.Documents.Html/UserAgent/html.css` (HTML Standard §15, as Chromium 159");
        Line("  applies it).");
        Line("- A document in quirks mode is refused (add `<!DOCTYPE html>`).");
        Line("- Refused when drawn (display not none): " + string.Join(", ", HtmlStyleResolver.FormControls) + ".");
        Line("- Refused wherever they are: " + string.Join(", ", HtmlStyleResolver.OutsideProfile.Keys.Order(StringComparer.Ordinal)) + ", svg and math (their own");
        Line("  matching and style rules), and an element whose user-agent style is outside the profile (sub, sup, del, s, ruby …).");
        Line("- Scripts are not run, so a script element a browser runs (no type, a JavaScript type, module, importmap) and every");
        Line("  event handler attribute (`on…`) are refused; a data block (any other type) is not.");
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
