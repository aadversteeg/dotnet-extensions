namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Whether an underline or an overline leaves gaps where it would cross a glyph's ink: the CSS property
/// <c>text-decoration-skip-ink</c> (CSS Text Decoration 4 §2.10.4, "Skipping Glyphs"). Inherited; the initial value is
/// <see cref="Auto"/>. A line-through is never cut ("line-throughs are always continuous"). The value <c>all</c> is not in
/// the model.
/// </summary>
public enum TextDecorationSkipInk
{
    /// <summary>Underlines and overlines are interrupted where a glyph's outline crosses them (<c>auto</c>: "UAs may interrupt
    /// underlines and overlines where the line would cross glyph ink"), as Chromium cuts them.</summary>
    Auto,

    /// <summary>Every line is drawn whole, across the ink of the glyphs (<c>none</c>).</summary>
    None,
}
