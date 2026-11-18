using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Combines an element's own modifier values with its shared style into the values the element specifies
/// (modelled on CSS Cascade 4: an inline style outranks a style sheet rule, §6.1, and what neither sets is
/// inherited, §7.2). The order is fixed, never the order of the calls:
/// <list type="number">
/// <item>the element's own <c>Format</c> (like an inline style),</item>
/// <item>its shared <c>Style</c> (like a class rule),</item>
/// <item>for a table cell: the table's cell defaults (<c>CellPadding</c>, <c>GridLines</c>), like a rule for every
/// cell of the table;</item>
/// <item>for inherited text properties: the parent's computed value (applied by the translator).</item>
/// </list>
/// Within one layer, a single side (<c>MarginTop</c>) wins over the all-sides value (<c>Margin</c>).
/// </summary>
internal static class Cascade
{
    /// <summary>The specified box values of an element: every side resolved, zero where nothing is set
    /// (the CSS initial values).</summary>
    /// <param name="Margin">The four margins.</param>
    /// <param name="Padding">The four paddings.</param>
    /// <param name="Border">The four border sides.</param>
    /// <param name="Background">The background colour, or null for transparent.</param>
    /// <param name="KeepTogether">Whether breaks inside are avoided.</param>
    /// <param name="KeepWithNext">Whether a break after is avoided.</param>
    /// <param name="KeepWithPrevious">Whether a break before is avoided.</param>
    public sealed record BoxValues(Edges Margin, Edges Padding, Border Border, Color? Background, bool KeepTogether, bool KeepWithNext, bool KeepWithPrevious);

    /// <summary>The text values an element specifies: its own, else its style's; null where neither sets one.</summary>
    public static TextFormat Text(Style own, Style shared) => new(
        own.Text.FontFamily ?? shared.Text.FontFamily,
        own.Text.FontSize ?? shared.Text.FontSize,
        own.Text.FontWeight ?? shared.Text.FontWeight,
        own.Text.Color ?? shared.Text.Color,
        own.Text.TextAlign ?? shared.Text.TextAlign,
        own.Text.FontStyle ?? shared.Text.FontStyle)
    {
        Orphans = own.Text.Orphans ?? shared.Text.Orphans,
        Widows = own.Text.Widows ?? shared.Text.Widows,
        LineHeight = own.Text.LineHeight ?? shared.Text.LineHeight,
    };

    /// <summary>The box values an element specifies; <paramref name="defaults"/> fills what neither the element nor its
    /// style sets (a table's cell defaults; nothing for other elements).</summary>
    public static BoxValues Box(Style own, Style shared, BoxFormat? defaults = null)
    {
        var (mine, style, fallback) = (own.Box, shared.Box, defaults ?? BoxFormat.None);
        return new BoxValues(
            new Edges(
                Side(mine.MarginTop, mine.Margin, style.MarginTop, style.Margin) ?? Length.Zero,
                Side(mine.MarginRight, mine.Margin, style.MarginRight, style.Margin) ?? Length.Zero,
                Side(mine.MarginBottom, mine.Margin, style.MarginBottom, style.Margin) ?? Length.Zero,
                Side(mine.MarginLeft, mine.Margin, style.MarginLeft, style.Margin) ?? Length.Zero),
            new Edges(
                Side(mine.PaddingTop, mine.Padding, style.PaddingTop, style.Padding) ?? Side(fallback.PaddingTop, fallback.Padding, null, null) ?? Length.Zero,
                Side(mine.PaddingRight, mine.Padding, style.PaddingRight, style.Padding) ?? Side(fallback.PaddingRight, fallback.Padding, null, null) ?? Length.Zero,
                Side(mine.PaddingBottom, mine.Padding, style.PaddingBottom, style.Padding) ?? Side(fallback.PaddingBottom, fallback.Padding, null, null) ?? Length.Zero,
                Side(mine.PaddingLeft, mine.Padding, style.PaddingLeft, style.Padding) ?? Side(fallback.PaddingLeft, fallback.Padding, null, null) ?? Length.Zero),
            new Border(
                Side(mine.BorderTop, mine.Border, style.BorderTop, style.Border) ?? Side(fallback.BorderTop, fallback.Border, null, null) ?? BorderSide.None,
                Side(mine.BorderRight, mine.Border, style.BorderRight, style.Border) ?? Side(fallback.BorderRight, fallback.Border, null, null) ?? BorderSide.None,
                Side(mine.BorderBottom, mine.Border, style.BorderBottom, style.Border) ?? Side(fallback.BorderBottom, fallback.Border, null, null) ?? BorderSide.None,
                Side(mine.BorderLeft, mine.Border, style.BorderLeft, style.Border) ?? Side(fallback.BorderLeft, fallback.Border, null, null) ?? BorderSide.None),
            mine.Background ?? style.Background ?? fallback.Background,
            mine.KeepTogether ?? style.KeepTogether ?? false,
            mine.KeepWithNext ?? style.KeepWithNext ?? false,
            mine.KeepWithPrevious ?? style.KeepWithPrevious ?? false);
    }

    /// <summary>One side: own side, own all-sides, style side, style all-sides; null when none is set.</summary>
    private static T? Side<T>(T? ownSide, T? ownAll, T? styleSide, T? styleAll)
        where T : struct =>
        ownSide ?? ownAll ?? styleSide ?? styleAll;
}
