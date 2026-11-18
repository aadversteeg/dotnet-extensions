using Ave.Extensions.Documents.Graphics;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The rasteriser against <see cref="ExactCoverage"/> (exact rational areas, computed in vertical slabs; no code shared)
/// on shapes whose winding number takes more than two values inside a pixel: overlapping contours, contours that cross
/// themselves, coinciding edges, with and without a clip box whose sides lie inside pixels. Coordinates are multiples of
/// 1/64 pixel, so the expected areas are exact.
/// </summary>
public class OverlapCoverageTests(ITestOutputHelper output)
{
    /// <summary>The rounding of doubles the rasteriser's areas may carry.</summary>
    private const double Rounding = 1e-9;

    /// <summary>A whole-number box around every shape here (coordinates 0 to 8 pixels): no clip.</summary>
    private static readonly PixelBox Whole = new(-2, -2, 12, 12);

    /// <summary>A shape for the comparison: contours in 1/64 pixel and an optional clip box, also in 1/64 pixel.</summary>
    internal sealed record Shape(string Name, (long X, long Y)[][] Contours, (long Left, long Top, long Right, long Bottom)? Box = null);

    /// <summary>The rasteriser's coverage per cell and its flagged cells, for one fill rule, as the canvas computes them:
    /// the polygons clipped to the box first.</summary>
    private static (Dictionary<(int X, int Y), double> Coverage, HashSet<(int X, int Y)> Flagged) Rasterize(Shape shape, FillRule rule)
    {
        var box = shape.Box is { } b ? new PixelBox(b.Left / 64.0, b.Top / 64.0, b.Right / 64.0, b.Bottom / 64.0) : Whole;
        var polygons = shape.Contours
            .Where(contour => contour.Length > 0)
            .Select(contour => contour.Select(point => new Vertex(point.X / 64.0, point.Y / 64.0)).ToList())
            .ToList();
        var clipped = Clipping.ToBox(polygons, box);
        var rasterizer = new AreaRasterizer();
        var coverage = new Dictionary<(int X, int Y), double>();
        var flagged = rasterizer.FlaggedCells(clipped, box).ToHashSet();
        rasterizer.Rasterize(clipped, rule, box, (row, first, values) =>
        {
            for (var index = 0; index < values.Length; index++)
            {
                coverage[(first + index, row)] = values[index];
            }
        });

        return (coverage, flagged);
    }

    /// <summary>Statistics of a comparison: cells checked and the largest difference, flagged and not flagged.</summary>
    internal sealed class Tally
    {
        public long Unflagged { get; set; }

        public long Flagged { get; set; }

        public double LargestUnflagged { get; set; }

        public double LargestFlagged { get; set; }

        public List<string> Breaches { get; } = [];

        public override string ToString() =>
            $"{Unflagged} cells not flagged (largest difference {LargestUnflagged:E2}), {Flagged} flagged (largest difference {LargestFlagged:E2})";
    }

    /// <summary>Compares every cell of the shape's extent, under both rules, and adds to the tally; a cell not flagged
    /// that differs from the exact area is a breach.</summary>
    private static void Compare(Shape shape, Tally tally, bool flaggedMustMatch)
    {
        var exact = ExactCoverage.Areas(shape.Contours, shape.Box);
        foreach (var rule in new[] { FillRule.NonZero, FillRule.EvenOdd })
        {
            var (coverage, flagged) = Rasterize(shape, rule);
            foreach (var cell in coverage.Keys.Union(exact.Keys))
            {
                var expected = exact.TryGetValue(cell, out var areas) ? (rule == FillRule.NonZero ? areas.NonZero : areas.EvenOdd) : 0;
                var actual = coverage.TryGetValue(cell, out var value) ? value : 0;
                var difference = Math.Abs(actual - expected);
                if (flagged.Contains(cell))
                {
                    tally.Flagged++;
                    tally.LargestFlagged = Math.Max(tally.LargestFlagged, difference);
                    if (flaggedMustMatch && !(difference <= Rounding))
                    {
                        tally.Breaches.Add($"{shape.Name} {rule} flagged cell {cell}: {actual:R} instead of {expected:R}");
                    }
                }
                else
                {
                    tally.Unflagged++;
                    tally.LargestUnflagged = Math.Max(tally.LargestUnflagged, difference);
                    if (!(difference <= Rounding))
                    {
                        tally.Breaches.Add($"{shape.Name} {rule} cell {cell} (not flagged): {actual:R} instead of {expected:R}");
                    }
                }
            }
        }
    }

    private static (long X, long Y)[] Points(params (double X, double Y)[] points) => [.. points.Select(point => ((long)Math.Round(point.X * 64), (long)Math.Round(point.Y * 64)))];

    private static (long X, long Y)[] Rectangle(double x0, double y0, double x1, double y1, bool reversed = false) =>
        reversed ? Points((x0, y0), (x0, y1), (x1, y1), (x1, y0)) : Points((x0, y0), (x1, y0), (x1, y1), (x0, y1));

    private static (long, long, long, long) Box(double left, double top, double right, double bottom) =>
        ((long)Math.Round(left * 64), (long)Math.Round(top * 64), (long)Math.Round(right * 64), (long)Math.Round(bottom * 64));

    /// <summary>
    /// The degenerate cases: coinciding and partly coinciding edges, a vertex on another edge, crossings on a pixel's
    /// corner, side and vertex, vertices on whole pixels, horizontal and vertical edges on pixel lines and at pixel
    /// centres, zero-length edges and repeated points, contours of one and two points, spikes, a pinch point, a figure
    /// eight, a small loop in one pixel, nested contours both ways, a spiral, a long thin triangle, bars with edges on one
    /// line through pixel centres; clip boxes of zero width, of one pixel and with sides inside pixels; and three shapes
    /// made to catch a weaker rule: a horizontal edge between two crossing edges, a clip side between two crossing edges,
    /// and a run of edges through a polygon's start.
    /// </summary>
    internal static IEnumerable<Shape> Degenerate()
    {
        var square = Rectangle(1.25, 1.5, 4.75, 4.5);
        yield return new Shape("square twice", [square, square]);
        yield return new Shape("square and its reverse", [square, Rectangle(1.25, 1.5, 4.75, 4.5, reversed: true)]);
        yield return new Shape("square three times, clipped", [square, square, square], Box(1.5, 1.75, 3.25, 4.0));
        yield return new Shape("partly coinciding sides", [Rectangle(1, 1, 3.5, 3), Rectangle(2.25, 3, 5, 5.5)]);
        yield return new Shape("partly coinciding sides, same line", [Rectangle(1.1, 1.2, 3.6, 3.3), Rectangle(2.3, 1.2, 5.4, 4.6)]);
        yield return new Shape("vertex on another edge", [Points((1, 1), (5, 1), (5, 5), (1, 5)), Points((3, 1), (6.5, 3.25), (3, 4.5))]);
        yield return new Shape("crossing on a pixel corner", [Points((1, 1), (5, 5), (5, 1)), Points((1, 5), (5, 1.5), (1, 3))]);
        yield return new Shape("crossing on a pixel side", [Points((1, 2.5), (5, 2.5), (5, 2.75)), Points((3, 0.5), (3, 4.5), (2.5, 4.5))]);
        yield return new Shape("crossing at a vertex", [Points((1, 1), (3, 3), (1, 5)), Points((3, 3), (6, 1.5), (6, 4.5))]);
        yield return new Shape("vertices on whole pixels", [Points((1, 1), (4, 1), (4, 4), (1, 4)), Points((2, 2), (6, 2), (6, 6), (2, 6))]);
        yield return new Shape("horizontal edges on rows and at centres", [Rectangle(1.3, 2, 4.7, 3.5), Rectangle(2.1, 2.5, 5.9, 4)]);
        yield return new Shape("vertical edges on columns and at centres", [Rectangle(2, 1.3, 3.5, 4.7), Rectangle(2.5, 2.1, 4, 5.9)]);
        yield return new Shape("vertical edge on the left of the box", [Rectangle(2.25, 1, 4, 4), Rectangle(2.25, 2, 5, 5)], Box(2.25, 0.5, 6, 6));
        yield return new Shape("zero-length edges and repeated points", [Points((1, 1), (1, 1), (4.5, 1.25), (4.5, 1.25), (4.5, 1.25), (3, 4), (1, 1)), Points((2, 0.5), (2, 3.5), (2, 3.5), (5, 2))]);
        yield return new Shape("contours of one and two points", [Points((2.5, 2.5)), Points((1, 1), (4, 3.25)), Rectangle(1.5, 1.5, 3.5, 3.5)]);
        yield return new Shape("spike", [Points((1, 1), (5, 1), (5, 2.5), (2.25, 2.5), (5, 2.5), (5, 4), (1, 4))]);
        yield return new Shape("spike across a pixel", [Points((1, 1), (3.25, 2.75), (1.5, 1.25), (4.5, 1.5), (4.5, 4.5), (1, 4.5))]);
        yield return new Shape("pinch point", [Points((1, 1), (3, 3), (5, 1), (5, 5), (3, 3), (1, 5))]);
        yield return new Shape("figure eight", [Points((1, 1), (5, 5), (5, 1), (1, 5))]);
        yield return new Shape("figure eight centred on a pixel centre", [Points((1.5, 1.5), (3.5, 3.5), (3.5, 1.5), (1.5, 3.5))]);
        yield return new Shape("small loop in one pixel", [Points((1, 1), (2.75, 2.25), (2.25, 2.25), (2.5, 2.125), (2.625, 2.5), (5, 4), (1, 4))]);
        yield return new Shape("nested, same direction", [Rectangle(1, 1, 6, 6), Rectangle(2.3, 2.3, 4.7, 4.7)]);
        yield return new Shape("nested, a hole", [Rectangle(1, 1, 6, 6), Rectangle(2.3, 2.3, 4.7, 4.7, reversed: true)]);
        yield return new Shape("spiral of winding five", [Spiral(4, 4, 5)]);
        yield return new Shape("long thin triangle", [Points((0.5, 0.25), (7.5, 6.75), (0.5, 0.375)), Points((7.5, 0.25), (0.5, 6.75), (7.5, 0.5))]);
        yield return new Shape("bars with edges on one line through centres", [Rectangle(2.5, 1, 3.5, 8), Rectangle(2.5, 5, 3.5, 11)]);
        yield return new Shape("two crossing bars", [Rectangle(2.5, 1, 3.5, 8), Rectangle(1.25, 4.5, 6.75, 5.5)]);
        yield return new Shape("clip of zero width", [square, Rectangle(2, 2, 5, 5)], Box(2.5, 1, 2.5, 5));
        yield return new Shape("clip of zero height", [square, Rectangle(2, 2, 5, 5)], Box(1, 3.5, 5, 3.5));
        yield return new Shape("clip of one pixel", [square, Rectangle(2, 2, 5, 5)], Box(2, 2, 3, 3));
        yield return new Shape("clip inside one pixel", [square, Rectangle(2, 2, 5, 5)], Box(2.125, 2.25, 2.75, 2.875));
        yield return new Shape("clip with sides in pixels", [square, Rectangle(2, 2, 5, 5), Points((1, 5), (4.5, 1), (5, 5))], Box(1.375, 1.6875, 4.3125, 4.65625));

        // A goes up and right, H back left, B down and right across A: with H left out, A and B alone pass as two pieces.
        yield return new Shape("horizontal edge between crossing edges", [Points((2.125, 2.875), (2.875, 2.125), (2.25, 2.125), (2.75, 2.75), (2.75, 3.5), (1.5, 3.5))]);

        // A goes down to the clip's right side, the clipped polygon goes up that side, B goes down across A.
        yield return new Shape("clip side between crossing edges", [Points((2.25, 2.125), (3.5, 2.5), (3.5, 2.25), (2.375, 2.875), (2.25, 3.25))], Box(1, 1, 2.75, 5));

        // The polygon starts inside the pixel: its last and first edges are the run through it.
        yield return new Shape("run through the start", [Points((2.5, 2.5), (5, 1.5), (5, 4.5), (1, 4), (1, 1.5))]);
        yield return new Shape("run through the start, with an overlap", [Points((2.5, 2.5), (5, 1.5), (5, 4.5), (1, 4), (1, 1.5)), Rectangle(2.25, 2.25, 2.75, 3.75)]);
    }

    /// <summary>A square spiral that winds <paramref name="turns"/> times around its centre, closed back to its start.</summary>
    private static (long X, long Y)[] Spiral(double cx, double cy, int turns)
    {
        var points = new List<(double X, double Y)>();
        for (var turn = 0; turn < turns; turn++)
        {
            var r = 3.5 - (turn * 0.625);
            points.Add((cx - r, cy - r));
            points.Add((cx + r, cy - r));
            points.Add((cx + r, cy + r));
            points.Add((cx - r + 0.3125, cy + r));
        }

        return Points([.. points]);
    }

    /// <summary>
    /// Random shapes: one to five contours of three to eight vertices on grids of 1/64 to 1 pixel, so that vertices on
    /// pixel lines, coinciding edges and crossings on lines come up; every fourth with a clip box.
    /// </summary>
    internal static IEnumerable<Shape> Random(int seed, int count, double extent)
    {
        var random = new Random(seed);
        int[] grids = [1, 2, 4, 8, 16, 32, 64, 64, 64];
        for (var index = 0; index < count; index++)
        {
            var step = 64 / grids[random.Next(grids.Length)];
            var limit = (long)(extent * 64 / step);
            var contours = new (long X, long Y)[random.Next(1, 6)][];
            for (var c = 0; c < contours.Length; c++)
            {
                contours[c] = new (long X, long Y)[random.Next(3, 9)];
                for (var v = 0; v < contours[c].Length; v++)
                {
                    contours[c][v] = (random.NextInt64(limit + 1) * step, random.NextInt64(limit + 1) * step);
                }
            }

            (long, long, long, long)? box = null;
            if (index % 4 == 3)
            {
                var (x0, x1) = (random.NextInt64((long)(extent * 64) + 1), random.NextInt64((long)(extent * 64) + 1));
                var (y0, y1) = (random.NextInt64((long)(extent * 64) + 1), random.NextInt64((long)(extent * 64) + 1));
                box = (long.Min(x0, x1), long.Min(y0, y1), long.Max(x0, x1), long.Max(y0, y1));
            }

            yield return new Shape($"random {seed}/{index}", contours, box);
        }
    }

    /// <summary>Overlapping convex blobs a few pixels wide, as the strokes of a glyph overlap: many cells with one
    /// contour's edges only, and some with two or three.</summary>
    internal static IEnumerable<Shape> Blobs(int seed, int count)
    {
        var random = new Random(seed);
        for (var index = 0; index < count; index++)
        {
            var contours = new (long X, long Y)[random.Next(2, 5)][];
            for (var c = 0; c < contours.Length; c++)
            {
                var (cx, cy, r) = (random.Next(128, 384), random.Next(128, 384), random.Next(40, 200));
                var sides = random.Next(3, 9);
                var turn = random.Next(2) == 0 ? 1 : -1;
                contours[c] = new (long X, long Y)[sides];
                for (var v = 0; v < sides; v++)
                {
                    var angle = turn * 2 * Math.PI * v / sides;
                    contours[c][v] = ((long)(cx + (r * Math.Cos(angle))), (long)(cy + (r * Math.Sin(angle))));
                }
            }

            yield return new Shape($"blobs {seed}/{index}", contours, index % 3 == 2 ? Box(1.40625, 1.734375, 6.21875, 5.890625) : null);
        }
    }

    [Fact]
    public void The_degenerate_shapes_are_exact_in_every_cell_the_rule_does_not_flag()
    {
        var tally = new Tally();
        foreach (var shape in Degenerate())
        {
            Compare(shape, tally, flaggedMustMatch: false);
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell not flagged must be exact; all breaches: {string.Join("; ", tally.Breaches)}");
        tally.Flagged.Should().BePositive("the shapes are made to have pixels with more than two winding numbers");
    }

    [Fact]
    public void Random_overlapping_and_self_crossing_shapes_are_exact_in_every_cell_the_rule_does_not_flag()
    {
        var tally = new Tally();
        foreach (var shape in Random(20261119, 3000, 6).Concat(Random(7, 2000, 1.5)).Concat(Blobs(11, 500)))
        {
            Compare(shape, tally, flaggedMustMatch: false);
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell not flagged must be exact; all breaches: {string.Join("; ", tally.Breaches.Take(20))}");
        tally.Unflagged.Should().BeGreaterThan(100_000);
    }

    [Fact]
    public void The_exact_areas_of_a_simple_polygon_match_its_clipped_shoelace_areas()
    {
        // The oracle itself, on shapes whose areas the shoelace formula gives: one simple polygon per shape.
        (double X, double Y)[][] polygons =
        [
            [(1.25, 0.5), (6.75, 2.125), (4.5, 6.875), (0.5, 4.25)],
            [(0.5, 0.5), (7, 0.5), (7, 7), (4, 3.25), (0.5, 7)],
            [(2, 2), (6, 2), (6, 6), (2, 6)],
        ];
        foreach (var polygon in polygons)
        {
            var exact = ExactCoverage.Areas([Points(polygon)], null);
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var expected = Geometry.AreaInPixel(polygon, x, y);
                    var area = exact.TryGetValue((x, y), out var areas) ? areas : (0, 0);
                    area.NonZero.Should().BeApproximately(expected, 1e-12, $"pixel ({x}, {y})");
                    area.EvenOdd.Should().BeApproximately(expected, 1e-12, $"pixel ({x}, {y})");
                }
            }
        }
    }
}
