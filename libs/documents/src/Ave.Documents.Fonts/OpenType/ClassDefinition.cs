using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// A class definition table (OpenType spec, "OpenType Layout Common Table Formats" → "Class definition table"):
/// assigns glyphs to numbered classes. "Any glyph not included in the range of covered glyph IDs automatically
/// belongs to Class 0."
/// </summary>
/// <param name="Classes">The class of each glyph that is not in class 0.</param>
public sealed record ClassDefinition(FrozenDictionary<ushort, ushort> Classes)
{
    /// <summary>No glyph in any class but 0.</summary>
    public static ClassDefinition Empty { get; } = new(FrozenDictionary<ushort, ushort>.Empty);

    /// <summary>The class of a glyph.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>Its class; 0 when the table does not list it.</returns>
    public ushort ClassOf(GlyphId glyph) => Classes.TryGetValue(glyph.Value, out var value) ? value : (ushort)0;

    /// <summary>
    /// Reads a class definition table. Format 1: "startGlyphID, glyphCount, classValueArray[glyphCount]". Format 2:
    /// "classRangeCount, classRangeRecords[]" of (startGlyphID, endGlyphID, class). Offset 0 means "no table".
    /// </summary>
    internal static ClassDefinition Read(TableReader reader, int offset)
    {
        if (offset == 0)
        {
            return Empty;
        }

        var classes = new Dictionary<ushort, ushort>();
        var format = reader.UInt16(offset);
        if (format == 1)
        {
            var start = reader.UInt16(offset + 2);
            var count = reader.Count(offset + 4, offset + 6, 2);
            for (var index = 0; index < count && start + index <= ushort.MaxValue; index++)
            {
                classes.TryAdd((ushort)(start + index), reader.UInt16(offset + 6 + index * 2));
            }
        }
        else if (format == 2)
        {
            // Ranges are "in startGlyphID order" and do not overlap; checked, which also bounds the work.
            var count = reader.Count(offset + 2, offset + 4, 6);
            var previousEnd = -1;
            for (var range = 0; range < count; range++)
            {
                var record = offset + 4 + range * 6;
                var (start, end, value) = (reader.UInt16(record), reader.UInt16(record + 2), reader.UInt16(record + 4));
                if (start <= previousEnd || end < start)
                {
                    reader.Invalid($"class range {start}–{end} out of order");
                    break;
                }

                previousEnd = end;
                if (!reader.Spend(end - start + 1))
                {
                    break;
                }

                for (var glyph = (int)start; glyph <= end; glyph++)
                {
                    classes.TryAdd((ushort)glyph, value);
                }
            }
        }
        else
        {
            reader.Invalid($"{nameof(ClassDefinition)} format {format}");
        }

        return new ClassDefinition(classes.ToFrozenDictionary());
    }
}
