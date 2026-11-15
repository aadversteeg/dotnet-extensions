using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Documents.Core;
using Ave.Documents.Fonts.Tables;
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
    /// The adjustment for a pair, in font design units. "Values in subtables are added" unless a subtable has the
    /// override bit, whose value then replaces the sum so far (coverage bit 3, "If set to 1 the value in this table
    /// should replace the value currently being accumulated").
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
    /// Reads a version-0 (OpenType) 'kern' table: "version 0, nTables", then per subtable "version, length,
    /// coverage" and, for format 0 (coverage high byte), "nPairs, searchRange, entrySelector, rangeShift" and the
    /// pairs (left, right, value FWORD). The Apple variant (version 1.0, 32-bit) is not an OpenType table and is
    /// read as "no kerning"; other subtable formats are skipped.
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
        var offset = 4;
        for (var index = 0; index < reader.UInt16(2) && reader.FirstOutOfRange is null; index++)
        {
            var (length, coverage) = (reader.UInt16(offset + 2), reader.UInt16(offset + 4));
            var applies = (coverage >> 8) == 0 && (coverage & (Horizontal | Minimum | CrossStream)) == Horizontal;
            if (applies)
            {
                var pairs = new Dictionary<(ushort, ushort), short>();
                var pairCount = reader.Count(offset + 6, offset + 14, 6);
                for (var pair = 0; pair < pairCount; pair++)
                {
                    var record = offset + 14 + pair * 6;
                    pairs.TryAdd((reader.UInt16(record), reader.UInt16(record + 2)), reader.Int16(record + 4));
                }

                subtables.Add(new KerningSubtable(pairs.ToFrozenDictionary(), (coverage & OverrideBit) != 0));
            }

            offset += length;
        }

        return reader.FirstOutOfRange is { } outside
            ? FontParseErrors.Truncated<LegacyKerning>(Tag, outside, reader.Length)
            : Result<LegacyKerning, Error>.Success(new LegacyKerning([.. subtables]));
    }
}

/// <summary>One format-0 subtable of the 'kern' table.</summary>
/// <param name="Pairs">The adjustment for each (left, right) glyph id pair, in font design units.</param>
/// <param name="Override">True when the subtable's values replace, rather than add to, earlier ones.</param>
public sealed record KerningSubtable(FrozenDictionary<(ushort Left, ushort Right), short> Pairs, bool Override);
