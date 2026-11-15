using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// A coverage table (OpenType spec, "OpenType Layout Common Table Formats" → "Coverage table"): the glyphs a
/// subtable applies to, each with its coverage index, which selects the glyph's data in the subtable.
/// </summary>
/// <param name="Indices">The coverage index of each covered glyph id.</param>
public sealed record Coverage(FrozenDictionary<ushort, int> Indices)
{
    /// <summary>The coverage index of a glyph, or null when the glyph is not covered.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>Its index, or null.</returns>
    public int? IndexOf(GlyphId glyph) => Indices.TryGetValue(glyph.Value, out var index) ? index : null;

    /// <summary>
    /// Reads a coverage table. Format 1: "glyphCount, glyphArray[glyphCount]" — the index is the position in the
    /// array. Format 2: "rangeCount, rangeRecords[rangeCount]" of (startGlyphID, endGlyphID, startCoverageIndex) —
    /// the index counts up from startCoverageIndex through the range. Another format is invalid.
    /// </summary>
    internal static Coverage Read(TableReader reader, int offset)
    {
        var indices = new Dictionary<ushort, int>();
        var format = reader.UInt16(offset);
        if (format == 1)
        {
            var count = reader.Count(offset + 2, offset + 4, 2);
            for (var index = 0; index < count; index++)
            {
                indices.TryAdd(reader.UInt16(offset + 4 + index * 2), index);
            }
        }
        else if (format == 2)
        {
            // "rangeRecords … in startGlyphID order", and ranges do not overlap; checking that also bounds the
            // work to one entry per glyph id, whatever a malformed table says.
            var count = reader.Count(offset + 2, offset + 4, 6);
            var previousEnd = -1;
            for (var range = 0; range < count; range++)
            {
                var record = offset + 4 + range * 6;
                var (start, end, startIndex) = (reader.UInt16(record), reader.UInt16(record + 2), reader.UInt16(record + 4));
                if (start <= previousEnd || end < start)
                {
                    reader.Invalid($"coverage range {start}–{end} out of order");
                    break;
                }

                previousEnd = end;
                if (!reader.Spend(end - start + 1))
                {
                    break;
                }

                for (var glyph = (int)start; glyph <= end; glyph++)
                {
                    indices.TryAdd((ushort)glyph, startIndex + glyph - start);
                }
            }
        }
        else
        {
            reader.Invalid($"{nameof(Coverage)} format {format}");
        }

        return new Coverage(indices.ToFrozenDictionary());
    }
}
