using Ave.Documents.Fonts;

namespace Ave.Documents.Typesetting.Shaping;

/// <summary>
/// One glyph in the shaping buffer, in font design units. Each lookup turns the whole buffer into a new one;
/// nothing is changed in place.
/// </summary>
/// <param name="Glyph">The glyph id.</param>
/// <param name="Cluster">The index of the first character (Unicode scalar value) of the run that this glyph shows.</param>
/// <param name="Text">The characters this glyph shows: one character, several for a ligature, or empty for the
/// later glyphs of a one-to-many substitution.</param>
/// <param name="Class">The glyph's class, for the lookup flags and for finding mark bases.</param>
/// <param name="XAdvance">How far the pen moves after the glyph.</param>
/// <param name="XOffset">How far the glyph is drawn to the right of the pen.</param>
/// <param name="YOffset">How far the glyph is drawn above the baseline.</param>
/// <param name="Attachment">For a mark attached to a base: where it sits relative to the base. Resolved into the
/// offsets when positioning ends (<see cref="GlyphPositioning"/>).</param>
internal sealed record GlyphItem(
    GlyphId Glyph,
    int Cluster,
    string Text,
    GlyphClass Class,
    int XAdvance = 0,
    int XOffset = 0,
    int YOffset = 0,
    MarkAttachment? Attachment = null);

/// <summary>The classes of the OpenType glyph definition table (GDEF glyphClassDef) that shaping uses.</summary>
internal enum GlyphClass
{
    /// <summary>A base glyph (class 1), or a glyph with no class.</summary>
    Base,

    /// <summary>A ligature glyph (class 2).</summary>
    Ligature,

    /// <summary>A mark glyph (class 3).</summary>
    Mark,
}

/// <summary>A mark attached to a base glyph by GPOS mark-to-base.</summary>
/// <param name="BaseIndex">The index of the base glyph in the buffer.</param>
/// <param name="X">Base anchor x minus mark anchor x.</param>
/// <param name="Y">Base anchor y minus mark anchor y.</param>
internal sealed record MarkAttachment(int BaseIndex, int X, int Y);
