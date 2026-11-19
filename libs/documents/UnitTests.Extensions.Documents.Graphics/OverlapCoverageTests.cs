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

    /// <summary>The rasteriser's coverage per cell and its flagged cells (true for a two-sided one, which keeps its
    /// integral), for one fill rule, as the canvas computes them: the polygons clipped to the box first.</summary>
    private static (Dictionary<(int X, int Y), double> Coverage, Dictionary<(int X, int Y), bool> Flagged) Rasterize(Shape shape, FillRule rule)
    {
        var box = shape.Box is { } b ? new PixelBox(b.Left / 64.0, b.Top / 64.0, b.Right / 64.0, b.Bottom / 64.0) : Whole;
        var polygons = shape.Contours
            .Where(contour => contour.Length > 0)
            .Select(contour => contour.Select(point => new Vertex(point.X / 64.0, point.Y / 64.0)).ToList())
            .ToList();
        var clipped = Clipping.ToBox(polygons, box);
        var rasterizer = new AreaRasterizer();
        var coverage = new Dictionary<(int X, int Y), double>();
        var flagged = rasterizer.FlaggedCells(clipped, box).ToDictionary(cell => (cell.Column, cell.Row), cell => cell.TwoSided);
        var result = rasterizer.Rasterize(clipped, rule, box, (row, first, values) =>
        {
            for (var index = 0; index < values.Length; index++)
            {
                coverage[(first + index, row)] = values[index];
            }
        });
        result.IsSuccess.Should().BeTrue($"{shape.Name} {rule}: {(result.IsFailure ? result.Error.Message : "")}");

        return (coverage, flagged);
    }

    /// <summary>Statistics of a comparison: cells checked and the largest difference, flagged and not flagged.</summary>
    internal sealed class Tally
    {
        public long Unflagged { get; set; }

        public long Flagged { get; set; }

        public double LargestUnflagged { get; set; }

        public double LargestFlagged { get; set; }

        public long CutFlagged { get; set; }

        public long TwoSided { get; set; }

        public double LargestTwoSided { get; set; }

        public long CutTwoSided { get; set; }

        public List<string> Breaches { get; } = [];

        public override string ToString() =>
            $"{Unflagged} cells not flagged (largest difference {LargestUnflagged:E2}), {Flagged} flagged (largest difference {LargestFlagged:E2}), {CutFlagged} flagged cells cut by the box; of the flagged, {TwoSided} two-sided (largest difference {LargestTwoSided:E2}, {CutTwoSided} cut by the box)";
    }

    /// <summary>Compares every cell of the shape's extent, under both rules, and adds to the tally; a cell that differs
    /// from the exact area is a breach.</summary>
    private static void Compare(Shape shape, Tally tally)
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
                if (flagged.TryGetValue(cell, out var twoSided))
                {
                    var cut = shape.Box is { } box && Cut(cell, box);
                    tally.Flagged++;
                    tally.LargestFlagged = Math.Max(tally.LargestFlagged, difference);
                    tally.CutFlagged += cut ? 1 : 0;
                    if (twoSided)
                    {
                        tally.TwoSided++;
                        tally.LargestTwoSided = Math.Max(tally.LargestTwoSided, difference);
                        tally.CutTwoSided += cut ? 1 : 0;
                    }

                    if (!(difference <= Rounding))
                    {
                        tally.Breaches.Add($"{shape.Name} {rule} flagged{(twoSided ? " two-sided" : "")} cell {cell}: {actual:R} instead of {expected:R}");
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

    /// <summary>True when a side of the box (in 1/64 pixel) lies inside the pixel.</summary>
    private static bool Cut((int X, int Y) cell, (long Left, long Top, long Right, long Bottom) box)
    {
        bool Inside(long side, int pixel) => side > pixel * 64L && side < (pixel + 1) * 64L;
        return Inside(box.Left, cell.X) || Inside(box.Right, cell.X) || Inside(box.Top, cell.Y) || Inside(box.Bottom, cell.Y);
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

    [Theory]
    [InlineData(FillRule.NonZero, new byte[] { 0, 128, 128, 0 })]
    [InlineData(FillRule.EvenOdd, new byte[] { 0, 0, 0, 0 })]
    public void Two_bars_on_top_of_each_other_cover_their_overlap_once(FillRule rule, byte[] overlapRow)
    {
        // Two bars from x 2.5 to 3.5, rows 1 to 8 and 5 to 11, wound the same way: a row of one bar and a row of both
        // cover columns 2 and 3 by half each under non-zero; under even-odd the rows of both are empty.
        var path = new PathBuilder()
            .MoveTo(2.5, 1).LineTo(3.5, 1).LineTo(3.5, 8).LineTo(2.5, 8).Close()
            .MoveTo(2.5, 5).LineTo(3.5, 5).LineTo(3.5, 11).LineTo(2.5, 11).Close()
            .ToPath();
        var mask = Canvas.Coverage(path, Affine.Identity, rule, new PixelBox(0, 0, 8, 12)).Value;

        Enumerable.Range(1, 4).Select(x => mask.At(x, 3)).Should().Equal((byte)0, (byte)128, (byte)128, (byte)0);
        for (var y = 5; y < 8; y++)
        {
            Enumerable.Range(1, 4).Select(x => mask.At(x, y)).Should().Equal(overlapRow, $"row {y}");
        }
    }

    [Fact]
    public void A_clip_box_one_denormal_wide_through_five_contours_gives_finite_exact_values()
    {
        // Five overlapping squares wound the same way, cut by a box from x 0 to the smallest double: under even-odd the
        // mean winding over such a part overflows; every value stays finite and at most the part's own area.
        var squares = Enumerable.Range(0, 5)
            .Select(index => new List<Vertex> { new(-3 - index, -2 + (0.7 * index)), new(3 + index, -2 + (0.7 * index)), new(3 + index, 4 + (0.9 * index)), new(-3 - index, 4 + (0.9 * index)) })
            .ToList();
        var box = new PixelBox(0, -1.5, double.Epsilon, 9.5);
        foreach (var rule in new[] { FillRule.NonZero, FillRule.EvenOdd })
        {
            var values = new List<double>();
            new AreaRasterizer().Rasterize(Clipping.ToBox(squares, box), rule, box, (_, _, coverage) => values.AddRange(coverage.ToArray())).IsSuccess.Should().BeTrue();

            values.Should().HaveCount(10).And.OnlyContain(value => double.IsFinite(value) && value >= 0 && value <= 1e-300, $"{rule}: {string.Join(" ", values)}");
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
    public void The_degenerate_shapes_are_exact_in_every_cell()
    {
        var tally = new Tally();
        foreach (var shape in Degenerate())
        {
            Compare(shape, tally);
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell must be exact; all breaches: {string.Join("; ", tally.Breaches)}");
        tally.Flagged.Should().BePositive("the shapes are made to have pixels with more than two winding numbers");
    }

    [Fact]
    public void Random_overlapping_and_self_crossing_shapes_are_exact_in_every_cell()
    {
        var tally = new Tally();
        foreach (var shape in Random(20261119, 3000, 6).Concat(Random(7, 2000, 1.5)).Concat(Blobs(11, 500)))
        {
            Compare(shape, tally);
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell must be exact; the first breaches: {string.Join("; ", tally.Breaches.Take(20))}");
        tally.Unflagged.Should().BeGreaterThan(100_000);
        tally.Flagged.Should().BeGreaterThan(50_000);
    }

    [Fact]
    public void Three_contours_in_one_pixel_cut_by_a_clip_box_are_exact()
    {
        // Three overlapping contours, wound the same way or not, under a clip box whose sides lie inside pixels: every
        // pixel the clip cuts, flagged or not, gets the exact area of the shape inside the box, under both rules.
        var tally = new Tally();
        foreach (var shape in Threes(19, 1500))
        {
            Compare(shape, tally);
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell must be exact; the first breaches: {string.Join("; ", tally.Breaches.Take(20))}");
        tally.Flagged.Should().BeGreaterThan(10_000);
        tally.CutFlagged.Should().BeGreaterThan(1_000, "pixels the clip box cuts with three contours in them");
    }

    /// <summary>Three convex contours through a common area, wound the same way (every other shape) or the first one
    /// reversed, each shape with a clip box whose four sides lie inside pixels.</summary>
    internal static IEnumerable<Shape> Threes(int seed, int count)
    {
        var random = new Random(seed);
        for (var index = 0; index < count; index++)
        {
            var contours = new (long X, long Y)[3][];
            for (var c = 0; c < 3; c++)
            {
                var (cx, cy, r) = (random.Next(192, 320), random.Next(192, 320), random.Next(60, 160));
                var sides = random.Next(3, 7);
                var turn = index % 2 == 1 && c == 0 ? -1 : 1;
                contours[c] = new (long X, long Y)[sides];
                for (var v = 0; v < sides; v++)
                {
                    var angle = (turn * 2 * Math.PI * v / sides) + random.NextDouble();
                    contours[c][v] = ((long)(cx + (r * Math.Cos(angle))), (long)(cy + (r * Math.Sin(angle))));
                }
            }

            long Inside() => (random.Next(1, 8) * 64) + random.Next(1, 64);
            var (x0, x1, y0, y1) = (Inside(), Inside(), Inside(), Inside());
            yield return new Shape($"three {seed}/{index}", contours, (long.Min(x0, x1), long.Min(y0, y1), long.Max(x0, x1) + 1, long.Max(y0, y1) + 1));
        }
    }

    /// <summary>
    /// Thin strokes as text has them, 1/64 to 1.5 pixels wide and 3 to 7 long, vertical, horizontal or at any angle, wound
    /// either way, each one contour of four corners: one stroke alone, two parallel strokes with a gap of 1/64 to 1.5
    /// pixels between them (wound the same way or not), or two strokes that cross; every third shape under a clip box with
    /// sides inside pixels. Most flagged pixels hold the two sides of one stroke or of the gap between two.
    /// </summary>
    internal static IEnumerable<Shape> Strokes(int seed, int count)
    {
        var random = new Random(seed);
        for (var index = 0; index < count; index++)
        {
            var (cx, cy) = (3 + (random.NextDouble() * 2), 3 + (random.NextDouble() * 2));
            var (length, width) = (3 + (random.NextDouble() * 4), random.Next(1, 97) / 64.0);
            var angle = random.Next(4) switch { 0 => 0, 1 => Math.PI / 2, _ => random.NextDouble() * Math.PI };
            var kind = index % 3;
            var contours = new List<(long X, long Y)[]> { Stroke(cx, cy, length, width, angle, random.Next(2) == 0) };
            if (kind == 1)
            {
                // A parallel stroke beyond a gap, across the first.
                var offset = (width / 2) + (random.Next(1, 97) / 64.0) + (width / 2);
                contours.Add(Stroke(cx - (Math.Sin(angle) * offset), cy + (Math.Cos(angle) * offset), length, width, angle, random.Next(2) == 0));
            }
            else if (kind == 2)
            {
                contours.Add(Stroke(cx, cy, length, random.Next(1, 97) / 64.0, angle + 0.3 + (random.NextDouble() * 2.5), random.Next(2) == 0));
            }

            yield return new Shape($"strokes {seed}/{index}", [.. contours], index % 4 == 3 ? Box(1.40625, 1.734375, 6.21875, 5.890625) : null);
        }
    }

    /// <summary>A straight stroke around (cx, cy): four corners on the 1/64 grid, in one direction or the other.</summary>
    private static (long X, long Y)[] Stroke(double cx, double cy, double length, double width, double angle, bool reversed)
    {
        var (ux, uy, nx, ny) = (Math.Cos(angle) * length / 2, Math.Sin(angle) * length / 2, -Math.Sin(angle) * width / 2, Math.Cos(angle) * width / 2);
        (double X, double Y)[] corners = [(cx - ux - nx, cy - uy - ny), (cx + ux - nx, cy + uy - ny), (cx + ux + nx, cy + uy + ny), (cx - ux + nx, cy - uy + ny)];
        return Points(reversed ? [.. corners.Reverse()] : corners);
    }

    /// <summary>
    /// The borders of the two-sided rule (<see cref="CellFlags.IsTwoSided"/>), each made so that a pixel sits on one side
    /// of a border, with the classification the rule must give it: two sides that run parallel, cross inside the pixel,
    /// touch in one point, meet on a pixel side or corner; two sides of the same direction (winding 0, 1 and 2); two sides
    /// inside another contour (winding 1 and 2, and −1 and 0); three sides; a stem cut by a clip side; a stem of zero
    /// width; a horizontal edge or a vertical edge of a third contour between two sides; the same for horizontal strokes;
    /// a side through the polygon's start.
    /// </summary>
    internal static IEnumerable<(Shape Shape, (int X, int Y) Pixel, bool TwoSided)> TwoSidedBorders()
    {
        var stem = Rectangle(2.25, 0.5, 2.75, 7.5);
        yield return (new Shape("stem, parallel sides", [stem]), (2, 3), true);
        yield return (new Shape("stem, parallel sides, reversed", [Rectangle(2.25, 0.5, 2.75, 7.5, reversed: true)]), (2, 3), true);
        yield return (new Shape("stem, slanted sides", [Points((2.125, 0.5), (2.5, 0.5), (2.875, 7.5), (2.5, 7.5))]), (2, 3), true);
        yield return (new Shape("gap between two stems", [Rectangle(0.5, 0.5, 2.25, 7.5), Rectangle(2.75, 0.5, 5, 7.5)]), (2, 3), true);
        yield return (new Shape("gap between two stems, wound against each other", [Rectangle(0.5, 0.5, 2.25, 7.5), Rectangle(2.75, 0.5, 5, 7.5, reversed: true)]), (2, 3), false);
        yield return (new Shape("stem inside a contour of the same direction", [Rectangle(0.5, 0.5, 6, 7.5), stem]), (2, 3), true);
        yield return (new Shape("stem inside a contour of the other direction", [Rectangle(0.5, 0.5, 6, 7.5, reversed: true), stem]), (2, 3), true);
        yield return (new Shape("stem whose sides cross in the pixel", [Points((2.125, 0.5), (2.875, 0.5), (2.125, 7), (2.875, 7))]), (2, 3), false);
        yield return (new Shape("stem whose sides touch in one point", [Points((2.125, 0.5), (2.875, 0.5), (2.5, 3.5), (2.875, 7.5), (2.125, 7.5), (2.5, 3.5))]), (2, 3), false);
        yield return (new Shape("sides of two contours that meet on the pixel's bottom side", [Points((0.5, 0.5), (2.25, 0.5), (2.5, 4), (0.5, 6)), Points((2.75, 0.5), (6, 0.5), (6, 6), (2.5, 4))]), (2, 3), false);
        yield return (new Shape("sides of two contours that meet on the pixel's corner", [Points((0.5, 0.5), (2.375, 0.5), (3, 4), (0.5, 6)), Points((2.875, 0.5), (6, 0.5), (6, 6), (3, 4))]), (2, 3), false);
        yield return (new Shape("slanted stem whose sides overlap in x, apart along its slope", [Points((0.875, 0.5), (1.125, 0.5), (4.625, 7.5), (4.375, 7.5))]), (2, 3), true);
        yield return (new Shape("slanted bar whose sides overlap in y, apart along its slope", [Points((0.5, 0.875), (0.5, 1.125), (7.5, 4.625), (7.5, 4.375))]), (3, 2), true);

        // Apart along a slope only by a line that leaves the pixel through its left side: the corners they cut off wind
        // −1 and +1 around a middle of 0.
        yield return (new Shape("two sides that cut off two corners of the pixel's left side, wound against each other", [Points((2.5, 2.5), (1.5, 4), (0.5, 2.5)), Points((2.5, 4.5), (1.5, 3), (0.5, 4.5))]), (2, 3), false);
        yield return (new Shape("two sides of the same direction (winding 0, 1, 2)", [Rectangle(2.25, 0.5, 4.5, 7.5), Rectangle(2.625, 0.5, 2.875, 7.5)]), (2, 3), false);
        yield return (new Shape("two sides going down, one going up (winding 0, 1, 2, 1)", [Rectangle(2.125, 0.5, 2.75, 7.5), Rectangle(2.375, 0.5, 5, 7.5)]), (2, 3), false);
        yield return (new Shape("three sides, down, up, down", [Rectangle(2.125, 0.5, 2.375, 7.5), Rectangle(2.625, 0.5, 5, 7.5)]), (2, 3), false);
        yield return (new Shape("stem cut by a vertical clip side", [stem], Box(0, 0, 2.5, 8)), (2, 3), true);
        yield return (new Shape("stem cut by a horizontal clip side", [stem], Box(0, 0, 8, 3.375)), (2, 3), false);
        yield return (new Shape("stem of zero width", [Points((2.5, 0.5), (2.5, 7.5), (2.5, 7.5), (2.5, 0.5)), Rectangle(2.25, 0.5, 2.375, 7.5)]), (2, 3), false);
        yield return (new Shape("stem with a horizontal edge of a third contour across", [stem, Rectangle(0.5, 3.5, 6, 6)]), (2, 3), false);
        yield return (new Shape("stem with a vertical edge of a third contour inside", [stem, Rectangle(2.5, 2.25, 6, 3.75)]), (2, 3), false);
        var bar = Rectangle(0.5, 3.25, 7.5, 3.75);
        yield return (new Shape("bar, parallel sides", [bar]), (3, 3), true);
        yield return (new Shape("bar, sloped sides", [Points((0.5, 3.125), (7.5, 3.25), (7.5, 3.75), (0.5, 3.625))]), (3, 3), true);
        yield return (new Shape("bar with a vertical edge of a third contour across", [bar, Rectangle(3.5, 0.5, 6, 6)]), (3, 3), false);
        yield return (new Shape("bar with a horizontal edge of a third contour inside", [bar, Rectangle(0.5, 3.5, 6, 6)]), (3, 3), false);
        yield return (new Shape("bar whose sides cross in the pixel", [Points((0.5, 3.125), (7, 3.875), (7, 3.125), (0.5, 3.875))]), (3, 3), false);
        yield return (new Shape("two bars of the same direction", [Rectangle(0.5, 3.125, 7.5, 3.875), Rectangle(0.5, 3.375, 7.5, 3.625)]), (3, 3), false);
        yield return (new Shape("bar cut by a horizontal clip side", [bar], Box(0, 0, 8, 3.5)), (3, 3), true);

        // The stem's left side is the polygon's last and first edge: one run in the numbering counted from half way round.
        yield return (new Shape("stem whose side runs through the polygon's start", [Points((2.25, 4.5), (2.25, 7.5), (2.75, 7.5), (2.75, 0.5), (2.25, 0.5))]), (2, 4), true);
    }

    [Fact]
    public void Thin_strokes_are_exact_in_every_cell_and_many_of_their_flagged_cells_are_two_sided()
    {
        var tally = new Tally();
        foreach (var shape in Strokes(20261120, 3000))
        {
            Compare(shape, tally);
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell must be exact; the first breaches: {string.Join("; ", tally.Breaches.Take(20))}");
        tally.Flagged.Should().BeGreaterThan(20_000);
        tally.TwoSided.Should().BeGreaterThan(10_000).And.BeGreaterThan(tally.Flagged / 3, "the strokes are made so that many flagged pixels hold two sides apart (the others: their ends and crossings)");
        tally.CutTwoSided.Should().BeGreaterThan(300, "two-sided pixels the clip box cuts");
    }

    [Fact]
    public void The_borders_of_the_two_sided_rule_are_exact_and_classified_as_the_rule_says()
    {
        var tally = new Tally();
        var classified = new List<string>();
        foreach (var (shape, pixel, twoSided) in TwoSidedBorders())
        {
            Compare(shape, tally);
            var (_, flagged) = Rasterize(shape, FillRule.NonZero);
            var actual = flagged.TryGetValue(pixel, out var kind) ? (kind ? "two-sided" : "arranged") : "not flagged";
            if (actual != (twoSided ? "two-sided" : "arranged"))
            {
                classified.Add($"{shape.Name}: pixel {pixel} {actual}, expected {(twoSided ? "two-sided" : "arranged")}");
            }
        }

        output.WriteLine(tally.ToString());
        tally.Breaches.Should().BeEmpty($"every cell must be exact; all breaches: {string.Join("; ", tally.Breaches)}");
        classified.Should().BeEmpty($"each shape's pixel lies on a known side of a border of the rule; all that do not: {string.Join("; ", classified)}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rotated_image_quad_is_exact_without_flags_or_a_charge(bool clipped)
    {
        // An image's outline as the canvas draws it (AreaRasterizer.RasterizeConvex: no flags, no arrangement, no charge):
        // a rectangle under a rotation and a scale, one convex quad, so its winding number is 0 or one value of one sign.
        // Its integral is the exact area in every pixel under both rules, with and without a clip box whose sides lie in
        // pixels (the clipped quad is still convex).
        var breaches = new List<string>();
        var compared = 0;
        foreach (var angle in new[] { 0.0, 0.1, 0.5, Math.PI / 4, 1.0, 2.2, 3.0, 4.4 })
        {
            var (c, n) = (Math.Cos(angle), Math.Sin(angle));
            (double X, double Y) Corner(double u, double v) => (4 + (u * c) - (v * n), 4 + (u * n) + (v * c));
            var quad = Points(Corner(-2.7, -1.9), Corner(2.7, -1.9), Corner(2.7, 1.9), Corner(-2.7, 1.9));
            (long, long, long, long)? cut = clipped ? Box(1.40625, 1.734375, 6.21875, 5.890625) : null;
            var exact = ExactCoverage.Areas([quad], cut);
            var box = cut is { } b ? new PixelBox(b.Item1 / 64.0, b.Item2 / 64.0, b.Item3 / 64.0, b.Item4 / 64.0) : Whole;
            var polygons = Clipping.ToBox([[.. quad.Select(point => new Vertex(point.X / 64.0, point.Y / 64.0))]], box);
            foreach (var rule in new[] { FillRule.NonZero, FillRule.EvenOdd })
            {
                var coverage = new Dictionary<(int X, int Y), double>();
                new AreaRasterizer().RasterizeConvex(polygons, rule, box, (row, first, values) =>
                {
                    for (var index = 0; index < values.Length; index++)
                    {
                        coverage[(first + index, row)] = values[index];
                    }
                });
                foreach (var cell in coverage.Keys.Union(exact.Keys))
                {
                    var expected = exact.TryGetValue(cell, out var areas) ? (rule == FillRule.NonZero ? areas.NonZero : areas.EvenOdd) : 0;
                    var actual = coverage.TryGetValue(cell, out var value) ? value : 0;
                    compared++;
                    if (!(Math.Abs(actual - expected) <= Rounding))
                    {
                        breaches.Add($"angle {angle} {rule} cell {cell}: {actual:R} instead of {expected:R}");
                    }
                }
            }
        }

        breaches.Should().BeEmpty($"every cell must be exact; all breaches: {string.Join("; ", breaches)}");
        compared.Should().BeGreaterThan(clipped ? 300 : 600);
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
