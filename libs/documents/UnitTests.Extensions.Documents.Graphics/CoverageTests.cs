using Ave.Extensions.Documents.Graphics;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The rasteriser's coverage against exact areas: every pixel of a shape with a known area gets that area to the
/// rounding of one 8-bit step (|coverage − 255·area| ≤ ½), the coverages add up to the shape's area, and the fill
/// rules, the clip box and shared edges behave as the geometry says. The expected areas come from
/// <see cref="Geometry"/>, not from the code under test.
/// </summary>
public class CoverageTests
{
    /// <summary>Half an 8-bit step, plus room for the rounding of doubles.</summary>
    private const double OneRounding = 0.5 + 1e-9;

    private static readonly PixelBox Area = new(0, 0, 64, 64);

    /// <summary>The coverage mask; the arguments are always in range here.</summary>
    private static CoverageMask Cover(Path path, Affine transform, FillRule rule, PixelBox clip) => Canvas.Coverage(path, transform, rule, clip).Value;

    private static Path Polygon(IReadOnlyList<(double X, double Y)> points)
    {
        var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1))
        {
            builder.LineTo(point.X, point.Y);
        }

        return builder.Close().ToPath();
    }

    private static void ShouldMatchExactAreas(IReadOnlyList<(double X, double Y)> polygon, CoverageMask mask)
    {
        for (var y = mask.Top; y < mask.Top + mask.Height; y++)
        {
            for (var x = mask.Left; x < mask.Left + mask.Width; x++)
            {
                var expected = 255 * Geometry.AreaInPixel(polygon, x, y);
                Math.Abs(mask.At(x, y) - expected).Should().BeLessThanOrEqualTo(OneRounding, $"pixel ({x}, {y}) of {string.Join(" ", polygon)}");
            }
        }
    }

    private static double Sum(CoverageMask mask) => mask.Values.Sum(value => (double)value) / 255;

    [Theory]
    [InlineData(10, 10, 20, 15)]
    [InlineData(10.25, 3.5, 7.125, 0.375)]
    [InlineData(0.1, 0.9, 0.3, 0.05)]
    [InlineData(5.75, 40.0625, 50.5, 13.3)]
    public void An_axis_aligned_rectangle_covers_each_pixel_by_its_overlap(double x, double y, double width, double height)
    {
        var mask = Cover(Path.Rectangle(x, y, width, height), Affine.Identity, FillRule.NonZero, Area);

        ShouldMatchExactAreas([(x, y), (x + width, y), (x + width, y + height), (x, y + height)], mask);
    }

    [Fact]
    public void Rectangles_at_random_fractional_positions_get_their_exact_coverage()
    {
        var random = new Random(20261118);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var x = random.NextDouble() * 40;
            var y = random.NextDouble() * 40;
            var width = random.NextDouble() * 20;
            var height = random.NextDouble() * 20;

            var mask = Cover(Path.Rectangle(x, y, width, height), Affine.Identity, FillRule.NonZero, Area);

            ShouldMatchExactAreas([(x, y), (x + width, y), (x + width, y + height), (x, y + height)], mask);
            Sum(mask).Should().BeApproximately(width * height, mask.Values.Count(value => value is > 0 and < 255) * 0.5 / 255 + 1e-9);
        }
    }

    [Fact]
    public void Rotated_rectangles_and_triangles_get_their_exact_coverage()
    {
        var random = new Random(7);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var points = attempt % 2 == 0 ? RotatedRectangle(random) : Triangle(random);

            var mask = Cover(Polygon(points), Affine.Identity, FillRule.NonZero, Area);

            ShouldMatchExactAreas(points, mask);
            Sum(mask).Should().BeApproximately(Math.Abs(Geometry.SignedArea(points)), mask.Values.Count(value => value is > 0 and < 255) * 0.5 / 255 + 1e-9);
        }
    }

    [Fact]
    public void A_transform_maps_the_path_before_it_is_covered()
    {
        // A 2 × 1 rectangle at the origin, scaled by 3.5 and 2, rotated by a quarter turn and moved: the rectangle
        // (10, 20)–(17, 23) with the corners the transform gives.
        var transform = Affine.Scale(3.5, 2).Then(new Affine(0, 1, -1, 0, 0, 0)).Then(Affine.Translate(30.25, 5.5));
        var mask = Cover(Path.Rectangle(0, 0, 2, 1), transform, FillRule.NonZero, Area);

        ShouldMatchExactAreas([(30.25, 5.5), (30.25, 12.5), (28.25, 12.5), (28.25, 5.5)], mask);
    }

    [Theory]
    [InlineData(20, 20, 15)]
    [InlineData(31.3, 12.7, 6.25)]
    [InlineData(5.5, 5.5, 1.2)]
    public void A_circle_of_cubic_curves_covers_its_area_within_the_flattening_bound(double cx, double cy, double r)
    {
        // Four cubic arcs, as PDF draws a circle; the area compared with is the area of these curves (exact), so the
        // only allowed difference is the flattening (at most 1/64 pixel from the curve, times the perimeter) and the
        // rounding of the partly covered pixels to 8 bits.
        const double k = 0.5522847498307936;
        var curves = new[]
        {
            ((cx + r, cy), (cx + r, cy + (k * r)), (cx + (k * r), cy + r), (cx, cy + r)),
            ((cx, cy + r), (cx - (k * r), cy + r), (cx - r, cy + (k * r)), (cx - r, cy)),
            ((cx - r, cy), (cx - r, cy - (k * r)), (cx - (k * r), cy - r), (cx, cy - r)),
            ((cx, cy - r), (cx + (k * r), cy - r), (cx + r, cy - (k * r)), (cx + r, cy)),
        };
        var builder = new PathBuilder().MoveTo(cx + r, cy);
        foreach (var (_, p1, p2, p3) in curves)
        {
            builder.CubicTo(p1.Item1, p1.Item2, p2.Item1, p2.Item2, p3.Item1, p3.Item2);
        }

        var mask = Cover(builder.Close().ToPath(), Affine.Identity, FillRule.NonZero, Area);

        var edgePixels = mask.Values.Count(value => value is > 0 and < 255);
        var bound = (2 * Math.PI * r * (1.0 / 64)) + (edgePixels * 0.5 / 255);
        Sum(mask).Should().BeApproximately(Geometry.CubicArea(curves), bound);
    }

    [Fact]
    public void Non_zero_fills_overlaps_and_even_odd_leaves_them_out()
    {
        // Two squares drawn the same way round overlap in (20, 20)–(30, 30): winding 2 there.
        var path = new PathBuilder()
            .MoveTo(10, 10).LineTo(30, 10).LineTo(30, 30).LineTo(10, 30).Close()
            .MoveTo(20, 20).LineTo(40, 20).LineTo(40, 40).LineTo(20, 40).Close()
            .ToPath();

        var nonZero = Cover(path, Affine.Identity, FillRule.NonZero, Area);
        var evenOdd = Cover(path, Affine.Identity, FillRule.EvenOdd, Area);

        nonZero.At(25, 25).Should().Be(255);
        evenOdd.At(25, 25).Should().Be(0);
        nonZero.At(15, 15).Should().Be(255);
        evenOdd.At(15, 15).Should().Be(255);
        evenOdd.At(35, 35).Should().Be(255);
        Sum(nonZero).Should().BeApproximately(700, 1e-9);
        Sum(evenOdd).Should().BeApproximately(600, 1e-9);
    }

    [Fact]
    public void The_coverage_of_a_pixel_is_never_more_than_the_whole_pixel()
    {
        // Winding 2 in (20.5, 20.5)–(30, 30): the integral there is 2 per pixel, the coverage 1.
        var polygons = new List<List<Vertex>>
        {
            new() { new(10, 10), new(30, 10), new(30, 30), new(10, 30) },
            new() { new(20.5, 20.5), new(40, 20.5), new(40, 40), new(20.5, 40) },
        };
        var largest = 0.0;
        var rasterizer = new AreaRasterizer();

        rasterizer.Rasterize(polygons, FillRule.NonZero, new PixelBox(0, 0, 64, 64), (_, _, coverage) => largest = Math.Max(largest, coverage.ToArray().Max()));

        largest.Should().Be(1);
    }

    [Fact]
    public void A_contour_drawn_the_other_way_round_is_a_hole_under_both_rules()
    {
        var path = new PathBuilder()
            .MoveTo(10, 10).LineTo(40, 10).LineTo(40, 40).LineTo(10, 40).Close()
            .MoveTo(20.5, 20.5).LineTo(20.5, 30.5).LineTo(30.5, 30.5).LineTo(30.5, 20.5).Close()
            .ToPath();

        foreach (var rule in new[] { FillRule.NonZero, FillRule.EvenOdd })
        {
            var mask = Cover(path, Affine.Identity, rule, Area);

            mask.At(25, 25).Should().Be(0);
            mask.At(20, 25).Should().Be(128, "half of the pixel is in the hole: 127.5 rounds up");
            Sum(mask).Should().BeApproximately(900 - 100, mask.Values.Count(value => value is > 0 and < 255) * 0.5 / 255);
        }
    }

    [Fact]
    public void Winding_three_is_covered_under_both_rules_and_two_under_non_zero_only()
    {
        var square = new PathBuilder().MoveTo(10, 10).LineTo(20, 10).LineTo(20, 20).LineTo(10, 20).Close();
        var twice = square.MoveTo(10, 10).LineTo(20, 10).LineTo(20, 20).LineTo(10, 20).Close().ToPath();
        var thrice = new PathBuilder()
            .MoveTo(10, 10).LineTo(20, 10).LineTo(20, 20).LineTo(10, 20).Close()
            .MoveTo(10, 10).LineTo(20, 10).LineTo(20, 20).LineTo(10, 20).Close()
            .MoveTo(10, 10).LineTo(20, 10).LineTo(20, 20).LineTo(10, 20).Close()
            .ToPath();

        Cover(twice, Affine.Identity, FillRule.EvenOdd, Area).At(15, 15).Should().Be(0);
        Cover(twice, Affine.Identity, FillRule.NonZero, Area).At(15, 15).Should().Be(255);
        Cover(thrice, Affine.Identity, FillRule.EvenOdd, Area).At(15, 15).Should().Be(255);
    }

    [Fact]
    public void The_clip_box_cuts_the_shape_at_fractional_edges()
    {
        var clip = new PixelBox(12.25, 10.5, 30.75, 25.125);

        var mask = Cover(Path.Rectangle(5.5, 8.3, 40, 12.6), Affine.Identity, FillRule.NonZero, clip);

        // The rectangle and the box overlap in (12.25, 10.5)–(30.75, 20.9).
        ShouldMatchExactAreas([(12.25, 10.5), (30.75, 10.5), (30.75, 20.9), (12.25, 20.9)], mask);
        mask.Left.Should().Be(12);
        mask.Width.Should().Be(31 - 12);
    }

    [Fact]
    public void Curves_the_clip_box_cannot_reach_change_nothing_inside_it()
    {
        // A circle of radius 2000 around a point far to the left: only a sliver of it reaches the box, and every curve
        // that does not reach the box becomes one straight line.
        var circle = Circle(-1990, 20, 2000);
        var small = new PixelBox(0, 0, 32, 40);

        var clipped = Cover(circle, Affine.Identity, FillRule.NonZero, small);
        var whole = Cover(circle, Affine.Identity, FillRule.NonZero, new PixelBox(-4000, -2000, 32, 2100));

        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                clipped.At(x, y).Should().Be(whole.At(x, y), $"pixel ({x}, {y})");
            }
        }

        clipped.At(5, 20).Should().Be(255);
        clipped.At(12, 20).Should().Be(0);
    }

    [Fact]
    public void Two_shapes_that_share_an_edge_cover_it_exactly_once()
    {
        // A square cut along a slanted line into two quadrilaterals: on every pixel the two coverages add up to the
        // square's, so there is no gap (sum smaller) and no double coverage (sum larger), to the two 8-bit roundings.
        var random = new Random(11);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var top = 10 + (random.NextDouble() * 20);
            var bottom = 10 + (random.NextDouble() * 20);
            var first = Polygon([(10.3, 10.3), (top, 10.3), (bottom, 29.6), (10.3, 29.6)]);
            var second = Polygon([(top, 10.3), (30.7, 10.3), (30.7, 29.6), (bottom, 29.6)]);
            var whole = Cover(Path.Rectangle(10.3, 10.3, 20.4, 19.3), Affine.Identity, FillRule.NonZero, Area);
            var left = Cover(first, Affine.Identity, FillRule.NonZero, Area);
            var right = Cover(second, Affine.Identity, FillRule.NonZero, Area);

            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    (left.At(x, y) + right.At(x, y) - whole.At(x, y)).Should().BeInRange(-1, 1, $"pixel ({x}, {y})");
                }
            }

            // In one path the shared edge cancels exactly: the coverage is the square's.
            var both = new PathBuilder()
                .MoveTo(10.3, 10.3).LineTo(top, 10.3).LineTo(bottom, 29.6).LineTo(10.3, 29.6).Close()
                .MoveTo(top, 10.3).LineTo(30.7, 10.3).LineTo(30.7, 29.6).LineTo(bottom, 29.6).Close()
                .ToPath();
            Cover(both, Affine.Identity, FillRule.NonZero, Area).Values.Should().Equal(whole.Values);
        }
    }

    [Fact]
    public void A_flattened_curve_stays_within_the_tolerance()
    {
        var random = new Random(3);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            (double X, double Y) P() => (random.NextDouble() * 200, random.NextDouble() * 200);
            var (p0, p1, p2, p3) = (P(), P(), P(), P());
            var path = new PathBuilder().MoveTo(p0.X, p0.Y).CubicTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y).ToPath();

            var polygon = Flattening.Polygons(path, Affine.Identity, new PixelBox(-1000, -1000, 1000, 1000)).Single();

            for (var step = 0; step <= 1000; step++)
            {
                var t = step / 1000.0;
                var u = 1 - t;
                var x = (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X);
                var y = (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y);
                DistanceToPolyline(polygon, x, y).Should().BeLessThanOrEqualTo(Flattening.Tolerance + 1e-9);
            }
        }
    }

    [Fact]
    public void A_huge_curve_is_cut_into_at_most_the_limit_of_pieces()
    {
        var path = new PathBuilder().MoveTo(0, 0).QuadraticTo(1e12, 5e11, 0, 1e12).ToPath();

        var polygon = Flattening.Polygons(path, Affine.Identity, new PixelBox(0, 0, 1e13, 1e13)).Single();

        polygon.Should().HaveCount(Flattening.MaxPiecesPerCurve + 1);
    }

    [Fact]
    public void Known_limit_far_from_the_origin_a_pixel_can_be_one_8_bit_step_off()
    {
        // The same triangle at the origin and moved by 2^30 − 8 pixels on both axes: its points are multiples of 2^-20, so
        // they are the same points exactly, and the box holds the whole triangle. The exact share of pixel (4, 4) is
        // 0.272549028878163 (255 times that is 69.5000024), so 70 is right; at the origin it is 70. Far out the row
        // integral interpolates x where an edge crosses a row line with only about 2^-22 of a pixel left in a double,
        // and the pixel is drawn 69 with success. When that is repaired, this test changes to 70.
        const double offset = (1 << 30) - 8;
        (double X, double Y)[] triangle = [(6.0392045974731445, 0.3825511932373047), (3.751575469970703, 7.563164710998535), (6.058171272277832, 7.639092445373535)];
        var near = Cover(Polygon(triangle), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 8, 8));
        var far = Canvas.Coverage(Polygon([.. triangle.Select(point => (point.X + offset, point.Y + offset))]), Affine.Identity, FillRule.NonZero, new PixelBox(offset, offset, offset + 8, offset + 8));

        near.At(4, 4).Should().Be(70);
        far.IsSuccess.Should().BeTrue();
        far.Value.At((int)offset + 4, (int)offset + 4).Should().Be(69);
    }

    private static double DistanceToPolyline(List<Vertex> polyline, double x, double y)
    {
        var best = double.PositiveInfinity;
        for (var index = 0; index + 1 < polyline.Count; index++)
        {
            var (a, b) = (polyline[index], polyline[index + 1]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var length = (dx * dx) + (dy * dy);
            var t = length == 0 ? 0 : Math.Clamp((((x - a.X) * dx) + ((y - a.Y) * dy)) / length, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(a.X + (t * dx) - x, 2) + Math.Pow(a.Y + (t * dy) - y, 2)));
        }

        return best;
    }

    private static Path Circle(double cx, double cy, double r)
    {
        const double k = 0.5522847498307936;
        return new PathBuilder().MoveTo(cx + r, cy)
            .CubicTo(cx + r, cy + (k * r), cx + (k * r), cy + r, cx, cy + r)
            .CubicTo(cx - (k * r), cy + r, cx - r, cy + (k * r), cx - r, cy)
            .CubicTo(cx - r, cy - (k * r), cx - (k * r), cy - r, cx, cy - r)
            .CubicTo(cx + (k * r), cy - r, cx + r, cy - (k * r), cx + r, cy)
            .Close().ToPath();
    }

    private static List<(double X, double Y)> RotatedRectangle(Random random)
    {
        var (cx, cy) = (16 + (random.NextDouble() * 32), 16 + (random.NextDouble() * 32));
        var (w, h) = (1 + (random.NextDouble() * 20), 0.2 + (random.NextDouble() * 12));
        var angle = random.NextDouble() * Math.PI;
        var (c, s) = (Math.Cos(angle), Math.Sin(angle));
        return [.. new[] { (-w, -h), (w, -h), (w, h), (-w, h) }.Select(p => (cx + (c * p.Item1 / 2) - (s * p.Item2 / 2), cy + (s * p.Item1 / 2) + (c * p.Item2 / 2)))];
    }

    private static List<(double X, double Y)> Triangle(Random random) =>
        [.. Enumerable.Range(0, 3).Select(_ => (2 + (random.NextDouble() * 60), 2 + (random.NextDouble() * 60)))];
}
