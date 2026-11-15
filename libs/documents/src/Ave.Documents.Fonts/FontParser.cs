using System;
using System.Runtime.CompilerServices;
using Ave.Documents.Core;
using Ave.Documents.Fonts.Tables;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts;

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
    /// <returns>The font, or a <see cref="DocumentErrorCodes.Font.Parse"/> error carrying the caller's location.</returns>
    public static Result<TrueTypeFont, Error> Parse(
        ReadOnlyMemory<byte> fontFile,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        TableDirectory.Parse(fontFile)
            .OnSuccessBind(directory => ParseTables(fontFile, directory))
            .MapError(error => error.WithSource(new SourceLocation(callerFile, callerLine)));

    /// <summary>
    /// Parses the tables in dependency order: 'hmtx' needs counts from 'hhea' and 'maxp', 'loca' needs the
    /// offset format from 'head', and 'cmap' is checked against the glyph count from 'maxp'. Each step stops at
    /// the first malformed table, because later tables cannot be checked without the earlier ones.
    /// </summary>
    private static Result<TrueTypeFont, Error> ParseTables(ReadOnlyMemory<byte> fontFile, TableDirectory directory)
    {
        var head = directory.Required(HeadTable.Tag).OnSuccessBind(table => HeadTable.Parse(table.Span));
        if (head.IsFailure)
        {
            return Fail(head.Error);
        }

        var hhea = directory.Required(HheaTable.Tag).OnSuccessBind(table => HheaTable.Parse(table.Span));
        if (hhea.IsFailure)
        {
            return Fail(hhea.Error);
        }

        var maxp = directory.Required(MaxpTable.Tag).OnSuccessBind(table => MaxpTable.Parse(table.Span));
        if (maxp.IsFailure)
        {
            return Fail(maxp.Error);
        }

        var glyphCount = maxp.Value.GlyphCount;
        var hmtx = directory.Required(HmtxTable.Tag)
            .OnSuccessBind(table => HmtxTable.Parse(table.Span, hhea.Value.NumberOfHMetrics, glyphCount));
        if (hmtx.IsFailure)
        {
            return Fail(hmtx.Error);
        }

        var cmap = directory.Required(CmapTable.Tag).OnSuccessBind(table => CmapTable.Parse(table.Span, glyphCount));
        if (cmap.IsFailure)
        {
            return Fail(cmap.Error);
        }

        var os2 = directory.Required(Os2Table.Tag).OnSuccessBind(table => Os2Table.Parse(table.Span));
        if (os2.IsFailure)
        {
            return Fail(os2.Error);
        }

        var names = directory.Required(NameTable.Tag).OnSuccessBind(table => NameTable.Parse(table.Span));
        if (names.IsFailure)
        {
            return Fail(names.Error);
        }

        var post = directory.Required(PostTable.Tag).OnSuccessBind(table => PostTable.Parse(table.Span));
        if (post.IsFailure)
        {
            return Fail(post.Error);
        }

        var glyf = directory.Required(GlyfTag);
        if (glyf.IsFailure)
        {
            return Fail(glyf.Error);
        }

        var loca = directory.Required(LocaTable.Tag)
            .OnSuccessBind(table => LocaTable.Parse(table.Span, head.Value.LongLocaOffsets, glyphCount, glyf.Value.Length));
        if (loca.IsFailure)
        {
            return Fail(loca.Error);
        }

        var metrics = new FontMetrics(
            UnitsPerEm: head.Value.UnitsPerEm,
            Ascender: hhea.Value.Ascender,
            Descender: hhea.Value.Descender,
            LineGap: hhea.Value.LineGap,
            CapHeight: os2.Value.CapHeight,
            BoundingBox: head.Value.BoundingBox,
            ItalicAngle: post.Value.ItalicAngle,
            IsFixedPitch: post.Value.IsFixedPitch,
            WeightClass: os2.Value.WeightClass);

        return Result<TrueTypeFont, Error>.Success(new TrueTypeFont(
            fontFile,
            names.Value,
            metrics,
            new EmbeddingPermissions(os2.Value.FsType),
            cmap.Value,
            hmtx.Value.AdvanceWidths,
            loca.Value.Offsets,
            glyf.Value));
    }

    private static Result<TrueTypeFont, Error> Fail(Error error) => Result<TrueTypeFont, Error>.Failure(error);
}
