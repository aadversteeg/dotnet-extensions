using System;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts.OpenType;
using Ave.Extensions.Documents.Fonts.Tables;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts;

/// <summary>Reads TrueType font files. Fonts are always explicit input: there is no lookup of system fonts.</summary>
public static class FontParser
{
    private const string GlyfTag = "glyf";

    /// <summary>
    /// Reads and validates a TrueType font file (sfnt version 0x00010000 with 'glyf' outlines). Every table the
    /// engine uses is checked against the OpenType specification here, once, so later layers can trust the
    /// font. Malformed data gives an error, never an exception.
    /// </summary>
    /// <param name="fontFile">The complete font file. Kept (not copied) by the returned font: do not change
    /// the underlying bytes afterwards.</param>
    /// <param name="callerFile">Filled in by the compiler: the file that loads the font, reported in errors.</param>
    /// <param name="callerLine">Filled in by the compiler: the line that loads the font, reported in errors.</param>
    /// <returns>The font, or a <see cref="FontErrorCodes.Parse"/> error carrying the caller's location.</returns>
    public static Result<TrueTypeFont, Error> Parse(
        ReadOnlyMemory<byte> fontFile,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        TableDirectory.Parse(fontFile)
            .OnSuccessBind(directory => ParseTables(fontFile, directory))
            .MapError(error => error.WithSource(new SourceLocation(callerFile, callerLine)));

    /// <summary>
    /// Parses the tables in dependency order, each step seeing the values of the steps before it: 'hmtx' needs counts
    /// from 'hhea' and 'maxp', 'cmap' is checked against the glyph count from 'maxp', 'loca' needs the offset format
    /// from 'head' and the length of 'glyf', and the layout tables are checked against the glyph count. The chain stops
    /// at the first malformed table, because later tables cannot be checked without the earlier ones.
    /// </summary>
    private static Result<TrueTypeFont, Error> ParseTables(ReadOnlyMemory<byte> fontFile, TableDirectory directory) =>
        SizeTables(directory)
            .OnSuccessCombine(size => GlyphTables(directory, size))
            .OnSuccessCombine((_, _) => DescriptionTables(directory))
            .OnSuccessCombine((size, _, _) => Layout(directory, size.Maxp.GlyphCount))
            .OnSuccessMap((size, glyphs, description, layout) => new TrueTypeFont(
                fontFile,
                description.Names,
                Metrics(size, description),
                new EmbeddingPermissions(description.Os2.FsType),
                glyphs.Cmap,
                glyphs.Hmtx.AdvanceWidths,
                glyphs.Loca.Offsets,
                glyphs.Glyf,
                layout));

    /// <summary>'head', 'hhea' and 'maxp': the sizes and counts every other table is checked against.</summary>
    private sealed record SizeTableSet(HeadTable Head, HheaTable Hhea, MaxpTable Maxp);

    /// <summary>'hmtx', 'cmap', 'glyf' and 'loca': from a character to a glyph, its advance and its outline.</summary>
    private sealed record GlyphTableSet(HmtxTable Hmtx, FrozenDictionary<int, GlyphId> Cmap, ReadOnlyMemory<byte> Glyf, LocaTable Loca);

    /// <summary>'OS/2', 'name' and 'post': what the font says about itself (weight, licence, names, italic angle).</summary>
    private sealed record DescriptionTableSet(Os2Table Os2, FontNames Names, PostTable Post);

    private static Result<SizeTableSet, Error> SizeTables(TableDirectory directory) =>
        Table(directory, HeadTable.Tag, HeadTable.Parse)
            .OnSuccessCombine(_ => Table(directory, HheaTable.Tag, HheaTable.Parse))
            .OnSuccessCombine((_, _) => Table(directory, MaxpTable.Tag, MaxpTable.Parse))
            .OnSuccessMap((head, hhea, maxp) => new SizeTableSet(head, hhea, maxp));

    private static Result<GlyphTableSet, Error> GlyphTables(TableDirectory directory, SizeTableSet size)
    {
        var glyphCount = size.Maxp.GlyphCount;
        return Table(directory, HmtxTable.Tag, table => HmtxTable.Parse(table, size.Hhea.NumberOfHMetrics, glyphCount))
            .OnSuccessCombine(_ => Table(directory, CmapTable.Tag, table => CmapTable.Parse(table, glyphCount)))
            .OnSuccessCombine((_, _) => directory.Required(GlyfTag))
            .OnSuccessCombine((_, _, glyf) => Table(directory, LocaTable.Tag, table => LocaTable.Parse(table, size.Head.LongLocaOffsets, glyphCount, glyf.Length)))
            .OnSuccessMap((hmtx, cmap, glyf, loca) => new GlyphTableSet(hmtx, cmap, glyf, loca));
    }

    private static Result<DescriptionTableSet, Error> DescriptionTables(TableDirectory directory) =>
        Table(directory, Os2Table.Tag, Os2Table.Parse)
            .OnSuccessCombine(_ => Table(directory, NameTable.Tag, NameTable.Parse))
            .OnSuccessCombine((_, _) => Table(directory, PostTable.Tag, PostTable.Parse))
            .OnSuccessMap((os2, names, post) => new DescriptionTableSet(os2, names, post));

    private static FontMetrics Metrics(SizeTableSet size, DescriptionTableSet description) => new(
        UnitsPerEm: size.Head.UnitsPerEm,
        Ascender: size.Hhea.Ascender,
        Descender: size.Hhea.Descender,
        LineGap: size.Hhea.LineGap,
        CapHeight: description.Os2.CapHeight,
        BoundingBox: size.Head.BoundingBox,
        ItalicAngle: description.Post.ItalicAngle,
        IsFixedPitch: description.Post.IsFixedPitch,
        WeightClass: description.Os2.WeightClass,
        IsItalic: description.Os2.IsItalic);

    /// <summary>A table the reader needs: absent is <c>Documents.Font.Parse.MissingTable</c>, present is parsed.</summary>
    private static Result<T, Error> Table<T>(TableDirectory directory, string tag, TableParser<T> parse) =>
        directory.Required(tag).OnSuccessBind(table => parse(table.Span));

    /// <summary>A table parser: reads one table's bytes, which are only borrowed for the call.</summary>
    private delegate Result<T, Error> TableParser<T>(ReadOnlySpan<byte> table);

    /// <summary>
    /// The optional advanced typography tables GSUB, GPOS, GDEF and 'kern'. A table the font does not have reads as
    /// empty; a table it has must be well formed, like every other table.
    /// </summary>
    private static Result<OpenTypeLayout, Error> Layout(TableDirectory directory, int glyphCount) =>
        Optional(directory, LayoutTableParser.Gsub, LayoutTable.Empty, data => LayoutTableParser.Parse(LayoutTableParser.Gsub, data, glyphCount))
            .OnSuccessCombine(_ => Optional(directory, LayoutTableParser.Gpos, LayoutTable.Empty, data => LayoutTableParser.Parse(LayoutTableParser.Gpos, data, glyphCount)))
            .OnSuccessCombine((_, _) => Optional<GlyphDefinitions?>(directory, GlyphDefinitions.Tag, null, data => GlyphDefinitions.Parse(data).OnSuccessMap(found => (GlyphDefinitions?)found)))
            .OnSuccessCombine((_, _, _) => Optional(directory, LegacyKerning.Tag, LegacyKerning.None, LegacyKerning.Parse))
            .OnSuccessMap((gsub, gpos, gdef, kern) => new OpenTypeLayout(gsub, gpos, gdef, kern));

    private static Result<T, Error> Optional<T>(TableDirectory directory, string tag, T absent, Func<ReadOnlyMemory<byte>, Result<T, Error>> parse) =>
        directory.Tables.TryGetValue(tag, out var data) ? parse(data) : Result<T, Error>.Success(absent);
}
