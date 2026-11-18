using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>The comparison of the raster reference tests on hand-made images.</summary>
public class RasterComparisonTests
{
    private static (int Width, int Height, bool[] AtEdge) NoEdges(int width, int height) => (width, height, new bool[width * height]);

    [Fact]
    public void Images_of_different_sizes_are_not_compared()
    {
        var small = (2, 2, 3, new byte[12]);
        var large = (3, 2, 3, new byte[18]);

        var compare = () => RasterComparison.Compare(small, large, NoEdges(2, 2));

        compare.Should().Throw<ArgumentException>().WithMessage("*2 x 2*3 x 2*");
    }

    [Fact]
    public void A_grey_image_counts_its_value_as_red_green_and_blue()
    {
        // Two pixels: grey 10 and 200 against RGB (10, 10, 10) and (200, 200, 200): no difference.
        var grey = (2, 1, 1, new byte[] { 10, 200 });
        var rgb = (2, 1, 3, new byte[] { 10, 10, 10, 200, 200, 200 });

        var distance = RasterComparison.Compare(grey, rgb, NoEdges(2, 1));

        distance.LargestAwayFromEdges.Should().Be(0);
        distance.MeanSquaredError.Should().Be(0);
    }

    [Fact]
    public void A_glyph_run_with_an_em_above_the_viewers_largest_keeps_its_baseline()
    {
        // At 72 dpi a point is a pixel: an em of 256 pixels and one of 256.01, both with the baseline at 10.3 pixels.
        var font = TestFonts.Regular;
        GlyphRun Run(double size) => new(font, size, Color.Black, new Point(5, 10.3), []);
        var page = new Page(new Size(100, 100), [Run(256), Run(256.01)]);

        var mupdf = RasterComparison.BaselinesOnPixelRows(page, 72, RasterComparison.MupdfRow, RasterComparison.MupdfLargestEmOnRow);
        var poppler = RasterComparison.BaselinesOnPixelRows(page, 72, RasterComparison.PopplerRow, RasterComparison.PopplerLargestEmOnRow);

        mupdf.Items.Cast<GlyphRun>().Select(run => run.Origin.Y).Should().Equal(10, 10.3);
        poppler.Items.Cast<GlyphRun>().Select(run => run.Origin.Y).Should().Equal(10, 10);
    }
}
