using System.Globalization;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// Writes a computed value as Chromium's getComputedStyle does, so the corpus can be compared as text (CSSOM §9,
/// "resolved value"): lengths in px with six significant digits, colours as rgb() or rgba(), currentcolor as the
/// element's colour (getComputedStyle gives the used colour), min-width and min-height auto as 0px (a box that is not a
/// flex or grid item). line-height as a number is written as the number; the corpus test turns Chromium's px back,
/// because Chromium returns a used value there even unrendered (README of the reference).
/// </summary>
internal static class ComputedText
{
    public static string Of(string property, ComputedStyle style) => Of(property, style.Find(property)!, style);

    private static string Of(string property, CssValue value, ComputedStyle style) => value switch
    {
        CssKeyword { Name: "currentcolor" } => style[PropertyRegistry.Color] is CssColorValue color ? color.Color.ToString() : "currentcolor",
        CssKeyword { Name: "auto" } when property is "min-width" or "min-height" => "0px",
        CssLength length => Pixels(length.Points / 0.75),
        CssNumber number => Number(number.Value),
        CssPercentage percentage => Number(percentage.Value) + "%",
        CssList { CommaSeparated: false, Items: [var first, var second] } when property == "border-spacing" =>
            Of(property, first, style) == Of(property, second, style) ? Of(property, first, style) : $"{Of(property, first, style)} {Of(property, second, style)}",
        CssList list => string.Join(list.CommaSeparated ? ", " : " ", list.Items.Select(item => Of(property, item, style))),
        CssString family when property == "font-family" => FamilyName(family.Value),
        _ => value.ToString()!,
    };

    /// <summary>A family name as Chromium writes it: without quotes when it is one identifier that is not a generic
    /// family or a CSS-wide keyword, else quoted.</summary>
    private static string FamilyName(string name)
    {
        string[] reserved = ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "math", "emoji", "fangsong", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "initial", "inherit", "unset", "revert", "revert-layer", "default"];
        var identifier = name.Length > 0 && !char.IsAsciiDigit(name[0]) && !(name.StartsWith('-') && name.Length > 1 && char.IsAsciiDigit(name[1]))
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c >= 0x80) && name != "-";
        return identifier && !reserved.Contains(name.ToLowerInvariant()) ? name : "\"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public static string Pixels(double pixels) => Number(pixels) + "px";

    /// <summary>A number with at most six significant digits, as Chromium writes them: C's %g, so in exponent form when
    /// the exponent is below -4 or at least 6 (1.23457e+06, 1e-07; review 39 N2).</summary>
    public static string Number(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
        if (exponent is < -4 or >= 6)
        {
            return value.ToString("G6", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);
        }

        var digits = 6 - exponent - 1;
        var rounded = Math.Round(value, Math.Clamp(digits, 0, 15), MidpointRounding.AwayFromZero);
        return rounded.ToString("0.###############", CultureInfo.InvariantCulture);
    }
}
