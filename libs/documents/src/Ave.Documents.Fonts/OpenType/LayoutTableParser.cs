using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Ave.Documents.Core;
using Ave.Documents.Fonts.Tables;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// Reads a GSUB or GPOS table (OpenType spec, "GSUB — Glyph substitution table", "GPOS — Glyph positioning table").
/// Both start with the same header — majorVersion 1, minorVersion, then 16-bit offsets to the script list, the
/// feature list and the lookup list — and differ only in their lookup types.
/// </summary>
internal static class LayoutTableParser
{
    /// <summary>The GSUB table tag.</summary>
    public const string Gsub = "GSUB";

    /// <summary>The GPOS table tag.</summary>
    public const string Gpos = "GPOS";

    // Lookup types (GSUB spec "Lookup type enumeration"; GPOS spec "Lookup type enumeration").
    private const int GsubSingle = 1, GsubMultiple = 2, GsubLigature = 4, GsubExtension = 7;
    private const int GposPair = 2, GposMarkToBase = 4, GposExtension = 9;

    /// <summary>Reads the table; every glyph id it produces must be below <paramref name="glyphCount"/>.</summary>
    public static Result<LayoutTable, Error> Parse(string tag, ReadOnlyMemory<byte> data, int glyphCount)
    {
        var reader = new TableReader(data);
        if (reader.UInt16(0) != 1)
        {
            reader.Invalid($"majorVersion {reader.UInt16(0)}");
        }

        var table = new LayoutTable(
            Scripts(reader, reader.UInt16(4)),
            Features(reader, reader.UInt16(6)),
            Lookups(reader, reader.UInt16(8), tag, glyphCount));

        return reader.FirstOutOfRange is { } offset
            ? FontParseErrors.Truncated<LayoutTable>(tag, offset, reader.Length)
            : reader.FirstInvalid is { } problem
                ? FontParseErrors.InvalidValue<LayoutTable>(tag, problem, problem)
                : Result<LayoutTable, Error>.Success(table);
    }

    /// <summary>Script list: "scriptCount, scriptRecords[]" of (scriptTag, scriptOffset); a script table:
    /// "defaultLangSysOffset, langSysCount, langSysRecords[]". Language-specific systems are not read: stage 1
    /// shapes with the default language system.</summary>
    private static ValueList<ScriptTable> Scripts(TableReader reader, int list) =>
        [.. Enumerable.Range(0, reader.Count(list, list + 2, 6)).Select(index =>
        {
            var record = list + 2 + index * 6;
            var script = list + reader.UInt16(record + 4);
            var defaultOffset = reader.UInt16(script);
            return new ScriptTable(reader.Tag(record), defaultOffset == 0 ? null : LanguageSystemAt(reader, script + defaultOffset));
        })];

    /// <summary>Language system: "lookupOrderOffset (reserved), requiredFeatureIndex (0xFFFF if none),
    /// featureIndexCount, featureIndices[]".</summary>
    private static LanguageSystem LanguageSystemAt(TableReader reader, int offset)
    {
        var required = reader.UInt16(offset + 2);
        return new LanguageSystem(
            required == 0xFFFF ? null : required,
            [.. Enumerable.Range(0, reader.Count(offset + 4, offset + 6, 2)).Select(index => (int)reader.UInt16(offset + 6 + index * 2))]);
    }

    /// <summary>Feature list: "featureCount, featureRecords[]" of (featureTag, featureOffset); a feature:
    /// "featureParamsOffset, lookupIndexCount, lookupListIndices[]".</summary>
    private static ValueList<FeatureTable> Features(TableReader reader, int list) =>
        [.. Enumerable.Range(0, reader.Count(list, list + 2, 6)).Select(index =>
        {
            var record = list + 2 + index * 6;
            var feature = list + reader.UInt16(record + 4);
            return new FeatureTable(
                reader.Tag(record),
                [.. Enumerable.Range(0, reader.Count(feature + 2, feature + 4, 2)).Select(lookup => (int)reader.UInt16(feature + 4 + lookup * 2))]);
        })];

    /// <summary>Lookup list: "lookupCount, lookupOffsets[]"; a lookup: "lookupType, lookupFlag, subTableCount,
    /// subtableOffsets[]" (and a markFilteringSet, which is not used).</summary>
    private static ValueList<Lookup> Lookups(TableReader reader, int list, string tag, int glyphCount) =>
        [.. Enumerable.Range(0, reader.Count(list, list + 2, 2)).Select(index =>
        {
            var lookup = list + reader.UInt16(list + 2 + index * 2);
            var type = reader.UInt16(lookup);
            var subtables = Enumerable.Range(0, reader.Count(lookup + 4, lookup + 6, 2))
                .Select(subtable => Subtable(reader, tag, type, lookup + reader.UInt16(lookup + 6 + subtable * 2), glyphCount));
            return new Lookup(index, new LookupFlags(reader.UInt16(lookup + 2)), [.. subtables]);
        })];

    /// <summary>
    /// One subtable, decoded by lookup type and format. An extension subtable (GSUB type 7, GPOS type 9: "format,
    /// extensionLookupType, extensionOffset32") is replaced by the subtable it points to; an extension may not
    /// point to another extension ("must not be 7" / "must not be 9"), so this never recurses further.
    /// </summary>
    private static LookupSubtable Subtable(TableReader reader, string tag, int type, int offset, int glyphCount)
    {
        var isGsub = tag == Gsub;
        if (type == (isGsub ? GsubExtension : GposExtension))
        {
            var target = offset + (long)reader.UInt32(offset + 4);
            var innerType = reader.UInt16(offset + 2);
            if (!reader.Fits(0, target))
            {
                return new UnsupportedSubtable(tag, type, reader.UInt16(offset));
            }

            if (innerType == type)
            {
                reader.Invalid($"extension subtable at {offset}");
                return new UnsupportedSubtable(tag, type, reader.UInt16(offset));
            }

            return Subtable(reader, tag, innerType, (int)target, glyphCount);
        }

        var format = reader.UInt16(offset);
        return (isGsub, type, format) switch
        {
            (true, GsubSingle, 1 or 2) => SingleSubstitutionAt(reader, offset, format, glyphCount),
            (true, GsubMultiple, 1) => MultipleSubstitutionAt(reader, offset, glyphCount),
            (true, GsubLigature, 1) => LigatureSubstitutionAt(reader, offset, glyphCount),
            (false, GposPair, 1) => PairByGlyphAt(reader, offset),
            (false, GposPair, 2) => PairByClassAt(reader, offset),
            (false, GposMarkToBase, 1) => MarkToBaseAt(reader, offset),
            _ => new UnsupportedSubtable(tag, type, format),
        };
    }

    /// <summary>The covered glyphs of a coverage table, each with its coverage index.</summary>
    private static IEnumerable<(ushort Glyph, int Index)> Covered(TableReader reader, int offset) =>
        Coverage.Read(reader, offset).Indices.Select(entry => (entry.Key, entry.Value));

    /// <summary>A glyph id a substitution produces; must exist in the font.</summary>
    private static GlyphId Output(TableReader reader, int glyph, int glyphCount)
    {
        if (glyph >= glyphCount)
        {
            reader.Invalid($"substitution to glyph {glyph}, beyond maxp.numGlyphs {glyphCount}");
        }

        return new GlyphId((ushort)glyph);
    }

    /// <summary>Single substitution format 1: "coverageOffset, deltaGlyphID" — "Add to original glyph ID to get
    /// substitute glyph ID", modulo 65536. Format 2: "coverageOffset, glyphCount, substituteGlyphIDs[]", indexed by
    /// coverage index.</summary>
    private static SingleSubstitution SingleSubstitutionAt(TableReader reader, int offset, int format, int glyphCount)
    {
        var delta = reader.Int16(offset + 4);
        return new SingleSubstitution(Covered(reader, offset + reader.UInt16(offset + 2)).ToFrozenDictionary(
            covered => covered.Glyph,
            covered => Output(reader, format == 1 ? (covered.Glyph + delta) & 0xFFFF : reader.UInt16(offset + 6 + covered.Index * 2), glyphCount)));
    }

    /// <summary>Multiple substitution format 1: "coverageOffset, sequenceCount, sequenceOffsets[]"; a sequence:
    /// "glyphCount, substituteGlyphIDs[]".</summary>
    private static MultipleSubstitution MultipleSubstitutionAt(TableReader reader, int offset, int glyphCount) =>
        new(Covered(reader, offset + reader.UInt16(offset + 2)).ToFrozenDictionary(
            covered => covered.Glyph,
            covered =>
            {
                var sequence = offset + reader.UInt16(offset + 6 + covered.Index * 2);
                return (ValueList<GlyphId>)[.. Enumerable.Range(0, reader.Count(sequence, sequence + 2, 2))
                    .Select(index => Output(reader, reader.UInt16(sequence + 2 + index * 2), glyphCount))];
            }));

    /// <summary>Ligature substitution format 1: "coverageOffset, ligatureSetCount, ligatureSetOffsets[]"; a ligature
    /// set: "ligatureCount, ligatureOffsets[]"; a ligature: "ligatureGlyph, componentCount,
    /// componentGlyphIDs[componentCount − 1]".</summary>
    private static LigatureSubstitution LigatureSubstitutionAt(TableReader reader, int offset, int glyphCount) =>
        new(Covered(reader, offset + reader.UInt16(offset + 2)).ToFrozenDictionary(
            covered => covered.Glyph,
            covered =>
            {
                var set = offset + reader.UInt16(offset + 6 + covered.Index * 2);
                return (ValueList<Ligature>)[.. Enumerable.Range(0, reader.Count(set, set + 2, 2)).Select(index =>
                {
                    var ligature = set + reader.UInt16(set + 2 + index * 2);
                    var components = Math.Max(0, reader.UInt16(ligature + 2) - 1);
                    components = reader.Fits(ligature + 4, components * 2L) ? components : 0;
                    return new Ligature(
                        Output(reader, reader.UInt16(ligature), glyphCount),
                        [.. Enumerable.Range(0, components).Select(component => new GlyphId(reader.UInt16(ligature + 4 + component * 2)))]);
                })];
            }));

    /// <summary>Pair adjustment format 1: "coverageOffset, valueFormat1, valueFormat2, pairSetCount,
    /// pairSetOffsets[]"; a pair set: "pairValueCount, pairValueRecords[]" of (secondGlyph, valueRecord1,
    /// valueRecord2).</summary>
    private static PairAdjustmentByGlyph PairByGlyphAt(TableReader reader, int offset)
    {
        var (format1, format2) = (reader.UInt16(offset + 4), reader.UInt16(offset + 6));
        var recordLength = 2 + ValueRecordLength(format1) + ValueRecordLength(format2);
        var pairs = new Dictionary<(ushort, ushort), PairValues>();
        foreach (var (first, index) in Covered(reader, offset + reader.UInt16(offset + 2)))
        {
            var set = offset + reader.UInt16(offset + 10 + index * 2);
            var pairCount = reader.Count(set, set + 2, recordLength);
            for (var pair = 0; pair < pairCount; pair++)
            {
                var record = set + 2 + pair * recordLength;
                pairs.TryAdd((first, reader.UInt16(record)), new PairValues(
                    ValueRecordAt(reader, record + 2, format1),
                    ValueRecordAt(reader, record + 2 + ValueRecordLength(format1), format2)));
            }
        }

        return new PairAdjustmentByGlyph(pairs.ToFrozenDictionary(), format2 != 0);
    }

    /// <summary>Pair adjustment format 2: "coverageOffset, valueFormat1, valueFormat2, classDef1Offset,
    /// classDef2Offset, class1Count, class2Count, class1Records[class1Count]", each holding class2Count pairs of
    /// value records. When both value formats are 0 the records take no bytes and adjust nothing, so none is stored
    /// (the counts alone could otherwise ask for 65535 × 65535 records from a few bytes); otherwise every record lies
    /// inside the table, so their number is bounded by its size.</summary>
    private static PairAdjustmentByClass PairByClassAt(TableReader reader, int offset)
    {
        var (format1, format2) = (reader.UInt16(offset + 4), reader.UInt16(offset + 6));
        var (class1Count, class2Count) = (reader.UInt16(offset + 12), reader.UInt16(offset + 14));
        var pairLength = ValueRecordLength(format1) + ValueRecordLength(format2);
        var records = pairLength == 0 || !reader.Fits(offset + 16, (long)class1Count * class2Count * pairLength)
            ? 0
            : class1Count * class2Count;

        var values = Enumerable.Range(0, records).Select(index =>
        {
            var record = offset + 16 + index * pairLength;
            return new PairValues(ValueRecordAt(reader, record, format1), ValueRecordAt(reader, record + ValueRecordLength(format1), format2));
        });
        return new PairAdjustmentByClass(
            Coverage.Read(reader, offset + reader.UInt16(offset + 2)),
            ClassDefinition.Read(reader, offset + reader.UInt16(offset + 8)),
            ClassDefinition.Read(reader, offset + reader.UInt16(offset + 10)),
            [.. values],
            records == 0 ? 0 : class1Count,
            records == 0 ? 0 : class2Count,
            format2 != 0);
    }

    /// <summary>The length of a value record: two bytes per bit set in the low byte of its valueFormat.</summary>
    private static int ValueRecordLength(ushort format) => BitOperations.PopCount((uint)(format & 0xFF)) * 2;

    /// <summary>
    /// A value record: the fields present are those whose valueFormat bit is set, in the order XPlacement (0x0001),
    /// YPlacement (0x0002), XAdvance (0x0004), YAdvance (0x0008), then the four device offsets (0x0010–0x0080),
    /// which are skipped.
    /// </summary>
    private static ValueRecord ValueRecordAt(TableReader reader, int offset, ushort format)
    {
        var fields = new short[4];
        var position = offset;
        for (var bit = 0; bit < 4; bit++)
        {
            if ((format & (1 << bit)) != 0)
            {
                fields[bit] = reader.Int16(position);
                position += 2;
            }
        }

        return new ValueRecord(fields[0], fields[1], fields[2], fields[3]);
    }

    /// <summary>Mark-to-base format 1: "markCoverageOffset, baseCoverageOffset, markClassCount, markArrayOffset,
    /// baseArrayOffset". Mark array: "markCount, markRecords[]" of (markClass, markAnchorOffset); base array:
    /// "baseCount, baseRecords[]", each "baseAnchorOffsets[markClassCount]" (0 = no anchor for that class).</summary>
    private static MarkToBase MarkToBaseAt(TableReader reader, int offset)
    {
        var classCount = reader.UInt16(offset + 6);
        var markArray = offset + reader.UInt16(offset + 8);
        var baseArray = offset + reader.UInt16(offset + 10);
        if (!reader.Fits(baseArray + 2, (long)reader.UInt16(baseArray) * classCount * 2))
        {
            classCount = 0;
        }

        var marks = Covered(reader, offset + reader.UInt16(offset + 2)).ToFrozenDictionary(
            covered => covered.Glyph,
            covered =>
            {
                var record = markArray + 2 + covered.Index * 4;
                return ((int)reader.UInt16(record), AnchorAt(reader, markArray + reader.UInt16(record + 2)));
            });
        var bases = Covered(reader, offset + reader.UInt16(offset + 4)).ToFrozenDictionary(
            covered => covered.Glyph,
            covered => (ValueList<Anchor?>)[.. Enumerable.Range(0, classCount).Select(markClass =>
            {
                var anchor = reader.UInt16(baseArray + 2 + (covered.Index * classCount + markClass) * 2);
                return anchor == 0 ? (Anchor?)null : AnchorAt(reader, baseArray + anchor);
            })]);
        return new MarkToBase(marks, bases);
    }

    /// <summary>Anchor formats 1–3 all start "anchorFormat, xCoordinate, yCoordinate".</summary>
    private static Anchor AnchorAt(TableReader reader, int offset)
    {
        var format = reader.UInt16(offset);
        if (format is < 1 or > 3)
        {
            reader.Invalid($"anchor format {format}");
        }

        return new Anchor(reader.Int16(offset + 2), reader.Int16(offset + 4));
    }
}
