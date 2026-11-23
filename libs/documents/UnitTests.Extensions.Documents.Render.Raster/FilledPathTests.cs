using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Render.Raster;

/// <summary>
/// The page model's filled path (<see cref="FillPath"/>) in the raster back-end: filled through the canvas's exact
/// coverage by its fill rule, black on white, so a pixel's red value is 255 − 255 · the area of the shape in it. A path
/// of straight lines along the axes is exact (within one 8-bit step). A circle is four cubic curves (within 0.03 % of the
/// radius from the true circle, <see cref="TestPaths.Kappa"/>) flattened to chords within 1/64 pixel of the curve, so a
/// pixel the edge crosses (at most √2 pixels of edge) misses at most √2/64 of its area to the chords, 5.6 steps; the test
/// holds each pixel to <see cref="CurveBound"/> steps of the true circle's area in it.
/// </summary>
public class FilledPathTests
{
    /// <summary>The bound for a pixel of a curve's edge, in 8-bit steps: the chords' √2/64 (5.6 steps), the four-curve
    /// circle's 0.03 % of a radius of 20 pixels (1.4 steps), and the rounding to 8 bits (1).</summary>
    private const double CurveBound = 8;

    private static RgbaImage Drawn(double resolution, params DrawItem[] items)
    {
        var result = PageRasterizer.Render(new Page(new Size(60, 60), [.. items]), new RasterOptions(resolution) { Background = Color.White });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static double Ink(RgbaImage image, int x, int y) => 255 - image.Pixels[((y * image.Width) + x) * 4];

    /// <summary>The area of the disc (cx, cy, r) inside the pixel (x, y), all in pixels: the integral over x of the part of
    /// the disc's vertical chord inside the pixel's rows, by the midpoint rule over 4 000 strips (error far below 10^-4).</summary>
    private static double DiscArea(double cx, double cy, double r, int x, int y)
    {
        const int strips = 4000;
        var area = 0.0;
        for (var strip = 0; strip < strips; strip++)
        {
            var at = x + ((strip + 0.5) / strips);
            var half = (r * r) - ((at - cx) * (at - cx));
            if (half <= 0)
            {
                continue;
            }

            var h = Math.Sqrt(half);
            area += Math.Max(0, Math.Min(cy + h, y + 1) - Math.Max(cy - h, y)) / strips;
        }

        return area;
    }

    /// <summary>The largest difference over the image between the ink and 255 times the expected area, in 8-bit steps.</summary>
    private static double LargestDifference(RgbaImage image, Func<int, int, double> area)
    {
        var largest = 0.0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                largest = Math.Max(largest, Math.Abs(Ink(image, x, y) - (255 * area(x, y))));
            }
        }

        return largest;
    }

    private static double BoxArea(double x0, double y0, double x1, double y1, int x, int y) =>
        Math.Max(0, Math.Min(x1, x + 1) - Math.Max(x0, x)) * Math.Max(0, Math.Min(y1, y + 1) - Math.Max(y0, y));

    [Fact]
    public void A_square_cornered_path_covers_each_pixel_by_its_exact_area()
    {
        // At 120 dpi the L's edges fall inside pixels: two boxes, (10.1, 20.2)–(14.3, 21.9) and (10.1, 21.9)–(11.6, 24.05).
        const double scale = 120.0 / 72;
        var image = Drawn(120, TestPaths.Polygon(new Point(10.1, 20.2), new Point(14.3, 20.2), new Point(14.3, 21.9), new Point(11.6, 21.9), new Point(11.6, 24.05), new Point(10.1, 24.05)));

        LargestDifference(image, (x, y) =>
            BoxArea(10.1 * scale, 20.2 * scale, 14.3 * scale, 21.9 * scale, x, y) + BoxArea(10.1 * scale, 21.9 * scale, 11.6 * scale, 24.05 * scale, x, y))
            .Should().BeLessThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(20)]
    public void A_disc_covers_each_pixel_by_the_area_of_the_circle_in_it_within_the_bound(double radius)
    {
        // 72 dpi: a point is a pixel. The centre lies off the pixel grid.
        var (cx, cy) = (30.3, 29.6);
        var image = Drawn(72, TestPaths.Disc(cx, cy, radius));

        LargestDifference(image, (x, y) => DiscArea(cx, cy, radius, x, y)).Should().BeLessThanOrEqualTo(CurveBound);
        Ink(image, (int)cx, (int)cy).Should().Be(255, "the centre pixel is inside");
    }

    [Theory]
    [InlineData(PathFillRule.EvenOdd)]
    [InlineData(PathFillRule.NonZero)]
    public void A_ring_1_px_wide_covers_each_pixel_by_the_area_between_its_circles_within_the_bound(PathFillRule rule)
    {
        // Both circles are drawn the same way round: even-odd leaves the inner one empty; non-zero fills it, a disc.
        var (cx, cy) = (30.3, 29.6);
        var image = Drawn(72, TestPaths.Ring(cx, cy, 5.5, 4.5, rule));

        LargestDifference(image, (x, y) => DiscArea(cx, cy, 5.5, x, y) - (rule == PathFillRule.EvenOdd ? DiscArea(cx, cy, 4.5, x, y) : 0))
            .Should().BeLessThanOrEqualTo(CurveBound);
    }

    [Fact]
    public void A_filled_path_is_drawn_only_inside_the_clip_around_it()
    {
        var image = Drawn(72, new ClipStart(new Rect(new Point(0, 0), new Size(30, 60))), TestPaths.Disc(30.3, 29.6, 20), ClipEnd.Instance);

        LargestDifference(image, (x, y) => DiscArea(30.3, 29.6, 20, x, y) * BoxArea(0, 0, 30, 60, x, y)).Should().BeLessThanOrEqualTo(CurveBound);
        Ink(image, 31, 29).Should().Be(0);
    }

    [Fact]
    public void A_filled_path_beyond_the_limit_of_segments_breaks_the_page_model_contract()
    {
        var segments = Enumerable.Range(1, PageModelRules.MaxPathSegments + 1).Select(index => (PathSegment)new PathLine(new Point(index * 0.01, index % 2)));
        var path = new FillPath([new PathContour(new Point(0, 0), [.. segments])], PathFillRule.NonZero, Color.Black);

        PageRasterizer.Render(new Page(new Size(60, 60), [path]), new RasterOptions(72)).Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }
}
