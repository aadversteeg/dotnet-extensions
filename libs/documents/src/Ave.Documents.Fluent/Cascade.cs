using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// Combines an element's own modifier values with its shared style into the values the element specifies
/// (CSS Cascade 4 §6: a later, more specific origin wins). The order is fixed, never the order of the calls:
/// <list type="number">
/// <item>the element's own <c>Format</c> (like an inline style),</item>
/// <item>its shared <c>Style</c> (like a class rule),</item>
/// <item>for inherited text properties: the parent's computed value (applied by the translator).</item>
/// </list>
/// Within one layer, a single side (<c>MarginTop</c>) wins over the all-sides value (<c>Margin</c>).
/// </summary>
internal static class Cascade
{
    /// <summary>The specified box values of an element: every side resolved, zero where nothing is set
    /// (the CSS initial value of margin and padding).</summary>
    /// <param name="Margin">The four margins.</param>
    /// <param name="Padding">The four paddings.</param>
    /// <param name="Background">The background colour, or null for transparent.</param>
    public sealed record BoxValues(Edges Margin, Edges Padding, Color? Background);

    /// <summary>The text values an element specifies: its own, else its style's; null where neither sets one.</summary>
    public static TextFormat Text(Style own, Style shared) => new(
        own.Text.FontFamily ?? shared.Text.FontFamily,
        own.Text.FontSize ?? shared.Text.FontSize,
        own.Text.FontWeight ?? shared.Text.FontWeight,
        own.Text.Color ?? shared.Text.Color);

    /// <summary>The box values an element specifies.</summary>
    public static BoxValues Box(Style own, Style shared)
    {
        var (mine, style) = (own.Box, shared.Box);
        return new BoxValues(
            new Edges(
                Side(mine.MarginTop, mine.Margin, style.MarginTop, style.Margin),
                Side(mine.MarginRight, mine.Margin, style.MarginRight, style.Margin),
                Side(mine.MarginBottom, mine.Margin, style.MarginBottom, style.Margin),
                Side(mine.MarginLeft, mine.Margin, style.MarginLeft, style.Margin)),
            new Edges(
                Side(mine.PaddingTop, mine.Padding, style.PaddingTop, style.Padding),
                Side(mine.PaddingRight, mine.Padding, style.PaddingRight, style.Padding),
                Side(mine.PaddingBottom, mine.Padding, style.PaddingBottom, style.Padding),
                Side(mine.PaddingLeft, mine.Padding, style.PaddingLeft, style.Padding)),
            mine.Background ?? style.Background);
    }

    /// <summary>One side: own side, own all-sides, style side, style all-sides, else zero.</summary>
    private static Length Side(Length? ownSide, Length? ownAll, Length? styleSide, Length? styleAll) =>
        ownSide ?? ownAll ?? styleSide ?? styleAll ?? Length.Zero;
}
