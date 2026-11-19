using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Render.Raster;

/// <summary>
/// Whole pages against values computed here from the geometry alone: every pixel of the rendering equals the exact share
/// of it that the page's shapes cover (black on white: red = 255 − 255 · area), within one 8-bit step. The glyphs are
/// those of the Roboto subset (<see cref="TestFonts.RobotoSubset"/>), whose outlines are rectangles, so their exact ink
/// is the area of rectangles too: a glyph's design-unit rectangle maps to the page by font size / units per em (y up
/// becomes y down), placed at the run origin plus the glyph's offset, then to pixels by resolution / 72.
/// </summary>
public class PageGeometryTests
{
    private const double UnitsPerEm = 2048;

    /// <summary>'I': one rectangle, in design units (x0, y0, x1, y1).</summary>
    private static readonly Box LetterI = new(183, 0, 376, 1456);

    /// <summary>'Ɨ': a stem and a bar that overlap, as two contours wound the same way.</summary>
    private static readonly Box[] BarredI = [new(238, 0, 431, 1456), new(20, 651, 657, 803)];

    private readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public double AreaIn(int x, int y) =>
            Math.Max(0, Math.Min(X1, x + 1) - Math.Max(X0, x)) * Math.Max(0, Math.Min(Y1, y + 1) - Math.Max(Y0, y));

        public bool HasEdgeIn(int x, int y) =>
            (Crosses(X0, x) || Crosses(X1, x)) && Y0 < y + 1 && Y1 > y || (Crosses(Y0, y) || Crosses(Y1, y)) && X0 < x + 1 && X1 > x;

        private static bool Crosses(double edge, int cell) => edge > cell && edge < cell + 1;
    }

    /// <summary>A glyph's design-unit box on the page in pixels: x' = (origin + glyph offset + u · em) · scale, and
    /// y' = (origin + glyph offset − v · em) · scale, with em = font size / units per em.</summary>
    private static Box Placed(Box design, double fontSize, double originX, double originY, double scale)
    {
        var em = fontSize / UnitsPerEm;
        return new Box((originX + (design.X0 * em)) * scale, (originY - (design.Y1 * em)) * scale, (originX + (design.X1 * em)) * scale, (originY - (design.Y0 * em)) * scale);
    }

    private static GlyphRun Run(char character, double fontSize, Point origin, double x = 0, double y = 0)
    {
        var font = TestFonts.RobotoSubset;
        return new GlyphRun(font, fontSize, Color.Black, origin, [new PositionedGlyph(font.GlyphFor(new Rune(character)).Value, x, character.ToString(), y)]);
    }

    private static RgbaImage Draw(double width, double height, double resolution, params DrawItem[] items) =>
        PageRasterizer.Render(new Page(new Size(width, height), [.. items]), new RasterOptions(resolution) { Background = Color.White }).Value;

    /// <summary>Every pixel shows the exact area of the boxes' union in it (two boxes at most: inclusion–exclusion), within
    /// one step, also where an edge of each of two overlapping boxes passes.</summary>
    private static void ShouldShowExactly(RgbaImage image, Box[] boxes)
    {
        var bothEdges = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var area = boxes.Sum(box => box.AreaIn(x, y));
                if (boxes.Length == 2)
                {
                    var (a, b) = (boxes[0], boxes[1]);
                    area -= new Box(Math.Max(a.X0, b.X0), Math.Max(a.Y0, b.Y0), Math.Min(a.X1, b.X1), Math.Min(a.Y1, b.Y1)) is var both && both.X0 < both.X1 && both.Y0 < both.Y1 ? both.AreaIn(x, y) : 0;
                    bothEdges += a.HasEdgeIn(x, y) && b.HasEdgeIn(x, y) ? 1 : 0;
                }

                var difference = Math.Abs(image.Pixels[((y * image.Width) + x) * 4] - (255 - (255 * area)));
                difference.Should().BeLessThanOrEqualTo(1, $"pixel ({x}, {y}) covers {area:F4}");
            }
        }

        if (boxes.Length == 2)
        {
            bothEdges.Should().BePositive("the boxes overlap with an edge of each in some pixels");
        }
    }

    [Fact]
    public void A_glyph_whose_contours_overlap_fills_their_union_by_the_non_zero_rule()
    {
        // Ɨ at 48 pt, 150 dpi: the stem and the bar share a 9.4 × 7.4 px square, which the even-odd rule would leave
        // empty. Every pixel shows the union, also those an edge of each contour crosses, where the winding number is 0,
        // 1 and 2 side by side: within one step everywhere.
        const double scale = 150.0 / 72;
        var image = Draw(40, 60, 150, Run('Ɨ', 48, new Point(10.3, 50.2)));

        ShouldShowExactly(image, [.. BarredI.Select(box => Placed(box, 48, 10.3, 50.2, scale))]);
    }

    [Fact]
    public void A_glyph_offset_down_the_line_lands_that_far_below_the_origin()
    {
        // The glyph's own Y offset (a mark's placement, a superscript) is added to the run origin.
        const double scale = 150.0 / 72;
        var image = Draw(30, 50, 150, Run('I', 30, new Point(6.2, 30.1), y: 4.35));

        ShouldShowExactly(image, [Placed(LetterI, 30, 6.2, 30.1 + 4.35, scale)]);
    }

    [Fact]
    public void A_negative_glyph_offset_moves_the_glyph_up_the_page()
    {
        // Page coordinates have y down: a negative offset is a raise.
        const double scale = 150.0 / 72;
        var image = Draw(30, 50, 150, Run('I', 30, new Point(6.2, 40.1), y: -6.8));

        ShouldShowExactly(image, [Placed(LetterI, 30, 6.2, 40.1 - 6.8, scale)]);
    }

    [Fact]
    public void A_glyph_lands_at_the_run_origin_plus_its_horizontal_position()
    {
        const double scale = 200.0 / 72;
        var image = Draw(40, 30, 200, Run('I', 20, new Point(3.15, 22.6), x: 11.43));

        ShouldShowExactly(image, [Placed(LetterI, 20, 3.15 + 11.43, 22.6, scale)]);
    }

    [Fact]
    public void A_glyph_has_the_size_of_its_outline_at_the_font_size()
    {
        const double scale = 96.0 / 72;
        var image = Draw(40, 70, 96, Run('I', 61.7, new Point(4.4, 60.25)));

        ShouldShowExactly(image, [Placed(LetterI, 61.7, 4.4, 60.25, scale)]);
    }

    [Fact]
    public void A_rule_thinner_than_a_pixel_shows_as_the_share_of_the_pixels_it_covers()
    {
        // 0.4 pt at 72 dpi: rows of pixels 40% covered, which a renderer that drops or snaps thin shapes gets wrong.
        var image = Draw(24, 20, 72, new FillRectangle(new Rect(new Point(2.25, 10.3), new Size(18.5, 0.4)), Color.Black));

        ShouldShowExactly(image, [new Box(2.25, 10.3, 20.75, 10.7)]);
        image.Pixels[((10 * 24) + 10) * 4].Should().BeInRange(152, 154);
    }

    [Fact]
    public void A_glyph_offset_both_ways_lands_at_the_origin_plus_each_offset_on_its_own_axis()
    {
        // X along the line, Y down the page, both set at once: each is added to its own coordinate of the origin.
        const double scale = 150.0 / 72;
        var image = Draw(40, 50, 150, Run('I', 24, new Point(4.15, 36.3), x: 9.7, y: -5.35));

        ShouldShowExactly(image, [Placed(LetterI, 24, 4.15 + 9.7, 36.3 - 5.35, scale)]);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(300)]
    public void A_vertical_rule_narrower_than_a_pixel_shows_as_the_share_of_the_pixels_it_covers(double resolution)
    {
        // 0.2 pt wide: 0.27 pixel at 96 dpi, 0.83 at 300 dpi; columns partly covered, never dropped or widened.
        var scale = resolution / 72;
        var image = Draw(20, 24, resolution, new FillRectangle(new Rect(new Point(9.37, 2.5), new Size(0.2, 18.25)), Color.Black));

        ShouldShowExactly(image, [new Box(9.37 * scale, 2.5 * scale, 9.57 * scale, 20.75 * scale)]);
    }

    [Fact]
    public void A_horizontal_rule_thinner_than_a_third_of_a_pixel_shows_as_its_share()
    {
        // 0.2 pt at 96 dpi: 0.27 pixel, below the 0.3 pixel some renderers drop.
        const double scale = 96.0 / 72;
        var image = Draw(24, 20, 96, new FillRectangle(new Rect(new Point(2.25, 10.05), new Size(18.5, 0.2)), Color.Black));

        ShouldShowExactly(image, [new Box(2.25 * scale, 10.05 * scale, 20.75 * scale, 10.25 * scale)]);
        image.Pixels[((13 * image.Width) + 10) * 4].Should().BeLessThan(255, "the rule darkens the row it lies in");
    }

    [Fact]
    public void Two_different_images_on_one_page_each_show_their_own_samples()
    {
        // Decoded once per render call, by image: the colour image and the grey one (the same size) must not be mixed up.
        var colour = TestImages.Load("rgb8.png");
        var grey = TestImages.Load("grey8.png");
        var image = Draw(18, 7, 72,
            new DrawImage(new Rect(new Point(1, 1), new Size(7, 5)), colour),
            new DrawImage(new Rect(new Point(10, 1), new Size(7, 5)), grey),
            new DrawImage(new Rect(new Point(1, 1), new Size(7, 5)), colour));

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var (source, left) = x < 9 ? ((RasterImage)colour, 1) : ((RasterImage)grey, 10);
                var (column, row) = (x - left, y - 1);
                var inside = column >= 0 && row >= 0 && column < 7 && row < 5;
                var channels = source.ColorSpace == ImageColorSpace.Gray ? 1 : 3;
                byte[] expected = !inside ? [255, 255, 255]
                    : channels == 1 ? [.. Enumerable.Repeat(source.Samples[(row * 7) + column], 3)]
                    : [.. source.Samples.Skip(((row * 7) + column) * 3).Take(3)];
                image.Pixels.Skip(((y * image.Width) + x) * 4).Take(3).Should().Equal(expected, $"pixel ({x}, {y})");
            }
        }
    }

    [Fact]
    public void An_image_in_a_box_of_another_shape_is_stretched_across_and_down_by_its_own_factors()
    {
        // rgb8.png into a box 3 times its width and 2 times its height, at 72 dpi: each sample becomes a block of
        // 3 × 2 pixels, exactly.
        var png = TestImages.Load("rgb8.png");
        var raster = (RasterImage)png;
        var (width, height) = (raster.PixelWidth, raster.PixelHeight);
        var image = Draw((3 * width) + 3, (2 * height) + 4, 72, new DrawImage(new Rect(new Point(1, 2), new Size(3 * width, 2 * height)), png));

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var (column, row) = ((x - 1) / 3, (y - 2) / 2);
                var inside = x >= 1 && y >= 2 && column < width && row < height;
                byte[] expected = inside ? [.. raster.Samples.Skip(((row * width) + column) * 3).Take(3)] : [255, 255, 255];
                image.Pixels.Skip(((y * image.Width) + x) * 4).Take(3).Should().Equal(expected, $"pixel ({x}, {y})");
            }
        }
    }
}
