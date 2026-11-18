using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Render.Raster;

/// <summary>
/// The page model's clips (<see cref="ClipStart"/>, <see cref="ClipEnd"/>) in the raster back-end: THE NESTING RULE FOR
/// MARKERS (<see cref="DrawItem"/>) and the limit of <see cref="PageModelRules.MaxClipDepth"/> clips are the page-model
/// contract both back-ends check (<see cref="PageModelRules"/>), so a page model the PDF back-end refuses for them, the
/// raster back-end refuses too, under its own code. What a clip lets through: only what lies inside its rectangle (and
/// inside every clip around it) is painted, and where a clip edge crosses a pixel the pixel shows the exact area of the
/// shape inside the clip, as for any shape edge (black on white: red = 255 − 255 · area, within one 8-bit step).
/// </summary>
public class ClipTests
{
    private static readonly ClipStart Clip = new(new Rect(new Point(0, 20), new Size(100, 30)));

    private static Page PageOf(params DrawItem[] items) => new(new Size(100, 100), [.. items]);

    private static Result<RgbaImage, Ave.Extensions.ErrorPaths.Error> Draw(Page page) => PageRasterizer.Render(page, new RasterOptions(72));

    private static DrawItem[] Nested(int depth) =>
    [
        .. Enumerable.Repeat<DrawItem>(Clip, depth),
        new FillRectangle(new Rect(new Point(10, 25), new Size(50, 10)), Color.Black),
        .. Enumerable.Repeat<DrawItem>(ClipEnd.Instance, depth),
    ];

    /// <summary>An axis-aligned box in pixels and its exact area in a pixel.</summary>
    private readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public Box Intersect(Box other) => new(
            Math.Max(X0, other.X0), Math.Max(Y0, other.Y0), Math.Max(Math.Max(X0, other.X0), Math.Min(X1, other.X1)), Math.Max(Math.Max(Y0, other.Y0), Math.Min(Y1, other.Y1)));

        public double AreaIn(int x, int y) =>
            Math.Max(0, Math.Min(X1, x + 1) - Math.Max(X0, x)) * Math.Max(0, Math.Min(Y1, y + 1) - Math.Max(Y0, y));
    }

    private static RgbaImage Drawn(double width, double height, double resolution, params DrawItem[] items)
    {
        var result = PageRasterizer.Render(new Page(new Size(width, height), [.. items]), new RasterOptions(resolution) { Background = Color.White });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Every pixel shows the exact area of the boxes in it (boxes that do not overlap), within one step.</summary>
    private static void ShouldShowExactly(RgbaImage image, params Box[] boxes)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var area = boxes.Sum(box => box.AreaIn(x, y));
                Math.Abs(image.Pixels[((y * image.Width) + x) * 4] - (255 - (255 * area))).Should().BeLessThanOrEqualTo(1, $"pixel ({x}, {y}) covers {area:F4}");
            }
        }
    }

    private static Box Pixels(Rect area, double scale) =>
        new(Math.Min(area.Origin.X, area.Origin.X + area.Size.Width) * scale, Math.Min(area.Origin.Y, area.Origin.Y + area.Size.Height) * scale,
            Math.Max(area.Origin.X, area.Origin.X + area.Size.Width) * scale, Math.Max(area.Origin.Y, area.Origin.Y + area.Size.Height) * scale);

    /// <summary>A black image of 2 × 2 pixels: it covers its rectangle fully, so its ink is the rectangle's area.</summary>
    private static ImageData BlackImage() =>
        ImageDecoder.Decode(ImageEncoder.EncodePng(2, 2, [.. Enumerable.Repeat<byte[]>([0, 0, 0, 255], 4).SelectMany(pixel => pixel)]).Value.ToArray()).Value;

    private static readonly Rect EdgeInsidePixels = new(new Point(5.3, 4.45), new Size(9.1, 6.2));

    [Fact]
    public void A_rule_cut_by_a_clip_edge_inside_a_pixel_shows_the_exact_area_inside_the_clip()
    {
        // 120 dpi: the clip's edges and the rule's fall inside pixels; every pixel is the area of rule ∩ clip in it.
        const double scale = 120.0 / 72;
        var rule = new Rect(new Point(2.2, 6.05), new Size(15.7, 3.3));
        var image = Drawn(20, 16, 120, new ClipStart(EdgeInsidePixels), new FillRectangle(rule, Color.Black), ClipEnd.Instance);

        ShouldShowExactly(image, Pixels(rule, scale).Intersect(Pixels(EdgeInsidePixels, scale)));
    }

    [Fact]
    public void A_glyph_cut_by_a_clip_edge_inside_a_pixel_shows_the_exact_area_inside_the_clip()
    {
        // 'I' of the Roboto subset is one rectangle (design units 183, 0, 376, 1456 of 2048): at 20 pt with its baseline at
        // 14.6 pt it reaches from 0.38 pt above the clip's top to below its bottom, and the clip cuts it across.
        const double scale = 120.0 / 72;
        var font = TestFonts.RobotoSubset;
        var (size, originX, originY) = (20.0, 6.1, 14.6);
        var em = size / 2048;
        var glyph = new Box((originX + (183 * em)) * scale, (originY - (1456 * em)) * scale, (originX + (376 * em)) * scale, originY * scale);
        var run = new GlyphRun(font, size, Color.Black, new Point(originX, originY), [new PositionedGlyph(font.GlyphFor(new Rune('I')).Value, 0, "I")]);
        var image = Drawn(20, 16, 120, new ClipStart(EdgeInsidePixels), run, ClipEnd.Instance);

        ShouldShowExactly(image, glyph.Intersect(Pixels(EdgeInsidePixels, scale)));
    }

    [Fact]
    public void An_image_half_outside_its_clip_shows_the_exact_area_of_its_part_inside()
    {
        const double scale = 120.0 / 72;
        var area = new Rect(new Point(9.65, 1.15), new Size(8.4, 9.9));
        var image = Drawn(20, 16, 120, new ClipStart(EdgeInsidePixels), new DrawImage(area, BlackImage()), ClipEnd.Instance);

        ShouldShowExactly(image, Pixels(area, scale).Intersect(Pixels(EdgeInsidePixels, scale)));
    }

    [Fact]
    public void Nested_clips_show_what_lies_inside_both()
    {
        const double scale = 120.0 / 72;
        var inner = new Rect(new Point(1.35, 7.9), new Size(10.2, 7.15));
        var rule = new Rect(new Point(0.5, 0.5), new Size(19, 15));
        var image = Drawn(20, 16, 120, new ClipStart(EdgeInsidePixels), new ClipStart(inner), new FillRectangle(rule, Color.Black), ClipEnd.Instance, ClipEnd.Instance);

        ShouldShowExactly(image, Pixels(rule, scale).Intersect(Pixels(EdgeInsidePixels, scale)).Intersect(Pixels(inner, scale)));
    }

    [Fact]
    public void After_an_inner_clip_ends_the_outer_clip_holds_again()
    {
        // The rule comes after the inner clip's end and before the outer one's: it is cut by the outer clip, not drawn
        // whole and not cut by the inner one.
        const double scale = 120.0 / 72;
        var inner = new Rect(new Point(1.35, 7.9), new Size(10.2, 7.15));
        var rule = new Rect(new Point(0.5, 0.5), new Size(19, 15));
        var image = Drawn(20, 16, 120, new ClipStart(EdgeInsidePixels), new ClipStart(inner), ClipEnd.Instance, new FillRectangle(rule, Color.Black), ClipEnd.Instance);

        ShouldShowExactly(image, Pixels(rule, scale).Intersect(Pixels(EdgeInsidePixels, scale)));
    }

    [Fact]
    public void Every_glyph_of_a_run_is_clipped_not_only_the_first()
    {
        // Two 'I's of the Roboto subset (one rectangle each, design units 183 to 376 of 2048 across) 6 pt apart: the clip's
        // right edge at 10.6 pt leaves the first whole and cuts the second across, inside a pixel.
        const double scale = 120.0 / 72;
        var font = TestFonts.RobotoSubset;
        var (size, originX, originY) = (20.0, 2.0, 14.6);
        var em = size / 2048;
        Box Glyph(double x) => new((originX + x + (183 * em)) * scale, (originY - (1456 * em)) * scale, (originX + x + (376 * em)) * scale, originY * scale);
        var clip = new Rect(new Point(0, 0), new Size(10.6, 16));
        var i = font.GlyphFor(new Rune('I')).Value;
        var run = new GlyphRun(font, size, Color.Black, new Point(originX, originY), [new PositionedGlyph(i, 0, "I"), new PositionedGlyph(i, 6, "I")]);
        var image = Drawn(20, 16, 120, new ClipStart(clip), run, ClipEnd.Instance);

        ShouldShowExactly(image, Glyph(0).Intersect(Pixels(clip, scale)), Glyph(6).Intersect(Pixels(clip, scale)));
    }

    [Fact]
    public void A_clip_ends_where_its_end_marker_is()
    {
        // What follows the clip's end is drawn whole.
        const double scale = 120.0 / 72;
        var rule = new Rect(new Point(2.2, 6.05), new Size(15.7, 3.3));
        var image = Drawn(20, 16, 120, new ClipStart(new Rect(new Point(0, 0), new Size(1, 1))), ClipEnd.Instance, new FillRectangle(rule, Color.Black));

        ShouldShowExactly(image, Pixels(rule, scale));
    }

    [Theory]
    [InlineData(0.0, 0.0, 5.0, 5.0)]
    [InlineData(4.0, 4.0, 0.0, 6.0)]
    [InlineData(30.0, 2.0, 5.0, 5.0)]
    [InlineData(-20.0, -20.0, 5.0, 5.0)]
    public void An_empty_clip_or_one_outside_the_page_lets_nothing_through(double x, double y, double width, double height)
    {
        // The first two are empty (no height, no width), the others lie beside the page.
        var clip = x == 0 ? new Rect(new Point(4, 4), new Size(5, 0)) : new Rect(new Point(x, y), new Size(width, height));
        var image = Drawn(20, 16, 120, new ClipStart(clip), new FillRectangle(new Rect(new Point(0, 0), new Size(20, 16)), Color.Black), ClipEnd.Instance);

        image.Pixels.Where((_, index) => index % 4 == 0).Should().AllSatisfy(red => red.Should().Be(255));
    }

    [Fact]
    public void A_clip_with_a_negative_width_and_height_is_the_rectangle_its_corners_span_as_in_PDF()
    {
        // PDF's re operator reads a negative width or height as the same rectangle from the other corner.
        const double scale = 120.0 / 72;
        var flipped = new Rect(new Point(EdgeInsidePixels.Origin.X + EdgeInsidePixels.Size.Width, EdgeInsidePixels.Origin.Y + EdgeInsidePixels.Size.Height),
            new Size(-EdgeInsidePixels.Size.Width, -EdgeInsidePixels.Size.Height));
        var rule = new Rect(new Point(2.2, 6.05), new Size(15.7, 3.3));
        var image = Drawn(20, 16, 120, new ClipStart(flipped), new FillRectangle(rule, Color.Black), ClipEnd.Instance);

        ShouldShowExactly(image, Pixels(rule, scale).Intersect(Pixels(EdgeInsidePixels, scale)));
    }

    [Theory]
    [InlineData(72.0)]
    [InlineData(120.0)]
    [InlineData(300.0)]
    public void A_rectangle_larger_than_its_clip_gives_the_pixels_of_a_rectangle_of_the_clip_s_size(double resolution)
    {
        // One map from points to pixels for a clip and for every item, so the clip's edges and the rectangle's are the same
        // doubles: the pixels are bit for bit equal.
        var clipped = Drawn(20, 16, resolution, new ClipStart(EdgeInsidePixels), new FillRectangle(new Rect(new Point(1, 1), new Size(18, 14)), Color.Black), ClipEnd.Instance);
        var plain = Drawn(20, 16, resolution, new FillRectangle(EdgeInsidePixels, Color.Black));

        clipped.Pixels.Should().Equal(plain.Pixels);
    }

    [Theory]
    [InlineData(-PageModelRules.MaxNumber / 2, PageModelRules.MaxNumber / 2)]
    [InlineData(-1e15, 1e15)]
    public void A_clip_with_extreme_coordinates_around_the_content_changes_nothing(double from, double to)
    {
        // Edges at ±1.7e38 pt (its size the largest number the contract allows), or at ±1e15 pt (more than 2^40 pixels at
        // 300 dpi), far outside the page: the content is drawn as without the clip, and nothing is refused.
        var clip = new ClipStart(new Rect(new Point(from, from), new Size(to - from, to - from)));
        var rule = new FillRectangle(new Rect(new Point(2.2, 6.05), new Size(15.7, 3.3)), Color.Black);

        Drawn(20, 16, 300, clip, rule, ClipEnd.Instance).Pixels.Should().Equal(Drawn(20, 16, 300, rule).Pixels);
    }

    [Fact]
    public void An_image_out_of_reach_is_refused_also_where_a_clip_hides_it()
    {
        // The raster back-end's reach (Canvas.MaxImageReach) is checked for the image as given; a clip that leaves nothing
        // of the image visible does not make the image drawable.
        var image = new DrawImage(new Rect(new Point(-1e30, 5), new Size(1e30, 1e30)), BlackImage());
        var clip = new ClipStart(new Rect(new Point(0, 0), new Size(20, 16)));

        var result = Draw(new Page(new Size(20, 16), [clip, image, ClipEnd.Instance]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.ImageOutOfReach);
    }

    [Fact]
    public void Twenty_seven_clips_one_inside_the_other_are_drawn_as_the_PDF_back_end_accepts_them()
    {
        var result = Draw(PageOf(Nested(PageModelRules.MaxClipDepth)));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(20_000)]
    public void More_than_27_clips_one_inside_the_other_are_refused_as_the_PDF_back_end_refuses_them(int depth)
    {
        var result = Draw(PageOf(Nested(depth)));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }

    [Theory]
    [InlineData("clip end without start")]
    [InlineData("clip start without end")]
    [InlineData("clip ends inside an artifact")]
    public void Clips_that_do_not_nest_with_the_other_markers_are_refused(string problem)
    {
        DrawItem[] items = problem switch
        {
            "clip end without start" => [ClipEnd.Instance],
            "clip start without end" => [Clip],
            _ => [Clip, new ArtifactStart(ArtifactKind.Layout), ClipEnd.Instance, ArtifactEnd.Instance],
        };

        Draw(PageOf(items)).Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }
}
