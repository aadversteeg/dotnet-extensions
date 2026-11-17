using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.Tables;

/// <summary>
/// The horizontal header (OpenType spec, "hhea — Horizontal header table"): the vertical metrics used for line
/// spacing and the number of entries in 'hmtx'.
/// </summary>
internal sealed record HheaTable(int Ascender, int Descender, int LineGap, int NumberOfHMetrics)
{
    public const string Tag = "hhea";
    private const int Length = 36;

    public static Result<HheaTable, Error> Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < Length)
        {
            return FontParseErrors.Truncated<HheaTable>(Tag, Length, table.Length);
        }

        int numberOfHMetrics = BigEndian.UInt16(table, 34);
        if (numberOfHMetrics == 0)
        {
            // hmtx needs at least one full record: glyphs past the last record reuse its advance width.
            return FontParseErrors.InvalidValue<HheaTable>(Tag, "numberOfHMetrics must be at least 1.", numberOfHMetrics);
        }

        return Result<HheaTable, Error>.Success(new HheaTable(
            Ascender: BigEndian.Int16(table, 4),
            Descender: BigEndian.Int16(table, 6),
            LineGap: BigEndian.Int16(table, 8),
            NumberOfHMetrics: numberOfHMetrics));
    }
}
