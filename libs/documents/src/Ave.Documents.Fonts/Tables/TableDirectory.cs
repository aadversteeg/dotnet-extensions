using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// The table directory at the start of an OpenType/TrueType file: which tables exist and where their bytes are
/// (OpenType spec, "Organization of an OpenType font" → "Table directory").
/// </summary>
/// <param name="Tables">Each table's bytes, by tag.</param>
internal sealed record TableDirectory(FrozenDictionary<string, ReadOnlyMemory<byte>> Tables)
{
    private const int HeaderLength = 12;
    private const int RecordLength = 16;

    // sfntVersion values (OpenType spec, "Table directory"): 0x00010000 for TrueType outlines; Apple also
    // uses the tag 'true' for TrueType fonts. 'OTTO' marks CFF outlines and 'ttcf' a font collection, which a
    // later stage reads.
    private const uint TrueTypeVersion = 0x00010000;
    private const uint AppleTrueTypeTag = 0x74727565; // 'true'
    private const uint CffTag = 0x4F54544F; // 'OTTO'
    private const uint CollectionTag = 0x74746366; // 'ttcf'

    /// <summary>Reads the directory and checks that every table lies inside the file.</summary>
    public static Result<TableDirectory, Error> Parse(ReadOnlyMemory<byte> file)
    {
        var data = file.Span;
        if (data.Length < HeaderLength)
        {
            return FontParseErrors.Truncated<TableDirectory>("table directory", HeaderLength, data.Length);
        }

        var version = BigEndian.UInt32(data, 0);
        switch (version)
        {
            case TrueTypeVersion or AppleTrueTypeTag:
                break;
            case CffTag:
                return FontParseErrors.UnsupportedFormat<TableDirectory>(
                    "OpenType fonts with CFF outlines are not supported yet; use a TrueType (glyf) font.", "OTTO");
            case CollectionTag:
                return FontParseErrors.UnsupportedFormat<TableDirectory>(
                    "Font collections (.ttc) are not supported; pass a single font file.", "ttcf");
            default:
                return FontParseErrors.UnsupportedFormat<TableDirectory>(
                    "The data is not a TrueType font file.", $"0x{version:X8}");
        }

        int tableCount = BigEndian.UInt16(data, 4);
        var directoryLength = HeaderLength + (long)tableCount * RecordLength;
        if (data.Length < directoryLength)
        {
            return FontParseErrors.Truncated<TableDirectory>("table directory", directoryLength, data.Length);
        }

        var tables = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        for (var index = 0; index < tableCount; index++)
        {
            var record = HeaderLength + index * RecordLength;
            var tag = BigEndian.Tag(data, record);
            long offset = BigEndian.UInt32(data, record + 8);
            long length = BigEndian.UInt32(data, record + 12);
            if (offset + length > data.Length)
            {
                return FontParseErrors.Truncated<TableDirectory>(tag, offset + length, data.Length);
            }

            // A tag listed twice is malformed; the first record wins, the way the directory is searched.
            tables.TryAdd(tag, file.Slice((int)offset, (int)length));
        }

        return Result<TableDirectory, Error>.Success(new TableDirectory(tables.ToFrozenDictionary(StringComparer.Ordinal)));
    }

    /// <summary>The bytes of a required table, or a MissingTable error.</summary>
    public Result<ReadOnlyMemory<byte>, Error> Required(string tag) =>
        Tables.TryGetValue(tag, out var table)
            ? Result<ReadOnlyMemory<byte>, Error>.Success(table)
            : FontParseErrors.MissingTable<ReadOnlyMemory<byte>>(tag);
}
