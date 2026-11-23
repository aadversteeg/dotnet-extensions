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
        "content-visibility", "counter-increment", "counter-reset", "counter-set", "direction", "filter", "float",
        "font-feature-settings", "font-language-override", "font-optical-sizing", "font-size-adjust", "font-stretch",
        "font-variant-alternates", "font-variant-caps", "font-variant-east-asian", "font-variant-emoji",
        "font-variant-ligatures", "font-variant-numeric", "font-variant-position", "font-variation-settings", "hyphens",
        "image-rendering", "isolation", "left", "letter-spacing", "line-break", "list-style-image", "max-height",
        "min-height", "mix-blend-mode", "object-fit", "opacity", "outline-style", "overflow-wrap", "page", "position",
        "quotes", "right", "row-gap", "tab-size", "text-align-last", "text-decoration-thickness", "text-indent",
        "text-justify", "text-overflow", "text-rendering", "text-shadow", "text-transform", "text-underline-offset",
        "text-underline-position", "text-wrap-style", "top",
        "transform", "visibility", "word-break", "word-spacing", "writing-mode", "z-index",
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
        yield return new("background-color", "a colour of alpha 1 on a block box; transparent everywhere", (value, facts) =>
            Alpha(value) switch
            {
                0 => null,
                _ when facts.IsCanvasElement => "a background on the root or body element is painted on the page canvas (CSS Backgrounds 3 §2.11.2), and the document model has no page background",
                _ when !facts.IsBlock => "a background on an inline box is not drawn in this version",
                _ when TableProperties.Background(facts) is { } table => table,
                1 => null,
                _ => "a colour with an alpha other than 0 or 1 is not drawn",
            });

        foreach (var side in Sides)
        {
            // CSS Backgrounds 3 §3.1–3.3: a style of none or hidden computes the width to 0 (no line, no room).
            yield return new($"border-{side}-style", "none, hidden, solid; inset and outset only where the width is 0", (value, facts) =>
                value is CssKeyword { Name: "none" or "hidden" or "solid" } || IsZero(facts.Style, $"border-{side}-width") ? null : "only solid borders are drawn");
            yield return new($"border-{side}-width", "a length on a block box; 0 on an inline box, a column or a column group",
                (value, facts) => TableProperties.BorderWidth(value, facts) ?? (facts.IsBlock || IsZero(value) ? null : "borders on inline boxes are not drawn in this version"));
            yield return new($"border-{side}-color", "a colour of alpha 1, or currentcolor; any colour where the width is 0", (value, facts) =>
                IsZero(facts.Style, $"border-{side}-width") || Alpha(Resolved(value, facts.Style)) == 1 ? null : "a border colour with an alpha other than 1 is not drawn");
        }

        // CSS Box Model 3 §5.1; a horizontal auto margin takes the width a block box with a width leaves (CSS 2.1 §10.3.3,
        // BoxStyle.AutoMargins; case sz-margin-auto) and is 0 at width auto; a vertical one is 0 (§10.6.3; inline: §10.3.1).
        foreach (var side in Sides)
        {
            var vertical = side is "top" or "bottom";
            yield return new($"margin-{side}", vertical ? "a length of 0 or more, auto (0); no effect on an inline box (CSS 2.1 §10.6.1) or an internal table part (§17.5)" : "a length of 0 or more, auto on a block box (the width its width leaves); 0 or auto on an inline box; 0 on a table, or auto at width 100%; no effect on an internal table part (CSS 2.1 §17.5)",
                (value, facts) => value switch
                {
                    _ when TableProperties.IsPart(facts) => TableProperties.Margin(side, value, facts),
                    CssKeyword { Name: "auto" } => null,
                    _ when !facts.IsBlock && vertical => null,
                    _ when IsZero(value) => null,
                    _ when !facts.IsBlock => "margins on inline boxes are not drawn in this version",
                    CssLength length when length.Value.Points > 0 => null,
                    CssLength => "negative margins are not drawn in this version",
                    _ => "percentage and calculated margins are not drawn in this version",
                });
            yield return new($"padding-{side}", "a length on a block box; 0 on an inline box", (value, facts) => value switch
            {
                _ when IsZero(value) => null,
                CssLength when facts.IsBlock => null,
                CssLength => "padding on inline boxes is not drawn in this version",
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

        // A fixed height takes room of its own across a page break, which the block algorithm does not build; measured
        // (cases sz-height, sz-height-break, sz-height-box-sizing), not drawn.
        yield return new("height", "auto; a positive length on an image; no effect on an inline box", (value, facts) =>
            IsKeyword(value, "auto") || !facts.IsBlock || IsImageSize(value, facts) ? null : "a height is not drawn in this version: a box's own height across a page break is not built");

        // CSS Box Model 3 §4.3: read for the width of a block box; on an image its padding and border would change the
        // image's size under border-box, which the image algorithm does not take.
        yield return new("box-sizing", "both values on a block box (read for its width; a height is refused); content-box on an image with padding or a border", (value, facts) =>
            facts.IsReplaced && IsKeyword(value, "border-box") && HasInsets(facts.Style) ? "border-box on an image with padding or a border is not drawn in this version" : null);

        // CSS Display 3 §2.
        yield return new("display", "block, inline, none, table and its parts but the caption", (value, _) => value is CssKeyword { Name: "block" or "inline" or "none" } ? null : TableProperties.Display(value));

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
        foreach (var name in new[] { "font-synthesis-weight", "font-synthesis-style", "font-synthesis-small-caps" })
        {
            yield return new(name, "both values: the engine never synthesises, and a missing face is refused", (_, _) => null);
        }

        // CSS Inline 3 §5.1, CSS Text 3 §6.1, CSS Fragmentation 3 §3.3: inherited, read for every box with lines.
        yield return new("line-height", "every value", (_, _) => null);
        yield return new("text-align", "every value (start and -webkit-left as left, end and -webkit-right as right, -webkit-center as center: left-to-right only)", (_, _) => null);
        yield return new("orphans", "every value", (_, _) => null);
        yield return new("widows", "every value", (_, _) => null);

        // CSS Text 3 §4.1.1 and CSS Text 4 §6.1: collapsing and wrapping, read where text is.
        yield return new("white-space-collapse", "collapse, preserve, preserve-breaks, break-spaces (a tab where spaces are preserved is refused at its text)", (_, _) => null);
        yield return new("text-wrap-mode", "wrap, nowrap", (_, _) => null);
        yield return new("unicode-bidi", "both values: isolate has no effect in left-to-right text, the only direction", (_, _) => null);
        yield return new("vertical-align", "baseline; top, middle, bottom on a table cell; any value on another block box (it applies to inline boxes and table cells)",
            (value, facts) => TableProperties.VerticalAlign(value, facts) ?? (IsKeyword(value, "baseline") || facts.IsBlock ? null : "vertical alignment of inline boxes is not drawn in this version"));

        // CSS Text Decoration 3 §2, CSS Text Decoration 4 §2.10.4: the lines go to the text of the element and of every
        // element inside it (HtmlBoxGenerator.Decorations.cs); the style sheet accepts only the solid style, the automatic
        // thickness, underline position and offset, and refuses skip-ink: all.
        yield return new("text-decoration-line", "none, or underline, overline and line-through in any combination", (_, _) => null);
        yield return new("text-decoration-color", "a colour of alpha 1, or currentcolor; any colour where text-decoration-line is none",
            (value, facts) => IsKeyword(facts.Style[PropertyRegistry.Find("text-decoration-line")!], "none") || Alpha(Resolved(value, facts.Style)) == 1
                ? null : "a decoration colour with an alpha other than 1 is not drawn");
        yield return new("text-decoration-style", "solid, the only value the style sheet accepts", (_, _) => null);
        yield return new("text-decoration-skip-ink", "auto, none", (_, _) => null);

        // CSS Fragmentation 3 §3.1, §3.2, §5.4; break properties apply to block-level boxes only.
        yield return new("break-before", "every value (avoid-page as avoid); no effect on an inline box", (_, _) => null);
        yield return new("break-after", "every value (avoid-page as avoid, page as a forced break before the next box); no effect on an inline box", (_, _) => null);
        yield return new("break-inside", "every value (avoid-page as avoid); no effect on an inline box", (_, _) => null);
        yield return new("box-decoration-break", "slice; clone on a box with no border, padding or background", (value, facts) =>
            IsKeyword(value, "slice") || !facts.IsBlock || !HasDecoration(facts.Style) ? null : "a cloned box decoration is not drawn in this version: the document model slices every block");

        // CSS Overflow 3 §3.1: hidden and clip would clip content that passes a block box (a long word).
        foreach (var name in new[] { "overflow-x", "overflow-y" })
        {
            yield return new(name, "visible; any value on an inline box or an image (its content fills its box: object-fit is fill)",
                (value, facts) => IsKeyword(value, "visible") || !facts.IsBlock || facts.IsReplaced ? null : "clipped overflow is not drawn in this version");
        }

        // CSS UI 4 §3: outline-style is only none, so nothing is drawn.
        foreach (var name in new[] { "outline-color", "outline-offset", "outline-width" })
        {
            yield return new(name, "every value: no effect while outline-style is none, its only value", (_, _) => null);
        }

        // Tables (CSS 2.1 §17.6): collapsed borders only; the rest has no effect with them, or is read for the columns.
        yield return new("border-collapse", "collapse on a table; no effect on another box", (value, facts) => TableProperties.TableProperty("border-collapse", value, facts));
        foreach (var name in new[] { "border-spacing", "caption-side", "empty-cells" })
        {
            yield return new(name, "every value: no effect with collapsed borders and no caption (separated borders and captions are refused)", (value, facts) => TableProperties.TableProperty(name, value, facts));
        }

        yield return new("table-layout", "both values: read for the column widths of a table", (_, _) => null);

        // Lists: no effect on a box that is not a list item (that display type is refused).
        foreach (var name in new[] { "list-style-position", "list-style-type" })
        {
            yield return new(name, "every value: no effect on a box that is not a list item (display: list-item is refused)", (_, _) => null);
        }
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

    private static bool IsZero(CssValue value) => value switch
    {
        CssLength length => length.Value.Points == 0,
        CssPercentage percentage => percentage.Value == 0,
        _ => false,
    };

    private static bool IsZero(ComputedStyle style, string property) => IsZero(style[PropertyRegistry.Find(property)!]);

    /// <summary>A border, padding or background on the box.</summary>
    private static bool HasDecoration(ComputedStyle style) =>
        Sides.Any(side => !IsZero(style, $"border-{side}-width") || !IsZero(style, $"padding-{side}"))
        || Alpha(style[PropertyRegistry.Find("background-color")!]) is not 0;
}
