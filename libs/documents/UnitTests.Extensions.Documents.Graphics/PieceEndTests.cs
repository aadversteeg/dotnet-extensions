using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// Shapes off the 1/64 grid whose corner points lie on whole columns and rows, or one rounding step of a double beside
/// them, against <see cref="ExactCoverage"/> at the exact values of their doubles: where the interpolation along an edge
/// and the edge's own end point fall on the two sides of a pixel line, every pixel is still drawn, and exact to
/// <see cref="AreaRasterizer.KeepWithin"/>.
/// </summary>
public class PieceEndTests
{
    /// <summary>The rasteriser's values per cell, the polygons clipped to the box first as the canvas does; the error
    /// code when it refuses.</summary>
    private static (Dictionary<(int X, int Y), double> Coverage, string? Error) Rasterize((double X, double Y)[][] contours, (double Left, double Top, double Right, double Bottom)? clip, FillRule rule)
    {
        var box = clip is { } b ? new PixelBox(b.Left, b.Top, b.Right, b.Bottom) : new PixelBox(-2, -2, 12, 12);
        var polygons = contours.Select(contour => contour.Select(point => new Vertex(point.X, point.Y)).ToList()).ToList();
        var coverage = new Dictionary<(int X, int Y), double>();
        var result = new AreaRasterizer().Rasterize(Clipping.ToBox(polygons, box), rule, box, (row, first, values) =>
        {
            for (var index = 0; index < values.Length; index++)
            {
                coverage[(first + index, row)] = values[index];
            }
        });
        return (coverage, result.IsFailure ? result.Error.Code.ToString() : null);
    }

    /// <summary>Every cell of the shape against the exact areas, under both rules: a breach for a refusal or a value
    /// further than <see cref="AreaRasterizer.KeepWithin"/> from the exact one.</summary>
    private static void Compare(string name, (double X, double Y)[][] contours, (double Left, double Top, double Right, double Bottom)? clip, List<string> breaches)
    {
        var exact = ExactCoverage.Areas(contours, clip);
        foreach (var rule in new[] { FillRule.NonZero, FillRule.EvenOdd })
        {
            var (coverage, error) = Rasterize(contours, clip, rule);
            if (error is not null)
            {
                breaches.Add($"{name} {rule}: {error}");
                continue;
            }

            foreach (var cell in coverage.Keys.Union(exact.Keys))
            {
                var expected = exact.TryGetValue(cell, out var areas) ? (rule == FillRule.NonZero ? areas.NonZero : areas.EvenOdd) : 0;
                var actual = coverage.TryGetValue(cell, out var value) ? value : 0;
                if (!(Math.Abs(actual - expected) <= AreaRasterizer.KeepWithin))
                {
                    breaches.Add($"{name} {rule} cell {cell}: {actual:R} instead of {expected:R}");
                }
            }
        }
    }

    /// <summary>A coordinate from <paramref name="low"/> to <paramref name="high"/>: a whole number, one rounding step
    /// beside one (either way), within 1/256 of one, or a random double.</summary>
    private static double Coordinate(Random random, int low, int high) => random.Next(10) switch
    {
        < 3 => random.Next(low, high + 1),
        < 5 => Beside(random.Next(low, high + 1), up: false),
        < 7 => Beside(random.Next(low, high + 1), up: true),
        < 8 => random.Next(low, high + 1) + ((random.NextDouble() - 0.5) / 128),
        _ => low + (random.NextDouble() * (high - low)),
    };

    /// <summary>One rounding step beside a whole number other than 0 (beside 0 lie denormals, another kind of input).</summary>
    private static double Beside(int whole, bool up) => whole == 0 ? 0 : up ? Math.BitIncrement(whole) : Math.BitDecrement(whole);

    [Fact]
    public void Corner_points_on_and_one_rounding_step_beside_pixel_lines_give_the_exact_coverage()
    {
        // Fixed seed: the same 2,000 shapes every run, each two or three contours of three or four points that overlap,
        // a third of them under a clip box whose sides lie on, beside or between pixel lines.
        var random = new Random(1119);
        var breaches = new List<string>();
        for (var shape = 0; shape < 2000; shape++)
        {
            var contours = Enumerable.Range(0, 2 + random.Next(2))
                .Select(_ => Enumerable.Range(0, 3 + random.Next(2)).Select(_ => (Coordinate(random, -1, 6), Coordinate(random, -1, 6))).ToArray())
                .ToArray();
            (double, double, double, double)? clip = random.Next(3) == 0
                ? (Coordinate(random, 0, 2), Coordinate(random, 0, 2), Coordinate(random, 3, 5), Coordinate(random, 3, 5))
                : null;
            Compare($"shape {shape}", contours, clip, breaches);
        }

        breaches.Should().BeEmpty($"every shape is drawn and every pixel is exact; all breaches: {string.Join("; ", breaches)}");
    }

    /// <summary>Known shapes, each made as small as it goes: a corner point one rounding step left of column 1, the
    /// interpolation along its edge on column 1 itself. Before the repair the first three were refused with
    /// <c>Documents.Graphics.InexactCoverage</c> and the last three drawn with a pixel off by up to 6.8 × 10^-4.</summary>
    public static TheoryData<string, (double X, double Y)[][]> Known() => new()
    {
        { "refused at pixel (1, 3)", [[(0.21705200998906604, 3.951298131119133), (2.191674682400969, 0.9999999999999999), (0.9999999999999999, 3.5)], [(-0.317332056033114, 4.972276039408649), (5.45870408623419, 0.028199512990284337), (-0.5, 1.500262091169256)]] },
        { "refused at pixel (1, 2)", [[(1.261800546786655, 2), (3.0000000000000004, 1.5170311073386253), (0.9999999999999999, 2.0495005394562615)], [(3.0000000000305187, 1.5170311077837115), (1.000000000030518, 2.0495005399013477), (1.2618005468171731, 2.000000000445086)]] },
        { "refused at pixel (1, 3), second", [[(4.999999999999999, 2.5), (1, 4.000000000000001), (0.9999999999999999, 3.7251347078593144)], [(4.253564809567092, 0.2441777923350119), (1, 3), (3.3248191854566422, 3.7839684150107056)]] },
        { "pixel (1, 1) off by 5.1e-4", [[(2.1985628931776446, 0.15778256401316382), (0.9999999999999999, 1.0005671960068137), (0.7973591586562615, 1.2312032921385034)], [(1.594377199464653, 2.5495049876857108), (3.021628670870154, 1.0622657118655114), (0.32762981640530264, 2.1012129276530875)]] },
        { "pixel (1, 0) off by 1.2e-4", [[(0.9999999999999999, 0.9998751209174948), (2.4240207580961384, 0.525064217171196), (2.886352620500304, 2.161047226824354)], [(-0.2098167814360078, 2.37202809162998), (3.408560064345859, 2.941682018312477), (-0.15355390829665305, 4.066842118774932), (3.196573013531311, 0.24103215580854198)]] },
        { "pixel (1, 1) off by 6.8e-4", [[(4.536922534246427, 2.2746759691623395), (0.9999999999999999, 1.9993105275058625), (2.7243099071664316, 1.9945616065499192)], [(0.5675227840279802, 3.8684711288048286), (2.1122962846943625, 3.647205355412888), (2.7928540621850892, 3.5335643643203953), (5.632323336150647, 1.286170193593097)], [(0.11458496568472354, 2.2713948834088606), (-0.15262066160916377, 1.4981769744763973), (5.458216973793794, 1.5823852408595318), (1.8030670051477231, 1.3023882356017773)]] },
    };

    [Theory]
    [MemberData(nameof(Known))]
    public void A_known_shape_with_a_corner_point_one_rounding_step_beside_a_pixel_column_is_exact(string name, (double X, double Y)[][] contours)
    {
        // On a canvas of 8 x 8 pixels, as found: the canvas's edges clip the shape.
        var breaches = new List<string>();
        Compare(name, contours, (0, 0, 8, 8), breaches);
        breaches.Should().BeEmpty($"every pixel is exact; all breaches: {string.Join("; ", breaches)}");
    }

    private static Path PathOf((double X, double Y)[][] contours)
    {
        var builder = new PathBuilder();
        foreach (var contour in contours)
        {
            var subpath = builder.MoveTo(contour[0].X, contour[0].Y);
            foreach (var point in contour.Skip(1))
            {
                subpath = subpath.LineTo(point.X, point.Y);
            }

            subpath.Close();
        }

        return builder.ToPath();
    }

    [Theory]
    [InlineData(FillRule.NonZero)]
    [InlineData(FillRule.EvenOdd)]
    public void The_known_shapes_draw_through_the_canvas_and_a_pixel_beside_a_corner_point_gets_its_exact_coverage(FillRule rule)
    {
        foreach (var contours in Known().Select(row => ((double X, double Y)[][])row[1]))
        {
            var canvas = Canvas.Create(8, 8, Color.White).Value;
            var fill = canvas.Fill(PathOf(contours), Affine.Identity, rule, Color.Black);
            fill.IsSuccess.Should().BeTrue(fill.IsFailure ? fill.Error.Message : "");
            Canvas.Coverage(PathOf(contours), Affine.Identity, rule, new PixelBox(0, 0, 8, 8)).IsSuccess.Should().BeTrue();
        }

        // Exact coverage of pixel (1, 1): 0.354681, 90.44 of 255; drawn as 91 before the repair.
        var mask = Canvas.Coverage(PathOf(Known().Select(row => ((double X, double Y)[][])row[1]).ElementAt(3)), Affine.Identity, rule, new PixelBox(0, 0, 8, 8)).Value;
        mask.Values[(1 * 8) + 1].Should().Be(90);
    }
}
