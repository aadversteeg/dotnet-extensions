using System;
using System.Collections.Frozen;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.Tables;

/// <summary>
/// The character-to-glyph map (OpenType spec, "cmap — Character to glyph index mapping table"). Reads one
/// Unicode subtable, in format 4 (Basic Multilingual Plane) or format 12 (all of Unicode), and expands it into
/// a dictionary from code point to glyph id. Code points that map to glyph 0 (.notdef) are left out: glyph 0
/// means the font has no glyph for the character ("character codes that do not correspond to any glyph in the font
/// should be mapped to glyph index 0").
/// </summary>
internal static class CmapTable
{
    public const string Tag = "cmap";
    private const int HeaderLength = 4;
    private const int EncodingRecordLength = 8;

    /// <summary>
    /// (platform, encoding) pairs that hold Unicode subtables, in order of preference: the full-repertoire
    /// encodings first (Windows UCS-4, Unicode full repertoire, Unicode 2.0 full), then the BMP-only ones
    /// (Windows Unicode BMP, Unicode 2.0 BMP). OpenType spec, "cmap" → "Platform IDs" and "Encoding records".
    /// </summary>
    private static readonly (int Platform, int Encoding)[] UnicodeEncodings = [(3, 10), (0, 6), (0, 4), (3, 1), (0, 3)];

    /// <param name="table">The table bytes.</param>
    /// <param name="glyphCount">From 'maxp': every glyph id in the map must be below it.</param>
    public static Result<FrozenDictionary<int, GlyphId>, Error> Parse(ReadOnlySpan<byte> table, int glyphCount)
    {
        if (table.Length < HeaderLength)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, HeaderLength, table.Length);
        }

        int recordCount = BigEndian.UInt16(table, 2);
        var recordsEnd = HeaderLength + (long)recordCount * EncodingRecordLength;
        if (table.Length < recordsEnd)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, recordsEnd, table.Length);
        }

        foreach (var (platform, encoding) in UnicodeEncodings)
        {
            for (var index = 0; index < recordCount; index++)
            {
                var record = HeaderLength + index * EncodingRecordLength;
                if (BigEndian.UInt16(table, record) != platform || BigEndian.UInt16(table, record + 2) != encoding)
                {
                    continue;
                }

                long offset = BigEndian.UInt32(table, record + 4);
                if (offset + 2 > table.Length)
                {
                    return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, offset + 2, table.Length);
                }

                var subtable = table[(int)offset..];
                switch (BigEndian.UInt16(subtable, 0))
                {
                    case 4:
                        return CmapFormat4.Parse(subtable, glyphCount);
                    case 12:
                        return CmapFormat12.Parse(subtable, glyphCount);
                }

                // A Unicode subtable in another format: try the next preference.
            }
        }

        return FontParseErrors.UnsupportedFormat<FrozenDictionary<int, GlyphId>>(
            "The font has no Unicode 'cmap' subtable in format 4 or 12.", Tag);
    }
}
