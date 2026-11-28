namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The characters that ask for right-to-left text, which the engine does not lay out: it sets every line left to right,
/// in text order, and has no bidirectional algorithm (UAX #9). They are the characters whose Bidi_Class (UAX #9 §3.2,
/// Table 4) is one of
/// <list type="bullet">
/// <item>R, right-to-left: Hebrew and the other right-to-left scripts, and the right-to-left mark U+200F;</item>
/// <item>AL, right-to-left Arabic: the Arabic, Syriac and Thaana letters, and the Arabic letter mark U+061C;</item>
/// <item>AN, Arabic number: the Arabic-Indic digits and the Arabic number signs;</item>
/// <item>RLE, RLO and RLI: the explicit formatting characters right-to-left embedding U+202B, right-to-left override U+202E
/// and right-to-left isolate U+2067.</item>
/// </list>
/// Chromium 159 prints each of these otherwise than in text order: <c>x &#x202E;abc&#x202C; y</c> as <c>cba</c>, a full stop
/// at the end of a right-to-left embedding or isolate at its front, and <c>!?</c> between two right-to-left marks as
/// <c>?!</c> (cases <c>bidi-*</c>). Every front-end refuses them as input, whatever the font has, so text with them is
/// never printed differently without a word. The left-to-right formatting characters LRE U+202A, LRO U+202D, LRI U+2066,
/// FSI U+2068, PDF U+202C, PDI U+2069 and the left-to-right mark U+200E are accepted: they need no glyph, and Chromium's
/// print is the same with them as without them (measured). FSI takes the direction of the first strong character after
/// it (UAX #9 §3.3.2), and with R and AL refused that is always left to right.
/// </summary>
/// <remarks>
/// The classes come from <c>UnicodeData.txt</c> of the Unicode Character Database the family ships (field 4, Bidi_Class;
/// generated into <c>RightToLeftCharacters.g.cs</c>). A code point without an entry there (an unassigned one) is not one of
/// these characters here, even where DerivedBidiClass.txt gives it R or AL by default: no font has a glyph for it, so it
/// is refused as a missing glyph.
/// </remarks>
public static partial class RightToLeftCharacters
{
    /// <summary>The first character of a text that asks for right-to-left text, and how many the text holds; null when it
    /// holds none. One pass over the text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The offset of the first one in UTF-16 code units, its code point, and the number of such characters; or
    /// null.</returns>
    public static (int Offset, int CodePoint, int Count)? Find(string text)
    {
        var (offset, first, count) = (-1, 0, 0);
        for (var index = 0; index < text.Length; index++)
        {
            var at = index;
            var codePoint = (int)text[index];
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                codePoint = char.ConvertToUtf32(text[index], text[index + 1]);
                index++;
            }

            if (Is(codePoint))
            {
                (offset, first) = count == 0 ? (at, codePoint) : (offset, first);
                count++;
            }
        }

        return count == 0 ? null : (offset, first, count);
    }

    /// <summary>Whether a code point asks for right-to-left text: its Bidi_Class is R, AL, AN, RLE, RLO or RLI.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>True for such a character.</returns>
    public static bool Is(int codePoint) => ClassOf(codePoint) is not null;

    /// <summary>
    /// The message of the refusal of a character that asks for right-to-left text, for every front-end: the code point and
    /// its name or its kind, that the engine lays out left to right only, and what to do.
    /// </summary>
    /// <param name="codePoint">A code point for which <see cref="Is"/> is true.</param>
    /// <param name="count">How many such characters the text holds; more than one is said.</param>
    /// <returns>The message.</returns>
    public static string Message(int codePoint, int count = 1)
    {
        var name = codePoint switch
        {
            0x200F => "the RIGHT-TO-LEFT MARK",
            0x061C => "the ARABIC LETTER MARK",
            0x202B => "the RIGHT-TO-LEFT EMBEDDING",
            0x202E => "the RIGHT-TO-LEFT OVERRIDE",
            0x2067 => "the RIGHT-TO-LEFT ISOLATE",
            _ => ClassOf(codePoint) switch
            {
                RightToLeftClass.R => "a right-to-left character (Bidi_Class R, such as a Hebrew letter)",
                RightToLeftClass.AL => "a right-to-left Arabic character (Bidi_Class AL, such as an Arabic letter)",
                RightToLeftClass.AN => "an Arabic number character (Bidi_Class AN, such as an Arabic-Indic digit)",
                _ => "a character that asks for right-to-left text",
            },
        };
        var others = count > 1 ? $" ({count.ToString(System.Globalization.CultureInfo.InvariantCulture)} such characters in this text)" : string.Empty;
        return $"The character U+{codePoint:X4}, {name}, asks for right-to-left text{others}, and the engine lays out left to right only: remove it.";
    }

    /// <summary>The class of a code point among the right-to-left ones, or null for every other code point: a binary
    /// search of <see cref="Ranges"/>.</summary>
    private static RightToLeftClass? ClassOf(int codePoint)
    {
        var (low, high) = (0, Ranges.Length - 1);
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var range = Ranges[middle];
            if (codePoint < range.First)
            {
                high = middle - 1;
            }
            else if (codePoint > range.Last)
            {
                low = middle + 1;
            }
            else
            {
                return range.Class;
            }
        }

        return null;
    }

    /// <summary>The Bidi_Class values that ask for right-to-left text (UAX #9 §3.2, Table 4).</summary>
    private enum RightToLeftClass
    {
        R,
        AL,
        AN,
        RLE,
        RLO,
        RLI,
    }

    /// <summary>The code points <paramref name="First"/> to <paramref name="Last"/>, all of one class.</summary>
    private readonly record struct Range(int First, int Last, RightToLeftClass Class);
}
