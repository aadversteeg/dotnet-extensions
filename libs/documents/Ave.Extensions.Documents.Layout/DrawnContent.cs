using System;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Typesetting;

namespace Ave.Documents.Layout;

/// <summary>
/// What a fragment draws on its page, read from the fragment tree the way <see cref="PagePainter"/> paints it. A
/// fragment draws something when it holds a visible glyph, an image, a rule, or a box with a background or a visible
/// border side — each with an area that is not empty (an image or a rule of no width has no height either, as an
/// image keeps its ratio and a table draws no rule of no width). Layout asks this where a decision depends on whether placed
/// content shows anything: a row cut by a page break (<see cref="TableBreaks"/>).
/// </summary>
internal static class DrawnContent
{
    /// <summary>
    /// True when <paramref name="fragment"/> draws something below <paramref name="y"/> (in the fragment's own
    /// coordinates, y down): some drawn area of it has a part lower than <paramref name="y"/>.
    /// </summary>
    /// <param name="fragment">The fragment, as placed on its page.</param>
    /// <param name="y">The height, from the fragment's top edge; may be negative.</param>
    /// <returns>Whether anything it draws reaches below that height.</returns>
    public static bool DrawsBelow(Fragment fragment, double y) => fragment switch
    {
        BoxFragment box => DecorationBelow(box, y) || ChildrenDrawBelow(box.Children, y),
        LineFragment line => ChildrenDrawBelow(line.Children, y),
        TextFragment text => text.Glyphs.Any(IsVisible) && AreaBelow(0, text.Size.Height, y),
        ImageFragment or RuleFragment => AreaBelow(0, fragment.Size.Height, y),
        _ => throw new InvalidOperationException($"Unknown fragment type {fragment.GetType().Name}."),
    };

    /// <summary>True when the children of a box or line draw something below <paramref name="y"/>.</summary>
    public static bool ChildrenDrawBelow(ValueList<PlacedFragment> children, double y) =>
        children.Any(child => DrawsBelow(child.Fragment, y - child.Offset.Y));

    /// <summary>
    /// A box's own decoration below <paramref name="y"/>: its background fills its whole border box; each visible
    /// border side is a band along its edge (as <see cref="PagePainter"/> draws them: the top and bottom sides over the
    /// full width, the left and right sides between them).
    /// </summary>
    private static bool DecorationBelow(BoxFragment box, double y)
    {
        var (border, height) = (box.Border, box.Size.Height);
        var (top, bottom) = (border.Top.Width.Points, border.Bottom.Width.Points);
        return box.Size.Width > 0 && (
            (box.Box.Style.Background is not null && AreaBelow(0, height, y))
            || (border.Top.IsVisible && AreaBelow(0, top, y))
            || (border.Bottom.IsVisible && AreaBelow(height - bottom, height, y))
            || ((border.Left.IsVisible || border.Right.IsVisible) && AreaBelow(top, height - bottom, y)));
    }

    /// <summary>True when the band from <paramref name="top"/> to <paramref name="bottom"/> is not empty and reaches
    /// below <paramref name="y"/>.</summary>
    private static bool AreaBelow(double top, double bottom, double y) =>
        bottom - top > Fitting.TolerancePoints && bottom > y + Fitting.TolerancePoints;

    /// <summary>
    /// A glyph that leaves ink. The painter skips the invisible ones (default-ignorable characters and forced line
    /// breaks, <see cref="ShapedGlyph.IsInvisible"/>); a glyph of white space, such as a no-break space, is painted but
    /// shows nothing. The later glyphs of a one-to-many substitution have no text of their own and do show.
    /// </summary>
    private static bool IsVisible(ShapedGlyph glyph) =>
        !glyph.IsInvisible && !(glyph.Text.Length > 0 && glyph.Text.All(char.IsWhiteSpace));
}
