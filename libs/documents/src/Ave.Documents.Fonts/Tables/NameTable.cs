using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// The naming table (OpenType spec, "name — Naming table"): the human-readable names of the font.
/// </summary>
internal static class NameTable
{
    public const string Tag = "name";
    private const int HeaderLength = 6;
    private const int RecordLength = 12;

    // Name IDs (OpenType spec, "name" → "Name IDs").
    private const int FamilyId = 1;
    private const int SubfamilyId = 2;
    private const int FullNameId = 4;
    private const int PostScriptNameId = 6;
    private const int TypographicFamilyId = 16;
    private const int TypographicSubfamilyId = 17;

    // Platform IDs: 0 = Unicode, 3 = Windows. Both store strings as UTF-16BE (Windows encodings 1 = BMP and
    // 10 = full repertoire). The Macintosh platform (1) uses legacy 8-bit encodings and is not read.
    private const int UnicodePlatform = 0;
    private const int WindowsPlatform = 3;
    private const int WindowsEnglishUnitedStates = 0x0409;

    /// <summary>The name IDs the engine reads; every other record is skipped unread.</summary>
    private static readonly int[] UsedIds = [FamilyId, SubfamilyId, FullNameId, PostScriptNameId, TypographicFamilyId, TypographicSubfamilyId];

    /// <summary>A record's place in the table; its text is decoded only for the record chosen per name ID, so the
    /// strings decoded are at most one per used ID, however many records point at the same bytes.</summary>
    private sealed record NameRecord(int PlatformId, int LanguageId, int NameId, int Start, int Length);

    public static Result<FontNames, Error> Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < HeaderLength)
        {
            return FontParseErrors.Truncated<FontNames>(Tag, HeaderLength, table.Length);
        }

        int count = BigEndian.UInt16(table, 2);
        int storageOffset = BigEndian.UInt16(table, 4);
        var recordsEnd = HeaderLength + (long)count * RecordLength;
        if (table.Length < recordsEnd)
        {
            return FontParseErrors.Truncated<FontNames>(Tag, recordsEnd, table.Length);
        }

        var records = new List<NameRecord>();
        for (var index = 0; index < count; index++)
        {
            var record = HeaderLength + index * RecordLength;
            int platform = BigEndian.UInt16(table, record);
            int encoding = BigEndian.UInt16(table, record + 2);
            if (!IsUtf16(platform, encoding))
            {
                continue;
            }

            int length = BigEndian.UInt16(table, record + 8);
            long start = storageOffset + (long)BigEndian.UInt16(table, record + 10);
            if (start + length > table.Length)
            {
                return FontParseErrors.Truncated<FontNames>(Tag, start + length, table.Length);
            }

            int nameId = BigEndian.UInt16(table, record + 6);
            if (UsedIds.Contains(nameId))
            {
                records.Add(new NameRecord(platform, BigEndian.UInt16(table, record + 4), nameId, (int)start, length));
            }
        }

        var names = table.ToArray();
        string? Find(int nameId) => FindIn(records, nameId, names);
        var family = Find(TypographicFamilyId) ?? Find(FamilyId);
        var postScriptName = Find(PostScriptNameId);
        if (family is null || postScriptName is null)
        {
            // The PDF writer needs the PostScript name (BaseFont, ISO 32000-1 §9.6.2.1) and the font set needs
            // the family name to match requests against.
            return FontParseErrors.InvalidValue<FontNames>(
                Tag, "the font has no Unicode family name (ID 1 or 16) or PostScript name (ID 6).", "missing");
        }

        var subfamily = Find(TypographicSubfamilyId) ?? Find(SubfamilyId) ?? string.Empty;
        var fullName = Find(FullNameId) ?? $"{family} {subfamily}".Trim();
        return Result<FontNames, Error>.Success(new FontNames(family, subfamily, fullName, postScriptName));
    }

    private static bool IsUtf16(int platform, int encoding) =>
        platform == UnicodePlatform || (platform == WindowsPlatform && encoding is 1 or 10);

    /// <summary>
    /// The best record for a name ID: Windows US English first (the language every font is required to have
    /// for Windows), then any other Windows record, then a Unicode-platform record. Within a rank the first
    /// record in the table wins, so the choice is deterministic.
    /// </summary>
    private static string? FindIn(IReadOnlyList<NameRecord> records, int nameId, byte[] table) =>
        records
            .Where(record => record.NameId == nameId && record.Length >= 2)
            .OrderBy(Rank)
            .Select(record => Encoding.BigEndianUnicode.GetString(table, record.Start, record.Length))
            .FirstOrDefault();

    private static int Rank(NameRecord record) => record switch
    {
        { PlatformId: WindowsPlatform, LanguageId: WindowsEnglishUnitedStates } => 0,
        { PlatformId: WindowsPlatform } => 1,
        _ => 2,
    };
}
