using System.Diagnostics;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.ErrorPaths;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The library's one rule for arguments (<see cref="Canvas"/> remarks): a value it cannot draw with is a
/// <c>Documents.Graphics.*</c> error value, never an exception; inside the stated ranges nothing throws and the work of a
/// fill is bounded by the pixels, not by the shape. The hostile values are the review's (review 38, B2, B3).
/// </summary>
public class ArgumentRuleTests
{
    private static readonly double[] Hostile =
    [
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, -double.MaxValue, 1e308, -1e308,
        1.7e308, 1e300, Canvas.MaxCoordinate * 1.0001, double.Epsilon, -0.0, 0, 1, -3.5,
    ];

    private static Path Polygon(params (double X, double Y)[] points)
    {
        var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1))
        {
            builder.LineTo(point.X, point.Y);
        }

        return builder.Close().ToPath();
    }

    private static Canvas Canvas8() => Canvas.Create(8, 8, Color.White).Value;

    [Theory]
    [InlineData(0, 0, 4, 4, "255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255")]
    [InlineData(2, 2, 2, 2, "0,0,0,0,0,0,0,0,0,0,255,255,0,0,255,255")]
    [InlineData(2, 2, 1.5, 2, "0,0,0,0,0,0,0,0,0,0,255,128,0,0,255,128")]
    [InlineData(2, 2, 9, 9, "0,0,0,0,0,0,0,0,0,0,255,255,0,0,255,255")]
    public void A_shape_that_reaches_the_right_edge_of_the_box_in_its_last_row_is_covered(double x, double y, double width, double height, string expected)
    {
        // Review 38, B3: these threw IndexOutOfRangeException, because the rasteriser reported one column more than the
        // shape's extent.
        var mask = Canvas.Coverage(Path.Rectangle(x, y, width, height), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 4, 4));

        mask.IsSuccess.Should().BeTrue();
        string.Join(",", mask.Value.Values).Should().Be(expected);
        mask.Value.Width.Should().Be(4);
    }

    [Fact]
    public void A_shape_larger_than_a_fractional_box_is_covered_inside_the_box()
    {
        var mask = Canvas.Coverage(Path.Rectangle(-5, -5, 20, 20), Affine.Identity, FillRule.NonZero, new PixelBox(0.5, 0.5, 3.5, 3.5)).Value;

        // Pixels 0 and 3 are half inside the box (128), pixels 1 and 2 whole (255); corners a quarter (64).
        string.Join(",", mask.Values).Should().Be("64,128,128,64,128,255,255,128,128,255,255,128,64,128,128,64");
    }

    [Fact]
    public void Random_clipped_polygons_that_reach_the_right_edge_get_their_exact_coverage()
    {
        var random = new Random(38);
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var (width, height) = (random.Next(1, 12), random.Next(1, 12));
            var points = Enumerable.Range(0, random.Next(3, 7))
                .Select(_ => ((random.NextDouble() * (width + 6)) - 3, (random.NextDouble() * (height + 6)) - 3))
                .Append((width + random.NextDouble() * 3, random.NextDouble() * height))
                .ToList();
            if (Math.Abs(Geometry.SignedArea(points)) < 1e-9 || !IsSimple(points))
            {
                continue;
            }

            var mask = Canvas.Coverage(Polygon([.. points]), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, width, height));

            mask.IsSuccess.Should().BeTrue();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    Math.Abs(mask.Value.At(x, y) - (255 * Geometry.AreaInPixel(points, x, y))).Should().BeLessThanOrEqualTo(0.5 + 1e-9, $"attempt {attempt}, pixel ({x}, {y})");
                }
            }
        }
    }

    [Fact]
    public void Values_out_of_range_are_error_values()
    {
        var canvas = Canvas8();

        Code(canvas.Fill(Path.Rectangle(0, 0, double.NaN, 1), Affine.Identity, FillRule.NonZero, Color.Black)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.Fill(Path.Rectangle(0, 0, 4, 1e308), Affine.Scale(2, 2), FillRule.NonZero, Color.Black)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.Fill(Path.Rectangle(0, 1e308, 4, 1e308), Affine.Identity, FillRule.NonZero, Color.Black)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.Fill(Polygon((-1e308, -1e308), (1e308, -1e308), (0, 1e308)), Affine.Identity, FillRule.NonZero, Color.Black)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Scale(double.PositiveInfinity, 1), FillRule.NonZero, Color.Black)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Identity, (FillRule)7, Color.Black)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Identity, FillRule.NonZero, Color.Black, new PixelBox(2, 0, 1, 1))).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(canvas.DrawImage(RgbaImage.Create(1, 1, [0, 0, 0, 255]).Value, new Affine(double.NaN, 0, 0, 1, 0, 0))).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(Canvas.Coverage(Path.Rectangle(3e9, 3e9, 2, 2), Affine.Identity, FillRule.NonZero, new PixelBox(3e9, 3e9, 3e9 + 4, 3e9 + 4))).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(Canvas.Coverage(Path.Rectangle(1, 1, 2, 2), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 1e6, 1e6))).Should().Be(GraphicsErrorCodes.TooLarge);
        Code(Canvas.Create(0, 4, null)).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(Canvas.Create(1 << 14, (1 << 12) + 1, null)).Should().Be(GraphicsErrorCodes.TooLarge);
        Code(RgbaImage.Create(2, 2, [1, 2, 3])).Should().Be(GraphicsErrorCodes.InvalidValue);
        Code(RgbaImage.Create(-1, 2, [])).Should().Be(GraphicsErrorCodes.InvalidValue);
        canvas.ToImage().Pixels.Should().OnlyContain(value => value == 255, "nothing was drawn by a refused call");
    }

    [Fact]
    public void No_hostile_value_makes_a_public_member_throw()
    {
        foreach (var a in Hostile)
        {
            foreach (var b in Hostile)
            {
                var canvas = Canvas8();
                var image = RgbaImage.Create(2, 1, [1, 2, 3, 255, 4, 5, 6, 128]).Value;
                var calls = new Func<object>[]
                {
                    () => canvas.Fill(Path.Rectangle(a, b, a, b), Affine.Identity, FillRule.NonZero, Color.Black),
                    () => canvas.Fill(Path.Rectangle(1, 1, 3, 3), new Affine(a, b, 0, 1, a, b), FillRule.EvenOdd, Color.Black),
                    () => canvas.Fill(new PathBuilder().MoveTo(a, 0).QuadraticTo(b, a, 4, 4).CubicTo(a, b, b, a, 1, 7).ToPath(), Affine.Identity, FillRule.NonZero, Color.Black, new PixelBox(0, 0, 8, 8)),
                    () => canvas.Fill(Path.Rectangle(0, 0, 4, 4), Affine.Identity, FillRule.NonZero, Color.Black, new PixelBox(a, b, a, b)),
                    () => canvas.DrawImage(image, new Affine(a, 0, 0, b, 1, 1)),
                    () => canvas.DrawImage(image, new Affine(1, a, b, 1, 0, 0), new PixelBox(0, 0, a, b)),
                    () => Canvas.Coverage(Path.Rectangle(a, b, 2, 2), Affine.Scale(a, b), FillRule.NonZero, new PixelBox(0, 0, 8, 8)),
                    () => Canvas.Coverage(Path.Rectangle(0, 0, 2, 2), Affine.Identity, FillRule.NonZero, new PixelBox(a, a, b, b)),
                    () => Canvas.Create((int)Math.Clamp(a, int.MinValue, int.MaxValue), (int)Math.Clamp(b, int.MinValue, int.MaxValue), Color.Black),
                    () => new Affine(a, b, b, a, a, b).Inverse() ?? Affine.Identity,
                    () => new PixelBox(a, b, a, b).Intersect(new PixelBox(b, a, b, a)),
                };

                foreach (var call in calls)
                {
                    call.Invoking(invoke => invoke()).Should().NotThrow($"values {a} and {b}");
                }
            }
        }
    }

    [Fact]
    public void Path_builder_order_rules()
    {
        FluentActions.Invoking(() => new PathBuilder().LineTo(1, 1)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new PathBuilder().Close()).Should().Throw<InvalidOperationException>();

        // After Close the current point is the subpath's start (ISO 32000-1 §8.5.2.1), so a line may follow: it starts a
        // new subpath there. Two triangles that share the start (0, 0).
        var path = new PathBuilder().MoveTo(0, 0).LineTo(4, 0).LineTo(0, 4).Close().LineTo(0, -4).LineTo(-4, 0).ToPath();
        var mask = Canvas.Coverage(path, Affine.Translate(5, 5), FillRule.NonZero, new PixelBox(0, 0, 10, 10)).Value;

        mask.Values.Sum(value => value / 255.0).Should().BeApproximately(16, mask.Values.Count(value => value is > 0 and < 255) * 0.5 / 255);
    }

    [Fact]
    public void Inside_the_range_a_huge_shape_costs_what_the_canvas_costs()
    {
        // A rectangle of 10^140 pixels each way and one that just covers the canvas fill the same pixels; the first must
        // not cost more than the second (it is clipped in floating point before any number becomes an integer). Medians of
        // several runs, with room for the noise of a shared machine.
        var huge = Path.Rectangle(-1e140, -1e140, 2e140, 2e140);
        var page = Path.Rectangle(0, 0, 1000, 1000);

        double Median(Path path)
        {
            var times = new List<double>();
            for (var run = 0; run < 7; run++)
            {
                var canvas = Canvas.Create(1000, 1000, Color.White).Value;
                var watch = Stopwatch.StartNew();
                canvas.Fill(path, Affine.Identity, FillRule.NonZero, Color.Black).IsSuccess.Should().BeTrue();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }

            times.Sort();
            return times[3];
        }

        var pageTime = Median(page);
        var hugeTime = Median(huge);
        hugeTime.Should().BeLessThan((pageTime * 3) + 20, $"huge {hugeTime:F1} ms, page {pageTime:F1} ms");

        var a = Canvas.Create(50, 50, Color.White).Value;
        var b = Canvas.Create(50, 50, Color.White).Value;
        a.Fill(huge, Affine.Identity, FillRule.NonZero, Color.Black);
        b.Fill(Path.Rectangle(0, 0, 50, 50), Affine.Identity, FillRule.NonZero, Color.Black);
        a.ToImage().Pixels.Should().Equal(b.ToImage().Pixels);
    }

    private static ErrorCode Code<T>(Ave.Extensions.Functional.Result<T, Error> result)
    {
        result.IsFailure.Should().BeTrue();
        return result.Error.Code;
    }

    /// <summary>True when no two non-adjacent edges of the closed polygon cross (so its winding is 0 or ±1).</summary>
    private static bool IsSimple(List<(double X, double Y)> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 2; j < points.Count; j++)
            {
                if (i == 0 && j == points.Count - 1)
                {
                    continue;
                }

                if (Cross(points[i], points[(i + 1) % points.Count], points[j], points[(j + 1) % points.Count]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double Side((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) => ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));
        return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
    }
}
