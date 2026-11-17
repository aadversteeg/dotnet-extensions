namespace Ave.Documents.Fonts;

/// <summary>
/// The index of a glyph in a font (OpenType "glyph ID"): 0 to maxp.numGlyphs − 1. Glyph 0 is <c>.notdef</c>,
/// the "missing glyph" box, which the engine never draws on purpose.
/// </summary>
/// <param name="Value">The glyph index.</param>
public readonly record struct GlyphId(ushort Value)
{
    /// <summary>Formats as <c>g36</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"g{Value}";
}
