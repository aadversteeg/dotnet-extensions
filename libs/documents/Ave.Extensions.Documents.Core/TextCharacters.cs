namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The characters a document's text may hold, whatever the output format. Text is a sequence of Unicode scalar values
/// that stand for something a reader can read; three kinds of code point do not:
/// <list type="bullet">
/// <item>control characters (general category Cc), except the ones the engine gives a meaning: tab, and the forced line
/// breaks line feed, vertical tab, form feed, carriage return and next line (U+0085), which UAX #14 classes as
/// mandatory breaks (BK, CR, LF, NL);</item>
/// <item>noncharacters: U+FDD0 to U+FDEF and the last two code points of every plane (U+FFFE, U+FFFF, U+1FFFE, …),
/// "permanently reserved in the Unicode Standard for internal use" and "not recommended for use in open interchange
/// of Unicode text data" (the Unicode Standard §23.7);</item>
/// <item>unpaired surrogates, which are not Unicode scalar values at all.</item>
/// </list>
/// Such a character cannot be shown, and a PDF that maps a glyph to it fails PDF/A and PDF/UA (veraPDF rules
/// 6.2.11.7.2-2 and 7.21.7-2 refuse U+0000, U+FEFF and U+FFFE in a ToUnicode map), so every front-end refuses it as
/// input. Metadata text has its own, stricter rule: <see cref="XmlCharacters"/>.
/// </summary>
public static class TextCharacters
{
    /// <summary>The position of the first UTF-16 code unit of a character text may not hold, or null when there is
    /// none.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The offset in UTF-16 code units, or null.</returns>
    public static int? IndexOfForbidden(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                if (IsNoncharacter(char.ConvertToUtf32(text[index], text[index + 1])))
                {
                    return index;
                }

                index++;
                continue;
            }

            if (char.IsSurrogate(text[index]) || !IsAllowed(text[index]))
            {
                return index;
            }
        }

        return null;
    }

    private static bool IsAllowed(char unit) =>
        unit is '\t' or '\n' or '\v' or '\f' or '\r' or '\u0085'
        || !(char.IsControl(unit) || IsNoncharacter(unit));

    /// <summary>U+FDD0..U+FDEF, and every code point whose low 16 bits are FFFE or FFFF.</summary>
    private static bool IsNoncharacter(int codePoint) =>
        codePoint is >= 0xFDD0 and <= 0xFDEF || (codePoint & 0xFFFE) == 0xFFFE;
}
