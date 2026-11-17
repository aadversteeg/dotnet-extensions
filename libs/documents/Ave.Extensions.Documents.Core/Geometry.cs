namespace Ave.Extensions.Documents.Core;

// Geometry used by layout and the page model. All values are in points (1/72 inch). The coordinate system is
// the one CSS uses: origin at the top-left corner, x grows to the right, y grows DOWN the page. The PDF back-end
// converts to PDF user space (origin bottom-left, y up; ISO 32000-1 §8.3.2.3) in exactly one place.

/// <summary>A position in points; origin top-left, y grows down the page.</summary>
/// <param name="X">Distance from the left edge, in points.</param>
/// <param name="Y">Distance from the top edge, in points.</param>
public readonly record struct Point(double X, double Y)
{
    /// <summary>Moves the point by another point used as an offset.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The offset to add.</param>
    /// <returns>The moved point.</returns>
    public static Point operator +(Point point, Point offset) => new(point.X + offset.X, point.Y + offset.Y);

    /// <summary>Formats as <c>(x, y)</c> with at most three decimals.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"({InvariantNumber.Format(X)}, {InvariantNumber.Format(Y)})";
}

/// <summary>A width and a height in points.</summary>
/// <param name="Width">Horizontal extent, in points.</param>
/// <param name="Height">Vertical extent, in points.</param>
public readonly record struct Size(double Width, double Height)
{
    /// <summary>Formats as <c>width x height</c> with at most three decimals.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{InvariantNumber.Format(Width)}x{InvariantNumber.Format(Height)}";
}

/// <summary>An axis-aligned rectangle: its top-left corner and its size, in points.</summary>
/// <param name="Origin">The top-left corner.</param>
/// <param name="Size">The width and height.</param>
public readonly record struct Rect(Point Origin, Size Size)
{
    /// <summary>Formats as <c>(x, y) width x height</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Origin} {Size}";
}
