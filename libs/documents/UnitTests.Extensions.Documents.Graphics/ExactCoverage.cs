using System.Numerics;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The exact coverage of polygons per pixel, in rational arithmetic, written independently of the rasteriser: for shapes
/// whose coordinates are multiples of 1/64 pixel, the area of {winding ≠ 0} and of {winding odd} inside each pixel and a
/// clip box, as fractions of whole numbers. The plane is cut into vertical slabs (the rasteriser works in rows) at every
/// vertex, every crossing of two edges, every pixel column and every place an edge meets a pixel row or the box; inside a
/// slab no two edges cross and none crosses a row line, so the regions between them, sorted from the top, are trapezoids
/// whose area is the slab's width times their height at its middle, and the winding number of each is counted from the
/// edges above it.
/// </summary>
internal static class ExactCoverage
{
    /// <summary>The scale of the input: coordinates are given in 1/64 pixel.</summary>
    public const int Unit = 64;

    /// <summary>
    /// The exact coverage of every pixel the polygons' extent touches: the share of the pixel (clipped to the box) where
    /// the winding number is not 0, and where it is odd.
    /// </summary>
    /// <param name="contours">Closed polygons, coordinates in 1/64 pixel.</param>
    /// <param name="box">A clip box in 1/64 pixel (left, top, right, bottom), or null.</param>
    /// <returns>Per pixel (column, row): the non-zero and the even-odd coverage, 0 to 1.</returns>
    public static Dictionary<(int X, int Y), (double NonZero, double EvenOdd)> Areas(IReadOnlyList<(long X, long Y)[]> contours, (long Left, long Top, long Right, long Bottom)? box)
    {
        var edges = new List<(long X0, long Y0, long X1, long Y1)>();
        foreach (var contour in contours)
        {
            for (var index = 0; index < contour.Length; index++)
            {
                var (from, to) = (contour[index], contour[(index + 1) % contour.Length]);
                if (from.X != to.X)
                {
                    edges.Add((from.X, from.Y, to.X, to.Y));
                }
            }
        }

        var all = contours.SelectMany(contour => contour).ToList();
        var result = new Dictionary<(int X, int Y), (Fraction NonZero, Fraction EvenOdd)>();
        if (all.Count == 0)
        {
            return [];
        }

        var (minX, maxX) = (FloorDiv(all.Min(point => point.X), Unit), CeilDiv(all.Max(point => point.X), Unit));
        var (minY, maxY) = (FloorDiv(all.Min(point => point.Y), Unit), CeilDiv(all.Max(point => point.Y), Unit));
        var lines = new List<long>();
        for (var row = minY; row <= maxY; row++)
        {
            lines.Add(row * Unit);
        }

        if (box is { } clip)
        {
            lines.Add(clip.Top);
            lines.Add(clip.Bottom);
        }

        var cuts = new List<Fraction>();
        foreach (var point in all)
        {
            cuts.Add(new Fraction(point.X));
        }

        for (var column = minX; column <= maxX; column++)
        {
            cuts.Add(new Fraction(column * Unit));
        }

        if (box is { } sides)
        {
            cuts.Add(new Fraction(sides.Left));
            cuts.Add(new Fraction(sides.Right));
        }

        for (var first = 0; first < edges.Count; first++)
        {
            var a = edges[first];
            foreach (var line in lines)
            {
                if (line > long.Min(a.Y0, a.Y1) && line < long.Max(a.Y0, a.Y1))
                {
                    // x where the edge meets the horizontal line.
                    cuts.Add(new Fraction(a.X0) + new Fraction((line - a.Y0) * (a.X1 - a.X0), a.Y1 - a.Y0));
                }
            }

            for (var second = first + 1; second < edges.Count; second++)
            {
                if (Crossing(a, edges[second]) is { } x)
                {
                    cuts.Add(x);
                }
            }
        }

        cuts.Sort();
        var slabs = new List<Fraction>();
        foreach (var cut in cuts)
        {
            if (slabs.Count == 0 || slabs[^1].CompareTo(cut) != 0)
            {
                slabs.Add(cut);
            }
        }

        for (var index = 0; index + 1 < slabs.Count; index++)
        {
            var (left, right) = (slabs[index], slabs[index + 1]);
            var middle = (left + right) / new Fraction(2);
            var width = right - left;
            var column = (int)FloorDiv(middle, Unit);
            if (box is { } inside && !(middle.CompareTo(new Fraction(inside.Left)) > 0 && middle.CompareTo(new Fraction(inside.Right)) < 0))
            {
                continue;
            }

            // The lines crossing the slab, at its middle: edges with their winding step, row lines and box sides with none.
            var crossing = new List<(Fraction Y, int Step)>();
            foreach (var edge in edges)
            {
                if (middle.CompareTo(new Fraction(long.Min(edge.X0, edge.X1))) > 0 && middle.CompareTo(new Fraction(long.Max(edge.X0, edge.X1))) < 0)
                {
                    var y = new Fraction(edge.Y0) + ((middle - new Fraction(edge.X0)) * new Fraction(edge.Y1 - edge.Y0, edge.X1 - edge.X0));

                    // Going down across an edge that runs left raises the winding number by one, one that runs right lowers it.
                    crossing.Add((y, edge.X1 < edge.X0 ? 1 : -1));
                }
            }

            foreach (var line in lines)
            {
                crossing.Add((new Fraction(line), 0));
            }

            crossing.Sort((p, q) => p.Y.CompareTo(q.Y));
            var winding = 0;
            for (var k = 0; k + 1 < crossing.Count; k++)
            {
                winding += crossing[k].Step;
                var height = crossing[k + 1].Y - crossing[k].Y;
                if (winding == 0 || height.IsZero)
                {
                    continue;
                }

                var centre = (crossing[k].Y + crossing[k + 1].Y) / new Fraction(2);
                if (box is { } within && !(centre.CompareTo(new Fraction(within.Top)) > 0 && centre.CompareTo(new Fraction(within.Bottom)) < 0))
                {
                    continue;
                }

                var row = (int)FloorDiv(centre, Unit);
                var area = width * height;
                var (nonZero, evenOdd) = result.TryGetValue((column, row), out var sum) ? sum : (Fraction.Zero, Fraction.Zero);
                result[(column, row)] = (nonZero + area, int.IsOddInteger(winding) ? evenOdd + area : evenOdd);
            }
        }

        var pixel = new Fraction(Unit * Unit);
        return result.ToDictionary(entry => entry.Key, entry => ((entry.Value.NonZero / pixel).ToDouble(), (entry.Value.EvenOdd / pixel).ToDouble()));
    }

    /// <summary>The x of a point two non-vertical edges have in common, strictly inside both x ranges; null when they do
    /// not meet there or run parallel.</summary>
    private static Fraction? Crossing((long X0, long Y0, long X1, long Y1) a, (long X0, long Y0, long X1, long Y1) b)
    {
        // a(t) = A0 + t·(A1 − A0), b(u) = B0 + u·(B1 − B0); solve with Cramer's rule.
        var (rx, ry, sx, sy) = (a.X1 - a.X0, a.Y1 - a.Y0, b.X1 - b.X0, b.Y1 - b.Y0);
        var denominator = (rx * sy) - (ry * sx);
        if (denominator == 0)
        {
            return null;
        }

        var (qx, qy) = (b.X0 - a.X0, b.Y0 - a.Y0);
        var t = new Fraction((qx * sy) - (qy * sx), denominator);
        var u = new Fraction((qx * ry) - (qy * rx), denominator);
        if (t.Sign <= 0 || t.CompareTo(Fraction.One) >= 0 || u.Sign <= 0 || u.CompareTo(Fraction.One) >= 0)
        {
            return null;
        }

        return new Fraction(a.X0) + (t * new Fraction(rx));
    }

    private static long FloorDiv(long value, long divisor) => (long)Math.Floor((double)value / divisor);

    private static long CeilDiv(long value, long divisor) => (long)Math.Ceiling((double)value / divisor);

    private static long FloorDiv(Fraction value, long divisor) => (long)BigInteger.Divide(value.Numerator - (value.Numerator.Sign < 0 ? (value.Denominator * divisor) - 1 : 0), value.Denominator * divisor);

    /// <summary>A rational number in lowest terms, denominator positive.</summary>
    private readonly struct Fraction : IComparable<Fraction>
    {
        public Fraction(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign < 0)
            {
                (numerator, denominator) = (-numerator, -denominator);
            }

            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            (Numerator, Denominator) = divisor.IsZero || divisor.IsOne ? (numerator, denominator) : (numerator / divisor, denominator / divisor);
        }

        public Fraction(long value)
            : this(value, BigInteger.One)
        {
        }

        public static Fraction Zero => new(0);

        public static Fraction One => new(1);

        public BigInteger Numerator { get; }

        public BigInteger Denominator { get; }

        public int Sign => Numerator.Sign;

        public bool IsZero => Numerator.IsZero;

        public static Fraction operator +(Fraction a, Fraction b) => new((a.Numerator * b.Denominator) + (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

        public static Fraction operator -(Fraction a, Fraction b) => new((a.Numerator * b.Denominator) - (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

        public static Fraction operator *(Fraction a, Fraction b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

        public static Fraction operator /(Fraction a, Fraction b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

        public int CompareTo(Fraction other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

        /// <summary>The double next to it, to about 2^-60 of a value up to a few units: the denominator is cut to 62 bits
        /// first (the numerator by as much), so a fraction of very long terms still converts.</summary>
        public double ToDouble()
        {
            var shift = (int)long.Max(0, Denominator.GetBitLength() - 62);
            return (double)(Numerator >> shift) / (double)(Denominator >> shift);
        }
    }
}
