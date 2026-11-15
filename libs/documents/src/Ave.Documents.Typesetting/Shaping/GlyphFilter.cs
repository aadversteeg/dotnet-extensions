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
    /// The class of a glyph. When GDEF has a glyph class definition, it decides. Otherwise (no GDEF, or a GDEF that
    /// holds only other parts) the font says nothing, and the character decides: a glyph for a nonspacing mark
    /// (General_Category Mn) is a mark, every other glyph a base. HarfBuzz does the same for such fonts: it
    /// synthesises glyph classes when the font has none (hb-ot-shape.cc, under the condition
    /// <c>!hb_ot_layout_has_glyph_classes</c>). It is also the only way a mark-to-base lookup can find the base in a
    /// font without GDEF (Lato has none).
    /// </summary>
    public GlyphClass ClassOf(GlyphId glyph, bool isNonspacingMarkCharacter) =>
        Definitions is { HasGlyphClasses: true } definitions
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
        Definitions is { HasGlyphClasses: true } ? ClassOf(glyph, isNonspacingMarkCharacter: false) : previous;

    /// <summary>
    /// True when a substitution lookup with these flags skips the glyph while matching: as <see cref="Skips"/>, and a
    /// default-ignorable character, so that it does not stop a ligature (glyphs the match skips are kept after the
    /// ligature). HarfBuzz skips every default ignorable but the zero width non-joiner while matching GSUB
    /// (hb-ot-layout-gsubgpos.hh, the skipping iterator), and its output for "f", an ignorable and "i" in Lato is the
    /// fi ligature followed by the invisible glyph; the reference tests compare. Two exceptions:
    /// <list type="bullet">
    /// <item>The zero width non-joiner is matched, so it stops a ligature: it is "intended to break both cursive
    /// connections and ligatures" (the Unicode Standard §23.2). HarfBuzz does the same.</item>
    /// <item>The soft hyphen and the zero width space are matched too, so they stop a ligature. Here the engine does not
    /// do what HarfBuzz does, on purpose: they are line break opportunities, and the engine does not shape a line
    /// again where it breaks (HarfBuzz leaves that to its caller). A ligature across one would keep both letters on
    /// one line whichever way the line breaks.</item>
    /// </list>
    /// </summary>
    public bool SkipsInSubstitution(LookupFlags flags, GlyphItem item) =>
        (item.IsDefaultIgnorable && !item.IsLineBreak && item.Text is not (ZeroWidthNonJoiner or SoftHyphen or ZeroWidthSpace)) || Skips(flags, item);

    /// <summary>U+200C ZERO WIDTH NON-JOINER.</summary>
    private const string ZeroWidthNonJoiner = "\u200C";

    /// <summary>U+00AD SOFT HYPHEN.</summary>
    private const string SoftHyphen = "\u00AD";

    /// <summary>U+200B ZERO WIDTH SPACE.</summary>
    private const string ZeroWidthSpace = "\u200B";

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
