namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>The ASCII character classes of the Infra Standard (§4.6) that the parser uses.</summary>
internal static class Ascii
{
    /// <summary>ASCII whitespace: tab, line feed, form feed, carriage return, space (Infra §4.6).</summary>
    public static bool IsWhitespace(int c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    /// <summary>An ASCII upper alpha, A–Z.</summary>
    public static bool IsUpperAlpha(int c) => c is >= 'A' and <= 'Z';

    /// <summary>An ASCII lower alpha, a–z.</summary>
    public static bool IsLowerAlpha(int c) => c is >= 'a' and <= 'z';

    /// <summary>An ASCII alpha.</summary>
    public static bool IsAlpha(int c) => IsUpperAlpha(c) || IsLowerAlpha(c);

    /// <summary>An ASCII digit, 0–9.</summary>
    public static bool IsDigit(int c) => c is >= '0' and <= '9';

    /// <summary>An ASCII alphanumeric.</summary>
    public static bool IsAlphanumeric(int c) => IsAlpha(c) || IsDigit(c);

    /// <summary>An ASCII hex digit.</summary>
    public static bool IsHexDigit(int c) => IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>The ASCII lowercase of a character: A–Z become a–z, every other character stays.</summary>
    public static int ToLower(int c) => IsUpperAlpha(c) ? c + 0x20 : c;

    /// <summary>The ASCII lowercase of a string (Infra §4.6 "ASCII lowercase"): only A–Z change.</summary>
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
}
