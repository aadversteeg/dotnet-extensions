using System;
using System.Collections.Generic;
using System.Globalization;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Values;

/// <summary>The outcome of reading a &lt;color&gt;.</summary>
internal enum ColorOutcome
{
    /// <summary>A colour.</summary>
    Color,

    /// <summary><c>currentcolor</c> (CSS Color 4 §6.4).</summary>
    CurrentColor,

    /// <summary>Not a colour.</summary>
    Invalid,

    /// <summary>A colour CSS defines that the engine does not support (lab(), color(), a system colour …).</summary>
    Unsupported,
}

/// <summary>
/// &lt;color&gt; (CSS Color 4): hex colours (§5.2), named colours (§6.1), <c>transparent</c> (§6.3), <c>currentcolor</c>
/// (§6.4), <c>rgb()</c>/<c>rgba()</c> (§5.1) and <c>hsl()</c>/<c>hsla()</c> (§7) in their legacy and modern syntaxes, and
/// the system colours whose value HTML fixes (Canvas, CanvasText, LinkText, VisitedText, ActiveText, as Chromium has
/// them). Every other colour function and system colour is refused.
/// </summary>
internal static partial class Colors
{
    /// <summary>The system colours the engine resolves (CSS Color 4 §6.2), with Chromium 159's values in a light colour
    /// scheme (the HTML Standard §15.3.4 gives the link colours; a printed page is white).</summary>
    private static readonly Dictionary<string, CssColor> SystemColors = new(StringComparer.Ordinal)
    {
        ["canvas"] = new(255, 255, 255, 1),
        ["canvastext"] = new(0, 0, 0, 1),
        ["linktext"] = new(0, 0, 0xEE, 1),
        ["visitedtext"] = new(0x55, 0x1A, 0x8B, 1),
        ["activetext"] = new(0xFF, 0, 0, 1),
    };

    /// <summary>The system colours CSS Color 4 §6.2 and §6.2.1 define (current and deprecated) that are refused.</summary>
    private static readonly HashSet<string> OtherSystem =
    [
        "accentcolor", "accentcolortext", "buttonborder", "buttonface", "buttontext", "field", "fieldtext", "graytext",
        "highlight", "highlighttext", "mark", "marktext", "selecteditem", "selecteditemtext", "activeborder",
        "activecaption", "appworkspace", "background", "buttonhighlight", "buttonshadow", "captiontext",
        "inactiveborder", "inactivecaption", "inactivecaptiontext", "infobackground", "infotext", "menu", "menutext",
        "scrollbar", "threeddarkshadow", "threedface", "threedhighlight", "threedlightshadow", "threedshadow", "window",
        "windowframe", "windowtext",
    ];

    /// <summary>The colour functions of CSS Color 4 and 5 the engine refuses.</summary>
    private static readonly HashSet<string> OtherFunctions =
        ["hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix", "light-dark", "contrast-color", "device-cmyk", "-webkit-image-set"];

    /// <summary>Reads one component value as a &lt;color&gt;.</summary>
    /// <param name="value">The value.</param>
    /// <param name="color">The colour, for <see cref="ColorOutcome.Color"/>.</param>
    /// <param name="message">Why, for <see cref="ColorOutcome.Unsupported"/>.</param>
    /// <returns>What it is.</returns>
    public static ColorOutcome Read(ComponentValue value, out CssColor color, out string? message)
    {
        color = default;
        message = null;
        switch (value)
        {
            case CssToken { Kind: CssTokenKind.Hash } hash:
                return Hex(hash.Text, out color) ? ColorOutcome.Color : ColorOutcome.Invalid;
            case CssToken { Kind: CssTokenKind.Ident } ident:
                var name = Ascii.ToLower(ident.Text);
                if (name == "currentcolor")
                {
                    return ColorOutcome.CurrentColor;
                }

                if (name == "transparent")
                {
                    color = CssColor.Transparent;
                    return ColorOutcome.Color;
                }

                if (Named.TryGetValue(name, out var rgb))
                {
                    color = new CssColor(rgb.Red, rgb.Green, rgb.Blue, 1);
                    return ColorOutcome.Color;
                }

                if (SystemColors.TryGetValue(name, out color))
                {
                    return ColorOutcome.Color;
                }

                if (OtherSystem.Contains(name))
                {
                    message = $"the system colour '{ident.Text}' is not supported";
                    return ColorOutcome.Unsupported;
                }

                return ColorOutcome.Invalid;
            case FunctionValue function:
                var functionName = Ascii.ToLower(function.Name);
                if (functionName is "rgb" or "rgba")
                {
                    return Rgb(function, out color, out message);
                }

                if (functionName is "hsl" or "hsla")
                {
                    return Hsl(function, out color, out message);
                }

                if (OtherFunctions.Contains(functionName))
                {
                    message = $"the colour function {functionName}() is not supported";
                    return ColorOutcome.Unsupported;
                }

                return ColorOutcome.Invalid;
            default:
                return ColorOutcome.Invalid;
        }
    }

    /// <summary>§5.2: 3, 4, 6 or 8 hex digits.</summary>
    private static bool Hex(string digits, out CssColor color)
    {
        color = default;
        foreach (var c in digits)
        {
            if (!Ascii.IsHexDigit(c))
            {
                return false;
            }
        }

        int Pair(int i) => int.Parse(digits.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int Single(int i) => int.Parse(digits.AsSpan(i, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture) * 17;
        switch (digits.Length)
        {
            case 3:
                color = new CssColor(Single(0), Single(1), Single(2), 1);
                return true;
            case 4:
                color = new CssColor(Single(0), Single(1), Single(2), Single(3) / 255.0);
                return true;
            case 6:
                color = new CssColor(Pair(0), Pair(2), Pair(4), 1);
                return true;
            case 8:
                color = new CssColor(Pair(0), Pair(2), Pair(4), Pair(6) / 255.0);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The arguments of a colour function: legacy (commas) or modern (spaces, '/' before alpha).</summary>
    private static (List<ComponentValue> Channels, ComponentValue? Alpha, bool Legacy, bool Valid) Arguments(FunctionValue function)
    {
        var values = new List<ComponentValue>();
        foreach (var value in function.Values)
        {
            if (value is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                values.Add(value);
            }
        }

        if (values.Exists(value => value is CssToken { Kind: CssTokenKind.Comma }))
        {
            // Legacy: c, c, c [, a]
            var channels = new List<ComponentValue>();
            for (var i = 0; i < values.Count; i++)
            {
                if (i % 2 == 1)
                {
                    if (values[i] is not CssToken { Kind: CssTokenKind.Comma })
                    {
                        return ([], null, true, false);
                    }

                    continue;
                }

                channels.Add(values[i]);
            }

            if (values.Count % 2 == 0 || channels.Count is not (3 or 4))
            {
                return ([], null, true, false);
            }

            return (channels.GetRange(0, 3), channels.Count == 4 ? channels[3] : null, true, true);
        }

        var slash = values.FindIndex(value => value is CssToken token && token.IsDelim('/'));
        if (slash < 0)
        {
            return (values, null, false, values.Count == 3);
        }

        return (values.GetRange(0, slash), slash + 2 == values.Count ? values[slash + 1] : null, false, slash == 3 && slash + 2 == values.Count);
    }

    /// <summary>A number, a percentage, or a calc() of either, as (value, isPercentage); null when it is not one.</summary>
    private static (double Value, bool IsPercentage)? NumberOrPercentage(ComponentValue value)
    {
        switch (value)
        {
            case CssToken { Kind: CssTokenKind.Number } number:
                return (number.Number, false);
            case CssToken { Kind: CssTokenKind.Percentage } percentage:
                return (percentage.Number, true);
            case FunctionValue function when Calc.IsMathFunction(function) && Calc.Parse(function, out _) is { } node:
                var type = Calc.TypeOf(node);
                if (type is not (CalcType.Number or CalcType.Percentage) || Calc.Evaluate(node, (_, _) => null, null) is not { } result)
                {
                    return null;
                }

                return type == CalcType.Number ? (result.Number, false) : (result.Percent, true);
            default:
                return null;
        }
    }

    /// <summary>The reason a channel is refused when it is a math function the engine does not evaluate (random(),
    /// sign() …: valid in Chromium, so the colour is refused rather than dropped); null otherwise.</summary>
    private static string? UnsupportedMath(IEnumerable<ComponentValue?> channels)
    {
        foreach (var channel in channels)
        {
            if (channel is FunctionValue function && Calc.IsMathFunction(function) && Calc.Parse(function, out var problem) is null
                && problem is not null && problem.StartsWith("unsupported:", StringComparison.Ordinal))
            {
                return problem["unsupported:".Length..];
            }
        }

        return null;
    }

    private static bool IsNone(ComponentValue value) => value is CssToken token && token.IsIdent("none");

    /// <summary>&lt;alpha-value&gt; = &lt;number&gt; | &lt;percentage&gt;, clamped to 0–1.</summary>
    private static double? Alpha(ComponentValue? value, bool legacy)
    {
        if (value is null)
        {
            return 1;
        }

        if (!legacy && IsNone(value))
        {
            return 0;
        }

        return NumberOrPercentage(value) is { } alpha ? Math.Clamp(alpha.IsPercentage ? alpha.Value / 100 : alpha.Value, 0, 1) : null;
    }

    /// <summary>rgb() and rgba() (§5.1): legacy all numbers or all percentages; modern any mix and none.</summary>
    private static ColorOutcome Rgb(FunctionValue function, out CssColor color, out string? message)
    {
        color = default;
        message = null;
        var (channels, alphaValue, legacy, valid) = Arguments(function);
        if (UnsupportedMath([.. channels, alphaValue]) is { } unsupported)
        {
            message = unsupported;
            return ColorOutcome.Unsupported;
        }

        if (!valid)
        {
            if (channels.Count > 0 && channels[0] is CssToken from && from.IsIdent("from"))
            {
                message = "relative colour syntax is not supported";
                return ColorOutcome.Unsupported;
            }

            return ColorOutcome.Invalid;
        }

        var parsed = new double[3];
        bool? percentages = null;
        for (var i = 0; i < 3; i++)
        {
            if (!legacy && IsNone(channels[i]))
            {
                parsed[i] = 0;
                continue;
            }

            if (NumberOrPercentage(channels[i]) is not { } channel)
            {
                return ColorOutcome.Invalid;
            }

            if (legacy && percentages is { } same && same != channel.IsPercentage)
            {
                return ColorOutcome.Invalid;
            }

            percentages = channel.IsPercentage;
            parsed[i] = Math.Clamp(channel.IsPercentage ? channel.Value * 255 / 100 : channel.Value, 0, 255);
        }

        if (Alpha(alphaValue, legacy) is not { } alpha)
        {
            return ColorOutcome.Invalid;
        }

        color = new CssColor(parsed[0], parsed[1], parsed[2], alpha);
        return ColorOutcome.Color;
    }

    /// <summary>hsl() and hsla() (§7): hue as a number of degrees or an angle; saturation and lightness as percentages
    /// (legacy) or numbers or percentages (modern). Converted with the algorithm of §7.1.</summary>
    private static ColorOutcome Hsl(FunctionValue function, out CssColor color, out string? message)
    {
        color = default;
        message = null;
        var (channels, alphaValue, legacy, valid) = Arguments(function);
        if (UnsupportedMath([.. channels, alphaValue]) is { } unsupported)
        {
            message = unsupported;
            return ColorOutcome.Unsupported;
        }

        if (!valid)
        {
            if (channels.Count > 0 && channels[0] is CssToken from && from.IsIdent("from"))
            {
                message = "relative colour syntax is not supported";
                return ColorOutcome.Unsupported;
            }

            return ColorOutcome.Invalid;
        }

        double hue;
        if (!legacy && IsNone(channels[0]))
        {
            hue = 0;
        }
        else if (Hue(channels[0]) is { } degrees)
        {
            hue = degrees;
        }
        else
        {
            return ColorOutcome.Invalid;
        }

        var parts = new double[2];
        for (var i = 0; i < 2; i++)
        {
            var value = channels[i + 1];
            if (!legacy && IsNone(value))
            {
                parts[i] = 0;
                continue;
            }

            if (NumberOrPercentage(value) is not { } part || (legacy && !part.IsPercentage))
            {
                return ColorOutcome.Invalid;
            }

            parts[i] = part.Value;
        }

        if (Alpha(alphaValue, legacy) is not { } alpha)
        {
            return ColorOutcome.Invalid;
        }

        var (red, green, blue) = HslToRgb(hue, Math.Clamp(parts[0], 0, 100), Math.Clamp(parts[1], 0, 100));
        color = new CssColor(red * 255, green * 255, blue * 255, alpha);
        return ColorOutcome.Color;
    }

    /// <summary>&lt;hue&gt; = &lt;number&gt; | &lt;angle&gt;, in degrees.</summary>
    private static double? Hue(ComponentValue value) => value switch
    {
        CssToken { Kind: CssTokenKind.Number } number => number.Number,
        CssToken { Kind: CssTokenKind.Dimension } angle => Ascii.ToLower(angle.Text) switch
        {
            "deg" => angle.Number,
            "rad" => angle.Number * 180 / Math.PI,
            "grad" => angle.Number * 0.9,
            "turn" => angle.Number * 360,
            _ => null,
        },
        FunctionValue function when Calc.IsMathFunction(function) && Calc.Parse(function, out _) is { } node && Calc.TypeOf(node) == CalcType.Number
            => Calc.Evaluate(node, (_, _) => null, null)?.Number,
        _ => null,
    };

    /// <summary>The conversion of CSS Color 4 §7.1 (hslToRgb): hue in degrees, saturation and lightness 0–100; returns
    /// channels 0–1.</summary>
    private static (double Red, double Green, double Blue) HslToRgb(double hue, double saturation, double lightness)
    {
        hue %= 360;
        if (hue < 0)
        {
            hue += 360;
        }

        var s = saturation / 100;
        var l = lightness / 100;
        double F(double n)
        {
            var k = (n + (hue / 30)) % 12;
            var a = s * Math.Min(l, 1 - l);
            return l - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        return (F(0), F(8), F(4));
    }
}
