namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// What the line breaker (<see cref="GreedyLineBreaker"/>) does at the end of a line that ends at a collapsible space,
/// which the line end removes (CSS Text 3 §4.1.2). The paragraph was shaped with the space, so where the font kerns the
/// last letter with a space, the letter's advance holds that pair. Chromium keeps it or shapes the end again depending on
/// whether the line needs an accurate end position (Blink <c>LineBreaker</c>: "Reshaping between the last character and
/// trailing spaces is needed only when we need accurate end position, because kerning between trailing spaces is not
/// visible", <c>NeedsAccurateEndPosition</c>, <c>SetDontReshapeEndIfAtSpace</c>); the layout decides which, from the
/// paragraph's alignment.
/// </summary>
/// <remarks>
/// A line that ends before a forced break, and the last line of the paragraph, are shaped again before the space with
/// either value: measured in Chromium 159, the space before a forced break does not kern with the letter before it, in
/// start-aligned text too.
/// </remarks>
public enum LineEndShaping
{
    /// <summary>The line keeps the paragraph's glyphs up to the space: the last letter keeps its kerning with the space,
    /// and the line is measured with it (Chromium for <c>text-align: start</c> without text decoration).</summary>
    KeepParagraphGlyphs,

    /// <summary>The text before the space is shaped again from the last safe place, as at any other line end that is not
    /// safe to break (Chromium for <c>end</c>, <c>center</c>, <c>justify</c>, or with a text decoration).</summary>
    ShapeAgainBeforeSpace,
}
