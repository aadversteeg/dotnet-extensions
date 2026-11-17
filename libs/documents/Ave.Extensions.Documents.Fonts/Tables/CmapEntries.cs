using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.Tables;

/// <summary>The rules both cmap formats apply to each (code point, glyph id) pair they read.</summary>
internal static class CmapEntries
{
    /// <summary>
    /// Adds one mapping. Returns false when the glyph id does not exist in the font (a malformed font).
    /// Glyph 0 is .notdef, which means "this character has no glyph", so it is not added; surrogate code points
    /// (U+D800–U+DFFF) are not characters and are not added either.
    /// </summary>
    public static bool TryAdd(Dictionary<int, GlyphId> map, int codePoint, int glyph, int glyphCount)
    {
        if (glyph >= glyphCount)
        {
            return false;
        }

        var isSurrogate = codePoint is >= 0xD800 and <= 0xDFFF;
        if (glyph != 0 && !isSurrogate)
        {
            // The first mapping wins if a malformed table maps a code point twice.
            map.TryAdd(codePoint, new GlyphId((ushort)glyph));
        }

        return true;
    }

    /// <summary>The error for a mapping to a glyph id beyond maxp.numGlyphs.</summary>
    public static Result<FrozenDictionary<int, GlyphId>, Error> GlyphOutOfRange(int codePoint, long glyph) =>
        FontParseErrors.InvalidValue<FrozenDictionary<int, GlyphId>>(
            CmapTable.Tag, $"U+{codePoint:X4} maps to glyph {glyph}, which the font does not have.", glyph);
}
