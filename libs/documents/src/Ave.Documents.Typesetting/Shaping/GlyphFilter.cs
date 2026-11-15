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
    /// True when a substitution lookup with these flags skips the glyph while matching: as <see cref="Skips"/>, and
    /// always the zero width joiner, which therefore does not stop a ligature. Every other default-ignorable
    /// character is matched, so a zero width non-joiner between two letters stops their ligature ("ZWNJ … the
    /// characters are not to be connected", the Unicode Standard §23.2). This is HarfBuzz's rule (hb-ot-layout:
    /// GSUB matching ignores ZWJ, GPOS matching ignores ZWJ and ZWNJ, and both skip hidden default ignorables when
    /// positioning).
    /// </summary>
    public bool SkipsInSubstitution(LookupFlags flags, GlyphItem item) => item.Text == GlyphItem.ZeroWidthJoiner || Skips(flags, item);

    /// <summary>True when a positioning lookup with these flags skips the glyph: as <see cref="Skips"/>, and every
    /// default-ignorable character, which takes no space and so has no place in a kerning pair or under a mark.</summary>
    public bool SkipsInPositioning(LookupFlags flags, GlyphItem item) => item.IsDefaultIgnorable || Skips(flags, item);

    /// <summary>
    /// True when a lookup with these flags skips the glyph: "IgnoreBaseGlyphs", "IgnoreLigatures" and
    /// "IgnoreMarks" skip glyphs of that class; a MarkAttachmentType skips marks of any other attachment class.
    /// </summary>
    private bool Skips(LookupFlags flags, GlyphItem item) => item.Class switch
    {
        GlyphClass.Base => flags.IgnoreBaseGlyphs,
        GlyphClass.Ligature => flags.IgnoreLigatures,
        _ => flags.IgnoreMarks
            || (flags.MarkAttachmentType != 0
                && (Definitions?.MarkAttachmentClasses.ClassOf(item.Glyph) ?? 0) != flags.MarkAttachmentType),
    };
}
