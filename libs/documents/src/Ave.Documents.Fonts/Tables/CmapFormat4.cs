using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// cmap subtable format 4, "Segment mapping to delta values" (OpenType spec, "cmap" → "Format 4"). Maps the
/// Basic Multilingual Plane (U+0000–U+FFFF) as a list of segments [startCode, endCode], each with either a
/// constant delta or an index into a glyph id array.
/// </summary>
internal static class CmapFormat4
{
    private const string Tag = CmapTable.Tag;
    private const int HeaderLength = 14; // format, length, language, segCountX2, searchRange, entrySelector, rangeShift

    public static Result<FrozenDictionary<int, GlyphId>, Error> Parse(ReadOnlySpan<byte> subtable, int glyphCount)
    {
        if (subtable.Length < HeaderLength)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, HeaderLength, subtable.Length);
        }

        int length = BigEndian.UInt16(subtable, 2);
        int segCountX2 = BigEndian.UInt16(subtable, 6);
        if (length > subtable.Length)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, length, subtable.Length);
        }

        if (segCountX2 % 2 != 0)
        {
            return FontParseErrors.InvalidValue<FrozenDictionary<int, GlyphId>>(Tag, "format 4 segCountX2 must be even.", segCountX2);
        }

        // Layout after the header: endCode[segCount], reservedPad, startCode[segCount], idDelta[segCount],
        // idRangeOffset[segCount], then glyphIdArray up to 'length'.
        var endCodes = HeaderLength;
        var startCodes = endCodes + segCountX2 + 2;
        var idDeltas = startCodes + segCountX2;
        var idRangeOffsets = idDeltas + segCountX2;
        var arraysEnd = idRangeOffsets + segCountX2;
        if (length < arraysEnd)
        {
            return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, arraysEnd, length);
        }

        // "The segments are sorted in order of increasing endCode values", and a lookup finds the first segment whose
        // endCode is at or above the code, then checks its startCode. So a code at or below an earlier segment's
        // endCode belongs to that segment, never to a later one: each segment adds only the codes above the one
        // before it, which also bounds the work to one step per code (65 536 at most), however the segments overlap.
        var data = subtable[..length];
        var map = new Dictionary<int, GlyphId>();
        var previousEnd = -1;
        for (var segment = 0; segment < segCountX2 / 2; segment++)
        {
            var offset = segment * 2;
            int start = BigEndian.UInt16(data, startCodes + offset);
            int end = BigEndian.UInt16(data, endCodes + offset);
            int delta = BigEndian.Int16(data, idDeltas + offset);
            var rangeOffsetPosition = idRangeOffsets + offset;
            int rangeOffset = BigEndian.UInt16(data, rangeOffsetPosition);
            if (start > end)
            {
                return FontParseErrors.InvalidValue<FrozenDictionary<int, GlyphId>>(Tag, $"format 4 segment {segment} has startCode > endCode.", start);
            }

            if (end <= previousEnd)
            {
                return FontParseErrors.InvalidValue<FrozenDictionary<int, GlyphId>>(Tag, $"format 4 segment {segment} does not end after the segment before it.", end);
            }

            var first = Math.Max(start, previousEnd + 1);
            previousEnd = end;
            for (var codePoint = first; codePoint <= end; codePoint++)
            {
                int glyph;
                if (rangeOffset == 0)
                {
                    // "If the idRangeOffset is 0, the idDelta value is added directly to the character code offset
                    // (i.e. idDelta[i] + c) to get the corresponding glyph index", modulo 65536.
                    glyph = (codePoint + delta) & 0xFFFF;
                }
                else
                {
                    // "glyphId = *(idRangeOffset[i]/2 + (c - startCode[i]) + &idRangeOffset[i])": the
                    // offset is counted in bytes from the idRangeOffset entry itself. A glyph id of 0 found there
                    // means "missing"; any other value gets the delta added, modulo 65536.
                    var position = rangeOffsetPosition + rangeOffset + (codePoint - start) * 2;
                    if (position + 2 > data.Length)
                    {
                        return FontParseErrors.Truncated<FrozenDictionary<int, GlyphId>>(Tag, position + 2, data.Length);
                    }

                    int fromArray = BigEndian.UInt16(data, position);
                    glyph = fromArray == 0 ? 0 : (fromArray + delta) & 0xFFFF;
                }

                if (!CmapEntries.TryAdd(map, codePoint, glyph, glyphCount))
                {
                    return CmapEntries.GlyphOutOfRange(codePoint, glyph);
                }
            }
        }

        return Result<FrozenDictionary<int, GlyphId>, Error>.Success(map.ToFrozenDictionary());
    }
}
