using Ave.Extensions.Documents.Core;
using System;
using System.Globalization;
using System.Text;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Html;

/// <summary>A dimension of an HTML attribute (HTML Standard §2.3.4.4): a number of pixels or a percentage.</summary>
/// <param name="Value">The number.</param>
/// <param name="IsPercentage">Whether it is a percentage.</param>
internal readonly record struct HtmlDimension(double Value, bool IsPercentage)
{
    /// <summary>As CSS: <c>12px</c> or <c>50%</c>.</summary>
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture) + (IsPercentage ? "%" : "px");
}

/// <summary>The HTML Standard's microsyntaxes for attribute values that map to CSS (§2.3.4, §2.3.6).</summary>
internal static class AttributeValues
{
    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    private static int SkipWhitespace(string input, int position)
    {
        while (position < input.Length && IsWhitespace(input[position]))
        {
            position++;
        }

        return position;
    }

    /// <summary>"Rules for parsing non-negative integers" (§2.3.4.2, through §2.3.4.1): leading white space, an optional
    /// sign, digits; what follows the digits is ignored. Null for an error or a negative number.</summary>
    public static long? NonNegativeInteger(string input) => Integer(input) is { } value && value >= 0 ? value : null;

    /// <summary>"Rules for parsing integers" (§2.3.4.1): leading white space, an optional sign, digits; what follows the
    /// digits is ignored (<c>5abc</c> is 5, <c>7.9</c> is 7). Null for an error; the magnitude is clamped to
    /// int.MaxValue.</summary>
    public static long? Integer(string input)
    {
        var position = SkipWhitespace(input, 0);
        var negative = false;
        if (position < input.Length && input[position] is '-' or '+')
        {
            negative = input[position] == '-';
            position++;
        }

        var start = position;
        long value = 0;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            // Very long numbers are a parse result too; clamp instead of overflowing.
            value = Math.Min(value * 10 + (input[position] - '0'), int.MaxValue);
            position++;
        }

        if (position == start)
        {
            return null;
        }

        return negative ? -value : value;
    }

    /// <summary>"Rules for parsing dimension values" (§2.3.4.4): digits with an optional fraction, then a percentage
    /// sign or not; null for an error. A relative dimension (digits then <c>*</c>, as in <c>width="3*"</c>) is no value
    /// either: Chromium 159 gives it no hint (measured on td and img).</summary>
    public static HtmlDimension? Dimension(string input)
    {
        var position = SkipWhitespace(input, 0);
        if (position >= input.Length || !char.IsAsciiDigit(input[position]))
        {
            return null;
        }

        double value = 0;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            // Past 2^53 more digits change nothing a double keeps; stopping there keeps the value finite.
            value = Math.Min(value * 10 + (input[position] - '0'), 9007199254740992);
            position++;
        }

        if (position < input.Length && input[position] == '.')
        {
            position++;
            var divisor = 1.0;
            while (position < input.Length && char.IsAsciiDigit(input[position]))
            {
                divisor *= 10;
                value += (input[position] - '0') / divisor;
                position++;
            }
        }

        if (position < input.Length && input[position] == '*')
        {
            return null;
        }

        return new HtmlDimension(value, position < input.Length && input[position] == '%');
    }

    /// <summary>"Rules for parsing nonzero dimension values" (§2.3.4.5): a dimension, zero being an error.</summary>
    public static HtmlDimension? NonzeroDimension(string input) => Dimension(input) is { Value: > 0 } dimension ? dimension : null;

    /// <summary>"Rules for parsing a legacy color value" (§2.3.6): a named colour, <c>#rgb</c>, or any other text read
    /// as hex digits in the forgiving way browsers have always done; null for an error (empty, or transparent).</summary>
    public static CssColor? LegacyColor(string value)
    {
        var input = value.Trim(' ', '\t', '\n', '\f', '\r');
        if (input.Length == 0 || InfraAscii.EqualsIgnoreCase(input, "transparent"))
        {
            return null;
        }

        if (CssColor.Named(input) is { } named)
        {
            return named;
        }

        if (input.Length == 4 && input[0] == '#' && char.IsAsciiHexDigit(input[1]) && char.IsAsciiHexDigit(input[2]) && char.IsAsciiHexDigit(input[3]))
        {
            return new CssColor(Convert.ToInt32(input[1..2], 16) * 17, Convert.ToInt32(input[2..3], 16) * 17, Convert.ToInt32(input[3..4], 16) * 17, 1);
        }

        // Step 7: code points above U+FFFF become "00".
        var text = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (char.IsHighSurrogate(input[i]) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
            {
                text.Append("00");
                i++;
            }
            else
            {
                text.Append(input[i]);
            }
        }

        var digits = text.ToString();
        if (digits.Length > 128)
        {
            digits = digits[..128];
        }

        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        var hex = new StringBuilder(digits.Length + 3);
        foreach (var c in digits)
        {
            hex.Append(char.IsAsciiHexDigit(c) ? c : '0');
        }

        while (hex.Length == 0 || hex.Length % 3 != 0)
        {
            hex.Append('0');
        }

        var length = hex.Length / 3;
        string[] components = [hex.ToString(0, length), hex.ToString(length, length), hex.ToString(2 * length, length)];
        if (length > 8)
        {
            for (var i = 0; i < 3; i++)
            {
                components[i] = components[i][(length - 8)..];
            }

            length = 8;
        }

        while (length > 2 && components[0][0] == '0' && components[1][0] == '0' && components[2][0] == '0')
        {
            for (var i = 0; i < 3; i++)
            {
                components[i] = components[i][1..];
            }

            length--;
        }

        if (length > 2)
        {
            for (var i = 0; i < 3; i++)
            {
                components[i] = components[i][..2];
            }
        }

        return new CssColor(Convert.ToInt32(components[0], 16), Convert.ToInt32(components[1], 16), Convert.ToInt32(components[2], 16), 1);
    }

    /// <summary>"Rules for parsing a legacy font size" (§15.3.4): <c>3</c>, <c>+1</c>, <c>-2</c> to a font-size keyword;
    /// null for no hint.</summary>
    public static string? LegacyFontSize(string input)
    {
        var position = SkipWhitespace(input, 0);
        if (position >= input.Length)
        {
            return null;
        }

        var mode = input[position] switch { '+' => 1, '-' => -1, _ => 0 };
        if (mode != 0)
        {
            position++;
        }

        var start = position;
        var value = 0;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            value = Math.Min(value * 10 + (input[position] - '0'), 1000);
            position++;
        }

        if (position == start)
        {
            return null;
        }

        value = mode switch { 1 => value + 3, -1 => 3 - value, _ => value };
        return Math.Clamp(value, 1, 7) switch
        {
            1 => "x-small",
            2 => "small",
            3 => "medium",
            4 => "large",
            5 => "x-large",
            6 => "xx-large",
            _ => "xxx-large",
        };
    }

    /// <summary>A colour as CSS text.</summary>
    public static string Css(CssColor color) => color.ToString();
}
