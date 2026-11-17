using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// The font header (OpenType spec, "head — Font header table"): the size of the design grid, the bounding box
/// of all glyphs, and the format of the 'loca' table.
/// </summary>
internal sealed record HeadTable(int UnitsPerEm, FontBoundingBox BoundingBox, bool LongLocaOffsets)
{
    public const string Tag = "head";
    private const int Length = 54;
    private const uint MagicNumber = 0x5F0F3CF5;

    public static Result<HeadTable, Error> Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < Length)
        {
            return FontParseErrors.Truncated<HeadTable>(Tag, Length, table.Length);
        }

        var magic = BigEndian.UInt32(table, 12);
        if (magic != MagicNumber)
        {
            return FontParseErrors.InvalidValue<HeadTable>(Tag, "magicNumber must be 0x5F0F3CF5.", $"0x{magic:X8}");
        }

        // "unitsPerEm: Set to a value from 16 to 16384."
        int unitsPerEm = BigEndian.UInt16(table, 18);
        if (unitsPerEm is < 16 or > 16384)
        {
            return FontParseErrors.InvalidValue<HeadTable>(Tag, "unitsPerEm must be 16–16384.", unitsPerEm);
        }

        // "indexToLocFormat: 0 for short offsets (Offset16), 1 for long (Offset32)."
        var locaFormat = BigEndian.Int16(table, 50);
        if (locaFormat is not (0 or 1))
        {
            return FontParseErrors.InvalidValue<HeadTable>(Tag, "indexToLocFormat must be 0 or 1.", locaFormat);
        }

        var box = new FontBoundingBox(
            BigEndian.Int16(table, 36),
            BigEndian.Int16(table, 38),
            BigEndian.Int16(table, 40),
            BigEndian.Int16(table, 42));

        return Result<HeadTable, Error>.Success(new HeadTable(unitsPerEm, box, LongLocaOffsets: locaFormat == 1));
    }
}
