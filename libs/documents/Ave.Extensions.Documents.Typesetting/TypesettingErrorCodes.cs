using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>Errors the shaper and the line breaker raise.</summary>
public static class TypesettingErrorCodes
{
    /// <summary><c>Documents.Typesetting</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Typesetting";

    /// <summary>A font whose substitutions turn a text into more glyphs than the shaper allows
    /// (<see cref="Shaping.GlyphSubstitution.MaxGlyphs"/>): one-to-many substitutions that feed each other. No real font
    /// needs it; a malformed or hostile one would otherwise make shaping allocate without bound. Metadata: FontName,
    /// Value (the limit).</summary>
    public static readonly ErrorCode TooManyGlyphs = _ / "TooManyGlyphs";

    /// <summary>A font whose lookups need more operations for a text than the shaper allows
    /// (ShapingBudget.MaxOperations, a number per glyph of the text): lookups that repeat, lookups of very many
    /// subtables, or lookups that walk a buffer their own substitutions grew. The text's own shape costs a fixed few
    /// operations per glyph per lookup, so no real font needs it; a malformed or hostile one would otherwise make shaping
    /// take time and memory without bound. Metadata: FontName, Value (the limit).</summary>
    public static readonly ErrorCode TooMuchWork = _ / "TooMuchWork";
}
