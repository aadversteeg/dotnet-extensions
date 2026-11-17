using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;
using Ave.Documents.Core;
using Ave.Documents.Fonts.OpenType;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts;

/// <summary>
/// A parsed TrueType font: the original file (for embedding) plus the tables the engine needs, already
/// validated. Immutable. Create one with <see cref="FontParser.Parse"/>.
/// </summary>
/// <remarks>
/// Two instances are equal when their files are byte-identical (same <see cref="Id"/>), however they were loaded.
/// </remarks>
public sealed class TrueTypeFont : IEquatable<TrueTypeFont>
{
    private readonly FrozenDictionary<int, GlyphId> _characterMap;
    private readonly ImmutableArray<int> _advanceWidths;
    private readonly ImmutableArray<int> _glyphOffsets;
    private readonly ReadOnlyMemory<byte> _glyphData;

    internal TrueTypeFont(
        ReadOnlyMemory<byte> fileData,
        FontNames names,
        FontMetrics metrics,
        EmbeddingPermissions embedding,
        FrozenDictionary<int, GlyphId> characterMap,
        ImmutableArray<int> advanceWidths,
        ImmutableArray<int> glyphOffsets,
        ReadOnlyMemory<byte> glyphData,
        OpenTypeLayout layout)
    {
        Id = FontId.Of(fileData.Span);
        FileData = fileData;
        Names = names;
        Metrics = metrics;
        Embedding = embedding;
        _characterMap = characterMap;
        _advanceWidths = advanceWidths;
        _glyphOffsets = glyphOffsets;
        _glyphData = glyphData;
        Layout = layout;
    }

    /// <summary>The content digest of the font file.</summary>
    public FontId Id { get; }

    /// <summary>The complete, unmodified font file, as embedded in PDF output.</summary>
    public ReadOnlyMemory<byte> FileData { get; }

    /// <summary>The font's names.</summary>
    public FontNames Names { get; }

    /// <summary>The font-wide metrics.</summary>
    public FontMetrics Metrics { get; }

    /// <summary>What the font's licence allows for embedding.</summary>
    public EmbeddingPermissions Embedding { get; }

    /// <summary>The advanced typography tables (GSUB, GPOS, GDEF, 'kern') that shaping uses.</summary>
    public OpenTypeLayout Layout { get; }

    /// <summary>The number of glyphs ('maxp' numGlyphs); valid glyph ids are 0 to GlyphCount − 1.</summary>
    public int GlyphCount => _advanceWidths.Length;

    /// <summary>
    /// The glyph the font's 'cmap' assigns to a character, or None when the font has no glyph for it.
    /// </summary>
    /// <param name="character">The Unicode scalar value.</param>
    /// <returns>The glyph id, or None.</returns>
    public Maybe<GlyphId> GlyphFor(Rune character) =>
        _characterMap.TryGetValue(character.Value, out var glyph) ? glyph : Maybe<GlyphId>.None;

    /// <summary>
    /// The glyph the font's 'cmap' assigns to a character, or the error <see cref="FontErrorCodes.GlyphMissing"/>
    /// that names the font and the character: for text that must be shown, a missing glyph is never dropped.
    /// </summary>
    /// <param name="character">The Unicode scalar value.</param>
    /// <returns>The glyph id, or the error.</returns>
    public Result<GlyphId, Error> RequireGlyph(Rune character) =>
        GlyphFor(character).Match(
            Result<GlyphId, Error>.Success,
            () => Result<GlyphId, Error>.Failure(
                new Error(FontErrorCodes.GlyphMissing, $"Font '{Names.PostScriptName}' has no glyph for '{character}' (U+{character.Value:X4}).")
                    .With(ErrorMetadataKeys.FontName, Names.PostScriptName)
                    .With(ErrorMetadataKeys.Character, character.ToString())
                    .With(ErrorMetadataKeys.CodePoint, $"U+{character.Value:X4}")));

    /// <summary>The horizontal advance of a glyph in font design units ('hmtx').</summary>
    /// <param name="glyph">A glyph id of this font.</param>
    /// <returns>The advance width.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The glyph id does not belong to this font. Glyph ids come
    /// from this font's own <see cref="GlyphFor"/>, so this is a bug in the caller.</exception>
    public int AdvanceWidth(GlyphId glyph) => _advanceWidths[CheckedIndex(glyph)];

    /// <summary>
    /// The raw outline data of a glyph from 'glyf' (located through 'loca'); empty for glyphs without an
    /// outline, such as the space. Not interpreted: the subsetter copies it as it is
    /// (<see cref="FontSubsetter"/>), and only a raster back-end would need to decode it.
    /// </summary>
    /// <param name="glyph">A glyph id of this font.</param>
    /// <returns>The glyph's bytes in 'glyf'.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The glyph id does not belong to this font.</exception>
    public ReadOnlyMemory<byte> GlyphOutline(GlyphId glyph)
    {
        var index = CheckedIndex(glyph);
        return _glyphData[_glyphOffsets[index].._glyphOffsets[index + 1]];
    }

    /// <summary>
    /// The bounding box of a glyph's outline, from its glyph header in 'glyf' (OpenType spec, "glyf — Glyph data":
    /// xMin, yMin, xMax, yMax, "for coordinate data"; for a composite glyph the box of the whole glyph), in design units
    /// with y up from the baseline. None for a glyph without an outline, such as the space: it puts no ink on the page.
    /// The parser has checked that every outline holds its header (<see cref="Tables.LocaTable.GlyphHeaderLength"/>).
    /// </summary>
    /// <param name="glyph">A glyph id of this font.</param>
    /// <returns>The box, or None.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The glyph id does not belong to this font.</exception>
    public Maybe<FontBoundingBox> GlyphBounds(GlyphId glyph)
    {
        var outline = GlyphOutline(glyph).Span;
        return outline.IsEmpty
            ? Maybe<FontBoundingBox>.None
            : new FontBoundingBox(
                Tables.BigEndian.Int16(outline, 2),
                Tables.BigEndian.Int16(outline, 4),
                Tables.BigEndian.Int16(outline, 6),
                Tables.BigEndian.Int16(outline, 8));
    }

    /// <summary>Equal when the font files are byte-identical.</summary>
    /// <param name="other">The other font.</param>
    /// <returns>Whether both are the same font file.</returns>
    public bool Equals(TrueTypeFont? other) => other is not null && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrueTypeFont other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>Returns the PostScript name, for example <c>Lato-Bold</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Names.PostScriptName;

    private int CheckedIndex(GlyphId glyph)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(glyph.Value, GlyphCount, nameof(glyph));
        return glyph.Value;
    }
}
