using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// The kerning pairs of the old 'kern' table (OpenType spec, "kern — Kerning table"), used when a font has no
/// GPOS kerning. Only what applies to horizontal text is kept: format-0 subtables that are horizontal, not
/// "minimum" and not cross-stream.
/// </summary>
/// <param name="Subtables">The kept subtables, in table order.</param>
public sealed record LegacyKerning(ValueList<KerningSubtable> Subtables)
{
    /// <summary>A font without a 'kern' table.</summary>
    public static LegacyKerning None { get; } = new([]);

    /// <summary>The table tag.</summary>
    public const string Tag = "kern";

    /// <summary>
    /// The adjustment for a pair, in font design units: the values of the subtables are accumulated (the 'kern' page
    /// speaks of the value "currently being accumulated"), and a subtable with the override bit replaces the sum so far
    /// (coverage bit 3, "If this bit is set to 1 the value in this table should replace the value currently being
    /// accumulated.").
    /// </summary>
    /// <param name="left">The first glyph.</param>
    /// <param name="right">The second glyph.</param>
    /// <returns>The adjustment of the first glyph's advance; 0 when no subtable lists the pair.</returns>
    public int Adjustment(GlyphId left, GlyphId right)
    {
        var total = 0;
        foreach (var subtable in Subtables)
        {
            if (subtable.Pairs.TryGetValue((left.Value, right.Value), out var value))
            {
                total = subtable.Override ? value : total + value;
            }
        }

        return total;
    }

    /// <summary>
    /// Reads a version-0 (OpenType) 'kern' table: the fields <c>version</c> (0) and <c>nTables</c>, then per subtable
    /// <c>version</c>, <c>length</c>, <c>coverage</c> and, for format 0 (coverage high byte), <c>nPairs</c>,
    /// <c>searchRange</c>, <c>entrySelector</c>, <c>rangeShift</c> and the
    /// pairs (left, right, value FWORD). The Apple variant (version 1.0, 32-bit) is not an OpenType table and is
    /// read as "no kerning"; other subtable formats are skipped. Subtables follow each other by their length field,
    /// summed in 64 bits; any problem the reader records — a read past the end, the work budget used up by subtables
    /// that do not move on — ends the loop and makes the table an error.
    /// </summary>
    /// <param name="data">The table bytes.</param>
    /// <returns>The kerning, or a parse error.</returns>
    internal static Result<LegacyKerning, Error> Parse(ReadOnlyMemory<byte> data)
    {
        const ushort Horizontal = 0x0001, Minimum = 0x0002, CrossStream = 0x0004, OverrideBit = 0x0008;
        var reader = new TableReader(data);
        if (reader.UInt16(0) != 0)
        {
            return Result<LegacyKerning, Error>.Success(None);
        }

        var subtables = new List<KerningSubtable>();
        var offset = 4L;
        var count = reader.UInt16(2);
        for (var index = 0; index < count && !reader.HasProblem && offset <= reader.Length; index++)
        {
            var at = (int)offset;
            var (length, coverage) = (reader.UInt16(at + 2), reader.UInt16(at + 4));
            var applies = (coverage >> 8) == 0 && (coverage & (Horizontal | Minimum | CrossStream)) == Horizontal;
            if (applies)
            {
                var pairs = new Dictionary<(ushort, ushort), short>();
                var pairCount = reader.Count(at + 6, at + 14, 6);
                for (var pair = 0; pair < pairCount; pair++)
                {
                    var record = at + 14 + pair * 6;
                    pairs.TryAdd((reader.UInt16(record), reader.UInt16(record + 2)), reader.Int16(record + 4));
                }

                subtables.Add(new KerningSubtable(pairs.ToFrozenDictionary(), (coverage & OverrideBit) != 0));
            }

            offset += length;
        }

        return reader.Finish(Tag, new LegacyKerning([.. subtables]));
    }
}
