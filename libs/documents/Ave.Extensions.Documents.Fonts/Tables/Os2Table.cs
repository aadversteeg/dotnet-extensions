using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.Tables;

/// <summary>
/// The OS/2 and Windows metrics table (OpenType spec, "OS/2 — OS/2 and Windows metrics table"): the weight
/// class, the embedding licence bits, the cap height, and the typographic and Windows line metrics with the bit that
/// says which to use.
/// </summary>
/// <param name="WeightClass">usWeightClass, 1–1000.</param>
/// <param name="FsType">fsType, the embedding licensing rights.</param>
/// <param name="CapHeight">sCapHeight (table version 2 and later); null for older versions.</param>
/// <param name="IsItalic">fsSelection bit 0, ITALIC: "Font contains italic or oblique glyphs".</param>
internal sealed record Os2Table(int WeightClass, ushort FsType, int? CapHeight, bool IsItalic)
{
    /// <summary>fsSelection bit 7, USE_TYPO_METRICS: the font asks that its typographic metrics (sTypoAscender,
    /// sTypoDescender, sTypoLineGap) be used for the default line spacing.</summary>
    public bool UseTypoMetrics { get; init; }

    /// <summary>sTypoAscender, sTypoDescender (negative below the baseline) and sTypoLineGap, in design units.</summary>
    public (int Ascender, int Descender, int LineGap) Typo { get; init; }

    /// <summary>usWinAscent and usWinDescent (both positive), in design units.</summary>
    public (int Ascent, int Descent) Win { get; init; }

    public const string Tag = "OS/2";

    // Table sizes per version (OpenType spec, "OS/2" → version history): version 0 is 78 bytes, version 1 is 86,
    // versions 2 to 4 are 96, version 5 is 100.
    private const int Version0Length = 78;
    private const int Version2Length = 96;

    // fsSelection is at byte 62 in every version; bit 0 is ITALIC, bit 7 USE_TYPO_METRICS. The typographic and Windows
    // metrics follow at bytes 68 to 77, also in every version.
    private const int FsSelectionOffset = 62;
    private const int ItalicBit = 1;
    private const int UseTypoMetricsBit = 1 << 7;
    private const int TypoAscenderOffset = 68;

    public static Result<Os2Table, Error> Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < Version0Length)
        {
            return FontParseErrors.Truncated<Os2Table>(Tag, Version0Length, table.Length);
        }

        int version = BigEndian.UInt16(table, 0);
        if (version >= 2 && table.Length < Version2Length)
        {
            return FontParseErrors.Truncated<Os2Table>(Tag, Version2Length, table.Length);
        }

        int weightClass = BigEndian.UInt16(table, 4);
        if (weightClass is < 1 or > 1000)
        {
            return FontParseErrors.InvalidValue<Os2Table>(Tag, "usWeightClass must be 1–1000.", weightClass);
        }

        int? capHeight = version >= 2 ? BigEndian.Int16(table, 88) : null;
        var fsSelection = BigEndian.UInt16(table, FsSelectionOffset);
        return Result<Os2Table, Error>.Success(new Os2Table(weightClass, BigEndian.UInt16(table, 8), capHeight, (fsSelection & ItalicBit) != 0)
        {
            UseTypoMetrics = (fsSelection & UseTypoMetricsBit) != 0,
            Typo = (BigEndian.Int16(table, TypoAscenderOffset), BigEndian.Int16(table, TypoAscenderOffset + 2), BigEndian.Int16(table, TypoAscenderOffset + 4)),
            Win = (BigEndian.UInt16(table, TypoAscenderOffset + 6), BigEndian.UInt16(table, TypoAscenderOffset + 8)),
        });
    }
}
