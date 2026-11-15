using Ave.Documents.Fonts;

namespace Ave.Documents.Typesetting.Shaping;

/// <summary>
/// One glyph in the shaping buffer, in font design units. Each lookup turns the whole buffer into a new one;
/// nothing is changed in place.
/// </summary>
/// <param name="Glyph">The glyph id.</param>
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
    string Text,
    GlyphClass Class,
    int XAdvance = 0,
    int XOffset = 0,
    int YOffset = 0,
    MarkAttachment? Attachment = null)
{
    /// <summary>The zero width joiner U+200D.</summary>
    public const string ZeroWidthJoiner = "\u200D";

    /// <summary>True for a default-ignorable character (Unicode Default_Ignorable_Code_Point): it takes no space and
    /// draws nothing; see <see cref="GlyphFilter"/> for how lookups treat it.</summary>
    public bool IsDefaultIgnorable { get; init; }

    /// <summary>True for a mandatory line break (UAX #14 classes BK, CR, LF, NL): it ends the line, draws nothing, and
    /// ends the shaping run it is in.</summary>
    public bool IsLineBreak { get; init; }
}
