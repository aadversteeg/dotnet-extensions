using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>Font properties: family, size, weight, style, line height.</summary>
public static partial class PropertyRegistry
{
    /// <summary>The generic font families of CSS Fonts 4 §3.1.1: written without quotes, they are keywords.</summary>
    private static readonly string[] GenericFamilies =
    [
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace",
        "ui-rounded", "math", "emoji", "fangsong", "generic(fangsong)", "generic(kai)", "generic(khmer-mul)", "generic(nastaliq)",
    ];

    /// <summary>Whether a name, written without quotes, is a generic font family keyword of CSS Fonts 4 §2.1 (ASCII case
    /// insensitive).</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsGenericFamily(string name) => name is not null && GenericFamilies.Contains(InfraAscii.ToLower(name));

    /// <summary>The absolute-size keywords of CSS Fonts 4 §2.5 with their size at medium = 16px, in pixels.</summary>
    private static readonly Dictionary<string, double> AbsoluteSizes = new(StringComparer.Ordinal)
    {
        ["xx-small"] = 9, ["x-small"] = 10, ["small"] = 13, ["medium"] = 16, ["large"] = 18, ["x-large"] = 24,
        ["xx-large"] = 32, ["xxx-large"] = 48,
    };

    /// <summary>The same keywords for the family monospace, whose medium is 13px in Chromium (and Firefox): the sizes
    /// Chromium 159 computes, measured (tools/reference/chromium/computed-style).</summary>
    private static readonly Dictionary<string, double> MonospaceSizes = new(StringComparer.Ordinal)
    {
        ["xx-small"] = 9, ["x-small"] = 10, ["small"] = 12, ["medium"] = 13, ["large"] = 16, ["x-large"] = 20,
        ["xx-large"] = 26, ["xxx-large"] = 39,
    };

    /// <summary>The factor a size that is not absolute is scaled by from the parent's family to the element's
    /// (monospace's medium is 13px where the others' is 16px); 1 when neither or both are monospace.</summary>
    private static double FamilyScale(ComputeContext context, FontSizeSource parent) =>
        parent.Absolute || context.ParentIsMonospace == context.IsMonospace ? 1 : context.IsMonospace ? 13.0 / 16 : 16.0 / 13;

    /// <summary>The font size an element inherits (no declaration, inherit, unset): a keyword's size for the element's
    /// own family, or the parent's size scaled between families when it is not absolute.</summary>
    internal static CssValue InheritFontSize(ComputeContext context)
    {
        var parent = context.ParentFontSizeSource;
        context.FontSizeSource = parent;
        return new CssLength(Length.FromPoints(parent.Keyword is { } keyword
            ? (context.IsMonospace ? MonospaceSizes : AbsoluteSizes)[keyword] * 0.75
            : context.ParentFontSize * FamilyScale(context, parent)));
    }

    private static IEnumerable<PropertyDefinition> FontProperties()
    {
        yield return new PropertyDefinition(
            "font-size", inherited: true, new CssLength(Length.FromPoints(InitialFontSizePoints)), "all elements and text",
            "<absolute-size> | <relative-size> | <length-percentage [0,∞]>", PropertySupport.Supported, "CSS Fonts 4 §2.5",
            ParseFontSize,
            ComputeFontSize);

        yield return new PropertyDefinition(
            "font-family", inherited: true, new CssList([new CssString("Times New Roman")], CommaSeparated: true), "all elements and text",
            "[ <family-name> | <generic-family> ]#", PropertySupport.Supported, "CSS Fonts 4 §2.1; the initial value is the user agent's (Chromium: Times New Roman)",
            ParseFontFamily,
            AsSpecified);

        yield return new PropertyDefinition(
            "font-weight", inherited: true, new CssNumber(400), "all elements and text", "normal | bold | bolder | lighter | <number [1,1000]>",
            PropertySupport.Supported, "CSS Fonts 4 §2.2",
            ParseFontWeight,
            ComputeFontWeight);

        // normal prints as auto: Chromium 159 kerns with both (measured: cases h-kerning-auto, h-kerning-normal and
        // h-kerning-none of tools/reference/chromium), and the engine always applies the font's kerning.
        yield return new PropertyDefinition(
            "font-variant-numeric", inherited: true, new CssKeyword("normal"), "all elements and text",
            "normal | [ lining-nums | oldstyle-nums ] || [ proportional-nums | tabular-nums ] || ordinal", PropertySupport.Supported,
            "CSS Fonts 4 §6.7", ParseFontVariantNumeric, AsSpecified);

        yield return new PropertyDefinition(
            "font-feature-settings", inherited: true, new CssKeyword("normal"), "all elements and text",
            "normal | <feature-tag-value># (the tags \"lnum\", \"onum\", \"pnum\", \"tnum\" and \"ordn\", each with 0, 1, on or off)", PropertySupport.Supported,
            "CSS Fonts 4 §7.1", ParseFontFeatureSettings, AsSpecified);

        yield return Keywords("font-kerning", inherited: true, "auto", ["auto", "normal"], ["none"], "all elements and text",
            "CSS Fonts 4 §6.3; normal prints as auto (Chromium 159 kerns with both, measured)");

        yield return new PropertyDefinition(
            "font-style", inherited: true, new CssKeyword("normal"), "all elements and text", "normal | italic", PropertySupport.Supported,
            "CSS Fonts 4 §2.4",
            reader => reader.Keyword("normal", "italic") is { } keyword && reader.AtEnd
                ? ParsedValue.Of(new CssKeyword(keyword))
                : ParseRefusedOblique(reader),
            AsSpecified);

        yield return new PropertyDefinition(
            "line-height", inherited: true, new CssKeyword("normal"), "non-replaced inline boxes and SVG text content elements",
            "normal | <number [0,∞]> | <length-percentage [0,∞]>", PropertySupport.Supported, "CSS Inline 3 §5.1; CSS 2.1 §10.8.1",
            ParseLineHeight,
            ComputeLineHeight);
    }

    private static ParsedValue ParseRefusedOblique(ValueReader reader)
    {
        reader.Reset(0);
        return reader.Keyword("oblique") is not null
            ? ParsedValue.Refused("font-style: oblique is not supported (no synthesised slant)")
            : ParsedValue.Invalid("not a value of font-style");
    }

    /// <summary>font-size: a keyword of CSS Fonts 4 §2.5 or a non-negative length-percentage.</summary>
    private static ParsedValue ParseFontSize(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword([.. AbsoluteSizes.Keys, "larger", "smaller"]) is { } keyword)
        {
            return ParsedValue.Of(new CssKeyword(keyword));
        }

        if (reader.Count == 1 && reader.Keyword("math") is not null)
        {
            return ParsedValue.Refused("font-size: math is not supported");
        }

        return Whole(reader, reader.Length(percentages: true, nonNegative: true, "font-size"), "font-size");
    }

    /// <summary>
    /// The computed font size (CSS Fonts 4 §2.5): an absolute length. Keywords use the table of §2.5 at medium = 16px;
    /// larger and smaller multiply and divide the parent's size by 1.2, as Chromium does; em and percentages refer to
    /// the parent's size.
    /// </summary>
    /// <remarks>Chromium keeps where a size comes from (<see cref="FontSizeSource"/>): text in the one family monospace
    /// is 13px where other text is 16px, a keyword is taken from monospace's own table, and a size from em or a
    /// percentage of a keyword is scaled by 13/16 into monospace and back (measured in Chromium 159,
    /// tools/reference/chromium/computed-style, case fonts-tables-lists).</remarks>
    internal static ComputedResult ComputeFontSize(CssValue value, ComputeContext context)
    {
        var size = UnboundedFontSize(value, context);
        if (size.Value is not CssLength length)
        {
            return size;
        }

        // Chromium 159's font-size bounds (measured): it computes at most 10 000px, and raises a
        // size made from a relative value (em, %, larger, smaller of a size that was never absolute) above 0 and below
        // 6px, its minimum logical font size, to 6px, while children inherit the size before the change. The engine
        // keeps neither rule's second size, so it refuses both cases instead.
        var pixels = length.Points / 0.75;
        if (pixels > MaxFontSizePixels)
        {
            return ComputedResult.Refused($"a font size above {MaxFontSizePixels}px (Chromium 159 clamps it to {MaxFontSizePixels}px)");
        }

        // Chromium 159 computes 1e-7px as 0px and keeps 2e-7px (measured); between them the engine cannot tell.
        if (pixels is > 0 and < MinKeptFontSizePixels)
        {
            return ComputedResult.Refused("a font size above 0 and below 2e-7px (Chromium 159 computes 1e-7px as 0px)");
        }

        if (pixels is > 0 and < MinLogicalFontSizePixels && !context.FontSizeSource.Absolute)
        {
            return ComputedResult.Refused($"a font size below {MinLogicalFontSizePixels}px made from a relative size (Chromium 159 raises it to {MinLogicalFontSizePixels}px and lets children inherit the smaller size)");
        }

        return size;
    }

    /// <summary>The largest computed font size of Chromium 159, in px.</summary>
    private const double MaxFontSizePixels = 10000;

    /// <summary>The least font size above 0, in px, that Chromium 159 is measured to keep as it is.</summary>
    private const double MinKeptFontSizePixels = 2e-7;

    /// <summary>Chromium 159's minimum logical font size, in px: the least it computes for a relative size.</summary>
    private const double MinLogicalFontSizePixels = 6;

    private static ComputedResult UnboundedFontSize(CssValue value, ComputeContext context)
    {
        var parentSource = context.ParentFontSizeSource;
        // em, percentages, larger and smaller scale the parent's size, first taken to the element's family.
        var parent = context.ParentFontSize * FamilyScale(context, parentSource);
        var relative = new FontSizeSource(null, parentSource.Absolute);
        switch (value)
        {
            case CssKeyword { Name: "larger" }:
                context.FontSizeSource = relative;
                return new CssLength(Length.FromPoints(parent * 1.2));
            case CssKeyword { Name: "smaller" }:
                context.FontSizeSource = relative;
                return new CssLength(Length.FromPoints(parent / 1.2));
            case CssKeyword keyword:
                context.FontSizeSource = new FontSizeSource(keyword.Name, false);
                return new CssLength(Length.FromPoints((context.IsMonospace ? MonospaceSizes : AbsoluteSizes)[keyword.Name] * 0.75));
            case CssDimension { Unit: "em" } em:
                context.FontSizeSource = relative;
                return new CssLength(Length.FromPoints(parent * em.Value));
            case CssPercentage percentage:
                context.FontSizeSource = relative;
                return new CssLength(Length.FromPoints(parent * percentage.Value / 100));
            default:
                // Lengths, rem, ex, ch and calc() are absolute; em inside calc() is the parent's size as it is.
                context.FontSizeSource = new FontSizeSource(null, true);
                return context.LengthPercentage(value, forFontSize: true, percentBasis: context.ParentFontSize);
        }
    }

    /// <summary>font-family (CSS Fonts 4 §2.1): a comma-separated list of family names (a string, or identifiers joined by
    /// single spaces) and generic families (keywords). The CSS-wide keywords and "default" cannot be a family name
    /// written without quotes, and a generic family keyword cannot start a name of several identifiers (Chromium 159
    /// reads it as the generic family and then wants a comma; measured: <c>font: 12px sans-serif serif</c> is dropped).</summary>
    private static ParsedValue ParseFontFamily(ValueReader reader)
    {
        var families = new List<CssValue>();
        var words = new List<string>();
        void Flush()
        {
            if (words.Count == 1 && GenericFamilies.Contains(InfraAscii.ToLower(words[0])))
            {
                families.Add(new CssKeyword(InfraAscii.ToLower(words[0])));
            }
            else if (words.Count > 0)
            {
                families.Add(new CssString(string.Join(' ', words)));
            }

            words.Clear();
        }

        var expectName = true;
        while (!reader.AtEnd)
        {
            switch (reader.Next())
            {
                case CssToken { Kind: CssTokenKind.String } quoted when expectName && words.Count == 0:
                    families.Add(new CssString(quoted.Text));
                    expectName = false;
                    break;
                case CssToken { Kind: CssTokenKind.Ident } word when expectName:
                    if (words.Count == 0 && InfraAscii.ToLower(word.Text) is "initial" or "inherit" or "unset" or "revert" or "revert-layer" or "default")
                    {
                        return ParsedValue.Invalid($"'{CssSerializer.Shortened(word.Text)}' cannot be a font family name without quotes");
                    }

                    // Chromium also reads -webkit-body as a generic family here (ConsumeGenericFamily of
                    // css_parsing_utils.cc takes the keywords serif to math of css_value_keywords.json5; read in the
                    // Chromium 150 source), so 'font: 12px -webkit-body a' is dropped (measured in 159).
                    if (words.Count == 1 && (GenericFamilies.Contains(InfraAscii.ToLower(words[0])) || InfraAscii.ToLower(words[0]) == "-webkit-body"))
                    {
                        return ParsedValue.Invalid($"the generic family '{CssSerializer.Shortened(words[0])}' cannot start a font family name");
                    }

                    words.Add(word.Text);
                    break;
                case CssToken { Kind: CssTokenKind.Comma } when words.Count > 0 || !expectName:
                    Flush();
                    expectName = true;
                    break;
                case FunctionValue function when function.IsNamed("generic"):
                    return ParsedValue.Refused("generic() font families are not supported");
                default:
                    return ParsedValue.Invalid("not a value of font-family");
            }

            if (!expectName && words.Count > 0)
            {
                return ParsedValue.Invalid("not a value of font-family");
            }
        }

        if (expectName && words.Count == 0)
        {
            return ParsedValue.Invalid("font-family ends with a comma");
        }

        Flush();
        return ParsedValue.Of(new CssList([.. families], CommaSeparated: true));
    }

    /// <summary>font-weight (CSS Fonts 4 §2.2): normal is 400, bold 700, a number from 1 to 1000, or bolder and lighter.</summary>
    private static ParsedValue ParseFontWeight(ValueReader reader)
    {
        if (reader.Count != 1)
        {
            return ParsedValue.Invalid("font-weight takes one value");
        }

        if (reader.Keyword("normal", "bold", "bolder", "lighter") is { } keyword)
        {
            return ParsedValue.Of(keyword switch
            {
                "normal" => new CssNumber(400),
                "bold" => new CssNumber(700),
                _ => new CssKeyword(keyword),
            });
        }

        return reader.Number() is { } number && number is >= 1 and <= 1000
            ? ParsedValue.Of(new CssNumber(number))
            : ParsedValue.Invalid("font-weight is a number from 1 to 1000");
    }

    /// <summary>The computed weight: bolder and lighter step from the parent's weight by the table of CSS Fonts 4 §2.2.1;
    /// a number is kept in quarters, cut toward zero, as Chromium keeps a weight in a fixed-point value with two
    /// fractional bits (platform/fonts/font_selection_types.h, FontSelectionValue, read in the Chromium 150 source;
    /// measured in 159: 100.6 is 100.5, 999.99 is
    /// 999.75, calc(0.1234567 * 400) is 49.25).</summary>
    private static ComputedResult ComputeFontWeight(CssValue value, ComputeContext context)
    {
        var parent = context.Inherited(FontWeight) is CssNumber inherited ? inherited.Value : 400;
        return value switch
        {
            CssKeyword { Name: "bolder" } => new CssNumber(parent < 350 ? 400 : parent < 550 ? 700 : parent < 900 ? 900 : parent),
            CssKeyword { Name: "lighter" } => new CssNumber(parent < 100 ? parent : parent < 550 ? 100 : parent < 750 ? 400 : 700),
            CssNumber number when System.Math.Truncate((float)number.Value * 4f) / 4 is var quarters && quarters != number.Value => new CssNumber(quarters),
            _ => value,
        };
    }

    /// <summary>line-height: normal, a number (kept as a number), or a length-percentage.</summary>
    private static ParsedValue ParseLineHeight(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("normal") is not null)
        {
            return ParsedValue.Of(new CssKeyword("normal"));
        }

        if (reader.Count == 1 && reader.Peek is CssToken { Kind: CssTokenKind.Number } number)
        {
            reader.Next();
            return number.Number < 0 ? ParsedValue.Invalid("a negative line-height") : ParsedValue.Of(new CssNumber(number.Number));
        }

        if (reader.Count == 1 && reader.Peek is FunctionValue function && Calc.IsMathFunction(function)
            && Calc.Parse(function, out _) is { } node && Calc.TypeOf(node) == CalcType.Number && Calc.Evaluate(node, (_, _) => null, null) is { } result)
        {
            reader.Next();
            return result.Number < 0 ? ParsedValue.Invalid("a negative line-height") : ParsedValue.Of(new CssNumber(result.Number));
        }

        return Whole(reader, reader.Length(percentages: true, nonNegative: true, "line-height"), "line-height");
    }

    /// <summary>The computed line height (CSS Inline 3 §5.1): normal and numbers as specified; a length absolute; a
    /// percentage of the element's font size.</summary>
    /// <remarks>Chromium 159 keeps a calc() that mixes a length and a percentage in its layout unit, 1/64 px, rounded
    /// down: calc(1px + 10%) at 16px is 2.59375px, not 2.6px; a percentage or a length alone is kept as it is
    /// (measured).</remarks>
    private static ComputedResult ComputeLineHeight(CssValue value, ComputeContext context)
    {
        if (value is CssKeyword or CssNumber)
        {
            return value;
        }

        var computed = context.LengthPercentage(value, percentBasis: context.FontSize);
        if (value is CssCalc calc && Mixes(calc.Expression) == (true, true) && computed.Value is CssLength length)
        {
            return new CssLength(Length.FromPoints(Math.Floor(length.Points / 0.75 * 64) / 64 * 0.75));
        }

        return computed;
    }

    /// <summary>Whether a calc() expression has a length and a percentage in it. Its depth is bounded by the reader
    /// (CssInputLimits.MaxDepth).</summary>
    private static (bool Length, bool Percentage) Mixes(CalcNode node) => node switch
    {
        CalcLeaf leaf => (leaf.Unit is not ("" or "%"), leaf.Unit == "%"),
        CalcSum sum => sum.Terms.Select(term => Mixes(term.Term)).Aggregate((false, false), (all, one) => (all.Item1 || one.Length, all.Item2 || one.Percentage)),
        CalcProduct product => product.Factors.Select(factor => Mixes(factor.Factor)).Aggregate((false, false), (all, one) => (all.Item1 || one.Length, all.Item2 || one.Percentage)),
        CalcFunction function => function.Arguments.Select(Mixes).Aggregate((false, false), (all, one) => (all.Item1 || one.Length, all.Item2 || one.Percentage)),
        _ => (false, false),
    };

    /// <summary>The groups of font-variant-numeric (CSS Fonts 4 §6.7), each given at most once and one value per group;
    /// the last three groups the engine does not draw: fractions need contextual lookups the shaper does not apply (Lato's
    /// 'frac'), and no font of the measured cases has a slashed zero.</summary>
    private static readonly string[][] NumericGroups =
    [
        ["lining-nums", "oldstyle-nums"], ["proportional-nums", "tabular-nums"], ["ordinal"],
        ["diagonal-fractions", "stacked-fractions"], ["slashed-zero"],
    ];

    /// <summary>font-variant-numeric: <c>normal</c>, or keywords of different groups (<see cref="NumericGroups"/>) in any
    /// order, kept as written; a fraction value or slashed-zero is refused.</summary>
    private static ParsedValue ParseFontVariantNumeric(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("normal") is not null)
        {
            return ParsedValue.Of(new CssKeyword("normal"));
        }

        var keywords = new List<string>();
        while (!reader.AtEnd)
        {
            var keyword = reader.Keyword([.. NumericGroups.SelectMany(group => group)]);
            if (keyword is null || keywords.Exists(other => Array.FindIndex(NumericGroups, group => group.Contains(other)) == Array.FindIndex(NumericGroups, group => group.Contains(keyword))))
            {
                return ParsedValue.Invalid("not a value of font-variant-numeric");
            }

            keywords.Add(keyword);
        }

        if (keywords.Find(keyword => Array.FindIndex(NumericGroups, group => group.Contains(keyword)) >= 3) is { } refused)
        {
            return ParsedValue.Refused($"'font-variant-numeric: {refused}' is not supported: {(refused == "slashed-zero" ? "no measured font has a slashed zero" : "fractions need contextual substitutions, which the shaper does not apply")}");
        }

        return ParsedValue.Of(keywords.Count == 1 ? new CssKeyword(keywords[0]) : new CssList([.. keywords.Select(keyword => (CssValue)new CssKeyword(keyword))], CommaSeparated: false));
    }

    /// <summary>
    /// font-feature-settings (CSS Fonts 4 §7.1): <c>normal</c>, or a comma-separated list of a tag (a string of four
    /// characters) and an optional value (an integer of 0 or more, <c>on</c> = 1, <c>off</c> = 0; 1 when left out), each
    /// item as the pair of the tag and its value. Drawn: the figure and ordinal tags with 0 or 1; another tag or value is
    /// refused (an alternate's index, a feature the shaper does not apply).
    /// </summary>
    private static ParsedValue ParseFontFeatureSettings(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("normal") is not null)
        {
            return ParsedValue.Of(new CssKeyword("normal"));
        }

        var items = new List<CssValue>();
        while (true)
        {
            if (reader.AtEnd || reader.Next() is not CssToken { Kind: CssTokenKind.String } tag || tag.Text.Length != 4 || !tag.Text.All(c => c is >= ' ' and <= '~'))
            {
                return ParsedValue.Invalid("not a value of font-feature-settings: a feature tag is a string of four ASCII characters");
            }

            double value = 1;
            if (reader.Keyword("on", "off") is { } state)
            {
                value = state == "on" ? 1 : 0;
            }
            else if (reader.Peek is CssToken { Kind: CssTokenKind.Number } number)
            {
                reader.Next();
                if (number.Number < 0 || number.Number != Math.Floor(number.Number))
                {
                    return ParsedValue.Invalid("not a value of font-feature-settings: a feature value is an integer of 0 or more");
                }

                value = number.Number;
            }

            if (!OptionalFontFeatures.Tags.Contains(tag.Text) || value > 1)
            {
                return ParsedValue.Refused($"'font-feature-settings: \"{CssSerializer.Shortened(tag.Text)}\" {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}' is not supported: the engine switches lnum, onum, pnum, tnum and ordn on or off");
            }

            items.Add(new CssList([new CssString(tag.Text), new CssNumber(value, IsInteger: true)], CommaSeparated: false));
            if (reader.AtEnd)
            {
                break;
            }

            if (reader.Next() is not CssToken { Kind: CssTokenKind.Comma } || reader.AtEnd)
            {
                return ParsedValue.Invalid("not a value of font-feature-settings");
            }
        }

        return ParsedValue.Of(new CssList([.. items], CommaSeparated: true));
    }
}
