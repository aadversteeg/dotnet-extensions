using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>What box generation knows of one rendered element when it checks its computed values.</summary>
/// <param name="Style">The element's computed style.</param>
/// <param name="IsBlock">A block-level box (<c>display: block</c>, or the root); else an inline box.</param>
/// <param name="IsCanvasElement">The root or the body element, whose background Chromium paints on the canvas.</param>
/// <param name="HasText">A text child, also white space only.</param>
/// <param name="DrawsText">A text child that is not white space only: the element's own text style is drawn.</param>
/// <param name="HasChildren">An element or text child.</param>
/// <param name="IsReplaced">An img: a replaced element, whose width and height size the image (CSS 2.1 §10.3.2, §10.6.2).</param>
internal sealed record ElementFacts(ComputedStyle Style, bool IsBlock, bool IsCanvasElement, bool HasText, bool DrawsText, bool HasChildren, bool IsReplaced = false);

/// <summary>One row of <see cref="BoxProperties.Rows"/>: a registered longhand, what this version draws of it, and the
/// check that refuses every other value.</summary>
/// <param name="Name">The property.</param>
/// <param name="Draws">What is drawn, and where a value has no effect, the condition (for the architecture text).</param>
/// <param name="Check">Null when the value is drawn or has no effect on this element; else why it is refused.</param>
internal sealed record PropertyRow(string Name, string Draws, Func<CssValue, ElementFacts, string?> Check);

/// <summary>
/// The refusal side of box generation: one row per longhand of the property registry, saying which computed values
/// this version draws; every other value is refused (<c>Documents.Html.Unsupported.Value</c>) at the element's start
/// tag. A value is refused only where it would change the print: where it has no effect in the combination at hand, the
/// row says the condition. A test walks the registry and fails for a longhand without a row.
/// </summary>
internal static class BoxProperties
{
    /// <summary>The longhands the registry supports only at their initial value (or at values that compute to it):
    /// drawn as that value; any other value is refused here too, so a value the registry starts to accept is refused
    /// until its row is written.</summary>
    private static readonly string[] InitialOnly =
    [
        "backdrop-filter", "background-attachment", "background-clip", "background-image", "background-origin",
        "background-position-x", "background-position-y", "background-repeat", "background-size",
        "border-bottom-left-radius", "border-bottom-right-radius", "border-top-left-radius", "border-top-right-radius",
        "bottom", "box-shadow", "clear", "clip-path", "column-count", "column-gap", "column-width", "contain", "content",
        "content-visibility", "direction", "filter", "float",
        "font-language-override", "font-optical-sizing", "font-size-adjust", "font-stretch",
        "font-variant-alternates", "font-variant-caps", "font-variant-east-asian", "font-variant-emoji",
        "font-variant-ligatures", "font-variant-position", "font-variation-settings", "hyphens",
        "image-rendering", "isolation", "left", "line-break", "list-style-image",
        "mix-blend-mode", "object-fit", "opacity", "outline-style", "overflow-wrap", "page", "position",
        "quotes", "right", "row-gap", "tab-size", "text-align-last", "text-decoration-thickness", "text-indent",
        "text-justify", "text-overflow", "text-rendering", "text-shadow", "text-underline-offset",
        "text-underline-position", "text-wrap-style", "top",
        "transform", "visibility", "word-break", "writing-mode", "z-index",
    ];

    private static readonly string[] Sides = ["top", "right", "bottom", "left"];

    /// <summary>Every row, by property name.</summary>
    public static FrozenDictionary<string, PropertyRow> Rows { get; } = Build().ToFrozenDictionary(row => row.Name, StringComparer.Ordinal);

    /// <summary>The refused values of an element, as (property, reason), in registry order.</summary>
    /// <param name="facts">The element.</param>
    /// <returns>The refusals; empty when every value is drawn or without effect.</returns>
    public static IEnumerable<(PropertyDefinition Property, string Reason)> Refusals(ElementFacts facts)
    {
        foreach (var property in PropertyRegistry.Longhands)
        {
            // A longhand without a row is refused: the registry test names it, and no value slips through unread.
            var reason = Rows.TryGetValue(property.Name, out var row)
                ? row.Check(facts.Style[property], facts)
                : "box generation has no row for this property";
            if (reason is not null)
            {
                yield return (property, reason);
            }
        }
    }

    private static IEnumerable<PropertyRow> Build()
    {
        foreach (var name in InitialOnly)
        {
            var initial = PropertyRegistry.Find(name)?.Initial;
            yield return new(name, "the initial value", (value, _) => Equals(value, initial) ? null : "only the initial value is drawn");
        }

        // CSS Sizing 4 §2.1: "auto && <ratio>" is the natural aspect ratio where the element has one, and every decoded
        // image has one (the width and height attributes of an img map to it, HTML Standard §15.4.3).
        yield return new("aspect-ratio", "auto; auto and a ratio on an image; a ratio has no effect on an inline box",
            (value, facts) => value is CssKeyword { Name: "auto" } || !facts.IsBlock || (facts.IsReplaced && IsAutoAndRatio(value)) ? null : "a preferred aspect ratio sizes the box, and sizes are not drawn in this version");

        // CSS Backgrounds 3 §2.2 (background-color), §2.11.2 (the canvas background).
        yield return new("background-color", "a colour of alpha 1 on a block box and an inline box; transparent everywhere", (value, facts) =>
            Alpha(value) switch
            {
                0 => null,
                _ when facts.IsCanvasElement => "a background on the root or body element is painted on the page canvas (CSS Backgrounds 3 §2.11.2), and the document model has no page background",
                _ when TableProperties.Background(facts) is { } table => table,
                1 => null,
                _ => "a colour with an alpha other than 0 or 1 is not drawn",
            });

        foreach (var side in Sides)
        {
            // CSS Backgrounds 3 §3.1–3.3: a style of none or hidden computes the width to 0 (no line, no room). Inset and
            // outset are painted in two colours taken from the border colour (Layout's BorderShades, cases bs-*) on a block
            // box; a table's lines are drawn solid by the table's layout, an inline box's border is drawn solid, and inset and
            // outset sides on one box are not measured.
            yield return new($"border-{side}-style", "none, hidden, solid; inset and outset on a block box that is no table part, not both on one box; any value where the width is 0", (value, facts) => value switch
            {
                _ when IsZero(facts.Style, $"border-{side}-width") => null,
                CssKeyword { Name: "none" or "hidden" or "solid" } => null,
                CssKeyword { Name: "inset" or "outset" } when !facts.IsBlock => "an inset or outset border on an inline box is not drawn in this version; use solid",
                CssKeyword { Name: "inset" or "outset" } when TableProperties.IsPart(facts) => "an inset or outset border on a table or a part of a table is not drawn in this version: its lines are drawn solid",
                CssKeyword { Name: "inset" or "outset" } keyword when HasStyle(facts.Style, keyword.Name == "inset" ? "outset" : "inset") => "inset and outset sides on one box are not drawn in this version",
                CssKeyword { Name: "inset" or "outset" } => null,
                _ => "only solid, inset and outset borders are drawn; use solid",
            });
            yield return new($"border-{side}-width", "a length on a block box and an inline box; 0 on a column or a column group",
                (value, facts) => TableProperties.BorderWidth(value, facts));
            yield return new($"border-{side}-color", "a colour of alpha 1, or currentcolor; any colour where the width is 0", (value, facts) =>
                IsZero(facts.Style, $"border-{side}-width") || Alpha(Resolved(value, facts.Style)) == 1 ? null : "a border colour with an alpha other than 1 is not drawn");
        }

        // CSS Box Model 3 §5.1; a horizontal auto margin takes the width a block box with a width leaves (CSS 2.1 §10.3.3,
        // BoxStyle.AutoMargins; case sz-margin-auto) and is 0 at width auto; a vertical one is 0 (§10.6.3; inline: §10.3.1).
        foreach (var side in Sides)
        {
            var vertical = side is "top" or "bottom";
            yield return new($"margin-{side}", vertical ? "a length of 0 or more, auto (0); no effect on an inline box (CSS 2.1 §10.6.1) or an internal table part (§17.5)" : "a length of 0 or more, auto on a block box (the width its width leaves); a length of 0 or more, auto (0) on an inline box; 0 on a table, or auto at width 100%; no effect on an internal table part (CSS 2.1 §17.5)",
                (value, facts) => value switch
                {
                    _ when TableProperties.IsPart(facts) => TableProperties.Margin(side, value, facts),
                    CssKeyword { Name: "auto" } => null,
                    _ when !facts.IsBlock && vertical => null,
                    _ when IsZero(value) => null,
                    CssLength length when length.Value.Points > 0 => null,
                    CssLength => "negative margins are not drawn in this version",
                    _ => "percentage and calculated margins are not drawn in this version",
                });
            yield return new($"padding-{side}", "a length on a block box and an inline box", (value, facts) => value switch
            {
                _ when IsZero(value) => null,
                CssLength => null,
                _ => "percentage and calculated padding is not drawn in this version",
            });
        }

        // CSS Sizing 3 §3.1–3.2: the sizes do not apply to an inline box (CSS 2.1 §10.3.1, §10.6.1). A block box takes a
        // width, min-width and max-width of a length or a percentage (BoxStyle.SpecifiedWidth; cases sz-*); a table part
        // its own (TableProperties.Width), an image a positive length; a calculated value is refused.
        yield return new("width", "auto; a length or a percentage on a block box; a positive length on an image; no effect on an inline box; on a table part what TableProperties.Width says", (value, facts) =>
            TableProperties.IsPart(facts) ? TableProperties.Width(value, facts)
            : IsKeyword(value, "auto") || !facts.IsBlock || IsImageSize(value, facts) ? null
            : facts.IsReplaced ? "an image is drawn at a positive length width in this version"
            : BlockSize(value));
        yield return new("min-width", "auto or 0; a length or a percentage on a block box; no effect on an inline box", (value, facts) =>
            IsKeyword(value, "auto") || IsZero(value) || !facts.IsBlock ? null
            : TableProperties.IsPart(facts) || facts.IsReplaced ? "a minimum width on a table part or an image is not drawn in this version"
            : BlockSize(value));
        yield return new("max-width", "none; a length or a percentage on a block box; no effect on an inline box", (value, facts) =>
            IsKeyword(value, "none") || !facts.IsBlock ? null
            : TableProperties.IsPart(facts) || facts.IsReplaced ? "a maximum width on a table part or an image is not drawn in this version"
            : BlockSize(value));

        // A block box takes a height of a length, and a percentage of a parent with a height of a length, which the box
        // generator resolves (BoxStyle.SpecifiedHeight; cases sz-height*); min-height and max-height of a length
        // (sz-min-height-break, sz-max-height, sz-min-max-height). A percentage minimum or maximum is not measured.
        yield return new("height", "auto; a length or a percentage on a block box; a positive length on an image; no effect on an inline box; on a table part what TableProperties.Height says", (value, facts) =>
            TableProperties.IsPart(facts) ? TableProperties.Height(value, facts)
            : IsKeyword(value, "auto") || !facts.IsBlock || IsImageSize(value, facts) ? null
            : facts.IsReplaced ? "an image is drawn at a positive length height in this version"
            : BlockSize(value));
        yield return new("min-height", "auto or 0; a length on a block box; no effect on an inline box", (value, facts) =>
            IsKeyword(value, "auto") || IsZero(value) || !facts.IsBlock ? null
            : TableProperties.IsPart(facts) || facts.IsReplaced ? "a minimum height on a table part or an image is not drawn in this version"
            : value is CssLength ? null : "a percentage or calculated minimum height is not drawn in this version");
        yield return new("max-height", "none; a length on a block box; no effect on an inline box", (value, facts) =>
            IsKeyword(value, "none") || !facts.IsBlock ? null
            : TableProperties.IsPart(facts) || facts.IsReplaced ? "a maximum height on a table part or an image is not drawn in this version"
            : value is CssLength ? null : "a percentage or calculated maximum height is not drawn in this version");

        // CSS Box Model 3 §4.3: read for the width and the height of a block box; on an image its padding and border would change the
        // image's size under border-box, which the image algorithm does not take.
        yield return new("box-sizing", "both values on a block box (read for its width and height); on a table, a row and a cell with a height what the table draws (HtmlBoxGenerator, tables); content-box on an image with padding or a border", (value, facts) =>
            facts.IsReplaced && IsKeyword(value, "border-box") && HasInsets(facts.Style) ? "border-box on an image with padding or a border is not drawn in this version" : null);

        // CSS Display 3 §2.
        yield return new("display", "block, inline, inline-block (HtmlBoxGenerator, THE RULE FOR AN INLINE-BLOCK IN HTML), none, list-item (CSS Lists 3 §2; HtmlBoxGenerator.Lists.cs), table and its parts",
            (value, _) => value is CssKeyword { Name: "block" or "inline" or "inline-block" or "none" or "list-item" } ? null : TableProperties.Display(value));

        // CSS Color 4 §3.1: the colour of the element's own text; a border in currentcolor is checked in its row.
        yield return new("color", "a colour of alpha 1; any colour on an element without text of its own",
            (value, facts) => !facts.DrawsText || Alpha(value) == 1 ? null : "a text colour with an alpha other than 1 is not drawn");
        yield return new("color-scheme", "both values: print uses the light scheme", (_, _) => null);
        yield return new("print-color-adjust", "both values: no effect with backgrounds printed, as the reference prints them", (_, _) => null);

        // CSS Fonts 4: the family, size, weight and style are matched to a face (FontMatcher); a missing face is refused there.
        yield return new("font-family", "the first family of the list that a @font-face rule or the caller's fonts give", (_, _) => null);
        // The model holds no text of size 0 (layout would refuse it late, without the element's place).
        yield return new("font-size", "every size above 0", (value, _) => value is CssLength { Value.Points: <= 0 } ? "a font size of 0 is not drawn in this version" : null);
        yield return new("font-weight", "every value, matched exactly", (_, _) => null);
        yield return new("font-style", "normal, italic", (_, _) => null);
        yield return new("font-kerning", "auto, normal (both kern, measured)", (value, _) => IsKeyword(value, "auto") || IsKeyword(value, "normal") ? null : "only kerned text is drawn");
        foreach (var name in new[] { "font-synthesis-weight", "font-synthesis-style" })
        {
            yield return new(name, "both values: none takes the nearest face plainly where the family has no face of the weight or style (FontMatcher); auto refuses it, as the engine never synthesises", (_, _) => null);
        }

        yield return new("font-synthesis-small-caps", "both values: the engine never synthesises, and a missing face is refused", (_, _) => null);

        // CSS Inline 3 §5.1, CSS Text 3 §6.1, CSS Fragmentation 3 §3.3: inherited, read for every box with lines.
        yield return new("line-height", "every value", (_, _) => null);
        yield return new("text-align", "every value (start and -webkit-left as left, end and -webkit-right as right, -webkit-center as center: left-to-right only)", (_, _) => null);
        yield return new("orphans", "every value", (_, _) => null);
        yield return new("widows", "every value", (_, _) => null);

        // CSS Text 3 §4.1.1 and CSS Text 4 §6.1: collapsing and wrapping, read where text is.
        yield return new("white-space-collapse", "collapse, preserve, preserve-breaks, break-spaces (a tab where spaces are preserved is refused at its text)", (_, _) => null);
        yield return new("text-wrap-mode", "wrap, nowrap", (_, _) => null);
        yield return new("unicode-bidi", "both values: isolate has no effect in left-to-right text, the only direction", (_, _) => null);
        // CSS 2.1 §10.8.1: super, sub, a length and a percentage shift an inline element's text (TextStyle.BaselineShift).
        yield return new("vertical-align", "baseline (on a table cell drawn as top); super, sub, a length and a percentage on an inline element that is not an image; top, middle, bottom on a table cell; any value on another block box (it applies to inline boxes and table cells)",
            (value, facts) => TableProperties.VerticalAlign(value, facts) ?? (IsKeyword(value, "baseline") || facts.IsBlock || (!facts.IsReplaced && IsShift(value))
                ? null : "this vertical alignment of an inline box is not drawn in this version: baseline, super, sub, a length and a percentage are"));

        // CSS Text Decoration 3 §2, CSS Text Decoration 4 §2.10.4: the lines go to the text of the element and of every
        // element inside it (HtmlBoxGenerator.Decorations.cs); the style sheet accepts only the solid style, the automatic
        // thickness, underline position and offset, and refuses skip-ink: all.
        yield return new("text-decoration-line", "none, or underline, overline and line-through in any combination", (_, _) => null);
        yield return new("text-decoration-color", "a colour of alpha 1, or currentcolor; any colour where text-decoration-line is none",
            (value, facts) => IsKeyword(facts.Style[PropertyRegistry.Find("text-decoration-line")!], "none") || Alpha(Resolved(value, facts.Style)) == 1
                ? null : "a decoration colour with an alpha other than 1 is not drawn");
        yield return new("text-decoration-style", "solid, the only value the style sheet accepts", (_, _) => null);
        yield return new("text-decoration-skip-ink", "auto, none", (_, _) => null);
        // CSS Text 3 §7.2, CSS Text 4 §7.1: a computed length, added in layout (TextStyle.LetterSpacing, WordSpacing).
        yield return new("letter-spacing", "normal, a length (a mark or an invisible character in letter-spaced text is refused at its text)",
            (value, _) => value is CssKeyword { Name: "normal" } or CssLength ? null : "only normal and a length are drawn");
        yield return new("word-spacing", "normal, a length",
            (value, _) => value is CssLength or CssPercentage { Value: 0 } ? null : "only normal and a length are drawn");
        // CSS Fonts 4 §6.7, §7.1: the OpenType features they select, in the text style (TextStyle.FontFeatures).
        yield return new("font-variant-numeric", "normal, lining-nums, oldstyle-nums, proportional-nums, tabular-nums, ordinal", (_, _) => null);
        yield return new("font-feature-settings", "normal, the tags lnum, onum, pnum, tnum and ordn on or off", (_, _) => null);
        yield return new("text-transform", "none, uppercase, lowercase, capitalize, mapped in each text (a character the transform does not map is refused at its text)", (_, _) => null);

        // CSS Fragmentation 3 §3.1, §3.2, §5.4; break properties apply to block-level boxes only.
        yield return new("break-before", "every value (avoid-page as avoid); no effect on an inline box", (_, _) => null);
        yield return new("break-after", "every value (avoid-page as avoid, page as a forced break before the next box); no effect on an inline box", (_, _) => null);
        yield return new("break-inside", "every value (avoid-page as avoid); no effect on an inline box", (_, _) => null);
        yield return new("box-decoration-break", "slice; clone on a box with no border, padding or background, and on an inline box without horizontal margins too", (value, facts) =>
            IsKeyword(value, "slice") || !(HasDecoration(facts.Style) || (!facts.IsBlock && HasHorizontalMargin(facts.Style))) ? null
            : facts.IsBlock ? "a cloned box decoration is not drawn in this version: the document model slices every block"
            : "a cloned inline box decoration is not drawn in this version: an inline box is drawn on one line, its edges sliced; remove box-decoration-break: clone");

        // CSS Overflow 3 §3.1: hidden and clip would clip content that passes a block box (a long word). A block box with
        // no children has no content to clip, and hidden there (the user-agent style of hr) is drawn as visible, where a
        // border, padding or height at its top or bottom keeps its own margins from collapsing through it (CSS 2.1 §8.3.1),
        // so the formatting context hidden starts (CSS Overflow 3 §3.1) changes nothing either (cases hr-*).
        foreach (var name in new[] { "overflow-x", "overflow-y" })
        {
            yield return new(name, "visible; hidden on a block box with no children and a border, padding or height that is not 0 at its top or bottom; any value on an inline box or an image (its content fills its box: object-fit is fill)",
                (value, facts) => IsKeyword(value, "visible") || !facts.IsBlock || facts.IsReplaced || (IsKeyword(value, "hidden") && !facts.HasChildren && IsClosed(facts.Style))
                    ? null : "clipped overflow is not drawn in this version");
        }

        // CSS UI 4 §3: outline-style is only none, so nothing is drawn.
        foreach (var name in new[] { "outline-color", "outline-offset", "outline-width" })
        {
            yield return new(name, "every value: no effect while outline-style is none, its only value", (_, _) => null);
        }

        // Tables (CSS 2.1 §17.6): both border models; the rest has no effect with collapsed borders, or is read for the columns.
        yield return new("border-collapse", "collapse and separate on a table; no effect on another box", (value, facts) => TableProperties.TableProperty("border-collapse", value, facts));
        yield return new("border-spacing", "whole CSS pixels on a table with separated borders; no effect with collapsed borders", (value, facts) => TableProperties.TableProperty("border-spacing", value, facts));
        yield return new("caption-side", "top and bottom: where a table draws its caption", (value, facts) => TableProperties.TableProperty("caption-side", value, facts));
        yield return new("empty-cells", "show draws every cell; hide, with separated borders, paints no border or background of an empty cell", (value, facts) => TableProperties.TableProperty("empty-cells", value, facts));

        yield return new("table-layout", "both values: read for the column widths of a table", (_, _) => null);

        // CSS Lists 3 §3.1, §3.2: the marker of a list item, outside its box; the style sheet refuses inside, every image
        // and the counter styles beyond the predefined twelve. No effect on a box that is not a list item.
        foreach (var counter in new[] { "counter-reset", "counter-increment", "counter-set" })
        {
            yield return new(counter, "none and counters by name, but list-item, within 32 bits (counted in document order, read by counter() and counters() in ::before and ::after)", (_, _) => null);
        }

        yield return new("list-style-type", "disc, circle, square, decimal, decimal-leading-zero, lower-roman, upper-roman, lower-alpha, upper-alpha, lower-latin, upper-latin, none (the twelve the style sheet accepts)", (_, _) => null);
        yield return new("list-style-position", "outside, the only value the style sheet accepts", (_, _) => null);
    }

    /// <summary>The alpha of a colour value; NaN for anything else.</summary>
    internal static double Alpha(CssValue value) => value is CssColorValue color ? color.Color.Alpha : double.NaN;

    /// <summary>A colour with currentcolor taken from the element's colour (CSS Color 4 §4.4).</summary>
    internal static CssValue Resolved(CssValue value, ComputedStyle style) => IsKeyword(value, "currentcolor") ? style[PropertyRegistry.Color] : value;

    private static bool IsAutoAndRatio(CssValue value) =>
        value is CssList { Items: [CssKeyword { Name: "auto" }, CssRatio] or [CssRatio, CssKeyword { Name: "auto" }] };

    /// <summary>A width, min-width or max-width a block box takes: a length or a percentage (CSS Sizing 3 §3.1).</summary>
    private static string? BlockSize(CssValue value) => value is CssLength or CssPercentage ? null : "a calculated or intrinsic size is not drawn in this version";

    /// <summary>Padding or a border on the box.</summary>
    private static bool HasInsets(ComputedStyle style) =>
        Sides.Any(side => !IsZero(style, $"border-{side}-width") || !IsZero(style, $"padding-{side}"));

    /// <summary>A size the image algorithm takes: a positive length on a replaced element.</summary>
    private static bool IsImageSize(CssValue value, ElementFacts facts) => facts.IsReplaced && value is CssLength { Value.Points: > 0 };

    private static bool IsKeyword(CssValue value, string name) => value is CssKeyword keyword && string.Equals(keyword.Name, name, StringComparison.Ordinal);

    /// <summary>A vertical-align that shifts the baseline of an inline element's text: super, sub, a length or a percentage.</summary>
    internal static bool IsShift(CssValue value) => value is CssKeyword { Name: "super" or "sub" } or CssLength or CssPercentage;

    private static bool IsZero(CssValue value) => value switch
    {
        CssLength length => length.Value.Points == 0,
        CssPercentage percentage => percentage.Value == 0,
        _ => false,
    };

    private static bool IsZero(ComputedStyle style, string property) => IsZero(style[PropertyRegistry.Find(property)!]);

    /// <summary>A side of the box with this border style and a width that is not 0.</summary>
    private static bool HasStyle(ComputedStyle style, string borderStyle) =>
        Sides.Any(side => IsKeyword(style[PropertyRegistry.Find($"border-{side}-style")!], borderStyle) && !IsZero(style, $"border-{side}-width"));

    /// <summary>A border or padding that is not 0 at the box's top or bottom, or a height that is a length above 0: its top
    /// and bottom margins do not collapse through it (CSS 2.1 §8.3.1).</summary>
    private static bool IsClosed(ComputedStyle style) =>
        new[] { "border-top-width", "border-bottom-width", "padding-top", "padding-bottom" }.Any(property => !IsZero(style, property))
        || style[PropertyRegistry.Find("height")!] is CssLength { Value.Points: > 0 };

    /// <summary>A left or right margin that is not 0.</summary>
    private static bool HasHorizontalMargin(ComputedStyle style) => !IsZero(style, "margin-left") || !IsZero(style, "margin-right");

    /// <summary>A border, padding or background on the box.</summary>
    private static bool HasDecoration(ComputedStyle style) =>
        Sides.Any(side => !IsZero(style, $"border-{side}-width") || !IsZero(style, $"padding-{side}"))
        || Alpha(style[PropertyRegistry.Find("background-color")!]) is not 0;
}
