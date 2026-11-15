using Ave.Documents.Fonts;

namespace Ave.Documents.Text;

/// <summary>
/// One glyph produced by shaping: which glyph, how far it advances the pen, and which source text it shows.
/// </summary>
/// <param name="Glyph">The glyph id in the run's font.</param>
/// <param name="Advance">The horizontal advance, in points at the run's font size.</param>
/// <param name="Text">The characters this glyph represents (its cluster). Stage 0 maps exactly one character to
/// one glyph; ligatures (one glyph for "fi") will map several characters to one glyph. The PDF writer uses this
/// to build the ToUnicode map, so text can be copied and searched.</param>
public sealed record ShapedGlyph(GlyphId Glyph, double Advance, string Text)
{
    /// <summary>True for the glyph of a collapsed space U+0020, the only break opportunity in stage 0.</summary>
    public bool IsSpace => Text == " ";
}
