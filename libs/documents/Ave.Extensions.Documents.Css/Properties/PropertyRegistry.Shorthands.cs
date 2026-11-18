using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// The shorthands (CSS Cascade 5 §1.2): each expands to all its longhands, those it does not name set to
/// <c>initial</c>. The logical margin and padding properties are shorthands of the physical ones here, since the engine
/// is horizontal and left-to-right only (CSS Logical 1 §4).
/// </summary>
public static partial class PropertyRegistry
{
    private static readonly CssValue InitialKeywordValue = new CssKeyword("initial");

    private static IReadOnlyList<ShorthandDefinition> ShorthandProperties()
    {
        var shorthands = new List<ShorthandDefinition>
        {
            BoxSides("margin", side => $"margin-{side}", (reader, name) => ParseMargin(reader, name), "CSS Box Model 3 §5.1"),
            BoxSides("padding", side => $"padding-{side}", (reader, name) => Whole(reader, reader.Length(true, true, name), name), "CSS Box Model 3 §5.2"),
            BoxSides("border-width", side => $"border-{side}-width", ParseBorderWidth, "CSS Backgrounds 3 §3.3"),
            BoxSides("border-style", side => $"border-{side}-style", (reader, name) => Find(name)!.Parse(reader), "CSS Backgrounds 3 §3.2"),
            BoxSides("border-color", side => $"border-{side}-color", (reader, name) => Whole(reader, reader.Color(), name), "CSS Backgrounds 3 §3.1"),
            BoxSides("inset", side => side, (reader, name) => Find(name)!.Parse(reader), "CSS Positioned Layout 3 §3.1"),
            BoxSides("border-radius", side => $"border-{Corner(side)}-radius", (reader, name) => Find(name)!.Parse(reader), "CSS Backgrounds 3 §5.1"),
            Logical("margin-block", ["margin-top", "margin-bottom"], (reader, name) => ParseMargin(reader, name)),
            Logical("margin-inline", ["margin-left", "margin-right"], (reader, name) => ParseMargin(reader, name)),
            Logical("padding-block", ["padding-top", "padding-bottom"], (reader, name) => Whole(reader, reader.Length(true, true, name), name)),
            Logical("padding-inline", ["padding-left", "padding-right"], (reader, name) => Whole(reader, reader.Length(true, true, name), name)),
            Alias("margin-block-start", "margin-top", "CSS Logical 1 §4.2"),
            Alias("margin-block-end", "margin-bottom", "CSS Logical 1 §4.2"),
            Alias("margin-inline-start", "margin-left", "CSS Logical 1 §4.2"),
            Alias("margin-inline-end", "margin-right", "CSS Logical 1 §4.2"),
            Alias("padding-block-start", "padding-top", "CSS Logical 1 §4.3"),
            Alias("padding-block-end", "padding-bottom", "CSS Logical 1 §4.3"),
            Alias("padding-inline-start", "padding-left", "CSS Logical 1 §4.3"),
            Alias("padding-inline-end", "padding-right", "CSS Logical 1 §4.3"),
            Alias("word-wrap", "overflow-wrap", "CSS Text 3 §5.5 (legacy name)"),
        };

        foreach (var side in Sides)
        {
            shorthands.Add(new ShorthandDefinition(
                $"border-{side}", [$"border-{side}-width", $"border-{side}-style", $"border-{side}-color"], "<line-width> || <line-style> || <color>",
                "CSS Backgrounds 3 §3.4", reader => ParseBorderSide(reader, [side])));
        }

        shorthands.Add(new ShorthandDefinition(
            "border", [.. Sides.SelectMany(side => new[] { $"border-{side}-width", $"border-{side}-style", $"border-{side}-color" })],
            "<line-width> || <line-style> || <color>", "CSS Backgrounds 3 §3.4", reader => ParseBorderSide(reader, Sides)));

        shorthands.Add(new ShorthandDefinition(
            "outline", OutlineLonghands, "<line-width> || none || <color>", "CSS UI 4 §3.1", ParseOutline));

        shorthands.Add(new ShorthandDefinition(
            "overflow", ["overflow-x", "overflow-y"], "[ visible | hidden | clip ]{1,2}", "CSS Overflow 3 §3.1",
            reader => Pair(reader, "overflow-x", "overflow-y")));

        shorthands.Add(new ShorthandDefinition(
            "background", BackgroundLonghands,
            "<color> | none (one layer, no image)", "CSS Backgrounds 3 §2.10", ParseBackground));

        shorthands.Add(new ShorthandDefinition(
            "font", FontLonghands, "[ normal | italic || <font-weight> ]? <font-size> [ / <line-height> ]? <font-family>#", "CSS Fonts 4 §2.8", ParseFont));

        shorthands.Add(new ShorthandDefinition(
            "font-variant", FontVariantLonghands,
            "normal", "CSS Fonts 4 §6.12",
            reader => reader.Count == 1 && reader.Keyword("normal") is not null
                ? Expand(FontVariantLonghands, new Dictionary<string, CssValue>())
                : ShorthandParse.Refused("font-variant is supported only as normal")));

        shorthands.Add(new ShorthandDefinition(
            "font-synthesis", ["font-synthesis-weight", "font-synthesis-style", "font-synthesis-small-caps"], "none | [ weight || style || small-caps ]",
            "CSS Fonts 4 §7.3", ParseFontSynthesis));

        shorthands.Add(new ShorthandDefinition(
            "list-style", ListStyleLonghands, "<list-style-position> || none || <list-style-type>",
            "CSS Lists 3 §3.6", ParseListStyle));

        shorthands.Add(new ShorthandDefinition(
            "text-decoration", TextDecorationLonghands,
            "<text-decoration-line> || <text-decoration-style> || <color>", "CSS Text Decoration 3 §2.4", ParseTextDecoration));

        shorthands.Add(new ShorthandDefinition(
            "white-space", WhiteSpaceLonghands, "normal | pre | nowrap | pre-wrap | <white-space-collapse> || <text-wrap-mode>",
            "CSS Text 4 §3", ParseWhiteSpace));

        shorthands.Add(new ShorthandDefinition(
            "text-wrap", TextWrapLonghands, "<text-wrap-mode> || <text-wrap-style>", "CSS Text 4 §6.3",
            reader =>
            {
                var values = new Dictionary<string, CssValue>();
                foreach (var item in reader.Remaining)
                {
                    var single = new ValueReader([item]);
                    var parsed = !values.ContainsKey("text-wrap-mode") && single.Keyword("wrap", "nowrap") is { } mode
                        ? (Name: "text-wrap-mode", Value: ParsedValue.Of(new CssKeyword(mode)))
                        : (Name: "text-wrap-style", Value: values.ContainsKey("text-wrap-style") ? ParsedValue.Invalid("not a value of text-wrap") : Find("text-wrap-style")!.Parse(new ValueReader([item])));
                    if (!parsed.Value.IsValid)
                    {
                        return ShorthandParse.Failed(parsed.Value, "not a value of text-wrap");
                    }

                    values[parsed.Name] = parsed.Value.Value!;
                }

                return reader.Count == 0 ? ShorthandParse.Invalid("text-wrap needs a value") : Expand(TextWrapLonghands, values);
            }));

        shorthands.Add(new ShorthandDefinition(
            "gap", ["row-gap", "column-gap"], "normal", "CSS Box Alignment 3 §8.3", reader => Pair(reader, "row-gap", "column-gap")));

        shorthands.Add(new ShorthandDefinition(
            "columns", ColumnsLonghands, "auto", "CSS Multi-column 1 §3.3",
            reader => reader.Count == 1 && reader.Keyword("auto") is not null ? Expand(ColumnsLonghands, new Dictionary<string, CssValue>()) : ShorthandParse.Refused("multi-column layout is not supported")));

        foreach (var (name, longhand) in new[] { ("page-break-before", "break-before"), ("page-break-after", "break-after") })
        {
            shorthands.Add(new ShorthandDefinition(
                name, [longhand], "auto | always | avoid", "CSS Fragmentation 3 §3.4 (legacy)",
                reader => reader.Count == 1 && reader.Keyword("auto", "always", "avoid", "left", "right") is { } keyword
                    ? keyword is "left" or "right"
                        ? ShorthandParse.Refused($"'{name}: {keyword}' is not supported")
                        : new ShorthandParse(new Dictionary<string, CssValue> { [longhand] = new CssKeyword(keyword == "always" ? "page" : keyword) }, null, false)
                    : ShorthandParse.Invalid($"not a value of {name}")));
        }

        shorthands.Add(new ShorthandDefinition(
            "page-break-inside", ["break-inside"], "auto | avoid", "CSS Fragmentation 3 §3.4 (legacy)",
            reader => reader.Count == 1 && reader.Keyword("auto", "avoid") is { } keyword
                ? new ShorthandParse(new Dictionary<string, CssValue> { ["break-inside"] = new CssKeyword(keyword) }, null, false)
                : ShorthandParse.Invalid("not a value of page-break-inside")));

        return shorthands;
    }

    private static string Corner(string side) => side switch
    {
        "top" => "top-left",
        "right" => "top-right",
        "bottom" => "bottom-right",
        _ => "bottom-left",
    };

    /// <summary>The longhands the outline shorthand sets and resets (CSS UI 4 §3.1).</summary>
    private static readonly string[] OutlineLonghands = ["outline-width", "outline-style", "outline-color"];

    /// <summary>The longhands the font-variant shorthand sets and resets (CSS Fonts 4 §6.12).</summary>
    private static readonly string[] FontVariantLonghands = ["font-variant-ligatures", "font-variant-caps", "font-variant-alternates", "font-variant-numeric", "font-variant-east-asian", "font-variant-position", "font-variant-emoji"];

    /// <summary>The longhands the list-style shorthand sets and resets (CSS Lists 3 §3.6).</summary>
    private static readonly string[] ListStyleLonghands = ["list-style-position", "list-style-image", "list-style-type"];

    /// <summary>The longhands the text-decoration shorthand sets and resets (CSS Text Decoration 3 §2.4).</summary>
    private static readonly string[] TextDecorationLonghands = ["text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness"];

    /// <summary>The longhands the white-space shorthand sets and resets (CSS Text 4 §3).</summary>
    private static readonly string[] WhiteSpaceLonghands = ["white-space-collapse", "text-wrap-mode"];

    /// <summary>The longhands the text-wrap shorthand sets and resets (CSS Text 4 §6.3).</summary>
    private static readonly string[] TextWrapLonghands = ["text-wrap-mode", "text-wrap-style"];

    /// <summary>The longhands the columns shorthand sets and resets (CSS Multi-column 1 §3.3).</summary>
    private static readonly string[] ColumnsLonghands = ["column-width", "column-count"];

    /// <summary>The longhands the font shorthand sets and resets (CSS Fonts 4 §2.8).</summary>
    private static readonly string[] FontLonghands =
    [
        "font-style", "font-variant-caps", "font-weight", "font-stretch", "font-size", "line-height", "font-family",
        "font-variant-ligatures", "font-variant-numeric", "font-variant-east-asian", "font-variant-position",
        "font-variant-alternates", "font-variant-emoji", "font-size-adjust", "font-kerning", "font-optical-sizing",
        "font-feature-settings", "font-variation-settings", "font-language-override",
    ];

    /// <summary>Every longhand, the given values set and the rest <c>initial</c>.</summary>
    private static ShorthandParse Expand(IEnumerable<string> longhands, Dictionary<string, CssValue> values)
    {
        foreach (var longhand in longhands)
        {
            values.TryAdd(longhand, InitialKeywordValue);
        }

        return new ShorthandParse(values, null, false);
    }

    /// <summary>A shorthand of the four sides: 1 to 4 values, top, right, bottom, left (CSS Box Model 3 §5).</summary>
    private static ShorthandDefinition BoxSides(string name, Func<string, string> longhand, Func<ValueReader, string, ParsedValue> parse, string specification) =>
        new(name, [.. Sides.Select(longhand)], $"<'{longhand(Sides[0])}'>{{1,4}}", specification, reader =>
        {
            var values = new List<CssValue>();
            foreach (var item in reader.Remaining)
            {
                var one = parse(new ValueReader([item]), longhand(Sides[Math.Min(values.Count, 3)]));
                if (!one.IsValid)
                {
                    return ShorthandParse.Failed(one, $"not a value of {name}");
                }

                values.Add(one.Value!);
            }

            if (values.Count is < 1 or > 4)
            {
                return ShorthandParse.Invalid($"{name} takes 1 to 4 values");
            }

            var top = values[0];
            var right = values.Count > 1 ? values[1] : top;
            var bottom = values.Count > 2 ? values[2] : top;
            var left = values.Count > 3 ? values[3] : right;
            return new ShorthandParse(new Dictionary<string, CssValue>
            {
                [longhand("top")] = top,
                [longhand("right")] = right,
                [longhand("bottom")] = bottom,
                [longhand("left")] = left,
            }, null, false);
        });

    /// <summary>A logical shorthand of two physical longhands: one value for both, or start then end.</summary>
    private static ShorthandDefinition Logical(string name, string[] longhands, Func<ValueReader, string, ParsedValue> parse) =>
        new(name, longhands, $"<'{longhands[0]}'>{{1,2}}", "CSS Logical 1 §4", reader =>
        {
            var values = new List<CssValue>();
            foreach (var item in reader.Remaining)
            {
                var one = parse(new ValueReader([item]), longhands[Math.Min(values.Count, 1)]);
                if (!one.IsValid)
                {
                    return ShorthandParse.Failed(one, $"not a value of {name}");
                }

                values.Add(one.Value!);
            }

            return values.Count is 1 or 2
                ? new ShorthandParse(new Dictionary<string, CssValue> { [longhands[0]] = values[0], [longhands[1]] = values[^1] }, null, false)
                : ShorthandParse.Invalid($"{name} takes 1 or 2 values");
        });

    /// <summary>A property that is another's other name.</summary>
    private static ShorthandDefinition Alias(string name, string longhand, string specification) =>
        new(name, [longhand], Find(longhand)?.Supported ?? string.Empty, specification, reader =>
        {
            var value = Find(longhand)!.Parse(reader);
            return value.IsValid
                ? new ShorthandParse(new Dictionary<string, CssValue> { [longhand] = value.Value! }, null, false)
                : ShorthandParse.Failed(value, $"not a value of {name}");
        });

    /// <summary>Two keyword longhands: one value for both, or two values.</summary>
    private static ShorthandParse Pair(ValueReader reader, string first, string second)
    {
        if (reader.Count is < 1 or > 2)
        {
            return ShorthandParse.Invalid("one or two values");
        }

        var values = reader.Remaining;
        var a = Find(first)!.Parse(new ValueReader([values[0]]));
        var b = Find(second)!.Parse(new ValueReader([values[^1]]));
        foreach (var one in new[] { a, b })
        {
            if (!one.IsValid)
            {
                return ShorthandParse.Failed(one, "not a value");
            }
        }

        return new ShorthandParse(new Dictionary<string, CssValue> { [first] = a.Value!, [second] = b.Value! }, null, false);
    }

    /// <summary>border, border-top …: &lt;line-width&gt; || &lt;line-style&gt; || &lt;color&gt; for the given sides.</summary>
    private static ShorthandParse ParseBorderSide(ValueReader reader, string[] sides)
    {
        CssValue? width = null, style = null, color = null;
        foreach (var item in reader.Remaining)
        {
            var single = new ValueReader([item]);
            if (style is null && Find("border-top-style")!.Parse(single) is var styleValue && (styleValue.IsValid || styleValue.Unsupported))
            {
                if (!styleValue.IsValid)
                {
                    return ShorthandParse.Failed(styleValue);
                }

                style = styleValue.Value;
                continue;
            }

            single = new ValueReader([item]);
            if (width is null && ParseBorderWidth(single, "border-width") is var widthValue && (widthValue.IsValid || widthValue.Unsupported))
            {
                if (!widthValue.IsValid)
                {
                    return ShorthandParse.Failed(widthValue);
                }

                width = widthValue.Value;
                continue;
            }

            single = new ValueReader([item]);
            if (color is null && single.Color() is { } colorValue)
            {
                if (!colorValue.IsValid)
                {
                    return ShorthandParse.Failed(colorValue);
                }

                color = colorValue.Value;
                continue;
            }

            return ShorthandParse.Invalid("not a value of border");
        }

        if (reader.Count == 0)
        {
            return ShorthandParse.Invalid("border needs a value");
        }

        var values = new Dictionary<string, CssValue>();
        foreach (var side in sides)
        {
            values[$"border-{side}-width"] = width ?? InitialKeywordValue;
            values[$"border-{side}-style"] = style ?? InitialKeywordValue;
            values[$"border-{side}-color"] = color ?? InitialKeywordValue;
        }

        return new ShorthandParse(values, null, false);
    }

    /// <summary>outline = &lt;'outline-width'&gt; || &lt;'outline-style'&gt; || &lt;'outline-color'&gt; (CSS UI 4 §3.1), in
    /// any order, each at most once; a style the longhand refuses refuses the shorthand.</summary>
    private static ShorthandParse ParseOutline(ValueReader reader)
    {
        var values = new Dictionary<string, CssValue>();
        foreach (var item in reader.Remaining)
        {
            var single = new ValueReader([item]);
            if (!values.ContainsKey("outline-style") && Find("outline-style")!.Parse(single) is var style && (style.IsValid || style.Unsupported))
            {
                if (!style.IsValid)
                {
                    return ShorthandParse.Failed(style);
                }

                values["outline-style"] = style.Value!;
                continue;
            }

            single = new ValueReader([item]);
            if (!values.ContainsKey("outline-width") && ParseBorderWidth(single, "outline-width") is { IsValid: true } width)
            {
                values["outline-width"] = width.Value!;
                continue;
            }

            single = new ValueReader([item]);
            if (!values.ContainsKey("outline-color") && Find("outline-color")!.Parse(single) is { IsValid: true } color)
            {
                values["outline-color"] = color.Value!;
                continue;
            }

            return ShorthandParse.Invalid("not a value of outline");
        }

        return reader.Count == 0 ? ShorthandParse.Invalid("outline needs a value") : Expand(OutlineLonghands, values);
    }

    /// <summary>background (CSS Backgrounds 3 §2.10): one layer with a colour and no image. An image, a position, a size
    /// or several layers are refused. One box sets both background-origin and background-clip, two set them in that
    /// order (§2.10; Chromium 159, measured: <c>background: red padding-box</c> clips to the padding box); each
    /// component comes at most once, so a third box or a second attachment is invalid.</summary>
    private static ShorthandParse ParseBackground(ValueReader reader)
    {
        var values = new Dictionary<string, CssValue>();
        foreach (var item in reader.Remaining)
        {
            if (item is CssToken { Kind: CssTokenKind.Comma })
            {
                return ShorthandParse.Refused("several background layers are not supported");
            }

            var single = new ValueReader([item]);
            if (single.Keyword("none") is not null && !values.ContainsKey("background-image"))
            {
                values["background-image"] = new CssKeyword("none");
                continue;
            }

            single = new ValueReader([item]);
            if (!values.ContainsKey("background-color") && single.Color() is { } color)
            {
                if (!color.IsValid)
                {
                    return ShorthandParse.Failed(color);
                }

                values["background-color"] = color.Value!;
                continue;
            }

            single = new ValueReader([item]);
            if (single.Keyword("repeat", "scroll", "padding-box", "border-box") is { } initialKeyword)
            {
                var longhand = initialKeyword switch
                {
                    "repeat" => "background-repeat",
                    "scroll" => "background-attachment",
                    _ => values.ContainsKey("background-origin") ? "background-clip" : "background-origin",
                };
                // <repeat-style> takes one or two keywords (repeat repeat is repeat); the rest come once.
                if (values.ContainsKey(longhand) && longhand != "background-repeat")
                {
                    return ShorthandParse.Invalid("not a value of background");
                }

                values[longhand] = new CssKeyword(initialKeyword);
                continue;
            }

            return ShorthandParse.Refused("a background image, position, size or repeat is not supported; only a colour");
        }

        if (values.TryGetValue("background-origin", out var origin) && !values.ContainsKey("background-clip"))
        {
            values["background-clip"] = origin;
        }

        return reader.Count == 0 ? ShorthandParse.Invalid("background needs a value") : Expand(BackgroundLonghands, values);
    }

    /// <summary>The longhands the background shorthand sets and resets (CSS Backgrounds 3 §2.10).</summary>
    private static readonly string[] BackgroundLonghands =
    [
        "background-color", "background-image", "background-repeat", "background-attachment", "background-position-x",
        "background-position-y", "background-size", "background-origin", "background-clip",
    ];

    /// <summary>font (CSS Fonts 4 §2.8): style and weight (and normal) before the size, the size, an optional line height
    /// after a slash, and the family list. The system font keywords, small-caps and stretch keywords are refused.</summary>
    private static ShorthandParse ParseFont(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("caption", "icon", "menu", "message-box", "small-caption", "status-bar") is { } system)
        {
            return ShorthandParse.Refused($"the system font '{system}' is not supported");
        }

        var values = new Dictionary<string, CssValue>();
        for (var i = 0; i < 4 && !reader.AtEnd; i++)
        {
            var mark = reader.Mark;
            if (reader.Keyword("normal") is not null)
            {
                continue;
            }

            if (reader.Keyword("italic") is not null && !values.ContainsKey("font-style"))
            {
                values["font-style"] = new CssKeyword("italic");
                continue;
            }

            if (reader.Keyword("oblique") is not null)
            {
                return ShorthandParse.Refused("font-style: oblique is not supported");
            }

            if (reader.Keyword("small-caps") is not null)
            {
                return ShorthandParse.Refused("font-variant: small-caps is not supported");
            }

            if (reader.Keyword("ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "semi-expanded", "expanded", "extra-expanded", "ultra-expanded") is { } stretch)
            {
                return ShorthandParse.Refused($"font-stretch: {stretch} is not supported");
            }

            if (!values.ContainsKey("font-weight") && reader.Keyword("bold", "bolder", "lighter") is { } weight)
            {
                values["font-weight"] = weight == "bold" ? new CssNumber(700) : new CssKeyword(weight);
                continue;
            }

            if (!values.ContainsKey("font-weight") && reader.Peek is CssToken { Kind: CssTokenKind.Number } number && number.Number is >= 1 and <= 1000)
            {
                reader.Next();
                values["font-weight"] = new CssNumber(number.Number);
                continue;
            }

            reader.Reset(mark);
            break;
        }

        if (reader.AtEnd)
        {
            return ShorthandParse.Invalid("font needs a size and a family");
        }

        var sizeReader = new ValueReader([reader.Next()]);
        var size = ParseFontSize(sizeReader);
        if (!size.IsValid)
        {
            return ShorthandParse.Failed(size, "font needs a size before the family");
        }

        values["font-size"] = size.Value!;
        if (reader.Delim('/'))
        {
            if (reader.AtEnd)
            {
                return ShorthandParse.Invalid("font needs a line height after the slash");
            }

            var lineHeight = ParseLineHeight(new ValueReader([reader.Next()]));
            if (!lineHeight.IsValid)
            {
                return ShorthandParse.Failed(lineHeight, "not a line height");
            }

            values["line-height"] = lineHeight.Value!;
        }

        var family = ParseFontFamily(reader.Rest());
        if (!family.IsValid)
        {
            return ShorthandParse.Failed(family, "not a font family");
        }

        values["font-family"] = family.Value!;
        return Expand(FontLonghands, values);
    }

    /// <summary>font-synthesis = none | [ weight || style || small-caps ] (CSS Fonts 4 §7.3): each named longhand
    /// auto, the others none; a keyword named twice is invalid (Chromium 159, measured: <c>weight weight</c> is
    /// dropped).</summary>
    private static ShorthandParse ParseFontSynthesis(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("none") is not null)
        {
            return new ShorthandParse(new Dictionary<string, CssValue>
            {
                ["font-synthesis-weight"] = new CssKeyword("none"),
                ["font-synthesis-style"] = new CssKeyword("none"),
                ["font-synthesis-small-caps"] = new CssKeyword("none"),
            }, null, false);
        }

        var values = new Dictionary<string, CssValue>
        {
            ["font-synthesis-weight"] = new CssKeyword("none"),
            ["font-synthesis-style"] = new CssKeyword("none"),
            ["font-synthesis-small-caps"] = new CssKeyword("none"),
        };
        var named = new HashSet<string>(StringComparer.Ordinal);
        while (!reader.AtEnd)
        {
            var keyword = reader.Keyword("weight", "style", "small-caps");
            if (keyword is not null && !named.Add(keyword))
            {
                return ShorthandParse.Invalid("not a value of font-synthesis");
            }

            switch (keyword)
            {
                case "weight":
                    values["font-synthesis-weight"] = new CssKeyword("auto");
                    break;
                case "style":
                    values["font-synthesis-style"] = new CssKeyword("auto");
                    break;
                case "small-caps":
                    values["font-synthesis-small-caps"] = new CssKeyword("auto");
                    break;
                default:
                    return ShorthandParse.Invalid("not a value of font-synthesis");
            }
        }

        return new ShorthandParse(values, null, false);
    }

    /// <summary>list-style (CSS Lists 3 §3.6): position || image || type; <c>none</c> sets the image and, if no type is
    /// given, the type.</summary>
    private static ShorthandParse ParseListStyle(ValueReader reader)
    {
        var values = new Dictionary<string, CssValue>();
        var nones = 0;
        foreach (var item in reader.Remaining)
        {
            var single = new ValueReader([item]);
            if (single.Keyword("none") is not null)
            {
                nones++;
                continue;
            }

            single = new ValueReader([item]);
            if (!values.ContainsKey("list-style-position") && single.Keyword("inside", "outside") is { } position)
            {
                if (position == "inside")
                {
                    return ShorthandParse.Refused("list-style-position: inside is not supported");
                }

                values["list-style-position"] = new CssKeyword(position);
                continue;
            }

            if (item is CssToken { Kind: CssTokenKind.Url } || item is FunctionValue)
            {
                return ShorthandParse.Refused("list-style-image is not supported");
            }

            var type = Find("list-style-type")!.Parse(new ValueReader([item]));
            if (!values.ContainsKey("list-style-type") && (type.IsValid || type.Unsupported))
            {
                if (!type.IsValid)
                {
                    return ShorthandParse.Failed(type);
                }

                values["list-style-type"] = type.Value!;
                continue;
            }

            return ShorthandParse.Invalid("not a value of list-style");
        }

        if (nones > 2 || (nones == 2 && values.ContainsKey("list-style-type")) || reader.Count == 0)
        {
            return ShorthandParse.Invalid("not a value of list-style");
        }

        if (nones > 0)
        {
            values["list-style-image"] = new CssKeyword("none");
            if (!values.ContainsKey("list-style-type"))
            {
                values["list-style-type"] = new CssKeyword("none");
            }
        }

        return Expand(ListStyleLonghands, values);
    }

    /// <summary>white-space (CSS Text 4 §3): the CSS 2 keywords, each a pair of white-space-collapse and text-wrap-mode
    /// (normal = collapse wrap, pre = preserve nowrap, nowrap = collapse nowrap, pre-wrap = preserve wrap; pre-line and
    /// break-spaces keep line breaks or spaces the layout does not), or the longhands' values in any order.</summary>
    private static ShorthandParse ParseWhiteSpace(ValueReader reader)
    {
        static ShorthandParse Pair(string collapse, string wrap) =>
            new(new Dictionary<string, CssValue> { ["white-space-collapse"] = new CssKeyword(collapse), ["text-wrap-mode"] = new CssKeyword(wrap) }, null, false);

        if (reader.Count == 1 && reader.Keyword("normal", "pre", "nowrap", "pre-wrap", "pre-line", "break-spaces") is { } keyword)
        {
            return keyword switch
            {
                "normal" => Pair("collapse", "wrap"),
                "pre" => Pair("preserve", "nowrap"),
                "nowrap" => Pair("collapse", "nowrap"),
                "pre-wrap" => Pair("preserve", "wrap"),
                _ => ShorthandParse.Refused($"'white-space: {keyword}' is not supported"),
            };
        }

        var values = new Dictionary<string, CssValue>();
        foreach (var item in reader.Remaining)
        {
            var collapse = Find("white-space-collapse")!.Parse(new ValueReader([item]));
            var wrap = Find("text-wrap-mode")!.Parse(new ValueReader([item]));
            var (name, value) = collapse.IsValid || collapse.Unsupported ? ("white-space-collapse", collapse) : ("text-wrap-mode", wrap);
            if (!value.IsValid || values.ContainsKey(name))
            {
                return value.Unsupported ? ShorthandParse.Failed(value) : ShorthandParse.Invalid("not a value of white-space");
            }

            values[name] = value.Value!;
        }

        return reader.Count == 0 ? ShorthandParse.Invalid("white-space needs a value") : Expand(WhiteSpaceLonghands, values);
    }

    /// <summary>text-decoration (CSS Text Decoration 3 §2.4): line || style || color.</summary>
    private static ShorthandParse ParseTextDecoration(ValueReader reader)
    {
        var values = new Dictionary<string, CssValue>();
        var lines = new List<ComponentValue>();
        foreach (var item in reader.Remaining)
        {
            var single = new ValueReader([item]);
            if (single.Keyword("none", "underline", "overline", "line-through", "blink") is not null)
            {
                lines.Add(item);
                continue;
            }

            single = new ValueReader([item]);
            if (!values.ContainsKey("text-decoration-style") && Find("text-decoration-style")!.Parse(single) is var style && (style.IsValid || style.Unsupported))
            {
                if (!style.IsValid)
                {
                    return ShorthandParse.Failed(style);
                }

                values["text-decoration-style"] = style.Value!;
                continue;
            }

            single = new ValueReader([item]);
            if (!values.ContainsKey("text-decoration-color") && single.Color() is { } color)
            {
                if (!color.IsValid)
                {
                    return ShorthandParse.Failed(color);
                }

                values["text-decoration-color"] = color.Value!;
                continue;
            }

            return ShorthandParse.Refused("a text-decoration thickness or other value is not supported");
        }

        if (lines.Count > 0)
        {
            var line = Find("text-decoration-line")!.Parse(new ValueReader(lines));
            if (!line.IsValid)
            {
                return ShorthandParse.Failed(line, "not a decoration line");
            }

            values["text-decoration-line"] = line.Value!;
        }

        return reader.Count == 0 ? ShorthandParse.Invalid("text-decoration needs a value") : Expand(TextDecorationLonghands, values);
    }
}
