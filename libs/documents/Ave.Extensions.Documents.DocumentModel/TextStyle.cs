using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>The computed text properties of an <see cref="InlineRun"/>, and of a box with lines (its strut,
/// <see cref="BoxStyle.Text"/>).</summary>
/// <param name="FontFamily">One family name (CSS Fonts 4 §2.1; a fallback list comes later).</param>
/// <param name="FontSize">The font size (CSS Fonts 4 §2.5), positive.</param>
/// <param name="FontWeight">The weight (CSS Fonts 4 §2.2).</param>
/// <param name="Color">The text colour (CSS Color 4 §3.2, property <c>color</c>).</param>
/// <param name="FontStyle">Upright or italic (CSS Fonts 4 §2.4).</param>
public sealed record TextStyle(string FontFamily, Length FontSize, FontWeight FontWeight, Color Color, FontStyle FontStyle = FontStyle.Normal)
{
    /// <summary>The line height (CSS 2.1 §10.8.1, <c>line-height</c>, inherited); <see cref="LineHeight.Normal"/>, the
    /// initial value, unless set.</summary>
    public LineHeight LineHeight { get; init; } = LineHeight.Normal.Instance;

    /// <summary>
    /// How the run's white space is collapsed (CSS Text 4, <c>white-space-collapse</c>, inherited); <see
    /// cref="WhiteSpaceCollapse.Collapse"/>, the initial value, unless set. Under <see cref="WhiteSpaceCollapse.Preserve"/> and
    /// <see cref="WhiteSpaceCollapse.PreserveBreaks"/> a line feed U+000A in the text is a preserved segment break: a forced
    /// line break, the same as the line separator U+2028 that is the model's forced break in every mode (white-space
    /// collapsing turns the one into the other). Two forms remain because they say different things: U+2028 breaks the line
    /// whatever the mode, a line feed only where the mode preserves it (elsewhere it is a space), as the text of an HTML
    /// source is written. Under <see cref="WhiteSpaceCollapse.Preserve"/> a tab is refused (<see cref="ModelCheck"/>): tab
    /// stops are not built.
    /// </summary>
    public WhiteSpaceCollapse WhiteSpaceCollapse { get; init; } = WhiteSpaceCollapse.Collapse;

    /// <summary>Whether the run's text wraps at its soft wrap opportunities (CSS Text 4 §5.1, <c>text-wrap-mode</c>,
    /// inherited); <see cref="TextWrapMode.Wrap"/>, the initial value, unless set. A break opportunity belongs to the run of the
    /// character before it (the line breaker's rule): under <see cref="TextWrapMode.NoWrap"/> it is not taken, and forced
    /// line breaks still break.</summary>
    public TextWrapMode TextWrapMode { get; init; } = TextWrapMode.Wrap;
}
