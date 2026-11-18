using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.OpenType;

namespace Ave.Extensions.Documents.Typesetting.Shaping;

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
        Definitions is { GlyphClasses: { } classes }
            ? classes.ClassOf(glyph) switch
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
    /// ligature, in its cluster). HarfBuzz 15.0.0, the reference the tests compare with, skips a default ignorable while matching GSUB
    /// (hb-ot-layout-gsubgpos.hh, <c>matcher_t::may_skip</c>) unless it is the zero width non-joiner, the zero width
    /// joiner of a lookup that does not skip it, or a glyph HarfBuzz marks hidden (hb-ot-layout.hh,
    /// <c>UPROPS_MASK_HIDDEN</c>: U+180B–U+180D and U+180F, the Mongolian free variation selectors; U+E0020–U+E007F, the
    /// tag characters; and U+034F COMBINING GRAPHEME JOINER where it blocks a reordering). Its output for "f", an
    /// ignorable and "i" in Lato is the fi ligature followed by the invisible glyph. Where the engine differs from
    /// HarfBuzz: it skips the hidden ones too (f, a tag character and i ligate here, not in HarfBuzz); and a default
    /// ignorable of another script (U+180E, U+061C, U+17B4) ends the script run here (<see cref="Shaper"/>), so no
    /// ligature joins across it, where HarfBuzz, given the whole text as one run, ligates. One exception, as in
    /// HarfBuzz: the zero width non-joiner is matched, so it stops a ligature: it is "intended to break both cursive
    /// connections and ligatures" (the Unicode Standard §23.2). The soft hyphen and the zero width space are skipped like
    /// every other default ignorable, so a ligature forms across them as in HarfBuzz; where a line breaks at one of them,
    /// the line breaker shapes both lines again (<see cref="GreedyLineBreaker"/>), which splits the ligature.
    /// </summary>
    /// <remarks>A forced line break counts as a default ignorable here too: the shaper cuts the text after every one
    /// (<see cref="Shaper"/>), so it is the last glyph of its piece and nothing follows it that a match could join to it,
    /// and as a glyph the lookup skips it is not substituted either.</remarks>
    public bool SkipsInSubstitution(LookupFlags flags, GlyphItem item) =>
        (item.IsDefaultIgnorable && item.Text != ZeroWidthNonJoiner) || Skips(flags, item);

    /// <summary>U+200C ZERO WIDTH NON-JOINER.</summary>
    private const string ZeroWidthNonJoiner = "\u200C";

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
