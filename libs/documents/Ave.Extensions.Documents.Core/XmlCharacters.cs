namespace Ave.Documents.Core;

/// <summary>
/// The characters XML 1.0 allows in a document (XML 1.0, fifth edition, §2.2, production [2]: <c>Char ::= #x9 | #xA |
/// #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]</c>). Text that goes into XML — the XMP metadata of a
/// PDF — must consist of these only; a .NET string can also hold control characters, U+FFFE, U+FFFF and unpaired
/// surrogates, which an XML writer refuses.
/// </summary>
public static class XmlCharacters
{
    /// <summary>The position of the first UTF-16 code unit that is not part of an allowed character, or null when
    /// every character is allowed. A surrogate pair is one allowed character (U+10000 and up).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The offset in UTF-16 code units, or null.</returns>
    public static int? IndexOfForbidden(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var unit = text[index];
            if (char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
                continue;
            }

            if (!IsAllowed(unit))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>A character of the Basic Multilingual Plane that production [2] allows; surrogates are not allowed on
    /// their own.</summary>
    private static bool IsAllowed(char unit) =>
        unit is '\t' or '\n' or '\r'
        || unit is >= (char)0x20 and <= (char)0xD7FF
        || unit is >= (char)0xE000 and <= (char)0xFFFD;
}
