using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>Filled paths for tests, built in the page model directly: a circle of four cubic curves, a disc, a ring and
/// a polygon of straight lines. Coordinates are page coordinates in points (origin top left, y down).</summary>
public static class TestPaths
{
    /// <summary>The distance of a control point from its end along the tangent, for a quarter circle of radius 1 drawn
    /// as one cubic curve: 4/3 (√2 − 1), to ten digits (the curve lies within 0.03 % of the radius from the circle).</summary>
    public const double Kappa = 0.5522847498;

    /// <summary>A circle as one contour of four cubic curves, from its rightmost point on, through the bottom (y down).</summary>
    /// <param name="cx">The centre's x.</param>
    /// <param name="cy">The centre's y.</param>
    /// <param name="r">The radius.</param>
    /// <returns>The contour.</returns>
    public static PathContour Circle(double cx, double cy, double r)
    {
        var k = Kappa * r;
        return new PathContour(new Point(cx + r, cy),
        [
            new PathCubic(new Point(cx + r, cy + k), new Point(cx + k, cy + r), new Point(cx, cy + r)),
            new PathCubic(new Point(cx - k, cy + r), new Point(cx - r, cy + k), new Point(cx - r, cy)),
            new PathCubic(new Point(cx - r, cy - k), new Point(cx - k, cy - r), new Point(cx, cy - r)),
            new PathCubic(new Point(cx + k, cy - r), new Point(cx + r, cy - k), new Point(cx + r, cy)),
        ]);
    }

    /// <summary>A disc: one circle, non-zero.</summary>
    /// <param name="cx">The centre's x.</param>
    /// <param name="cy">The centre's y.</param>
    /// <param name="r">The radius.</param>
    /// <param name="color">The colour (black when not given).</param>
    /// <returns>The filled path.</returns>
    public static FillPath Disc(double cx, double cy, double r, Color? color = null) =>
        new([Circle(cx, cy, r)], PathFillRule.NonZero, color ?? Color.Black);

    /// <summary>A ring: two circles about one centre, both drawn the same way round, even-odd, so the inner one is empty.</summary>
    /// <param name="cx">The centre's x.</param>
    /// <param name="cy">The centre's y.</param>
    /// <param name="outer">The outer radius.</param>
    /// <param name="inner">The inner radius.</param>
    /// <param name="rule">The fill rule (even-odd unless given; non-zero fills the inner circle too).</param>
    /// <returns>The filled path.</returns>
    public static FillPath Ring(double cx, double cy, double outer, double inner, PathFillRule rule = PathFillRule.EvenOdd) =>
        new([Circle(cx, cy, outer), Circle(cx, cy, inner)], rule, Color.Black);

    /// <summary>A polygon: one contour of straight lines through the points, closed back to the first, non-zero.</summary>
    /// <param name="points">The corners, at least two.</param>
    /// <returns>The filled path.</returns>
    public static FillPath Polygon(params Point[] points) =>
        new([new PathContour(points[0], [.. points[1..].Select(point => (PathSegment)new PathLine(point))])], PathFillRule.NonZero, Color.Black);
}
