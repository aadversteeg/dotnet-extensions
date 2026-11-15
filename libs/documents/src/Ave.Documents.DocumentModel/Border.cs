using Ave.Documents.Core;

namespace Ave.Documents.DocumentModel;

/// <summary>
/// One side of a box's border: a solid line of a width and a colour (CSS Backgrounds 3 §4: <c>border-width</c>,
/// <c>border-color</c>; <c>border-style</c> is always <c>solid</c> in this version). A width of zero is the
/// initial <c>border-style: none</c>: no border, and the side takes no space.
/// </summary>
/// <param name="Width">The line width; zero or more.</param>
/// <param name="Color">The line colour.</param>
public readonly record struct BorderSide(Length Width, Color Color)
{
    /// <summary>No border on this side.</summary>
    public static BorderSide None => default;

    /// <summary>True when the side draws a line (a positive width).</summary>
    public bool IsVisible => Width.Points > 0;

    /// <summary>Formats as <c>1pt #d3d3d3</c>, or <c>none</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => IsVisible ? $"{Width} {Color}" : "none";
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
