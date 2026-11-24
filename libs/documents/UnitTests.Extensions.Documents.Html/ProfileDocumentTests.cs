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
        Line("Pseudo-elements never draw in an accepted document and are ignored by rule: " + string.Join(", ", PseudoClasses.AllElements.Where(entry => entry.Support == SelectorSupport.NeverMatches).Select(entry => $"`::{entry.Name}{(entry.Functional ? "()" : string.Empty)}`").Distinct()) + ". The others are refused: "
            + string.Join(", ", PseudoClasses.AllElements.Where(entry => entry.Support != SelectorSupport.NeverMatches).Select(entry => $"`::{entry.Name}{(entry.Functional ? "()" : string.Empty)}`").Distinct()) + ", and Chromium's other `::-webkit-*`.");
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
        Line("- Links: an `a` with `href` inside a line of text is a link to a URI (a link annotation; in a tagged PDF a Link element),");
        Line("  around all of its content: text, images and the inline elements inside it. Its URI is the `href` resolved as HTML");
        Line("  resolves it (`LinkUrls`): white space at the ends and tabs and line breaks inside removed, the scheme in lower case, a");
        Line("  relative URL resolved against the document base only when that base is `http` or `https`, and the C0 controls, space,");
        Line("  `\"`, `<`, `>`, U+007F and every character beyond ASCII percent-encoded as UTF-8. Accepted are the schemes `http`, `https`,");
        Line("  `mailto` and `tel` (`LinkUris`, the model's one rule): a link opens a web page, a mail program or a phone call. Its");
        Line("  description (`/Contents`, which PDF/UA-1 needs) is the `title`, else the link's text, else the `alt` of an image in it,");
        Line("  else the URI. Refused at the start tag (`Documents.Html.Unsupported.Link`, with the `href`): any other scheme");
        Line("  (`javascript`, `vbscript`, `file`, `data`, `ftp`, …), also in mixed case or with white space, tabs or line breaks in it;");
        Line("  a relative URL without an `http` or `https` base (a document read from a file has a `file:` base, and Chromium would");
        Line("  write a path of the printing machine); a host with characters beyond ASCII; an `a` with `href` that is not inside a");
        Line("  line (`display: block`, a table display). An `a` without `href`, and an `a` with `href` that draws nothing, is no link,");
        Line("  as in Chromium. A link in a table header row that repeats on every page leads from its first copy only (the copies");
        Line("  are artifacts); Chromium writes it on every page (a listed difference, case `lk-header-row`).");
        Line("- Links inside the document: an `href` that starts with `#` (also under a `base`) leads to the element the HTML");
        Line("  Standard's \"indicated part of the document\" names: `#` alone, and `#top` in any case when no element has that id, the");
        Line("  top of the first page; else the first element with that `id`, else the first `a` with that `name`, then both again");
        Line("  with the fragment percent-decoded. The PDF's destination is the page where the target starts and the top of its box");
        Line("  (or of its first line part, for an inline element), the left edge and the zoom unchanged; no names are written.");
        Line("  Refused at the start tag: a name no element has (Chromium writes no link), and an element that makes no box or text");
        Line("  a link can lead to (not rendered, an inline element that draws nothing, an image).");
        Line("- Languages: an element's language (`xml:lang` in the XML namespace, else `lang`, else its parent's) that differs from");
        Line("  its parent's is written as `/Lang` on its structure element in a tagged PDF: a block's on its box's element, an inline");
        Line("  element's on a `Span` element made for it (an `a href`: on its `Link` element). The tag is written as given when it is");
        Line("  well-formed by BCP 47 (at most 64 characters); any other tag, and `lang=\"\"` (an unknown language, which a PDF/UA-1");
        Line("  `/Lang` cannot say), is refused at the start tag (`Documents.Html.Unsupported.Language`) and the element keeps its");
        Line("  parent's language. The root's language is the document's, in the catalog.");
        Line("- Table header cells: a `th` is a `TH` element whose `/Scope` says what it heads (`Html/Boxes/HtmlBoxGenerator.HeaderScopes.cs`).");
        Line("  `scope=row` and `scope=rowgroup` head the row, `scope=col` and `scope=colgroup` the column, in any ASCII case; PDF has");
        Line("  no scope for a group, so a group scope heads the cell's own row or column only. Without a valid `scope`, a `th` in a");
        Line("  `thead`, or in a row whose cells are all `th`, heads its column; any other `th` heads its row. No cell heads both.");
        Line("  Not read, and drawn as without them: the `headers` attribute of a cell (no `/Headers` and no element ids are");
        Line("  written, so a cell under headers that nest has only the scopes to name its headers), `scope` on a `td`, and `abbr`.");
        Line("- Document metadata: the first `title` is the title. The first `meta` in tree order whose `name` is `author`, in any");
        Line("  ASCII case, with a `content` that is not empty after stripping white space at both ends is the author; the same for");
        Line("  `description`, written as the PDF's subject and XMP description (`HtmlMetadata`). Such a content longer than 4 096");
        Line("  characters, or a title or content with a control character (other than tab, line feed and carriage return) or a");
        Line("  character XML does not allow (`MetadataTexts`), is refused at the `meta` or the `title`");
        Line("  (`Documents.Html.Unsupported.Metadata`) and left out, not cleaned. Every other `meta` name (`keywords`, `generator`,");
        Line("  `date`, `application-name`, …) is ignored. Chromium writes neither the author nor the description into its PDF.");
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
