using System.Numerics;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The exact coverage of polygons per pixel, in rational arithmetic, written independently of the rasteriser: the area
/// of {winding ≠ 0} and of {winding odd} inside each pixel and a clip box, as fractions of whole numbers, for shapes whose
/// coordinates are multiples of 1/64 pixel, or any doubles (each double is a fraction with a power of two below it). The
/// plane is cut into vertical slabs (the rasteriser works in rows) at every vertex, every crossing of two edges, every
/// pixel column and every place an edge meets a pixel row or the box; inside a slab no two edges cross and none crosses a
/// row line, so the regions between them, sorted from the top, are trapezoids whose area is the slab's width times their
/// height at its middle, and the winding number of each is counted from the edges above it.
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
    public static Dictionary<(int X, int Y), (double NonZero, double EvenOdd)> Areas(IReadOnlyList<(long X, long Y)[]> contours, (long Left, long Top, long Right, long Bottom)? box) =>
        Areas(
            [.. contours.Select(contour => contour.Select(point => ((BigInteger)point.X, (BigInteger)point.Y)).ToArray())],
            box is { } b ? ((BigInteger)b.Left, (BigInteger)b.Top, (BigInteger)b.Right, (BigInteger)b.Bottom) : null,
            Unit);

    /// <summary>
    /// The exact coverage, as for coordinates in 1/64 pixel, of polygons whose coordinates are any finite doubles in
    /// pixels, each taken at its exact value.
    /// </summary>
    /// <param name="contours">Closed polygons, coordinates in pixels.</param>
    /// <param name="box">A clip box in pixels (left, top, right, bottom), or null.</param>
    /// <returns>Per pixel (column, row): the non-zero and the even-odd coverage, 0 to 1.</returns>
    public static Dictionary<(int X, int Y), (double NonZero, double EvenOdd)> Areas(IReadOnlyList<(double X, double Y)[]> contours, (double Left, double Top, double Right, double Bottom)? box)
    {
        // The finest power of two any coordinate needs: every one is then a whole number of that unit.
        var values = contours.SelectMany(contour => contour.SelectMany(point => new[] { point.X, point.Y })).ToList();
        if (box is { } sides)
        {
            values.AddRange([sides.Left, sides.Top, sides.Right, sides.Bottom]);
        }

        var shift = values.Count == 0 ? 0 : int.Max(0, values.Max(value => -Dyadic(value).Exponent));
        BigInteger Scaled(double value)
        {
            var (mantissa, exponent) = Dyadic(value);
            return mantissa << (exponent + shift);
        }

        return Areas(
            [.. contours.Select(contour => contour.Select(point => (Scaled(point.X), Scaled(point.Y))).ToArray())],
            box is { } b ? (Scaled(b.Left), Scaled(b.Top), Scaled(b.Right), Scaled(b.Bottom)) : null,
            BigInteger.One << shift);
    }

    /// <summary>A finite double as mantissa · 2^exponent exactly (0 as 0 · 2^0).</summary>
    private static (BigInteger Mantissa, int Exponent) Dyadic(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        (mantissa, exponent) = exponent == 0 ? (mantissa, -1074) : (mantissa | (1L << 52), exponent - 1075);
        if (mantissa == 0)
        {
            return (BigInteger.Zero, 0);
        }

        while ((mantissa & 1) == 0)
        {
            (mantissa, exponent) = (mantissa >> 1, exponent + 1);
        }

        return (bits < 0 ? -mantissa : mantissa, exponent);
    }

    private static Dictionary<(int X, int Y), (double NonZero, double EvenOdd)> Areas(IReadOnlyList<(BigInteger X, BigInteger Y)[]> contours, (BigInteger Left, BigInteger Top, BigInteger Right, BigInteger Bottom)? box, BigInteger unit)
    {
        var edges = new List<(BigInteger X0, BigInteger Y0, BigInteger X1, BigInteger Y1)>();
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

        var (minX, maxX) = (FloorDiv(all.Min(point => point.X), unit), CeilDiv(all.Max(point => point.X), unit));
        var (minY, maxY) = (FloorDiv(all.Min(point => point.Y), unit), CeilDiv(all.Max(point => point.Y), unit));
        var lines = new List<BigInteger>();
        for (var row = minY; row <= maxY; row++)
        {
            lines.Add(row * unit);
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
            cuts.Add(new Fraction(column * unit));
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
                if (line > BigInteger.Min(a.Y0, a.Y1) && line < BigInteger.Max(a.Y0, a.Y1))
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
            var column = (int)FloorDiv(middle, unit);
            if (box is { } inside && !(middle.CompareTo(new Fraction(inside.Left)) > 0 && middle.CompareTo(new Fraction(inside.Right)) < 0))
            {
                continue;
            }

            // The lines crossing the slab, at its middle: edges with their winding step, row lines and box sides with none.
            var crossing = new List<(Fraction Y, int Step)>();
            foreach (var edge in edges)
            {
                if (middle.CompareTo(new Fraction(BigInteger.Min(edge.X0, edge.X1))) > 0 && middle.CompareTo(new Fraction(BigInteger.Max(edge.X0, edge.X1))) < 0)
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

                var row = (int)FloorDiv(centre, unit);
                var area = width * height;
                var (nonZero, evenOdd) = result.TryGetValue((column, row), out var sum) ? sum : (Fraction.Zero, Fraction.Zero);
                result[(column, row)] = (nonZero + area, int.IsOddInteger(winding) ? evenOdd + area : evenOdd);
            }
        }

        var pixel = new Fraction(unit * unit);
        return result.ToDictionary(entry => entry.Key, entry => ((entry.Value.NonZero / pixel).ToDouble(), (entry.Value.EvenOdd / pixel).ToDouble()));
    }

    /// <summary>The x of a point two non-vertical edges have in common, strictly inside both x ranges; null when they do
    /// not meet there or run parallel.</summary>
    private static Fraction? Crossing((BigInteger X0, BigInteger Y0, BigInteger X1, BigInteger Y1) a, (BigInteger X0, BigInteger Y0, BigInteger X1, BigInteger Y1) b)
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

    private static BigInteger FloorDiv(BigInteger value, BigInteger divisor) => BigInteger.Divide(value - (value.Sign < 0 ? divisor - 1 : 0), divisor);

    private static BigInteger CeilDiv(BigInteger value, BigInteger divisor) => -FloorDiv(-value, divisor);

    private static BigInteger FloorDiv(Fraction value, BigInteger divisor) => FloorDiv(value.Numerator, value.Denominator * divisor);

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

        public Fraction(BigInteger value)
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
