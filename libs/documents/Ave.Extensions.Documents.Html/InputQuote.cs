namespace Ave.Extensions.Documents.Html;

/// <summary>
/// A value from the input as a diagnostic quotes it: at most <see cref="MaxCharacters"/> characters, and a mark
/// (<c>…</c>) where it was shortened. An attribute value can be megabytes, and the parser shares one value between the
/// copies of an element it opens again, so a message that quoted it whole would grow with elements times its length.
/// </summary>
internal static class InputQuote
{
    /// <summary>The most characters of one input value a diagnostic quotes.</summary>
    public const int MaxCharacters = 100;

    /// <summary>The value, or its first <see cref="MaxCharacters"/> characters followed by <c>…</c>; a cut never
    /// splits a surrogate pair.</summary>
    /// <param name="value">The value as the input has it.</param>
    /// <returns>The text to quote.</returns>
    public static string Of(string value)
    {
        if (value.Length <= MaxCharacters)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[MaxCharacters - 1]) ? MaxCharacters - 1 : MaxCharacters;
        return value.Substring(0, length) + "…";
    }
}
