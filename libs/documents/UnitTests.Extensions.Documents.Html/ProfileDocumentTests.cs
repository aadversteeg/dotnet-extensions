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
        Line("profile such as `vertical-align: -webkit-baseline-middle`. The engine cannot tell the two apart, and a browser drops the first and uses the");
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
        Line("`border-style: inset` and `outset` on a block box: the top and left sides are drawn darker and the bottom and right");
        Line("sides lighter for `inset`, the other way round for `outset`, in two colours taken from the border colour as Chromium");
        Line("takes them (by the colour's luminance: a dark colour gets two lighter shades, a light one its darker shade and");
        Line("itself, any other a darker and a lighter shade; `currentcolor` gives the shades of #eeeeee whatever the text colour);");
        Line("where two sides of different colours meet, the corner is split on the line from its outer to its inner point (cases");
        Line("`bs-*`, `hr-*`). Refused: on a table or a part of one, whose lines are drawn solid, and inset beside outset sides on");
        Line("one box. Solid sides of more than one colour on a block box are split at the corners in the same way (cases");
        Line("`bs-solid-*`); a solid border of one colour is drawn as four rectangles. `hr` is drawn with its default style and");
        Line("its `color`, `noshade`, `size`, `width` and `align` attributes:");
        Line("`overflow: hidden` is accepted on a block box with no children and a border, padding or height that is not 0 at its");
        Line("top or bottom, where it clips nothing.");
        Line();
        Line("`border-radius` and its longhands: one length or percentage per corner on a block box, an inline box, an inline-block,");
        Line("a list item, a table and a table cell, with a background, a solid border of one width and one colour, or both. A");
        Line("block box, an inline-block, a list item, a cell and a table in the separate border model fill the background to the");
        Line("middle of the border; an inline box fills it to the border's outer edge, as Chromium does (cases `br-*`, `bri-*`).");
        Line("An inline box broken over lines has its left corners on its first line and its right ones on its last; with a border");
        Line("it is refused at layout. In the collapsed border model the table's own radius has no effect, as in Chromium: its");
        Line("corners stay square and nothing is refused (case `bri-collapse`); a cell there keeps its rounded background, and its");
        Line("lines stay square. A collapsed table with a radius and a background is refused (not measured). Refused: elliptical");
        Line("corners, a radius on an image, a row, a row group, a column or a caption, with `overflow` other than `visible`, and");
        Line("beside border sides of different widths, colours or styles.");
        Line();
        Line("`border-style: dashed` on a block box: each dashed side a row of dashes three times the width long below 3 px and");
        Line("twice the width from 3 px, flush with both ends of the side, the gaps evened out, as Chromium prints them (cases");
        Line("`bd-*`); beside it solid sides of the same width and colour or none. Refused: dashed on an inline box, an");
        Line("inline-block, a list item and a table or a part of one, beside sides of another width or colour, with a radius, and a");
        Line("dashed side shorter than two dashes and a gap (Chromium draws it as a solid line). `dotted`, `double`, `groove` and");
        Line("`ridge` are refused: they are not drawn yet.");
        Line();
        Line("`width` on a table part: a table is drawn at `auto`, a length or a percentage of its container (its border box; never");
        Line("narrower than its content needs, and refused where wider than its container); a cell (`td`, `th`) takes a length, of");
        Line("its content box, which its column takes: the widest in the automatic layout, the first row's in the fixed layout,");
        Line("where a `col` length wins. A `col`, a cell and a `colgroup` without `col` elements (for each column of its span) take a");
        Line("length or a percentage. In the automatic layout a percentage is of the width the columns share, and a table of width");
        Line("`auto` grows until its columns have their percentages and the others their widest content; in the fixed layout a");
        Line("percentage on a `col` when every other column is a percentage or has no width and, beside a column without a width,");
        Line("the percentages add up to less than 100. Refused: in the automatic layout percentages of 100 or more beside other");
        Line("columns, a percentage beside another width in one column, a percentage on a cell over several columns and a cell over");
        Line("several columns beside a percent column; in the fixed layout a percentage on a cell, a width on a cell over several");
        Line("columns and a cell width beside percent columns; a width on a `colgroup` with `col` elements. In the fixed layout a");
        Line("column without a width is never narrower than its content, where Chromium lets the content overflow (Known limits). A");
        Line("`tfoot` is drawn after the body rows, once; Chromium repeats a small one on every page of a table that breaks (Known");
        Line("limits).");
        Line();
        Line("`border-collapse`: both models are drawn. A table with separated borders (`separate`, the initial value) keeps its");
        Line("border, padding and background, each cell paints its own border, and `border-spacing` lies between the cells and");
        Line("around them, in whole CSS pixels (another spacing is refused). Such a table breaks over pages as Chromium prints it,");
        Line("sliced: between rows and inside rows, the cells of a cut row at the top of each part whatever their alignment, with a");
        Line("repeated header. It is refused at the table's line where it breaks with a cell over several rows across the break,");
        Line("with content sliced in a cell, or with `box-decoration-break: clone`. A border on a row or a row group of such a table");
        Line("is refused; put borders on the cells. In both models the background of a row, a row group, a column or a column group");
        Line("is drawn on the cells of it that have none of their own (the row's first, then the group's, the column's, the column");
        Line("group's), as Chromium prints it; a table's background is drawn in both models, under its border box.");
        Line();
        Line("`vertical-align: baseline` on a table cell, the initial value, is drawn as `top`, as Chromium prints it: each cell's");
        Line("first line at its own content top, whatever the font sizes and paddings in the row (cases `tv-baseline-sizes`,");
        Line("`tv-baseline-height`). A browser window aligns the cells' first baselines instead.");
        Line();
        Line("`width`, `min-width`, `max-width` and `box-sizing` on a block box: a length or a percentage of the containing block's");
        Line("width, for the content box or the border box; `max-width` limits the width and `min-width` wins over both. The");
        Line("horizontal margins that are `auto` share the width the box leaves: both centre it, a left one alone puts it at the");
        Line("right end; a box as wide as its container or wider starts at its left margin and passes the container's right edge,");
        Line("as in Chromium. Laid out as Chromium lays them out: a length cut toward zero to 1/64 px, a percentage and the left");
        Line("of two auto margins floored to it (cases `sz-*`). Refused: a calculated or intrinsic size (`calc()` with a");
        Line("percentage, `fit-content`), a minimum or maximum width on a table part or an image, and a width, minimum or maximum");
        Line("on a box inside a table cell or a page-margin box (`Documents.Input.Unsupported.Size`: their columns are sized by");
        Line("their content's widths, and a sized box's share in them is not built).");
        Line();
        Line("`height`, `min-height` and `max-height` on a block box: a length, cut toward zero to 1/64 px, for the content box or");
        Line("the border box; `max-height` limits the height and `min-height` wins over both. A percentage height is of the");
        Line("parent's height where that is a length in a content box, floored to 1/64 px, and `auto` where the parent's height");
        Line("depends on its content. Content taller than the box overflows it and is drawn, and what follows the box starts at its");
        Line("end. Across a page break the box takes the rest of the page and the next page shows what the pages before did not");
        Line("take, also a minimum height, as in Chromium (cases `sz-height*`, `sz-min-*`, `sz-max-height`). Refused: a percentage");
        Line("height elsewhere (on the root, of a parent's percentage, border-box, minimum or maximum height), a percentage minimum");
        Line("or maximum, a height on a table part, and content that goes on past a page break after the box's height ended on");
        Line("the page (`Documents.Layout.OverflowAcrossPages`: Chromium prints the rest of it on the next page while what follows");
        Line("the box stays on the first, cases `sz-height-overflow-page`, `sz-height-forced`).");
        Line();
        Line("### Supported at the initial value only");
        Line();
        Line("What the layout always does: a declaration that sets one of these to its initial value is accepted, so resets and");
        Line("the user-agent style sheet are not refused. So is a value in the Accepted column: aspect-ratio auto with a ratio,");
        Line("which the width and height attributes of img make; an image with a natural ratio uses its own, as in Chromium, and");
        Line("on any other element stage 4 applies the ratio or refuses it. A zero length where the initial value is a keyword or");
        Line("a percentage is the same value and accepted too: `background-position-x: 0` and `background-position-y: 0`");
        Line("(computed 0px, as Chromium keeps it). Any other value is refused.");
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
        Line("## Fonts");
        Line();
        Line("There are no system fonts. A family name the document writes is taken from its `@font-face` rules (the first TrueType");
        Line("source, through the caller's resolver), then from the caller's fonts (`HtmlRenderOptions.Fonts`). The first family of a");
        Line("`font-family` list that has any face is the family of the text, and it must have the computed weight and style");
        Line("exactly: no synthesis, no fallback per character. Where a browser would synthesise bold or italic, the face is");
        Line("refused (\"… Give the face, or write font-synthesis: none to print in the nearest face.\"). Under");
        Line("`font-synthesis-weight: none` a face of another weight is taken, under `font-synthesis-style: none` one of another");
        Line("style, plainly, the nearest by CSS Fonts 4 §5.2 (the style first, then the weight: from 400 to 500 the heavier ones up");
        Line("to 500, then the lighter, then those above 500; below 400 the lighter first; above 500 the heavier first), as Chromium");
        Line("159 prints it (cases `fs-none-regular-only`, `fs-none-regular-bold`: 600 and 800 take a 700 face, 500 and 300 a 400");
        Line("face, italic the upright face).");
        Line();
        Line("A browser takes a font of the system for a generic family. Here the caller maps them in");
        Line("`HtmlRenderOptions.GenericFamilies`, and a mapped name is looked up as a written name is (`@font-face` rules first):");
        Line();
        Line("- `Standard`: the initial value of `font-family`, and `cursive`, `fantasy`, `math`, `emoji`, `fangsong` and the `ui-*`");
        Line("  keywords, which Chromium 159 prints in its standard font (measured);");
        Line("- `Serif`: `serif`; when it is not set, `serif` takes `Standard`;");
        Line("- `SansSerif`: `sans-serif` and `system-ui`;");
        Line("- `Monospace`: `monospace` (the keyword still gives text in `monospace` alone its own sizes, 13px at `medium`).");
        Line();
        Line("The initial value is the name \"Times New Roman\", as Chromium computes it: an `@font-face` rule or a caller's font of");
        Line("that family is taken first, then `Standard`. `initial`, `inherit`, `unset` and `revert` keep the initial value; a");
        Line("\"Times New Roman\" written in the document is a family name like any other. A generic family never takes an");
        Line("`@font-face` rule of its own name, as in Chromium.");
        Line();
        Line("Refused in both modes, also with warnings only (`Documents.Html.Unsupported.Font`, at the element's start tag; for a");
        Line("page-margin box at its `content` declaration):");
        Line();
        Line("- a generic family the options do not map, where the list reaches it, also when a later family is supplied:");
        Line("  \"'font-family: sans-serif' needs a family for the generic family 'sans-serif': set GenericFamilies.SansSerif in the");
        Line("  options (a browser would take a font of the system; there are no system fonts).\"");
        Line("- a mapped name no face answers: \"The family 'Inter' that GenericFamilies.SansSerif maps 'sans-serif' to has no face:");
        Line("  give it in a @font-face rule or in the fonts of the options.\"");
        Line("- the initial value with neither a \"Times New Roman\" face nor `Standard`: \"'font-family' is not set here, and its");
        Line("  initial value needs a family: set GenericFamilies.Standard in the options, or give 'Times New Roman' in a @font-face");
        Line("  rule or in the fonts of the options (…).\"");
        Line("- a list that runs out: \"No family of 'font-family: Inter, Roboto' is supplied: give one in a @font-face rule or in");
        Line("  the fonts of the options, or end the list with a generic family the options map (there are no system fonts).\"");
        Line();
        Line("The mapped names are checked once, as input (`Documents.Html.InvalidOptions`): not empty, no control character, at");
        Line("most 32,767 characters (the longest family name of a TrueType font), and not a generic family keyword.");
        Line();
        Line("What is shown, and how. Chromium prints a generic family in a font of the system it runs on, not in the family the");
        Line("caller maps, so a document with a generic family is not a measured case. Equal to Chromium is shown for the");
        Line("written-out family: the cases `gf-*` measure the initial value through an `@font-face` rule of \"Times New Roman\" (in");
        Line("the body, in a page-margin box, through `inherit`, `unset` and `revert`) and lists that end in a generic family after");
        Line("a supplied one. That a mapping prints exactly as the written-out family is shown by `GenericFamilyTests`: each generic");
        Line("document, printed with its mapping, gives the same page model item for item as the equal case it was made from.");
        Line();
        Line("### A character the face has no glyph for");
        Line();
        Line("A browser takes such a character from a later family of the list, or from any font of the system. The engine takes");
        Line("no glyph from another font and draws no box for a missing one, so the character is refused in both modes");
        Line("(`Documents.Font.GlyphMissing`), at its own line and column in the document (a character reference: its ampersand;");
        Line("the text of a page-margin box: its `content` declaration). Every missing character of the document is reported, each");
        Line("pair of face and character once, in source order, up to 64 (`HtmlBoxGenerator.MaxMissingGlyphs`). The message names");
        Line("the character, its code point, the face, the list and what to do, and a later family of the list that has it:");
        Line();
        Line("\"The character '※' (U+203B) has no glyph in the face 'Lato-Regular' that 'font-family: Lato, Theano Didot' gives");
        Line("here, and the engine takes no glyph from another font: put the character in an element whose font has it, or use an");
        Line("image. The family 'Theano Didot' of the list has it: set 'Theano Didot' on an element around the character.\"");
        Line();
        Line("The text is checked as it is drawn, after its `text-transform`: a character only the transform makes (a capital, a");
        Line("small letter, the final sigma) is refused at the place of the character it is made of, and the message says so: \"The");
        Line("character 'Ɑ' (U+2C6D), which 'text-transform: uppercase' makes of 'ɑ' (U+0251) written here, has no glyph in the face");
        Line("'Lato-Regular' …\"");
        Line();
        Line("The check is the shaper's own rule, which `HtmlRendering` passes in (`HtmlBoxOptions.MissingCharacters`); a caller of");
        Line("`HtmlBoxGenerator` alone who leaves it out gets layout's refusal of the first paragraph with a missing character, at");
        Line("the line of its text. Never missing: the characters that need no glyph (Default_Ignorable_Code_Point, among them the");
        Line("soft hyphen where no line breaks at it, U+200B, U+200C, U+200D, U+2060, U+FEFF and the variation selectors; the");
        Line("mandatory breaks), white space that collapses (a tab, a line feed), and a tab where white space is preserved (refused");
        Line("on its own). One character is drawn with another's glyph, as Chromium prints it: U+2011 NON-BREAKING HYPHEN, in a face");
        Line("without it, with the face's U+2010 (case `gm-nbhyphen-lato`, Lato at 12 and 20 pt: the same advance, no break at it,");
        Line("read back as U+2011). Refused, because Chromium takes them from a font of the system: U+2010 and U+2011 in a face that");
        Line("has only U+002D (case `gm-nbhyphen-theano`), and the spaces U+00A0, U+202F, U+2009, U+2002, U+2003, U+2007, U+2008 and");
        Line("U+200A in a face without them (case `gm-spaces-roboto`).");
        Line();
        Line("## Where a line may end");
        Line();
        Line("The HTML front-end ends lines where Chromium 159's print ends them. Its rules are the Unicode Line Breaking Algorithm");
        Line("(UAX #14, Unicode 18.0.0) with the departures Chromium's line breaker shows (the layout policy");
        Line("`LineBreakRules.Chromium`; the rules and the rows that show each: `Typesetting/LineBreaking/ChromiumLineBreaking.cs`).");
        Line("For a template author that means:");
        Line();
        Line("- A line may break after every space, also before a closing bracket, a semicolon, an exclamation mark or a middle dot");
        Line("  that follows a space (`( see table 2 )`, `Street · Town`).");
        Line("- A line does not break after a slash or a backslash before a letter or a digit: `and/or`, `he/she`, `/usr/local/share`,");
        Line("  `C:\\Users\\Public` and `INV/2026/0042` each stay in one piece. Nor after `!` or `|` between letters, nor between");
        Line("  the two plus signs of `C++`.");
        Line("- A line may break after a hyphen in a number or a code when a letter or a digit comes before the hyphen:");
        Line("  `2026-` / `11-` / `28`, `REF-` / `2026-` / `0042`, `06-` / `12345678`. A minus sign stays with its number: `-5`,");
        Line("  `12 -34`.");
        Line("- In an address a line may break after `?` and after a hyphen, not after each slash:");
        Line("  `https://example.com/orders/2026/11/overview?` / `page=2&sort=date`.");
        Line("- The signs ¢ £ ¤ ¥ ° ± may move to the next line after a slash, a colon, a semicolon, a comma, a full stop or a closing");
        Line("  bracket in a number, and a line may break after them before an opening bracket: `12.50/` / `£15.00`. The euro and");
        Line("  dollar signs, and every sign beyond U+00FF, stay with the number: `12,50/€15,00` is one piece.");
        Line("- A line may break after an en dash, a bar or another character of class BA before a no-break space (`10–` / `12`");
        Line("  written with `&nbsp;`), and after a soft hyphen that follows a no-break space.");
        Line("- An en dash or a figure dash written right before a word after a space (`Rotterdam –Den Haag`) stays with the word.");
        Line();
        Line("To keep two words together, put a no-break space (`&nbsp;`) between them, or wrap them in an element with");
        Line("`white-space: nowrap`. To allow a break where these rules forbid one, put a zero width space (`&#x200B;`) there; `wbr`");
        Line("is refused.");
        Line();
        Line("Measured against Chromium's print: every pair of printable ASCII characters, between letters and between digits; every");
        Line("pair of 55 characters met in documents in both contexts; 104 strings of ordinary documents (addresses, paths, dates,");
        Line("amounts, phone numbers, codes, abbreviations, quotes); ASCII punctuation beside letters that are not ASCII; and the");
        Line("currency, percent and degree signs beside the punctuation of a number: 23 907 rows, every one reproduced (the matrix of");
        Line("`tools/reference/chromium/line-breaks.py`, kept as `Reference/Chromium/line-breaks.txt` of the typesetting tests), and");
        Line("the cases `lb-*`. Not measured, and so laid out by the standard rules: text in the scripts no font of the repository");
        Line("draws (Chinese, Japanese, Korean, Thai and the other scripts that UAX #14 breaks by dictionary or by syllable), which");
        Line("is refused today for its glyphs.");
        Line();
        Line("What stays refused: every value of `word-break`, `overflow-wrap` (and `word-wrap`), `line-break` and `hyphens` but the");
        Line("initial one. Chromium breaks between any two letters under `word-break: break-all`, `overflow-wrap: anywhere` or");
        Line("`break-word` and `line-break: anywhere`, and hyphenates English words by its dictionary under `hyphens: auto` with");
        Line("`lang=\"en\"`; the engine has neither.");
        Line();
        Line("Documents built in code (the fluent front-end) follow the standard rules (`LineBreakRules.Standard`, the default).");
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
