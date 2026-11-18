namespace Ave.Extensions.Documents.Core;

/// <summary>The ASCII character classes and case rules of the Infra Standard (§4.6), which the HTML parser and the CSS
/// reader share ("ASCII case-insensitive" in CSS Syntax 3 §4.2 too): only A–Z and a–z have a case.</summary>
public static class InfraAscii
{
    /// <summary>ASCII whitespace: tab, line feed, form feed, carriage return, space (Infra §4.6).</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsWhitespace(int c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    /// <summary>An ASCII upper alpha, A–Z.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsUpperAlpha(int c) => c is >= 'A' and <= 'Z';

    /// <summary>An ASCII lower alpha, a–z.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsLowerAlpha(int c) => c is >= 'a' and <= 'z';

    /// <summary>An ASCII alpha.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsAlpha(int c) => IsUpperAlpha(c) || IsLowerAlpha(c);

    /// <summary>An ASCII digit, 0–9.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsDigit(int c) => c is >= '0' and <= '9';

    /// <summary>An ASCII alphanumeric.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsAlphanumeric(int c) => IsAlpha(c) || IsDigit(c);

    /// <summary>An ASCII hex digit.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsHexDigit(int c) => IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>The ASCII lowercase of a code point: A–Z become a–z, every other code point stays.</summary>
    /// <param name="c">A code point.</param>
    /// <returns>Its lowercase.</returns>
    public static int ToLower(int c) => IsUpperAlpha(c) ? c + 0x20 : c;

    /// <summary>The ASCII lowercase of a string (Infra §4.6 "ASCII lowercase"): only A–Z change.</summary>
    /// <param name="text">The string.</param>
    /// <returns>Its lowercase (the same string when nothing changes).</returns>
    public static string ToLower(string text)
    {
        var index = 0;
        while (index < text.Length && !IsUpperAlpha(text[index]))
        {
            index++;
        }

        if (index == text.Length)
        {
            return text;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = (char)ToLower(source[i]);
            }
        });
    }

    /// <summary>Whether two strings are equal ASCII case-insensitively (Infra §4.6).</summary>
    /// <param name="left">One string.</param>
    /// <param name="right">The other.</param>
    /// <returns>Whether they are.</returns>
    public static bool EqualsIgnoreCase(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (ToLower(left[i]) != ToLower(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a string starts with a prefix, ASCII case-insensitively.</summary>
    /// <param name="text">The string.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>Whether it does.</returns>
    public static bool StartsWithIgnoreCase(string text, string prefix) =>
        text.Length >= prefix.Length && EqualsIgnoreCase(text[..prefix.Length], prefix);
}
