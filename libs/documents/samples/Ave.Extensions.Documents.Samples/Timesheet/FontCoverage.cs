using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Fonts;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// Limits user text to the characters a font can show, as the reporting PoC does before it hands data to Typst
/// (<c>TypstFontCoverage.Displayable</c>). The engine itself never drops a character: a missing glyph is the error
/// <c>Documents.Font.GlyphMissing</c>. A template that shows text it does not control decides here what to do instead.
/// </summary>
public static class FontCoverage
{
    /// <summary>Replacements for characters the font lacks but that have a plain equivalent; matching the PoC's output.</summary>
    private static readonly Dictionary<int, string> Fallbacks = new()
    {
        [0x21D2] = "=>", // ⇒ RIGHTWARDS DOUBLE ARROW
        [0x2011] = "-",  // NON-BREAKING HYPHEN
    };

    /// <summary>
    /// A filter that keeps every character the font has a glyph for and all white space (layout collapses white space
    /// before it looks for glyphs), replaces a few with a plain equivalent, and drops the rest (emoji, CJK in Lato).
    /// </summary>
    /// <param name="font">The font the text will be set in.</param>
    /// <returns>The filter.</returns>
    public static Func<string, string> Displayable(TrueTypeFont font) => text =>
        string.Concat(text.EnumerateRunes().Select(rune =>
            Rune.IsWhiteSpace(rune) || font.GlyphFor(rune).HasValue ? rune.ToString()
            : Fallbacks.TryGetValue(rune.Value, out var fallback) ? fallback
            : string.Empty));
}
