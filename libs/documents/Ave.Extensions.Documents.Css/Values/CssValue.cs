using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Values;

/// <summary>
/// A CSS value as the cascade handles it (CSS Values 4 §2): specified values as parsed, and computed values. The
/// computed forms are <see cref="CssKeyword"/>, <see cref="CssLength"/> (absolute, in points), <see cref="CssPercentage"/>,
/// <see cref="CssNumber"/>, <see cref="CssString"/>, <see cref="CssUrl"/>, <see cref="CssColorValue"/>,
/// <see cref="CssList"/> and, for custom properties, <see cref="CssTokens"/>. A specified value may also hold a
/// relative length (<see cref="CssDimension"/>) or a math function (<see cref="CssCalc"/>), which computing resolves.
/// </summary>
public abstract record CssValue;

/// <summary>A keyword, lowercase (<c>auto</c>, <c>solid</c>, <c>currentcolor</c>).</summary>
/// <param name="Name">The keyword.</param>
public sealed record CssKeyword(string Name) : CssValue
{
    /// <summary>Formats as the keyword.</summary>
    public override string ToString() => Name;
}

/// <summary>An absolute length in points (computed, or an absolute specified length).</summary>
/// <param name="Value">The length.</param>
public sealed record CssLength(Length Value) : CssValue
{
    /// <summary>The length in points.</summary>
    public double Points => Value.Points;

    /// <summary>Formats as a length in points.</summary>
    public override string ToString() => Value.ToString();
}

/// <summary>A length in a unit that computing resolves (em, rem, ex, ch, vw, vh).</summary>
/// <param name="Value">The number.</param>
/// <param name="Unit">The unit, lowercase.</param>
public sealed record CssDimension(double Value, string Unit) : CssValue
{
    /// <summary>Formats as written, for example <c>1.5em</c>.</summary>
    public override string ToString() => $"{InvariantNumber.Format(Value)}{Unit}";
}

/// <summary>A percentage (50 for 50%).</summary>
/// <param name="Value">The number before the percent sign.</param>
public sealed record CssPercentage(double Value) : CssValue
{
    /// <summary>Formats as a percentage.</summary>
    public override string ToString() => $"{InvariantNumber.Format(Value)}%";
}

/// <summary>A number (or an integer, when <see cref="IsInteger"/>).</summary>
/// <param name="Value">The number.</param>
/// <param name="IsInteger">Whether it is an &lt;integer&gt;.</param>
public sealed record CssNumber(double Value, bool IsInteger = false) : CssValue
{
    /// <summary>Formats as the number.</summary>
    public override string ToString() => InvariantNumber.Format(Value);
}

/// <summary>A string.</summary>
/// <param name="Value">The characters.</param>
public sealed record CssString(string Value) : CssValue
{
    /// <summary>Formats as a quoted string.</summary>
    public override string ToString() => "\"" + Value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>A URL as written (CSS Values 4 §4.5); the caller resolves it against the style sheet's base.</summary>
/// <param name="Url">The URL text.</param>
public sealed record CssUrl(string Url) : CssValue
{
    /// <summary>Formats as url("…").</summary>
    public override string ToString() => $"url(\"{Url}\")";
}

/// <summary>A colour (CSS Color 4 §3): <see cref="CssColor"/>, sRGB with alpha.</summary>
/// <param name="Color">The colour.</param>
public sealed record CssColorValue(CssColor Color) : CssValue
{
    /// <summary>Formats as Chromium serialises a computed colour: rgb(r, g, b) or rgba(r, g, b, a).</summary>
    public override string ToString() => Color.ToString();
}

/// <summary>A list of values (<c>font-family</c>, the two lengths of <c>border-spacing</c>, the page size).</summary>
/// <param name="Items">The items.</param>
/// <param name="CommaSeparated">Whether the items are separated by commas (else by spaces).</param>
public sealed record CssList(ValueList<CssValue> Items, bool CommaSeparated) : CssValue
{
    /// <summary>Formats the items joined by ", " or " ".</summary>
    public override string ToString() => string.Join(CommaSeparated ? ", " : " ", Items);
}

/// <summary>The value of a custom property: tokens (CSS Variables 1 §2.1), and its text.</summary>
/// <param name="Tokens">The component values, with every var() substituted when computed.</param>
/// <param name="Text">The text (as written for a specified value).</param>
public sealed record CssTokens(ValueList<ComponentValue> Tokens, string Text) : CssValue
{
    /// <summary>Formats as the text.</summary>
    public override string ToString() => Text;
}

/// <summary>A &lt;ratio&gt; (CSS Values 4 §7.8): width / height.</summary>
/// <param name="Width">The first number.</param>
/// <param name="Height">The second number (1 when only one was written).</param>
public sealed record CssRatio(double Width, double Height) : CssValue
{
    /// <summary>Formats as <c>w / h</c>, each number with at most three decimals.</summary>
    public override string ToString() => $"{InvariantNumber.Format(Width)} / {InvariantNumber.Format(Height)}";
}

/// <summary>A math function (CSS Values 4 §10) whose value is known only when computing (it holds relative lengths or
/// percentages).</summary>
/// <param name="Expression">The calculation tree.</param>
public sealed record CssCalc(CalcNode Expression) : CssValue
{
    /// <summary>Formats as calc(…).</summary>
    public override string ToString() => $"calc({Expression})";
}

/// <summary>
/// A colour in sRGB with alpha (CSS Color 4 §4): channels from 0 to 255 as numbers, so <c>hsl()</c> keeps its exact
/// value until it is serialised, and alpha from 0 to 1. Core's <c>Color</c> has no alpha; stage 4 maps this to the
/// document model.
/// </summary>
/// <param name="Red">Red, 0–255.</param>
/// <param name="Green">Green, 0–255.</param>
/// <param name="Blue">Blue, 0–255.</param>
/// <param name="Alpha">Alpha, 0–1.</param>
public readonly record struct CssColor(double Red, double Green, double Blue, double Alpha)
{
    /// <summary>Transparent black, the value of <c>transparent</c>.</summary>
    public static CssColor Transparent => new(0, 0, 0, 0);

    /// <summary>Opaque black.</summary>
    public static CssColor Black => new(0, 0, 0, 1);

    /// <summary>The named colour of CSS Color 4 §6.1 (one of the 148, ASCII case-insensitive), or null; for the HTML
    /// Standard's legacy colour values (§2.3.6), which accept these names and nothing else of CSS.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The colour, or null.</returns>
    public static CssColor? Named(string name) => Colors.NamedColor(name);

    /// <summary>
    /// Formats as Chromium serialises a computed sRGB colour (CSS Color 4 §15.2, "serializing sRGB values"): rgb(r, g,
    /// b) when opaque, else rgba(r, g, b, a); channels rounded to integers; the alpha as the shortest decimal that keeps
    /// the byte it rounds to (at most three decimals).
    /// </summary>
    public override string ToString()
    {
        static int Channel(double value) => (int)Math.Round(Math.Clamp(value, 0, 255), MidpointRounding.AwayFromZero);
        var (r, g, b) = (Channel(Red), Channel(Green), Channel(Blue));
        if (Alpha >= 1)
        {
            return $"rgb({r}, {g}, {b})";
        }

        return $"rgba({r}, {g}, {b}, {AlphaText(Alpha)})";
    }

    /// <summary>The alpha as CSS Color 4 §15.2 serialises it: the shortest decimal that rounds to the same byte (0–255),
    /// with at most three decimals.</summary>
    private static string AlphaText(double alpha)
    {
        var target = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        var two = Math.Round(target / 255.0, 2);
        if ((int)Math.Round(two * 255) == target)
        {
            return InvariantNumber.Format(two);
        }

        return InvariantNumber.Format(Math.Round(target / 255.0, 3));
    }
}
