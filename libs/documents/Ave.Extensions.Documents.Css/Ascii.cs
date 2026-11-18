namespace Ave.Extensions.Documents.Css;

/// <summary>The ASCII character classes and case rules of the Infra Standard (§4.6) that CSS uses (CSS Syntax 3 §4.2,
/// "ASCII case-insensitive").</summary>
internal static class Ascii
{
    /// <summary>An ASCII upper alpha, A–Z.</summary>
    public static bool IsUpperAlpha(int c) => c is >= 'A' and <= 'Z';

    /// <summary>An ASCII alpha.</summary>
    public static bool IsAlpha(int c) => IsUpperAlpha(c) || c is >= 'a' and <= 'z';

    /// <summary>An ASCII digit.</summary>
    public static bool IsDigit(int c) => c is >= '0' and <= '9';

    /// <summary>An ASCII hex digit.</summary>
    public static bool IsHexDigit(int c) => IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>The ASCII lowercase of a character.</summary>
    public static int ToLower(int c) => IsUpperAlpha(c) ? c + 0x20 : c;

    /// <summary>The ASCII lowercase of a string: only A–Z change.</summary>
    public static string ToLower(string text)
    {
        var index = 0;
        while (index < text.Length && !IsUpperAlpha(text[index]))
        {
            index++;
        }

        return index == text.Length
            ? text
            : string.Create(text.Length, text, static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    span[i] = (char)ToLower(source[i]);
                }
            });
    }

    /// <summary>Whether two strings are equal ASCII case-insensitively.</summary>
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
    public static bool StartsWithIgnoreCase(string text, string prefix) =>
        text.Length >= prefix.Length && EqualsIgnoreCase(text[..prefix.Length], prefix);
}
