namespace Ave.Documents.Core;

/// <summary>
/// Upright or italic glyphs: the CSS property <c>font-style</c> (CSS Fonts 4 §2.4). The oblique-with-angle form
/// is not supported; <see cref="Italic"/> selects a face that the font marks as italic or oblique.
/// </summary>
public enum FontStyle
{
    /// <summary>An upright face (<c>normal</c>, the initial value).</summary>
    Normal,

    /// <summary>An italic face (<c>italic</c>).</summary>
    Italic,
}
