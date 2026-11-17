using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.OpenType;

/// <summary>
/// A coverage table (OpenType spec, "OpenType Layout Common Table Formats" → "Coverage table"): the glyphs a
/// subtable applies to, each with its coverage index, which selects the glyph's data in the subtable.
/// </summary>
/// <remarks>
/// Kept as the table stores it — sorted ranges of glyph ids — and searched, never expanded glyph by glyph: one range
/// record may cover tens of thousands of glyphs, so the work and the memory are those of the records, not of the
/// glyphs they cover.
/// </remarks>
public sealed record Coverage
{
    // Made only by Of (ranges checked) or by the parser (which checks them itself, see Read): the binary search needs
    // the ranges in glyph order, and there is no other way to set them.
    private Coverage(ImmutableArray<Range> ranges) => Ranges = ranges;

    /// <summary>The covered ranges in glyph order, none overlapping and none ending before it starts.</summary>
    public ImmutableArray<Range> Ranges { get; }

    /// <summary>Makes a coverage from ranges, which must be in glyph order, none overlapping and none ending before it
    /// starts ("in startGlyphID order"), as the binary search needs.</summary>
    /// <param name="ranges">The ranges; an unset array is no ranges.</param>
    /// <returns>The value, or a <see cref="FontErrorCodes.InvalidRanges"/> error naming the first range that breaks the
    /// order.</returns>
    public static Result<Coverage, Error> Of(ImmutableArray<Range> ranges)
    {
        ImmutableArray<Range> all = ranges.IsDefault ? [] : ranges;
        var broken = Enumerable.Range(0, all.Length)
            .FirstOrDefault(index => all[index].End < all[index].Start || (index > 0 && all[index].Start <= all[index - 1].End), -1);
        return broken < 0
            ? Result<Coverage, Error>.Success(new Coverage(all))
            : Result<Coverage, Error>.Failure(new Error(
                    FontErrorCodes.InvalidRanges,
                    $"Coverage ranges must be in glyph order and not overlap; range {broken} ({all[broken].Start}–{all[broken].End}) is not.")
                .With(ErrorMetadataKeys.Value, broken.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>A run of consecutive covered glyphs whose coverage indexes count up from <paramref name="StartIndex"/>.</summary>
    /// <param name="Start">The first glyph id.</param>
    /// <param name="End">The last glyph id.</param>
    /// <param name="StartIndex">The coverage index of <paramref name="Start"/>.</param>
    public readonly record struct Range(ushort Start, ushort End, int StartIndex);

    /// <summary>No glyph covered.</summary>
    public static Coverage Empty { get; } = new(ImmutableArray<Range>.Empty);

    /// <summary>The coverage index of a glyph, or null when the glyph is not covered (a binary search of the ranges).</summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>Its index, or null.</returns>
    public int? IndexOf(GlyphId glyph)
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
                return range.StartIndex + glyph.Value - range.Start;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a coverage table; one that several subtables point at is read once per table (see
    /// <see cref="TableReader.Shared{T}"/>). Format 1: <c>glyphCount, glyphArray[glyphCount]</c> — "in numerical order";
    /// the index is the position in the array. A glyph listed twice keeps its first index, and an array out of order
    /// is sorted, so a lookup always finds the index the array gives. Format 2: <c>rangeCount,
    /// rangeRecords[rangeCount]</c> of (startGlyphID, endGlyphID, startCoverageIndex), "in startGlyphID order" and not
    /// overlapping, which is checked. Another format is invalid.
    /// </summary>
    internal static Coverage Read(TableReader reader, int offset) => reader.Shared(offset, () => ReadAt(reader, offset));

    private static Coverage ReadAt(TableReader reader, int offset)
    {
        var format = reader.UInt16(offset);
        if (format == 1)
        {
            var count = reader.Count(offset + 2, offset + 4, 2);
            var glyphs = Enumerable.Range(0, count).Select(index => (Glyph: reader.UInt16(offset + 4 + index * 2), Index: index));
            return new Coverage([.. glyphs.OrderBy(entry => entry.Glyph).DistinctBy(entry => entry.Glyph).Select(entry => new Range(entry.Glyph, entry.Glyph, entry.Index))]);
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
                    reader.Invalid($"coverage range {range.Start}–{range.End} out of order");
                    return Empty;
                }

                ranges.Add(range);
            }

            return new Coverage([.. ranges]);
        }

        reader.Invalid($"{nameof(Coverage)} format {format}");
        return Empty;
    }
}
