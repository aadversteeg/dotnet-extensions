using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// A class definition table (OpenType spec, "OpenType Layout Common Table Formats" → "Class definition table"):
/// assigns glyphs to numbered classes. "Any glyph not included in the range of covered glyph IDs is assigned to
/// Class 0."
/// </summary>
/// <remarks>
/// Kept as ranges and searched, never expanded glyph by glyph: in the fonts of large scripts one range record gives
/// a class to tens of thousands of glyphs (Malgun Gothic: 28 215 glyphs in a GDEF of 30 bytes).
/// </remarks>
/// <param name="Ranges">The ranges of glyphs with a class other than 0, in glyph order, none overlapping.</param>
internal sealed record ClassDefinition(ImmutableArray<ClassDefinition.Range> Ranges)
{
    /// <summary>Consecutive glyphs that share a class.</summary>
    /// <param name="Start">The first glyph id.</param>
    /// <param name="End">The last glyph id.</param>
    /// <param name="Class">Their class.</param>
    public readonly record struct Range(ushort Start, ushort End, ushort Class);

    /// <summary>No glyph in any class but 0.</summary>
    public static ClassDefinition Empty { get; } = new(ImmutableArray<Range>.Empty);

    /// <summary>The class of a glyph (a binary search of the ranges).</summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>Its class; 0 when the table does not list it.</returns>
    public ushort ClassOf(GlyphId glyph)
    {
        var (low, high) = (0, Ranges.Length - 1);
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var range = Ranges[middle];
            if (glyph.Value < range.Start)
            {
                high = middle - 1;
            }
            else if (glyph.Value > range.End)
            {
                low = middle + 1;
            }
            else
            {
                return range.Class;
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads a class definition table; one that several subtables point at is read once per table (see
    /// <see cref="TableReader.Shared{T}"/>). Format 1: "startGlyphID, glyphCount, classValueArray[glyphCount]" — each
    /// glyph from startGlyphID on becomes a range of one. Format 2: "classRangeCount, classRangeRecords[]" of
    /// (startGlyphID, endGlyphID, class), "in startGlyphID order" and not overlapping, which is checked. An offset of
    /// 0 is NULL, "no table": every glyph is in class 0.
    /// </summary>
    /// <param name="reader">The table's reader.</param>
    /// <param name="parent">Where the structure that holds the offset starts; offsets count from there.</param>
    /// <param name="relative">The offset as stored, from <paramref name="parent"/>.</param>
    internal static ClassDefinition Read(TableReader reader, int parent, int relative) =>
        relative == 0 ? Empty : reader.Shared(parent + relative, () => ReadAt(reader, parent + relative));

    private static ClassDefinition ReadAt(TableReader reader, int offset)
    {
        var format = reader.UInt16(offset);
        if (format == 1)
        {
            var start = reader.UInt16(offset + 2);
            var count = Math.Min(reader.Count(offset + 4, offset + 6, 2), ushort.MaxValue + 1 - start);
            return new ClassDefinition([.. Enumerable.Range(0, count)
                .Select(index => new Range((ushort)(start + index), (ushort)(start + index), reader.UInt16(offset + 6 + index * 2)))
                .Where(range => range.Class != 0)]);
        }

        if (format == 2)
        {
            var count = reader.Count(offset + 2, offset + 4, 6);
            var ranges = new List<Range>(count);
            for (var index = 0; index < count; index++)
            {
                var record = offset + 4 + index * 6;
                var range = new Range(reader.UInt16(record), reader.UInt16(record + 2), reader.UInt16(record + 4));
                if (range.End < range.Start || (ranges.Count > 0 && range.Start <= ranges[^1].End))
                {
                    reader.Invalid($"class range {range.Start}–{range.End} out of order");
                    return Empty;
                }

                ranges.Add(range);
            }

            return new ClassDefinition([.. ranges]);
        }

        reader.Invalid($"{nameof(ClassDefinition)} format {format}");
        return Empty;
    }
}
