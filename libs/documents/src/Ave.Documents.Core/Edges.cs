namespace Ave.Documents.Core;

/// <summary>
/// Four lengths, one per side of a box, for margins and padding. The parameter order is top, right, bottom,
/// left: the order of the CSS shorthand properties (CSS Box Model 3 §4.2 "margin", §5.2 "padding").
/// </summary>
/// <param name="Top">The top side.</param>
/// <param name="Right">The right side.</param>
/// <param name="Bottom">The bottom side.</param>
/// <param name="Left">The left side.</param>
public readonly record struct Edges(Length Top, Length Right, Length Bottom, Length Left)
{
    /// <summary>Zero on every side.</summary>
    public static Edges Zero => default;

    /// <summary>The same length on every side.</summary>
    /// <param name="all">The length for all four sides.</param>
    /// <returns>The edges.</returns>
    public static Edges All(Length all) => new(all, all, all, all);

    /// <summary>Only a bottom edge; the other sides are zero. Handy for space below a paragraph.</summary>
    /// <param name="bottom">The bottom length.</param>
    /// <returns>The edges.</returns>
    public static Edges OnlyBottom(Length bottom) => new(Length.Zero, Length.Zero, bottom, Length.Zero);

    /// <summary>Left plus right.</summary>
    public Length Horizontal => Left + Right;

    /// <summary>Top plus bottom.</summary>
    public Length Vertical => Top + Bottom;

    /// <summary>The four sides in the order top, right, bottom, left.</summary>
    public Length[] Sides => [Top, Right, Bottom, Left];

    /// <summary>Formats as <c>top,right,bottom,left</c> in points.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() =>
        $"{InvariantNumber.Format(Top.Points)},{InvariantNumber.Format(Right.Points)},"
        + $"{InvariantNumber.Format(Bottom.Points)},{InvariantNumber.Format(Left.Points)}";
}
