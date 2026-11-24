using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// Properties the engine implements only at their initial value: what the layout always does (no floats, no
/// positioning, no transforms, left-to-right horizontal text …). A declaration that sets one to its initial value is
/// accepted, which keeps style resets and the user-agent style sheet from being refused; any other value is refused.
/// For a keyword property the other keywords CSS defines are refused and anything else is invalid; for the others,
/// every other value is refused without checking it is valid CSS (a refusal is never silently different).
/// </summary>
public static partial class PropertyRegistry
{
    private static IEnumerable<PropertyDefinition> InitialOnlyProperties()
    {
        // Positioning and floats (CSS Positioned Layout 3, CSS 2.1 §9.5).
        yield return InitialKeyword("position", false, "static", ["relative", "absolute", "fixed", "sticky"], false, "all elements except table-column-group and table-column", "CSS Positioned Layout 3 §2.1");
        foreach (var side in Sides)
        {
            yield return InitialKeyword(side, false, "auto", [], true, "positioned elements", "CSS Positioned Layout 3 §3.1");
        }

        yield return InitialKeyword("z-index", false, "auto", [], true, "positioned elements", "CSS 2.1 §9.9.1");
        yield return InitialKeyword("float", false, "none", ["left", "right", "inline-start", "inline-end"], false, "all elements", "CSS 2.1 §9.5.1");
        yield return InitialKeyword("clear", false, "none", ["left", "right", "both", "inline-start", "inline-end"], false, "block-level elements", "CSS 2.1 §9.5.2");

        // Visibility, effects.
        yield return InitialKeyword("visibility", true, "visible", ["hidden", "collapse"], false, "all elements", "CSS Display 3 §4");
        yield return InitialNumber("opacity", inherited: false, 1, "all elements", "CSS Color 4 §11.2");
        yield return InitialKeyword("transform", false, "none", [], true, "transformable elements", "CSS Transforms 1 §5");
        yield return InitialKeyword("filter", false, "none", [], true, "all elements", "Filter Effects 1 §5");
        yield return InitialKeyword("backdrop-filter", false, "none", [], true, "all elements", "Filter Effects 2 §3");
        yield return InitialKeyword("box-shadow", false, "none", [], true, "all elements", "CSS Backgrounds 3 §6.1");
        yield return InitialKeyword("text-shadow", true, "none", [], true, "text", "CSS Text Decoration 3 §4");
        yield return InitialKeyword("clip-path", false, "none", [], true, "all elements", "CSS Masking 1 §5.1");
        yield return InitialKeyword("mix-blend-mode", false, "normal", [], true, "all elements", "Compositing 1 §3.2");
        yield return InitialKeyword("isolation", false, "auto", ["isolate"], false, "all elements", "Compositing 1 §3.3");
        foreach (var corner in new[] { "top-left", "top-right", "bottom-right", "bottom-left" })
        {
            yield return InitialZero($"border-{corner}-radius", false, "0", "all elements", "CSS Backgrounds 3 §5.1");
        }

        // Backgrounds other than the colour (the background shorthand resets them).
        yield return InitialKeyword("background-image", false, "none", [], true, "all elements", "CSS Backgrounds 3 §2.2");
        yield return InitialKeyword("background-repeat", false, "repeat", ["repeat-x", "repeat-y", "space", "round", "no-repeat"], true, "all elements", "CSS Backgrounds 3 §2.3");
        yield return InitialKeyword("background-attachment", false, "scroll", ["fixed", "local"], false, "all elements", "CSS Backgrounds 3 §2.4");
        yield return InitialKeyword("background-origin", false, "padding-box", ["border-box", "content-box"], false, "all elements", "CSS Backgrounds 3 §2.7");
        yield return InitialKeyword("background-clip", false, "border-box", ["padding-box", "content-box", "text"], false, "all elements", "CSS Backgrounds 3 §2.8");
        yield return InitialKeyword("background-size", false, "auto", ["cover", "contain"], true, "all elements", "CSS Backgrounds 3 §2.9");
        yield return InitialPercentPair("background-position-x", "all elements", "CSS Backgrounds 4 §2.6");
        yield return InitialPercentPair("background-position-y", "all elements", "CSS Backgrounds 4 §2.6");

        // Outline: never drawn (its style is only none), so its width, colour and offset are kept as written.
        yield return InitialKeyword("outline-style", false, "none", ["auto", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset", "hidden"], false, "all elements", "CSS UI 4 §3.3");
        yield return new PropertyDefinition(
            "outline-width", false, new CssLength(Length.FromPoints(2.25)), "all elements", "<line-width> (nothing is drawn: outline-style is only none)",
            PropertySupport.Supported, "CSS UI 4 §3.2", reader => ParseBorderWidth(reader, "outline-width"), (value, context) => SnappedLineWidth(value, context));
        yield return new PropertyDefinition(
            "outline-color", false, new CssKeyword("currentcolor"), "all elements", "<color> (nothing is drawn: outline-style is only none; Chromium 159 does not take auto)",
            PropertySupport.Supported, "CSS UI 4 §3.4",
            reader => Whole(reader, reader.Color(), "outline-color"),
            AsSpecified);
        yield return new PropertyDefinition(
            "outline-offset", false, Zero, "all elements", "<length> (nothing is drawn: outline-style is only none)", PropertySupport.Supported,
            "CSS UI 4 §3.5", reader => Whole(reader, reader.Length(percentages: false, nonNegative: false, "outline-offset"), "outline-offset"),
            SnappedOffset);

        // Text.
        yield return InitialZero("text-indent", true, "0", "block containers", "CSS Text 3 §8.1");
        yield return InitialKeyword("text-overflow", false, "clip", ["ellipsis"], true, "block containers", "CSS Overflow 3 §5.1");
        yield return InitialKeyword("text-align-last", true, "auto", ["start", "end", "left", "right", "center", "justify", "match-parent"], false, "block containers", "CSS Text 3 §6.2");
        yield return InitialKeyword("text-justify", true, "auto", ["none", "inter-word", "inter-character", "distribute"], false, "text", "CSS Text 3 §6.4");
        yield return InitialKeyword("direction", true, "ltr", ["rtl"], false, "all elements", "CSS Writing Modes 4 §2.1");
        yield return InitialKeyword("writing-mode", true, "horizontal-tb", ["vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr", "lr", "lr-tb", "rl", "rl-tb", "tb", "tb-rl"], false, "all elements except table row groups, rows, column groups, and columns", "CSS Writing Modes 4 §3.1");
        yield return InitialKeyword("hyphens", true, "manual", ["none", "auto"], false, "text", "CSS Text 3 §5.4");
        yield return InitialKeyword("overflow-wrap", true, "normal", ["break-word", "anywhere"], false, "text", "CSS Text 3 §5.5");
        yield return InitialKeyword("word-break", true, "normal", ["break-all", "keep-all", "break-word", "auto-phrase"], false, "text", "CSS Text 3 §5.2");
        yield return InitialKeyword("line-break", true, "auto", ["loose", "normal", "strict", "anywhere"], false, "text", "CSS Text 3 §5.3");
        yield return InitialNumber("tab-size", inherited: true, 8, "text", "CSS Text 3 §4.2");
        yield return InitialKeyword("text-wrap-style", true, "auto", ["balance", "stable", "pretty", "avoid-orphans"], false, "block containers", "CSS Text 4 §6.2");
        yield return InitialKeyword("text-rendering", true, "auto", ["optimizespeed", "optimizelegibility", "geometricprecision"], false, "text", "SVG 2 §11.9");
        yield return InitialKeyword("text-decoration-thickness", false, "auto", ["from-font"], true, "all elements", "CSS Text Decoration 4 §2.4");
        yield return InitialKeyword("text-underline-position", true, "auto", ["from-font", "under", "left", "right"], true, "all elements", "CSS Text Decoration 3 §2.5");
        yield return InitialKeyword("text-underline-offset", true, "auto", [], true, "all elements", "CSS Text Decoration 4 §2.8");
        yield return InitialKeyword("quotes", true, "auto", ["none", "match-parent"], true, "all elements", "CSS Generated Content 3 §3");
        yield return InitialKeyword("content", false, "normal", ["none"], true, "all elements, ::before, ::after, ::marker, page-margin boxes", "CSS Generated Content 3 §1");
        yield return InitialKeyword("counter-reset", false, "none", [], true, "all elements", "CSS Lists 3 §4.2");
        yield return InitialKeyword("counter-increment", false, "none", [], true, "all elements", "CSS Lists 3 §4.3");
        yield return InitialKeyword("counter-set", false, "none", [], true, "all elements", "CSS Lists 3 §4.4");

        // Fonts beyond family, size, weight and style (the font shorthand resets them).
        yield return InitialKeyword("font-variant-caps", true, "normal", ["small-caps", "all-small-caps", "petite-caps", "all-petite-caps", "unicase", "titling-caps"], false, "all elements and text", "CSS Fonts 4 §6.6");
        yield return InitialKeyword("font-variant-ligatures", true, "normal", ["none"], true, "all elements and text", "CSS Fonts 4 §6.4");
        yield return InitialKeyword("font-variant-east-asian", true, "normal", [], true, "all elements and text", "CSS Fonts 4 §6.10");
        yield return InitialKeyword("font-variant-position", true, "normal", ["sub", "super"], false, "all elements and text", "CSS Fonts 4 §6.5");
        yield return InitialKeyword("font-variant-alternates", true, "normal", [], true, "all elements and text", "CSS Fonts 4 §6.8");
        yield return InitialKeyword("font-variant-emoji", true, "normal", ["text", "emoji", "unicode"], false, "all elements and text", "CSS Fonts 4 §6.11");
        yield return InitialKeyword("font-variation-settings", true, "normal", [], true, "all elements and text", "CSS Fonts 4 §7.2");
        // font-stretch: normal is 100% (CSS Fonts 4 §2.3, the computed value is a percentage; Chromium writes 100%).
        yield return new PropertyDefinition(
            "font-stretch", inherited: true, new CssPercentage(100), "all elements and text", "normal | 100%", PropertySupport.InitialOnly, "CSS Fonts 4 §2.3",
            reader => reader.Count == 1 && (reader.Keyword("normal") is not null || reader.Peek is Syntax.CssToken { Kind: Syntax.CssTokenKind.Percentage, Number: 100 })
                ? ParsedValue.Of(new CssPercentage(100))
                : ParsedValue.Refused("'font-stretch' is supported only at its initial value, normal (100%)"),
            AsSpecified);
        yield return InitialKeyword("font-size-adjust", true, "none", [], true, "all elements and text", "CSS Fonts 5 §2.1");
        yield return InitialKeyword("font-optical-sizing", true, "auto", ["none"], false, "all elements and text", "CSS Fonts 4 §2.6");
        yield return InitialKeyword("font-language-override", true, "normal", [], true, "all elements and text", "CSS Fonts 4 §7.4");
        yield return Keywords("font-synthesis-weight", inherited: true, "auto", ["auto", "none"], [], "all elements and text", "CSS Fonts 4 §7.3 (the engine never synthesises: a missing face is an error)");
        yield return Keywords("font-synthesis-style", inherited: true, "auto", ["auto", "none"], [], "all elements and text", "CSS Fonts 4 §7.3 (the engine never synthesises: a missing face is an error)");
        yield return Keywords("font-synthesis-small-caps", inherited: true, "auto", ["auto", "none"], [], "all elements and text", "CSS Fonts 4 §7.3 (the engine never synthesises: a missing face is an error)");

        // Sizes and layout features outside the supported set.
        yield return InitialKeyword("min-height", false, "auto", [], true, "all elements that accept width or height", "CSS Sizing 3 §3.2");
        yield return InitialKeyword("max-height", false, "none", [], true, "all elements that accept width or height", "CSS Sizing 3 §3.2");
        // aspect-ratio: auto, and auto with a ratio, which the HTML width and height attributes of img make (HTML Standard
        // §15.4.3): a replaced element with a natural aspect ratio uses that, so auto w / h is auto for an image that
        // loads; a ratio without auto is refused.
        yield return new PropertyDefinition(
            "aspect-ratio", inherited: false, new CssKeyword("auto"), "all elements except inline boxes and internal ruby or table boxes",
            "auto | auto && <ratio>", PropertySupport.InitialOnly, "CSS Sizing 4 §2.1", ParseAspectRatio, AsSpecified);
        yield return InitialKeyword("object-fit", false, "fill", ["contain", "cover", "none", "scale-down"], false, "replaced elements", "CSS Images 3 §5.5");
        yield return InitialKeyword("image-rendering", true, "auto", ["smooth", "high-quality", "crisp-edges", "pixelated", "optimizespeed", "optimizequality", "-webkit-optimize-contrast"], false, "all elements", "CSS Images 3 §5.3");
        yield return InitialKeyword("column-count", false, "auto", [], true, "block containers except table wrapper boxes", "CSS Multi-column 1 §3.2");
        yield return InitialKeyword("column-width", false, "auto", [], true, "block containers except table wrapper boxes", "CSS Multi-column 1 §3.1");
        yield return InitialKeyword("column-gap", false, "normal", [], true, "multi-column containers, flex containers, grid containers", "CSS Box Alignment 3 §8.1");
        yield return InitialKeyword("row-gap", false, "normal", [], true, "multi-column containers, flex containers, grid containers", "CSS Box Alignment 3 §8.1");
        yield return InitialKeyword("page", false, "auto", [], true, "boxes that create class A break points", "CSS Paged Media 3 §6");
        yield return Keywords("color-scheme", inherited: true, "normal", ["normal", "light"], ["dark", "only"], "all elements and text", "CSS Color Adjust 1 §2.1");
        yield return InitialKeyword("content-visibility", false, "visible", ["hidden", "auto"], false, "elements with size containment", "CSS Containment 2 §4");
        yield return InitialKeyword("contain", false, "none", ["strict", "content", "size", "inline-size", "layout", "style", "paint"], true, "all elements", "CSS Containment 2 §2");
    }

    /// <summary>A number property the engine implements only at its initial value.</summary>
    private static PropertyDefinition InitialNumber(string name, bool inherited, double initial, string appliesTo, string specification) =>
        new(name, inherited, new CssNumber(initial), appliesTo, InvariantNumber.Format(initial), PropertySupport.InitialOnly, specification,
            reader =>
            {
                if (reader.Count == 1 && reader.Peek is Syntax.CssToken { Kind: Syntax.CssTokenKind.Number } number && number.Number == initial)
                {
                    reader.Next();
                    return ParsedValue.Of(new CssNumber(initial));
                }

                if (reader.Count == 1 && name == "opacity" && reader.Peek is Syntax.CssToken { Kind: Syntax.CssTokenKind.Percentage, Number: 100 })
                {
                    reader.Next();
                    return ParsedValue.Of(new CssNumber(1));
                }

                return ParsedValue.Refused($"'{name}' is supported only at its initial value, {InvariantNumber.Format(initial)}");
            },
            AsSpecified);

    /// <summary>A background position axis the engine implements only at its initial value, 0%.</summary>
    private static PropertyDefinition InitialPercentPair(string name, string appliesTo, string specification) =>
        new(name, false, new CssPercentage(0), appliesTo, "0%", PropertySupport.InitialOnly, specification,
            // A zero length is the same position, and Chromium 159 keeps it as written: 0px, not 0% (measured).
            reader => reader.Count == 1 && reader.Length(percentages: true, nonNegative: false, name) is { Value: (CssPercentage { Value: 0 } or CssLength { Points: 0 }) and var zero }
                ? ParsedValue.Of(zero)
                : ParsedValue.Refused($"'{name}' is supported only at its initial value, 0%"),
            AsSpecified);

    /// <summary>aspect-ratio = auto || &lt;ratio&gt;, &lt;ratio&gt; = &lt;number [0,∞]&gt; [ / &lt;number [0,∞]&gt; ]? (CSS Values 4
    /// §7.8). Only auto, alone or with a ratio, is supported. Each of the two comes at most once (Chromium 159, measured:
    /// <c>auto 16/9 auto</c> is dropped); a number past the largest single-precision float is that float, as Chromium
    /// keeps it (measured: <c>1e999/1</c> is <c>3.40282e+38 / 1</c>).</summary>
    private static ParsedValue ParseAspectRatio(ValueReader reader)
    {
        // Chromium keeps the ratio as two single-precision floats in a gfx::SizeF, which makes a component of 8
        // float epsilons (2^-20) or less 0 (ui/gfx/geometry/size_f.h, SizeF::clamp, read in the Chromium 150 source;
        // measured in 159: 0.0000001/1 is 0 / 1,
        // 2^-20 is 0, 0.000000954 stays). Other numbers keep their value (opacity: 0.0000001 is 1e-07).
        const float Trivial = 9.5367431640625E-07f; // 2^-20: 8 × the single-precision machine epsilon 2^-23
        static double RatioComponent(double value) => (float)value > Trivial ? System.Math.Min(value, float.MaxValue) : 0;

        // A math function's NaN is 0 and its result is clamped to the component's range [0, ∞] (CSS Values 4 §10.9,
        // §10.12; Chromium 159, measured: 'auto calc(0/0)' and 'auto calc(-1)' are auto 0 / 1).
        double? Component()
        {
            var math = reader.Peek is Syntax.FunctionValue;
            var number = reader.Number();
            return math && number is { } value && (double.IsNaN(value) || value < 0) ? 0 : number;
        }

        var first = reader.Keyword("auto") is not null;
        var auto = first;
        CssRatio? ratio = null;
        if (Component() is { } width && width >= 0)
        {
            var height = 1.0;
            if (reader.Delim('/'))
            {
                if (Component() is not { } second || second < 0)
                {
                    return ParsedValue.Invalid("not a value of aspect-ratio");
                }

                height = second;
            }

            ratio = new CssRatio(RatioComponent(width), RatioComponent(height));
        }

        auto |= !first && reader.Keyword("auto") is not null;
        if (!reader.AtEnd || (!auto && ratio is null))
        {
            return ParsedValue.Invalid("not a value of aspect-ratio");
        }

        if (!auto)
        {
            return ParsedValue.Refused("'aspect-ratio' with a ratio and without auto is not supported");
        }

        return ParsedValue.Of(ratio is { } value ? new CssList([new CssKeyword("auto"), value], CommaSeparated: false) : new CssKeyword("auto"));
    }
}
