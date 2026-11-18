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
}
