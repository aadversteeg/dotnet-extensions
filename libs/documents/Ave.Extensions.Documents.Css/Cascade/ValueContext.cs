using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>The metrics of a font that the <c>ex</c> and <c>ch</c> units need (CSS Values 4 §6.1.1), in points for the
/// font size asked.</summary>
/// <param name="XHeight">The x-height.</param>
/// <param name="ZeroAdvance">The advance of the "0" glyph.</param>
public readonly record struct FontMetrics(double XHeight, double ZeroAdvance);

/// <summary>Finds the metrics of the first available font of a family list, at a size; null when the caller has no
/// fonts (then <c>ex</c> and <c>ch</c> are refused).</summary>
/// <param name="families">The computed font-family list, in order.</param>
/// <param name="weight">The computed font-weight.</param>
/// <param name="italic">Whether the computed font-style is italic.</param>
/// <param name="sizePoints">The computed font size in points.</param>
/// <returns>The metrics, or null.</returns>
public delegate FontMetrics? FontMetricsResolver(ValueList<string> families, double weight, bool italic, double sizePoints);

/// <summary>
/// What computing values needs from outside the style sheets: the viewport (for <c>vw</c> and <c>vh</c>, CSS Values 4
/// §6.1.2; for print in Chromium, the page area) and the fonts' metrics (for <c>ex</c> and <c>ch</c>).
/// </summary>
/// <param name="ViewportWidth">The width vw is a hundredth of.</param>
/// <param name="ViewportHeight">The height vh is a hundredth of.</param>
/// <param name="FontMetrics">The font metrics, or null when the caller has none.</param>
public sealed record ValueContext(Length ViewportWidth, Length ViewportHeight, FontMetricsResolver? FontMetrics = null)
{
    /// <summary>A4 with no margins and no fonts.</summary>
    public static ValueContext A4 { get; } = new(Length.FromMillimetres(210), Length.FromMillimetres(297));
}
