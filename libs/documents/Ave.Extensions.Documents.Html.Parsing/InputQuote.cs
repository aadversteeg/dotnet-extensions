namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// A name or value from the input as a parse diagnostic quotes it: at most <see cref="MaxCharacters"/> characters, and a
/// mark (<c>…</c>) where it was shortened. A tag name or an encoding label can be megabytes, and a message that quoted it
/// whole would grow with its length times the places it is named. (The style resolver and the CSS reader quote by the
/// same rule; each package keeps its own copy.)
/// </summary>
internal static class InputQuote
{
    /// <summary>The most characters of one name or value a diagnostic quotes.</summary>
    public const int MaxCharacters = 100;

    /// <summary>The text, or its first <see cref="MaxCharacters"/> characters followed by <c>…</c>; a cut never splits
    /// a surrogate pair.</summary>
    /// <param name="text">The text as the input has it.</param>
    /// <returns>The text to quote.</returns>
    public static string Of(string text)
    {
        if (text.Length <= MaxCharacters)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[MaxCharacters - 1]) ? MaxCharacters - 1 : MaxCharacters;
        return text.Substring(0, length) + "…";
    }
}
