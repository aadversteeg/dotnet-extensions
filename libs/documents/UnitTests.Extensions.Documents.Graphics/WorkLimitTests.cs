using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.ErrorPaths;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The work limits of exact coverage (<see cref="Canvas.MaxFillWork"/>, <see cref="Canvas.MaxCanvasWork"/>,
/// <see cref="Canvas.MaxRowPieces"/>): a shape over a limit is refused with <c>Documents.Graphics.TooMuchWork</c>, the
/// work asked for and the limit in its metadata, and nothing of it is painted.
/// </summary>
public class WorkLimitTests
{
    /// <summary>A thousand bars 0.1 pixel wide through the centre of pixel (50, 50), each its own contour, turned
    /// evenly: two thousand pieces of edges in that pixel.</summary>
    private static Path ThousandBars()
    {
        var builder = new PathBuilder();
        for (var index = 0; index < 1000; index++)
        {
            var angle = Math.PI * index / 1000;
            var (c, s) = (Math.Cos(angle), Math.Sin(angle));
            double X(double u, double v) => 50.5 + (u * c) - (v * s);
            double Y(double u, double v) => 50.5 + (u * s) + (v * c);
            builder.MoveTo(X(-20, -0.05), Y(-20, -0.05)).LineTo(X(20, -0.05), Y(20, -0.05)).LineTo(X(20, 0.05), Y(20, 0.05)).LineTo(X(-20, 0.05), Y(-20, 0.05)).Close();
        }

        return builder.ToPath();
    }

    /// <summary>A star polygon {n/k} of radius 0.45 inside pixel (10, 10): one contour that crosses itself, all n
    /// edges in one pixel.</summary>
    internal static Path StarInOnePixel(int n, int k)
    {
        var builder = new PathBuilder();
        SubpathBuilder? subpath = null;
        for (var index = 0; index < n; index++)
        {
            var angle = 2 * Math.PI * ((long)index * k % n) / n;
            var (x, y) = (10.5 + (0.45 * Math.Cos(angle)), 10.5 + (0.45 * Math.Sin(angle)));
            subpath = subpath is null ? builder.MoveTo(x, y) : subpath.LineTo(x, y);
        }

        return subpath!.Close().ToPath();
    }

    private static void ShouldBeTooMuchWork(Error error, long limit)
    {
        error.Code.Should().Be(GraphicsErrorCodes.TooMuchWork);
        // Value: the work asked for and the limit, "asked>limit".
        var parts = error.Metadata![ErrorMetadataKeys.Value].ToString()!.Split('>');
        parts.Should().HaveCount(2);
        long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture).Should().Be(limit);
        long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(limit);
    }

    [Fact]
    public void A_thousand_bars_through_one_pixel_are_refused_and_nothing_is_painted()
    {
        var canvas = Canvas.Create(101, 101, Color.White).Value;
        var before = canvas.ToImage().Pixels;

        var result = canvas.Fill(ThousandBars(), Affine.Identity, FillRule.NonZero, Color.Black);

        result.IsFailure.Should().BeTrue();
        ShouldBeTooMuchWork(result.Error, Canvas.MaxFillWork);
        canvas.ToImage().Pixels.Should().Equal(before, "a refused fill paints no pixel");
        Canvas.Coverage(ThousandBars(), Affine.Identity, FillRule.EvenOdd, new PixelBox(0, 0, 101, 101)).Error.Code.Should().Be(GraphicsErrorCodes.TooMuchWork);
    }

    [Fact]
    public void A_contour_crossing_itself_a_thousand_times_in_one_pixel_is_refused()
    {
        var canvas = Canvas.Create(21, 21, Color.White).Value;
        var before = canvas.ToImage().Pixels;

        var result = canvas.Fill(StarInOnePixel(1001, 500), Affine.Identity, FillRule.EvenOdd, Color.Black);

        ShouldBeTooMuchWork(result.Error, Canvas.MaxFillWork);
        canvas.ToImage().Pixels.Should().Equal(before);
    }

    [Fact]
    public void A_star_just_inside_the_fill_limit_is_drawn()
    {
        // {643/321}: 643 edges in one pixel, a charge just below 2^27 (133,957,476); 644 pieces would be over it, so
        // {645/322} is refused.
        CellArrangement.Charge(643).Should().Be(133_957_476).And.BeLessThan(Canvas.MaxFillWork);
        CellArrangement.Charge(644).Should().BeGreaterThan(Canvas.MaxFillWork);

        Canvas.Coverage(StarInOnePixel(643, 321), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 21, 21)).IsSuccess.Should().BeTrue();
        ShouldBeTooMuchWork(Canvas.Coverage(StarInOnePixel(645, 322), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 21, 21)).Error, Canvas.MaxFillWork);
    }

    [Fact]
    public void A_row_with_more_pieces_in_flagged_pixels_than_the_limit_is_refused()
    {
        // Two thin overlapping bars along one row of 70,000 pixels: every pixel of the row holds four pieces of two
        // contours, 280,000 in the row, over 2^18; the charge itself (72 per pixel) is far below the fill limit.
        var path = new PathBuilder()
            .MoveTo(0.25, 0.2).LineTo(69_999.75, 0.2).LineTo(69_999.75, 0.4).LineTo(0.25, 0.4).Close()
            .MoveTo(0.25, 0.3).LineTo(69_999.75, 0.3).LineTo(69_999.75, 0.6).LineTo(0.25, 0.6).Close()
            .ToPath();

        var result = Canvas.Coverage(path, Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 70_000, 1));

        ShouldBeTooMuchWork(result.Error, Canvas.MaxRowPieces);
        CellArrangement.Charge(4).Should().Be(72);
    }

    [Fact]
    public void The_fills_of_one_canvas_share_one_limit()
    {
        // Each star has a charge just below the fill limit; two of them fit the canvas limit, the third does not and is
        // not painted, and a fill of a plain shape after it still is.
        var canvas = Canvas.Create(21, 21, Color.White).Value;
        var star = StarInOnePixel(639, 319);
        var charge = CellArrangement.Charge(639);
        var fits = (int)(Canvas.MaxCanvasWork / charge);
        for (var index = 0; index < fits; index++)
        {
            canvas.Fill(star, Affine.Identity, FillRule.NonZero, new Color(0, 0, (byte)index)).IsSuccess.Should().BeTrue($"fill {index + 1} of {fits}");
        }

        var before = canvas.ToImage().Pixels;
        var refused = canvas.Fill(star, Affine.Identity, FillRule.NonZero, Color.Black);

        ShouldBeTooMuchWork(refused.Error, Canvas.MaxCanvasWork);
        canvas.ToImage().Pixels.Should().Equal(before);
        canvas.Fill(Path.Rectangle(1, 1, 3, 3), Affine.Identity, FillRule.NonZero, Color.Black).IsSuccess.Should().BeTrue("a shape without flagged pixels costs nothing");
    }

    /// <summary>A box around every shape of the bound's tests: no clip.</summary>
    private static readonly PixelBox Around = new(-16, -16, 32, 32);

    /// <summary>The one amount the rasteriser puts to the caller before any row (the bound from the edges alone, or the
    /// exact charge where that bound does not show the shape within the row limit; 0 when it asks nothing), and the
    /// charge it counted.</summary>
    private static (long Asked, long Charge) AskedAndCharged(List<List<Vertex>> polygons, string name)
    {
        var asked = new List<long>();
        var result = new AreaRasterizer().Rasterize(polygons, FillRule.NonZero, Around, static (_, _, _) => { }, charge =>
        {
            asked.Add(charge);
            return null;
        });

        result.IsSuccess.Should().BeTrue($"{name}: {(result.IsFailure ? result.Error.Message : "")}");

        // Nothing is asked of a shape with no rows to report (a star folded onto a horizontal line).
        asked.Count.Should().BeLessThanOrEqualTo(1, name);
        return (asked.Count == 1 ? asked[0] : 0, result.Value);
    }

    /// <summary>
    /// The shapes whose charge comes nearest the bound: stars {n/k} that cross themselves, from a hundredth of a pixel to
    /// four pixels in radius, and a contour with a copy of itself shifted by nothing or next to nothing (sometimes with a
    /// third copy one rounding step off), whose coinciding edges put all their pieces in the same pixels.
    /// </summary>
    private static IEnumerable<(string Name, List<List<Vertex>> Polygons)> NearTheBound(int seed, int count)
    {
        var random = new Random(seed);
        double[] shifts = [0, 1e-15, 1e-12, 1e-9, 1e-6, 1e-3];
        for (var index = 0; index < count; index++)
        {
            if (index % 2 == 0)
            {
                var (cx, cy) = (4 + (random.NextDouble() * 8), 4 + (random.NextDouble() * 8));
                var n = 5 + random.Next(12);
                var k = 2 + random.Next(n / 2);
                var radius = Math.Pow(10, -2 + (2.6 * random.NextDouble()));
                var star = new List<Vertex>();
                for (var corner = 0; corner < n; corner++)
                {
                    var angle = 2 * Math.PI * corner * k / n;
                    star.Add(new Vertex(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle))));
                }

                yield return ($"star {seed}/{index} {{{n}/{k}}} radius {radius:G3}", [star]);
                continue;
            }

            var contour = new List<Vertex>();
            var snap = random.Next(2) == 0;
            for (var corner = 3 + random.Next(5); corner > 0; corner--)
            {
                var (x, y) = (random.NextDouble() * 8, random.NextDouble() * 8);
                contour.Add(snap ? new Vertex(Math.Round(x * 64) / 64, Math.Round(y * 64) / 64) : new Vertex(x, y));
            }

            var shift = shifts[random.Next(shifts.Length)];
            var (sx, sy) = (shift * (random.NextDouble() - 0.5), shift * (random.NextDouble() - 0.5));
            List<List<Vertex>> polygons = [contour, [.. contour.Select(point => new Vertex(point.X + sx, point.Y + sy))]];
            if (random.Next(3) == 0)
            {
                polygons.Add([.. contour.Select(point => new Vertex(Math.BitIncrement(point.X), Math.BitDecrement(point.Y)))]);
            }

            yield return ($"copies {seed}/{index} shifted {shift:G1}", polygons);
        }
    }

    [Fact]
    public void The_bound_put_to_the_caller_before_any_row_is_never_below_the_charge()
    {
        // The bound decides alone whether the first pass, which counts the exact charge, runs: a bound below the charge
        // would admit a shape over the limit. On the shapes where the charge comes nearest it, it never does; the nearest
        // is a star {10/5} folded onto a line, ten coinciding edges a rounding apart, at 0.68 of the bound, so a bound
        // made smaller by a factor of 1.5 fails here.
        var (bounded, largest, nearest) = (0, 0.0, "");
        foreach (var (name, polygons) in NearTheBound(20261119, 2000))
        {
            var (asked, charge) = AskedAndCharged(polygons, name);
            asked.Should().BeGreaterThanOrEqualTo(charge, $"{name}: the charge {charge} must not pass the bound {asked}");
            if (asked > charge && charge > 0)
            {
                // The bound itself was asked (the exact charge, when asked, is the one counted).
                bounded++;
                (largest, nearest) = (double)charge / asked > largest ? ((double)charge / asked, name) : (largest, nearest);
            }
        }

        bounded.Should().BeGreaterThan(1000, "most of the shapes are admitted on the bound alone");
        largest.Should().BeGreaterThan(0.6, $"the set holds shapes whose charge comes near the bound (nearest: {nearest}, {largest:G3})");
    }

    [Fact]
    public void Horizontal_edges_count_in_the_bound()
    {
        // Two contours that each run back and forth 21 times along a horizontal line inside row 0, closed by two slanted
        // edges: the four pixels of the line hold 44 pieces each, 42 of them horizontal, so the charge is 4 · c(44). A
        // bound from the slanted edges alone (what they can mark, times c(E)/E over all 46 edges) is far below it.
        List<List<Vertex>> polygons = [BackAndForth(0.5, 3.5, 0.25), BackAndForth(0.75, 3.25, 0.375)];

        var (asked, charge) = AskedAndCharged(polygons, "back and forth");

        charge.Should().Be(4 * CellArrangement.Charge(44));
        var slanted = (2 * (1.5 + 4)) + (2 * (1.25 + 4)) + 16;
        var withoutHorizontal = (long)Math.Ceiling(slanted * ((double)CellArrangement.Charge(46) / 46)) + 1;
        withoutHorizontal.Should().BeLessThan(charge, "the slanted edges alone cannot bound this charge");
        asked.Should().BeGreaterThan(charge, "the bound, with what the horizontal edges can mark, admits it on its own");

        static List<Vertex> BackAndForth(double left, double right, double y)
        {
            var contour = new List<Vertex>();
            for (var turn = 0; turn <= 21; turn++)
            {
                contour.Add(new Vertex(turn % 2 == 0 ? left : right, y));
            }

            contour.Add(new Vertex(2, 0.75));
            return contour;
        }
    }
}
