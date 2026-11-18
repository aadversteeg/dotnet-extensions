using System.Collections.Generic;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// Clips closed polygons to an axis-aligned box, one side of the box at a time (the Sutherland–Hodgman algorithm:
/// I. E. Sutherland, G. W. Hodgman, "Reentrant polygon clipping", Communications of the ACM 17(1), 1974).
/// </summary>
/// <remarks>
/// Against one side, the polygon keeps its points on the inner side and gets the crossing points where an edge passes
/// the side; a stretch of the polygon outside is replaced by the piece of the side between where it left and where it came
/// back. That stretch and the piece of the side form a loop that lies outside or on the side, so it winds around no inner
/// point: for every point inside the box the clipped polygon winds around it exactly as often as the original did. This
/// holds for any closed polygon, convex or not, self-intersecting or not, so both fill rules see the same coverage inside
/// the box. Pieces along the sides that cancel each other are left in; they add no area.
/// </remarks>
internal static class Clipping
{
    /// <summary>The polygons clipped to the box; polygons left with fewer than three points are dropped.</summary>
    /// <param name="polygons">Closed polygons.</param>
    /// <param name="box">The box.</param>
    /// <returns>New polygons.</returns>
    public static List<List<Vertex>> ToBox(List<List<Vertex>> polygons, PixelBox box)
    {
        var clipped = new List<List<Vertex>>(polygons.Count);
        foreach (var polygon in polygons)
        {
            var result = Side(polygon, box.Left, vertical: true, keepGreater: true);
            result = Side(result, box.Right, vertical: true, keepGreater: false);
            result = Side(result, box.Top, vertical: false, keepGreater: true);
            result = Side(result, box.Bottom, vertical: false, keepGreater: false);
            if (result.Count >= 3)
            {
                clipped.Add(result);
            }
        }

        return clipped;
    }

    /// <summary>
    /// Clips one polygon to the box using two buffers the caller keeps, so clipping allocates nothing; the result is one of
    /// the two buffers (fewer than three points: nothing is left).
    /// </summary>
    /// <param name="polygon">A closed polygon; not changed.</param>
    /// <param name="box">The box.</param>
    /// <param name="first">A buffer.</param>
    /// <param name="second">Another buffer.</param>
    /// <returns>The clipped polygon, in one of the buffers.</returns>
    public static List<Vertex> ToBox(List<Vertex> polygon, PixelBox box, List<Vertex> first, List<Vertex> second)
    {
        Side(polygon, box.Left, vertical: true, keepGreater: true, first);
        Side(first, box.Right, vertical: true, keepGreater: false, second);
        Side(second, box.Top, vertical: false, keepGreater: true, first);
        Side(first, box.Bottom, vertical: false, keepGreater: false, second);
        return second;
    }

    /// <summary>
    /// Clips against one side: the line x = <paramref name="edge"/> (vertical) or y = <paramref name="edge"/>, keeping the
    /// points with a greater or with a smaller coordinate. A point on the line is inside.
    /// </summary>
    private static List<Vertex> Side(List<Vertex> polygon, double edge, bool vertical, bool keepGreater) =>
        Side(polygon, edge, vertical, keepGreater, new List<Vertex>(polygon.Count + 4));

    /// <summary>The same, writing into <paramref name="result"/> (cleared first).</summary>
    private static List<Vertex> Side(List<Vertex> polygon, double edge, bool vertical, bool keepGreater, List<Vertex> result)
    {
        result.Clear();
        if (polygon.Count == 0)
        {
            return result;
        }

        var previous = polygon[^1];
        var previousInside = Inside(previous, edge, vertical, keepGreater);
        foreach (var point in polygon)
        {
            var inside = Inside(point, edge, vertical, keepGreater);
            if (inside != previousInside)
            {
                result.Add(Crossing(previous, point, edge, vertical));
            }

            if (inside)
            {
                result.Add(point);
            }

            previous = point;
            previousInside = inside;
        }

        return result;
    }

    private static bool Inside(Vertex point, double edge, bool vertical, bool keepGreater)
    {
        var value = vertical ? point.X : point.Y;
        return keepGreater ? value >= edge : value <= edge;
    }

    /// <summary>Where the edge from a to b crosses the line; the coordinate on the line is set exactly.</summary>
    private static Vertex Crossing(Vertex a, Vertex b, double edge, bool vertical)
    {
        if (vertical)
        {
            var t = (edge - a.X) / (b.X - a.X);
            return new Vertex(edge, a.Y + (t * (b.Y - a.Y)));
        }

        var s = (edge - a.Y) / (b.Y - a.Y);
        return new Vertex(a.X + (s * (b.X - a.X)), edge);
    }
}
