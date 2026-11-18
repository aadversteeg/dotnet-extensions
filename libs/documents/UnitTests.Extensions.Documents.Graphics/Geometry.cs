namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// Test-side geometry, written independently of the rasteriser: the exact area of a polygon inside a pixel, by clipping
/// the polygon to the pixel's square and the shoelace formula, and the exact area enclosed by cubic Bézier curves, by
/// Gauss–Legendre quadrature (exact for the polynomials involved).
/// </summary>
internal static class Geometry
{
    /// <summary>The area of a simple polygon inside the unit square of pixel (x, y).</summary>
    public static double AreaInPixel(IReadOnlyList<(double X, double Y)> polygon, int x, int y)
    {
        var points = polygon.ToList();
        points = ClipHalfPlane(points, p => p.X - x, (a, b) => Lerp(a, b, (x - a.X) / (b.X - a.X)));
        points = ClipHalfPlane(points, p => (x + 1) - p.X, (a, b) => Lerp(a, b, (x + 1 - a.X) / (b.X - a.X)));
        points = ClipHalfPlane(points, p => p.Y - y, (a, b) => Lerp(a, b, (y - a.Y) / (b.Y - a.Y)));
        points = ClipHalfPlane(points, p => (y + 1) - p.Y, (a, b) => Lerp(a, b, (y + 1 - a.Y) / (b.Y - a.Y)));
        return Math.Abs(SignedArea(points));
    }

    /// <summary>The area of a simple polygon inside the box from (left, top) to (right, bottom); 0 for an empty box.</summary>
    public static double AreaInBox(IReadOnlyList<(double X, double Y)> polygon, double left, double top, double right, double bottom)
    {
        if (!(right > left && bottom > top))
        {
            return 0;
        }

        var points = polygon.ToList();
        points = ClipHalfPlane(points, p => p.X - left, (a, b) => Lerp(a, b, (left - a.X) / (b.X - a.X)));
        points = ClipHalfPlane(points, p => right - p.X, (a, b) => Lerp(a, b, (right - a.X) / (b.X - a.X)));
        points = ClipHalfPlane(points, p => p.Y - top, (a, b) => Lerp(a, b, (top - a.Y) / (b.Y - a.Y)));
        points = ClipHalfPlane(points, p => bottom - p.Y, (a, b) => Lerp(a, b, (bottom - a.Y) / (b.Y - a.Y)));
        return Math.Abs(SignedArea(points));
    }

    /// <summary>The shoelace formula: positive for a polygon that turns clockwise in y-down coordinates.</summary>
    public static double SignedArea(IReadOnlyList<(double X, double Y)> polygon)
    {
        var sum = 0.0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2;
    }

    /// <summary>The area enclosed by a closed chain of cubic Bézier curves, ½ ∮ (x dy − y dx).</summary>
    public static double CubicArea(IReadOnlyList<((double X, double Y) P0, (double X, double Y) P1, (double X, double Y) P2, (double X, double Y) P3)> curves)
    {
        // Three-point Gauss–Legendre is exact up to degree 5; x·y' of a cubic has degree 5.
        double[] nodes = [0.5 - (Math.Sqrt(0.6) / 2), 0.5, 0.5 + (Math.Sqrt(0.6) / 2)];
        double[] weights = [5.0 / 18, 8.0 / 18, 5.0 / 18];
        var sum = 0.0;
        foreach (var (p0, p1, p2, p3) in curves)
        {
            for (var index = 0; index < 3; index++)
            {
                var t = nodes[index];
                var u = 1 - t;
                var x = (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X);
                var y = (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y);
                var dx = (3 * u * u * (p1.X - p0.X)) + (6 * u * t * (p2.X - p1.X)) + (3 * t * t * (p3.X - p2.X));
                var dy = (3 * u * u * (p1.Y - p0.Y)) + (6 * u * t * (p2.Y - p1.Y)) + (3 * t * t * (p3.Y - p2.Y));
                sum += weights[index] * ((x * dy) - (y * dx));
            }
        }

        return Math.Abs(sum / 2);
    }

    private static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double t) => (a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y)));

    private static List<(double X, double Y)> ClipHalfPlane(List<(double X, double Y)> points, Func<(double X, double Y), double> side, Func<(double X, double Y), (double X, double Y), (double X, double Y)> cross)
    {
        var result = new List<(double X, double Y)>();
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            var currentIn = side(current) >= 0;
            var nextIn = side(next) >= 0;
            if (currentIn)
            {
                result.Add(current);
            }

            if (currentIn != nextIn)
            {
                result.Add(cross(current, next));
            }
        }

        return result;
    }
}
