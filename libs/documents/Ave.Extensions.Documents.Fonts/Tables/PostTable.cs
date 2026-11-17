using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// The PostScript table (OpenType spec, "post — PostScript table"). Only the header is read: the italic angle
/// and whether the font is monospaced, both needed for the PDF font descriptor. Glyph names are not needed.
/// </summary>
internal sealed record PostTable(double ItalicAngle, bool IsFixedPitch)
{
    public const string Tag = "post";
    private const int HeaderLength = 32;

    public static Result<PostTable, Error> Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < HeaderLength)
        {
            return FontParseErrors.Truncated<PostTable>(Tag, HeaderLength, table.Length);
        }

        // italicAngle is a Fixed at offset 4; isFixedPitch a uint32 at offset 12, "0 if proportionally spaced".
        return Result<PostTable, Error>.Success(new PostTable(
            ItalicAngle: BigEndian.Fixed(table, 4),
            IsFixedPitch: BigEndian.UInt32(table, 12) != 0));
    }
}
