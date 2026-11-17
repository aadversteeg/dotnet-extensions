using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.Tables;

/// <summary>
/// cmap subtable format 12, "Segmented coverage" (OpenType spec, "cmap" → "Format 12"). Maps all of Unicode
/// as groups of consecutive code points that map to consecutive glyph ids.
/// </summary>
internal static class CmapFormat12
{
    private const string Tag = CmapTable.Tag;
    private const int HeaderLength = 16; // format, reserved, length, language, numGroups
    private const int GroupLength = 12; // startCharCode, endCharCode, startGlyphID
    private const int LastCodePoint = 0x10FFFF;

    public static Result<FrozenDictionary<int, GlyphId>, Error> Parse(ReadOnlySpan<byte> subtable, int glyphCount)
    {
        if (subtable.Length < HeaderLength)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, HeaderLength, subtable.Length);
        }

        long length = BigEndian.UInt32(subtable, 4);
        long groupCount = BigEndian.UInt32(subtable, 12);
        var needed = HeaderLength + groupCount * GroupLength;
        if (length > subtable.Length || needed > length)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, Math.Max(length, needed), subtable.Length);
        }

        var map = new Dictionary<int, GlyphId>();
        long previousEnd = -1;
        for (var group = 0; group < groupCount; group++)
        {
            var record = HeaderLength + group * GroupLength;
            long start = BigEndian.UInt32(subtable, record);
            long end = BigEndian.UInt32(subtable, record + 4);
            long startGlyph = BigEndian.UInt32(subtable, record + 8);

            // "Groups must be sorted by increasing startCharCode" and may not overlap; code points end at
            // U+10FFFF. Checking this also bounds the size of the map to one entry per Unicode code point.
            if (start <= previousEnd || start > end || end > LastCodePoint)
            {
                return FontParseErrors.InvalidValue<FrozenDictionary<int, GlyphId>>(
                    Tag, $"format 12 group {group} is out of order, overlapping or beyond U+10FFFF.", start);
            }

            for (var codePoint = start; codePoint <= end; codePoint++)
            {
                var glyph = startGlyph + (codePoint - start);
                if (glyph > int.MaxValue || !CmapEntries.TryAdd(map, (int)codePoint, (int)glyph, glyphCount))
                {
                    return CmapEntries.GlyphOutOfRange((int)codePoint, glyph);
                }
            }

            previousEnd = end;
        }

        return Result<FrozenDictionary<int, GlyphId>, Error>.Success(map.ToFrozenDictionary());
    }
}
