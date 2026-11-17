using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// The maximum profile (OpenType spec, "maxp — Maximum profile"). Only the glyph count is needed: it sizes
/// 'hmtx' and 'loca' and bounds every glyph id.
/// </summary>
internal sealed record MaxpTable(int GlyphCount)
{
    public const string Tag = "maxp";
    private const int Length = 6;

    public static Result<MaxpTable, Error> Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < Length)
        {
            return FontParseErrors.Truncated<MaxpTable>(Tag, Length, table.Length);
        }

        int glyphCount = BigEndian.UInt16(table, 4);
        if (glyphCount == 0)
        {
            // Glyph 0 (.notdef) is required in every font.
            return FontParseErrors.InvalidValue<MaxpTable>(Tag, "numGlyphs must be at least 1.", glyphCount);
        }

        return Result<MaxpTable, Error>.Success(new MaxpTable(glyphCount));
    }
}
