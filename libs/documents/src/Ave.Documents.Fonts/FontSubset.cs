using System.Collections.Immutable;
using Ave.Documents.Core;

namespace Ave.Documents.Fonts;

/// <summary>A font program reduced to the glyphs a document uses, made by <see cref="FontSubsetter"/>.</summary>
/// <param name="Tag">Six uppercase letters that name this subset; the PDF font name is <c>Tag+PostScriptName</c>
/// (ISO 32000-1 §9.6.4).</param>
/// <param name="Glyphs">The glyph ids that still have their outline, in ascending order (the used glyphs, glyph 0,
/// and every component of a composite among them).</param>
/// <param name="FontFile">The TrueType font program. A byte payload, not part of the value: two subsets with the
/// same <paramref name="Tag"/> and glyphs of the same font always have the same bytes.</param>
public sealed record FontSubset(string Tag, ValueList<GlyphId> Glyphs, ImmutableArray<byte> FontFile)
{
    /// <summary>The name of the subset font in a PDF: the tag, a plus sign, and the PostScript name.</summary>
    /// <param name="font">The font the subset was made from.</param>
    /// <returns>For example <c>EOODIA+Lato-Regular</c>.</returns>
    public string PdfName(TrueTypeFont font) => IsSubset ? $"{Tag}+{font.Names.PostScriptName}" : font.Names.PostScriptName;

    /// <summary>False when the font file is the whole font, because its licence does not allow subsetting
    /// (<see cref="EmbeddingPermissions.AllowsSubsetting"/>). Only a subset carries a tag in its name (ISO 32000-1
    /// §9.6.4).</summary>
    public bool IsSubset { get; init; } = true;
}
