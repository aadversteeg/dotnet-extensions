using System;
using System.Collections.Immutable;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// The index to location table (OpenType spec, "loca — Index to location"): where each glyph's outline starts
/// in 'glyf'. Glyph <c>i</c> occupies bytes <c>[Offsets[i], Offsets[i + 1])</c>; an empty range is a glyph
/// without an outline, such as the space.
/// </summary>
/// <param name="Offsets">glyphCount + 1 byte offsets into 'glyf', never decreasing; a glyph with an outline has at least
/// its <see cref="GlyphHeaderLength"/>-byte header.</param>
internal sealed record LocaTable(ImmutableArray<int> Offsets)
{
    public const string Tag = "loca";

    /// <summary>
    /// The length of the header every glyph description starts with (OpenType spec, "glyf — Glyph data", glyph headers):
    /// numberOfContours, xMin, yMin, xMax, yMax, each 16 bits. A glyph with fewer bytes but more than none is malformed.
    /// </summary>
    public const int GlyphHeaderLength = 10;

    /// <param name="table">The table bytes.</param>
    /// <param name="longOffsets">From 'head'.indexToLocFormat: true for Offset32, false for Offset16.</param>
    /// <param name="glyphCount">From 'maxp'.</param>
    /// <param name="glyfLength">The length of the 'glyf' table, which every offset must stay inside.</param>
    public static Result<LocaTable, Error> Parse(ReadOnlySpan<byte> table, bool longOffsets, int glyphCount, int glyfLength)
    {
        var entryLength = longOffsets ? 4 : 2;
        var needed = (long)(glyphCount + 1) * entryLength;
        if (table.Length < needed)
        {
            return FontParseErrors.Truncated<LocaTable>(Tag, needed, table.Length);
        }

        var offsets = ImmutableArray.CreateBuilder<int>(glyphCount + 1);
        long previous = 0;
        for (var index = 0; index <= glyphCount; index++)
        {
            // "Offset16: the actual local offset divided by 2 is stored."
            long offset = longOffsets
                ? BigEndian.UInt32(table, index * 4)
                : BigEndian.UInt16(table, index * 2) * 2L;

            // "The offsets must be in ascending order", and each must point inside 'glyf'.
            if (offset < previous || offset > glyfLength)
            {
                return FontParseErrors.InvalidValue<LocaTable>(
                    Tag, $"offset {index} is out of order or outside 'glyf' ({glyfLength} bytes).", offset);
            }

            if (offset > previous && offset - previous < GlyphHeaderLength)
            {
                return FontParseErrors.InvalidValue<LocaTable>(
                    Tag, $"glyph {index - 1} has {offset - previous} bytes, less than the {GlyphHeaderLength}-byte glyph header.", offset - previous);
            }

            offsets.Add((int)offset);
            previous = offset;
        }

        return Result<LocaTable, Error>.Success(new LocaTable(offsets.MoveToImmutable()));
    }
}
