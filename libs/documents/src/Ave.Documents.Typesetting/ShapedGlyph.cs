using Ave.Documents.Fonts;

namespace Ave.Documents.Typesetting;

/// <summary>
/// One glyph produced by shaping: which glyph, how far it advances the pen, where it is drawn relative to the
/// pen, and which source text it shows.
/// </summary>
/// <param name="Glyph">The glyph id in the run's font.</param>
/// <param name="Advance">The horizontal advance, in points at the run's font size, kerning included.</param>
/// <param name="Text">The characters this glyph shows (its cluster): one character, several for a ligature ("fi"),
/// or empty for the later glyphs of a one-to-many substitution. The PDF writer builds the ToUnicode map from it,
/// so text can be copied and searched.</param>
/// <param name="XOffset">How far the glyph is drawn to the right of the pen, in points (GPOS placement, mark
/// attachment).</param>
/// <param name="YOffset">How far the glyph is drawn ABOVE the baseline, in points (font convention, y up).</param>
public sealed record ShapedGlyph(GlyphId Glyph, double Advance, string Text, double XOffset = 0, double YOffset = 0)
{
    /// <summary>True for the glyph of a collapsed space U+0020: removed at the start and end of a line
    /// (CSS Text 3 §4.1.2), and stretched by justification.</summary>
    public bool IsSpace => Text == " ";

    /// <summary>True for a default-ignorable character (a soft hyphen, a joiner, a zero width space, …): its advance
    /// is 0 and it is not drawn, but it stays in the text for line breaking. The Unicode Standard §5.21.6, "Characters
    /// Ignored for Display": the characters to ignore for display are those with the Default_Ignorable_Code_Point
    /// property.</summary>
    public bool IsInvisible { get; init; }

    /// <summary>For a soft hyphen: the visible hyphen that replaces it when a line breaks after it (UAX #14 §5.4;
    /// CSS Text 3 §5.3, <c>hyphens: manual</c>); null for every other glyph, and for a soft hyphen in a font without a hyphen.</summary>
    public ShapedGlyph? BreakHyphen { get; init; }
}
