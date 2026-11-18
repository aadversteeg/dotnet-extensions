namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// An axis-aligned rectangle in device space, in pixels: pixel (i, j) is the unit square from (i, j) to (i + 1, j + 1),
/// so a box need not fall on pixel edges. Used to clip what is drawn.
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge, not less than the left one.</param>
/// <param name="Bottom">The bottom edge, not less than the top one.</param>
public readonly record struct PixelBox(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The box both boxes cover; empty (zero width or height) when they do not overlap.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>The intersection.</returns>
    public PixelBox Intersect(PixelBox other)
    {
        var left = double.Max(Left, other.Left);
        var top = double.Max(Top, other.Top);
        var right = double.Max(left, double.Min(Right, other.Right));
        var bottom = double.Max(top, double.Min(Bottom, other.Bottom));
        return new PixelBox(left, top, right, bottom);
    }

    /// <summary>True when the box has no area.</summary>
    public bool IsEmpty => !(Right > Left && Bottom > Top);

    /// <summary>True when the box is a real rectangle: finite edges in order.</summary>
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) && double.IsFinite(Bottom) && Right >= Left && Bottom >= Top;
}
