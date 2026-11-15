using System;
using System.Collections.Immutable;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// Horizontal metrics (OpenType spec, "hmtx — Horizontal metrics table"): the advance width of every glyph, in
/// font design units.
/// </summary>
/// <param name="AdvanceWidths">One advance width per glyph id, already expanded for the glyphs that share the
/// last record.</param>
internal sealed record HmtxTable(ImmutableArray<int> AdvanceWidths)
{
    public const string Tag = "hmtx";
    private const int LongMetricLength = 4; // advanceWidth (uint16) + lsb (int16)
    private const int LeftSideBearingLength = 2; // int16

    /// <param name="table">The table bytes.</param>
    /// <param name="numberOfHMetrics">From 'hhea': how many glyphs have a full (advance, bearing) record.</param>
    /// <param name="glyphCount">From 'maxp': how many glyphs the font has.</param>
    public static Result<HmtxTable, Error> Parse(ReadOnlySpan<byte> table, int numberOfHMetrics, int glyphCount)
    {
        if (numberOfHMetrics > glyphCount)
        {
            return FontParseErrors.InvalidValue<HmtxTable>(
                Tag, "hhea.numberOfHMetrics is larger than maxp.numGlyphs.", numberOfHMetrics);
        }

        // "hMetrics[numberOfHMetrics]" followed by "leftSideBearings[numGlyphs - numberOfHMetrics]".
        var needed = (long)numberOfHMetrics * LongMetricLength + (long)(glyphCount - numberOfHMetrics) * LeftSideBearingLength;
        if (table.Length < needed)
        {
            return FontParseErrors.Truncated<HmtxTable>(Tag, needed, table.Length);
        }

        // "If the font is monospaced, only one entry need be in the array, but that entry is required. The last
        // entry applies to all subsequent glyphs."
        var lastAdvance = BigEndian.UInt16(table, (numberOfHMetrics - 1) * LongMetricLength);
        var widths = ImmutableArray.CreateBuilder<int>(glyphCount);
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            widths.Add(glyph < numberOfHMetrics ? BigEndian.UInt16(table, glyph * LongMetricLength) : lastAdvance);
        }

        return Result<HmtxTable, Error>.Success(new HmtxTable(widths.MoveToImmutable()));
    }
}
