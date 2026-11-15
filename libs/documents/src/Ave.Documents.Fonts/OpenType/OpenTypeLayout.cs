namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// A font's advanced typography tables, as far as shaping needs them: glyph substitution (GSUB), glyph
/// positioning (GPOS), glyph definitions (GDEF) and the old kerning table ('kern'). All are optional in a
/// TrueType font; an absent table reads as empty.
/// </summary>
/// <param name="Substitutions">The GSUB table, or <see cref="LayoutTable.Empty"/>.</param>
/// <param name="Positions">The GPOS table, or <see cref="LayoutTable.Empty"/>.</param>
/// <param name="Definitions">The GDEF table, or null when the font has none.</param>
/// <param name="Kerning">The 'kern' table, or <see cref="LegacyKerning.None"/>.</param>
internal sealed record OpenTypeLayout(LayoutTable Substitutions, LayoutTable Positions, GlyphDefinitions? Definitions, LegacyKerning Kerning)
{
    /// <summary>A font with none of these tables.</summary>
    public static OpenTypeLayout None { get; } = new(LayoutTable.Empty, LayoutTable.Empty, null, LegacyKerning.None);
}
