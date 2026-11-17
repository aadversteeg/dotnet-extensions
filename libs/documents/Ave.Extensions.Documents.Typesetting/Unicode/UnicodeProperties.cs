namespace Ave.Extensions.Documents.Typesetting.Unicode;

/// <summary>
/// The Unicode properties of a code point that line breaking and shaping need: its Line_Break class, its
/// <see cref="CharacterTraits"/> and its <see cref="TextScript"/>. The data is a table of ranges generated from the Unicode Character Database,
/// version <see cref="UnicodeVersion"/>, committed as <c>UnicodePropertyRanges.g.cs</c> next to this file. The raw
/// data files are in <c>assets/unicode/</c>; a test regenerates the table from them and fails when the committed
/// table differs, so the two cannot drift apart.
/// </summary>
internal static partial class UnicodeProperties
{
    /// <summary>One run of consecutive code points with the same properties; it ends where the next one starts.</summary>
    /// <param name="Start">The first code point.</param>
    /// <param name="Class">The Line_Break class, as in LineBreak.txt (before rule LB1 resolves it).</param>
    /// <param name="Traits">The other properties used by line breaking.</param>
    /// <param name="Script">The Script property, as far as shaping distinguishes it.</param>
    internal readonly record struct Range(int Start, LineBreakClass Class, CharacterTraits Traits, TextScript Script);

    /// <summary>The properties of a code point: the range that contains it, found by binary search.</summary>
    /// <param name="codePoint">A code point, 0 to 0x10FFFF.</param>
    /// <returns>Its Line_Break class, traits and script.</returns>
    public static (LineBreakClass Class, CharacterTraits Traits, TextScript Script) Of(int codePoint)
    {
        var low = 0;
        var high = Ranges.Length - 1;
        while (low < high)
        {
            // The last range whose start is at or before the code point.
            var middle = (low + high + 1) / 2;
            if (Ranges[middle].Start <= codePoint)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (Ranges[low].Class, Ranges[low].Traits, Ranges[low].Script);
    }
}
