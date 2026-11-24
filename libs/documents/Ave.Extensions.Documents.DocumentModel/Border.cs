using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The style of a border side (CSS Backgrounds 3 §3.2, <c>border-style</c>): the values this model draws.
/// </summary>
public enum BorderStyle
{
    /// <summary>A single line in the side's colour.</summary>
    Solid,

    /// <summary>The box looks pressed in: the top and left sides are painted in a darker colour, the bottom and right
    /// sides in a lighter one, both taken from the side's colour (CSS Backgrounds 3 §3.2: "the colors of the bottom and
    /// right borders are lighter, the top and left darker"; the colours are the layout's, measured with Chromium).</summary>
    Inset,

    /// <summary>The box looks raised: the top and left sides lighter, the bottom and right sides darker, the other way
    /// round from <see cref="Inset"/>.</summary>
    Outset,
}

/// <summary>
/// One side of a box's border: a line of a width, a colour and a style (CSS Backgrounds 3 §3: <c>border-width</c>,
/// <c>border-color</c>, <c>border-style</c> of <c>solid</c>, <c>inset</c> or <c>outset</c>). A width of zero is the
/// initial <c>border-style: none</c>: no border, and the side takes no space. The colour is the one the box asks for; for
/// <see cref="BorderStyle.Inset"/> and <see cref="BorderStyle.Outset"/> the layout paints the darker and the lighter
/// colour taken from it.
/// </summary>
/// <param name="Width">The line width; zero or more.</param>
/// <param name="Color">The line colour; for inset and outset the colour the two shades are taken from.</param>
/// <param name="Style">The line style; <see cref="BorderStyle.Solid"/> unless given.</param>
public readonly record struct BorderSide(Length Width, Color Color, BorderStyle Style = BorderStyle.Solid)
{
    /// <summary>No border on this side.</summary>
    public static BorderSide None => default;

    /// <summary>True when the side draws a line (a positive width).</summary>
    public bool IsVisible => Width.Points > 0;

    /// <summary>Formats as <c>1pt #d3d3d3</c> for a solid side, <c>1pt inset #d3d3d3</c> for another style, or
    /// <c>none</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => !IsVisible ? "none" : Style == BorderStyle.Solid ? $"{Width} {Color}" : $"{Width} {Style.ToString().ToLowerInvariant()} {Color}";
}

/// <summary>
/// The border of a box, one <see cref="BorderSide"/> per side, in the CSS shorthand order top, right, bottom, left.
/// The border lies between the margin and the padding (CSS Box Model 3 §2: content, padding, border, margin).
/// </summary>
/// <param name="Top">The top side.</param>
/// <param name="Right">The right side.</param>
/// <param name="Bottom">The bottom side.</param>
/// <param name="Left">The left side.</param>
public readonly record struct Border(BorderSide Top, BorderSide Right, BorderSide Bottom, BorderSide Left)
{
    /// <summary>No border on any side, the initial value.</summary>
    public static Border None => default;

    /// <summary>The same side on all four sides.</summary>
    /// <param name="side">The side.</param>
    /// <returns>The border.</returns>
    public static Border All(BorderSide side) => new(side, side, side, side);

    /// <summary>The widths of the four sides, for insets: top, right, bottom, left.</summary>
    public Edges Widths => new(Top.Width, Right.Width, Bottom.Width, Left.Width);

    /// <summary>Formats as the four sides separated by <c>/</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Top}/{Right}/{Bottom}/{Left}";
}

/// <summary>
/// The radius of one corner of a box's border box (CSS Backgrounds 3 §5.1, <c>border-*-radius</c> with one value): a
/// length plus a percentage. The used radius across is the length plus the percentage of the border box's width, the
/// used radius down the length plus the percentage of its height (§5.1: percentages for the horizontal radius refer to
/// the width of the border box, for the vertical one to its height; case <c>br-bg-percent</c>: 50% on a box 150 × 40 px
/// is an ellipse of 75 × 20 px). So a length gives a quarter circle and a percentage a quarter ellipse unless the box is
/// square. Zero on both, the initial value, is a square corner.
/// </summary>
/// <param name="Length">The length; zero or more.</param>
/// <param name="Percent">The percentage: 50 is half the border box's width across and half its height down; zero or more.</param>
public readonly record struct CornerRadius(Length Length, double Percent = 0)
{
    /// <summary>A square corner, the initial value.</summary>
    public static CornerRadius Zero => default;

    /// <summary>True for a square corner.</summary>
    public bool IsZero => Length.Points == 0 && Percent == 0;

    /// <summary>A percentage of the border box only.</summary>
    /// <param name="percent">The percentage; zero or more.</param>
    /// <returns>The radius.</returns>
    public static CornerRadius OfPercent(double percent) => new(Length.Zero, percent);

    /// <summary>Formats as <c>4.5pt</c>, <c>50%</c>, or both joined by <c>+</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Percent == 0 ? Length.ToString() : Length.Points == 0 ? $"{InvariantNumber.Format(Percent)}%" : $"{Length}+{InvariantNumber.Format(Percent)}%";
}

/// <summary>
/// The rounded corners of a box (CSS Backgrounds 3 §5, <c>border-radius</c>), one <see cref="CornerRadius"/> per corner in
/// the CSS shorthand order top left, top right, bottom right, bottom left. The background and the border follow the
/// rounded border box; the box's content is not clipped by it. Drawn on a block box laid out by
/// <see cref="LayoutAlgorithmId.Flow"/> or <see cref="LayoutAlgorithmId.Inline"/> whose visible border sides, if any, are
/// solid sides of one width and one colour on all four sides; refused elsewhere (<see cref="ModelCheck"/>).
/// </summary>
/// <param name="TopLeft">The top left corner.</param>
/// <param name="TopRight">The top right corner.</param>
/// <param name="BottomRight">The bottom right corner.</param>
/// <param name="BottomLeft">The bottom left corner.</param>
public readonly record struct BorderRadius(CornerRadius TopLeft, CornerRadius TopRight, CornerRadius BottomRight, CornerRadius BottomLeft)
{
    /// <summary>Four square corners, the initial value.</summary>
    public static BorderRadius None => default;

    /// <summary>The same radius on all four corners.</summary>
    /// <param name="corner">The radius.</param>
    /// <returns>The corners.</returns>
    public static BorderRadius All(CornerRadius corner) => new(corner, corner, corner, corner);

    /// <summary>True when every corner is square.</summary>
    public bool IsZero => TopLeft.IsZero && TopRight.IsZero && BottomRight.IsZero && BottomLeft.IsZero;

    /// <summary>Formats as the four corners separated by <c>/</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{TopLeft}/{TopRight}/{BottomRight}/{BottomLeft}";
}
