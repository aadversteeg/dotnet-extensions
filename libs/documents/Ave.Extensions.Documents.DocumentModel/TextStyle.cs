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
    /// line breaks still break. An image in a line (a box among the runs) wraps by its own box's text style
    /// (<see cref="BoxStyle.Text"/>), else by its paragraph's: a front-end sets that text style to the computed values of the
    /// element the image lies in.</summary>
    public TextWrapMode TextWrapMode { get; init; } = TextWrapMode.Wrap;

    /// <summary>
    /// The decorations applied to the run (CSS Text Decoration 3 §2, the used value of <c>text-decoration-line</c> and
    /// <c>text-decoration-color</c> on the run's decorating boxes; not inherited but passed on to the text inside): outermost
    /// decorating box first, and within one box in the order underline, overline, line-through, as Chromium paints them (case
    /// <c>dl-lines</c>). Empty, the initial value, unless set. At most <see cref="ModelLimits.MaxDecorations"/>
    /// (<see cref="ModelCheck"/>). A line is drawn across each piece of the run on a line, never across an image among the
    /// runs, and does not change the line's height or any break.
    /// </summary>
    public ValueList<TextDecoration> Decorations { get; init; } = [];

    /// <summary>
    /// Whether the underlines and overlines drawn across the run's text leave gaps around its glyphs' ink (CSS Text Decoration 4
    /// §2.10.4, <c>text-decoration-skip-ink</c>, inherited); <see cref="TextDecorationSkipInk.Auto"/>, the initial value, unless
    /// set. The value of the text decides, not that of the decorating box (Blink <c>TextDecorationPainter</c> reads it from the
    /// style of the text it paints; case <c>sk-whose</c>): a front-end sets it to the computed value of the element the text lies
    /// in. A line-through is never cut.
    /// </summary>
    public TextDecorationSkipInk DecorationSkipInk { get; init; } = TextDecorationSkipInk.Auto;
}
