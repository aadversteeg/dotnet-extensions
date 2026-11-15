using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;

namespace Ave.Documents.Typesetting.Shaping;

/// <summary>
/// Which glyphs a lookup skips (OpenType spec, "Lookup table" → LookupFlag), and the class each glyph has. Both
/// substitution and positioning use it.
/// </summary>
/// <param name="Definitions">The font's GDEF table, or null when it has none.</param>
internal sealed record GlyphFilter(GlyphDefinitions? Definitions)
{
    /// <summary>
    /// The class of a glyph. With a GDEF table, its glyphClassDef decides. Without one, the font says nothing, and
    /// the character decides: a glyph for a nonspacing mark (General_Category Mn) is a mark, every other glyph a
    /// base. This is what HarfBuzz does for such fonts, and it is the only way a mark-to-base lookup can find the
    /// base in a font without GDEF (Lato has none).
    /// </summary>
    public GlyphClass ClassOf(GlyphId glyph, bool isNonspacingMarkCharacter) =>
        Definitions is { } definitions
            ? definitions.GlyphClasses.ClassOf(glyph) switch
            {
                GlyphDefinitions.MarkClass => GlyphClass.Mark,
                GlyphDefinitions.LigatureClass => GlyphClass.Ligature,
                _ => GlyphClass.Base,
            }
            : isNonspacingMarkCharacter ? GlyphClass.Mark : GlyphClass.Base;

    /// <summary>The class of a glyph a substitution produced: from GDEF when present; otherwise the class it
    /// replaces, or Ligature for a ligature.</summary>
    public GlyphClass ClassAfterSubstitution(GlyphId glyph, GlyphClass previous) =>
        Definitions is null ? previous : ClassOf(glyph, isNonspacingMarkCharacter: false);

    /// <summary>
    /// True when a lookup with these flags skips the glyph: "IgnoreBaseGlyphs", "IgnoreLigatures" and
    /// "IgnoreMarks" skip glyphs of that class; a MarkAttachmentType skips marks of any other attachment class.
    /// </summary>
    public bool Skips(LookupFlags flags, GlyphItem item) => item.Class switch
    {
        GlyphClass.Base => flags.IgnoreBaseGlyphs,
        GlyphClass.Ligature => flags.IgnoreLigatures,
        _ => flags.IgnoreMarks
            || (flags.MarkAttachmentType != 0
                && (Definitions?.MarkAttachmentClasses.ClassOf(item.Glyph) ?? 0) != flags.MarkAttachmentType),
    };
}
