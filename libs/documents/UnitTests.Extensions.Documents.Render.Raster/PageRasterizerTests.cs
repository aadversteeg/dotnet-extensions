using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Render.Raster;

/// <summary>
/// The raster back-end on hand-made page models: the contract and the options, the page size in pixels, and that each
/// item lands where the page model says, with the ink the geometry says: rectangles by their exact overlap, glyphs by the
/// area of their outlines and at their exact (unsnapped) positions, images pixel for pixel.
/// </summary>
public class PageRasterizerTests
{
    private static readonly DocumentMetadata Metadata = new("Title", null);

    private static Page PageOf(double width, double height, params DrawItem[] items) => new(new Size(width, height), [.. items]);

    private static RgbaImage Draw(Page page, double resolution = 72, Color? background = null) =>
        PageRasterizer.Render(page, new RasterOptions(resolution) { Background = background ?? Color.White }).Value;

    private static byte Red(RgbaImage image, int x, int y) => image.Pixels[((y * image.Width) + x) * 4];

    /// <summary>The ink of an image drawn in black on white: Σ (255 − red) / 255, in pixels.</summary>
    private static double Ink(RgbaImage image) => image.Pixels.Where((_, index) => index % 4 == 0).Sum(value => (255 - value) / 255.0);

    private static GlyphRun Run(TrueTypeFont font, double size, Point origin, string text) =>
        new(font, size, Color.Black, origin, [.. text.Select((character, index) => new PositionedGlyph(font.GlyphFor(new Rune(character)).Value, index * size, character.ToString()))]);

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_resolution_that_is_not_a_positive_finite_number_is_refused(double resolution)
    {
        var result = PageRasterizer.Render(PageOf(10, 10), new RasterOptions(resolution));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.InvalidOptions);
    }

    [Theory]
    [InlineData(10_000, 10_000, 300)]
    [InlineData(3.4e38, 10, 96)]
    [InlineData(10, 3.4e38, 96)]
    public void A_page_with_more_pixels_than_a_canvas_holds_is_refused(double width, double height, double resolution)
    {
        var result = PageRasterizer.Render(PageOf(width, height), new RasterOptions(resolution));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.TooLarge);
    }

    [Fact]
    public void A_page_model_that_breaks_the_contract_is_refused_under_the_raster_code()
    {
        var page = PageOf(10, 10, Run(TestFonts.Regular, 10, new Point(double.NaN, 5), "A"));

        var single = PageRasterizer.Render(page, new RasterOptions());
        var document = PageRasterizer.Render(new PageDocument(Metadata, [page]), new RasterOptions());

        single.Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
        document.Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
        PageRasterizer.Render(new PageDocument(Metadata, []), new RasterOptions()).Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }

    [Fact]
    public void One_page_of_a_document_is_drawn_after_the_whole_document_is_checked()
    {
        var start = new StructureStart(new NodeId(1), SemanticRole.Paragraph, new SourceLocation("x.cs", 1));
        var red = PageOf(2, 2, new FillRectangle(new Rect(new Point(0, 0), new Size(2, 2)), new Color(255, 0, 0)));
        var document = new PageDocument(Metadata, [PageOf(2, 2), red]);

        PageRasterizer.Render(document, 2, new RasterOptions(72)).Value.Pixels.Should().Equal(Draw(red).Pixels);
        PageRasterizer.Render(document, 3, new RasterOptions(72)).Error.Code.Should().Be(RasterErrorCodes.PageNotFound);
        PageRasterizer.Render(document, 0, new RasterOptions(72)).Error.Code.Should().Be(RasterErrorCodes.PageNotFound);

        // One node with two roles on two pages: a breach of the document, so page 1 is refused too.
        var broken = new PageDocument(Metadata, [PageOf(2, 2, start, new StructureEnd(new NodeId(1))), PageOf(2, 2, start with { Role = SemanticRole.Heading(1) }, new StructureEnd(new NodeId(1)))]);
        PageRasterizer.Render(broken, 1, new RasterOptions(72)).Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }

    [Fact]
    public void Numbers_beyond_what_PDF_can_hold_are_refused_by_the_contract_not_thrown()
    {
        // Numbers beyond ±3.403e38 (ISO 32000-1 Annex C) break the shared contract: an error value, never an exception
        // from the canvas.
        var font = TestFonts.Regular;
        var glyph = new PositionedGlyph(font.GlyphFor(new Rune('A')).Value, 1e308, "A");
        foreach (var item in new DrawItem[]
        {
            new FillRectangle(new Rect(new Point(10, 10), new Size(10, 1.5e308)), Color.Black),
            new FillRectangle(new Rect(new Point(10, 1e308), new Size(10, 1e308)), Color.Black),
            new GlyphRun(font, 10, Color.Black, new Point(1e308, 10), [glyph]),
        })
        {
            var result = PageRasterizer.Render(PageOf(100, 100, item), new RasterOptions(96));

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
        }
    }

    [Fact]
    public void Numbers_at_the_limit_are_drawn_without_an_error()
    {
        const double limit = PageModelRules.MaxNumber;
        var font = TestFonts.Regular;
        var glyph = new PositionedGlyph(font.GlyphFor(new Rune('W')).Value, limit, "W", -limit);
        var items = new DrawItem[]
        {
            new FillRectangle(new Rect(new Point(-limit, -limit), new Size(limit, limit)), Color.Black),
            new FillRectangle(new Rect(new Point(limit, limit), new Size(limit, limit)), Color.Black),
            new GlyphRun(font, limit, Color.Black, new Point(10, 10), [new PositionedGlyph(font.GlyphFor(new Rune('W')).Value, 0, "W")]),
            new GlyphRun(font, 10, Color.Black, new Point(limit, limit), [glyph]),
        };

        foreach (var item in items)
        {
            var result = PageRasterizer.Render(PageOf(100, 100, item), new RasterOptions(300));

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        }

        // An image of the same extent is refused by the raster back-end's own reach (Canvas.MaxImageReach), not drawn wrong.
        var image = new DrawImage(new Rect(new Point(-limit, 5), new Size(limit, limit)), TestImages.Load("rgb8.png"));
        PageRasterizer.Render(PageOf(100, 100, image), new RasterOptions(300)).Error.Code.Should().Be(RasterErrorCodes.ImageOutOfReach);

        // The first rectangle ends at (0, 0): nothing of the page; a page-filling one fills all of it.
        Draw(PageOf(100, 100, items[0])).Pixels.Should().OnlyContain(value => value == 255);
        Draw(PageOf(100, 100, new FillRectangle(new Rect(new Point(-limit / 2, -limit / 2), new Size(limit, limit)), Color.Black))).Pixels
            .Where((_, index) => index % 4 == 0).Should().OnlyContain(value => value == 0);
    }

    [Fact]
    public void A_rectangle_of_10_to_the_30_points_costs_what_a_page_sized_one_costs()
    {
        // The work of a fill is bounded by the canvas, not by the shape: the shape is clipped to the canvas before any
        // number becomes an integer, so the huge rectangle is drawn from the same polygon as the page-sized one, with the
        // same rows and buffers. Counted, not timed: the bytes allocated on this thread by a render (after one render of
        // each, so that nothing is allocated once only), which the rasteriser's buffers would follow if its rows or
        // columns followed the shape. A clock under a loaded host says nothing.
        var huge = PageOf(595, 842, new FillRectangle(new Rect(new Point(-1e30, -1e30), new Size(2e30, 2e30)), Color.Black));
        var page = PageOf(595, 842, new FillRectangle(new Rect(new Point(0, 0), new Size(600, 850)), Color.Black));
        var options = new RasterOptions(150);

        long Allocated(Page subject)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            PageRasterizer.Render(subject, options).IsSuccess.Should().BeTrue();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        PageRasterizer.Render(page, options).IsSuccess.Should().BeTrue();
        PageRasterizer.Render(huge, options).IsSuccess.Should().BeTrue();
        var (pageBytes, hugeBytes) = (Allocated(page), Allocated(huge));
        hugeBytes.Should().BeLessThanOrEqualTo(pageBytes + 4096, $"huge {hugeBytes} bytes, page {pageBytes} bytes");
        PageRasterizer.Render(huge, options).Value.Pixels.Should().Equal(PageRasterizer.Render(page, options).Value.Pixels);
    }

    [Theory]
    [InlineData(1e9)]
    [InlineData(1e11)]
    [InlineData(3.6e11)]
    public void An_image_around_the_page_shows_the_same_picture_however_far_its_edges_lie_within_the_reach(double half)
    {
        // The image's rectangle runs from -h to h both ways, so the page shows the middle of the image: for every h the
        // same picture, here the colour of pixel (30, 21) of a 61 x 43 image over the whole page. Each pixel's preimage is
        // a tiny part of one image pixel, far (h) from the image's origin; it must keep its area. At 72 dpi the image's
        // grid reaches 3h pixels from the origin: 3.6 × 10^11 is just within Canvas.MaxImageReach (2^40).
        var jpeg = TestImages.Load("jpeg-rgb.jpg");
        var near = Draw(PageOf(100, 100, new DrawImage(new Rect(new Point(-1e6, -1e6), new Size(2e6, 2e6)), jpeg)));

        var far = Draw(PageOf(100, 100, new DrawImage(new Rect(new Point(-half, -half), new Size(2 * half, 2 * half)), jpeg)));

        far.Pixels.Should().Equal(near.Pixels, $"h = {half}");
        var centre = ((21 * 61) + 30) * 3;
        var samples = ImageDecoder.DecodeSamples(jpeg).Value.Samples;
        far.Pixels.Chunk(4).Should().OnlyContain(pixel => pixel[0] == samples[centre] && pixel[1] == samples[centre + 1] && pixel[2] == samples[centre + 2] && pixel[3] == 255);
    }

    [Theory]
    [InlineData(1e9)]
    [InlineData(1e11)]
    [InlineData(5.4e11)]
    public void An_image_that_reaches_the_page_from_far_away_is_drawn_up_to_its_own_edge(double distance)
    {
        // Origin -h, width h + 50: the image's last column covers the left half of the page, up to its right edge, the
        // page model's own number -h + (h + 50) = 50. Left of the edge the picture of a near image, right of it the white
        // page. The grid reaches 2h + 50 pixels at 72 dpi: 5.4 × 10^11 is just within Canvas.MaxImageReach.
        var jpeg = TestImages.Load("jpeg-rgb.jpg");
        var near = Draw(PageOf(100, 100, new DrawImage(new Rect(new Point(-1e6, 0), new Size(1e6 + 50, 100)), jpeg)));
        var edge = -distance + (distance + 50);

        var far = Draw(PageOf(100, 100, new DrawImage(new Rect(new Point(-distance, 0), new Size(distance + 50, 100)), jpeg)));

        for (var index = 0; index < far.Pixels.Length; index++)
        {
            var x = index / 4 % 100;
            far.Pixels[index].Should().Be(x < edge ? near.Pixels[index] : (byte)255, $"h = {distance}, pixel ({x}, {index / 400}), edge {edge}");
        }
    }

    [Theory]
    [InlineData(-3.7e11, 6 * 3.7e11, 72)]
    [InlineData(-1e17, 2e17, 72)]
    [InlineData(-PageModelRules.MaxNumber / 2, PageModelRules.MaxNumber, 72)]
    [InlineData(-5.6e11, 5.6e11 + 50, 72)]
    [InlineData(-1e11, 2e11, 300)]
    public void An_image_whose_grid_reaches_beyond_the_limit_at_the_resolution_is_refused(double origin, double size, double resolution)
    {
        // Beyond Canvas.MaxImageReach (2^40 pixels) the edges of the image's pixels cannot be placed within a fraction of
        // a pixel in double precision; the page is refused, not drawn wrong. The same image at 72 dpi and at 300 dpi
        // reaches 4.2 times farther at the higher resolution (the last row: 3 × 10^11 pixels at 72 dpi is drawn).
        var jpeg = TestImages.Load("jpeg-rgb.jpg");

        var result = PageRasterizer.Render(PageOf(100, 100, new DrawImage(new Rect(new Point(origin, origin), new Size(size, size)), jpeg)), new RasterOptions(resolution));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.ImageOutOfReach);
    }

    [Theory]
    [InlineData(595.2756, 841.8898, 96, 794, 1123)]
    [InlineData(612, 792, 72, 612, 792)]
    [InlineData(612, 792, 300, 2550, 3300)]
    [InlineData(100.2, 50.1, 72, 101, 51)]
    [InlineData(10.1, 20.3, 96, 14, 28)]
    [InlineData(0.0001, 0.0001, 96, 1, 1)]
    [InlineData(0.0001, 85.38173927570774, 5e-324, 1, 1)]
    [InlineData(2.23421391529693, 1, 5e-324, 1, 1)]
    [InlineData(3.403e38, 3.403e38, 5e-324, 1, 1)]
    public void The_image_is_the_page_size_times_the_resolution_rounded_up(double width, double height, double resolution, int pixelsAcross, int pixelsDown)
    {
        PageRasterizer.PixelSize(new Size(width, height), resolution).Should().Be((pixelsAcross, pixelsDown));
        var image = Draw(PageOf(width, height), resolution);
        (image.Width, image.Height).Should().Be((pixelsAcross, pixelsDown));
    }

    [Theory]
    [InlineData(double.NaN, 100, 72)]
    [InlineData(100, double.NaN, 72)]
    [InlineData(-100, 100, 72)]
    [InlineData(100, -0.5, 72)]
    [InlineData(0, 100, 72)]
    [InlineData(0.00009, 100, 72)]
    [InlineData(double.PositiveInfinity, 100, 72)]
    [InlineData(100, double.NegativeInfinity, 72)]
    [InlineData(3.5e38, 100, 72)]
    [InlineData(100, 100, double.NaN)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 100, -72)]
    [InlineData(100, 100, double.PositiveInfinity)]
    public void A_page_size_or_resolution_that_is_refused_has_no_pixels(double width, double height, double resolution)
    {
        PageRasterizer.PixelSize(new Size(width, height), resolution).Should().Be((0L, 0L));
        var result = PageRasterizer.Render(PageOf(width, height), new RasterOptions(resolution));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf(RasterErrorCodes.InvalidPageModel, RasterErrorCodes.InvalidOptions);
    }

    [Fact]
    public void The_background_is_white_unless_set_and_transparent_when_null()
    {
        Draw(PageOf(2, 2)).Pixels.Should().OnlyContain(value => value == 255);
        PageRasterizer.Render(PageOf(2, 2), new RasterOptions(72) { Background = null }).Value.Pixels.Should().OnlyContain(value => value == 0);
        Draw(PageOf(1, 1), background: new Color(1, 2, 3)).Pixels.Should().Equal(1, 2, 3, 255);
    }

    [Fact]
    public void A_rectangle_covers_each_pixel_by_its_overlap_at_the_resolution()
    {
        // At 144 dpi a point is two pixels: (1.25, 0.5)–(2.5, 2) points is (2.5, 1)–(5, 4) pixels.
        var image = Draw(PageOf(4, 3, new FillRectangle(new Rect(new Point(1.25, 0.5), new Size(1.25, 1.5)), Color.Black)), 144);

        Red(image, 1, 1).Should().Be(255);
        Red(image, 2, 1).Should().Be(127, "half of the pixel: 255 − 128");
        Red(image, 3, 2).Should().Be(0);
        Red(image, 4, 3).Should().Be(0);
        Red(image, 5, 2).Should().Be(255);
        Red(image, 3, 0).Should().Be(255);
        Ink(image).Should().BeApproximately(2.5 * 3, 4 * 0.5 / 255 + 1e-9);
    }

    [Theory]
    [InlineData('H')]
    [InlineData('o')]
    [InlineData('&')]
    public void A_glyph_puts_down_the_area_of_its_outline(char character)
    {
        var font = TestFonts.Regular;
        var shape = font.Shape(font.GlyphFor(new Rune(character)).Value).Value;
        var size = 40.0;
        var image = Draw(PageOf(60, 60, Run(font, size, new Point(10.3, 45.7), character.ToString())), 72);

        // The area enclosed by quadratic curves: the polygon of the on-curve points plus, per curve, 2/3 of the triangle
        // its control point makes with its ends. Scaled from design units to pixels (72 dpi: points are pixels).
        var area = Math.Abs(shape.Contours.Sum(contour =>
        {
            var segments = contour.Segments();
            var sum = 0.0;
            var from = segments.Start;
            foreach (var segment in segments.Segments)
            {
                sum += (from.X * segment.End.Y) - (segment.End.X * from.Y);
                if (segment.Control is { } c)
                {
                    sum += 2.0 / 3 * (((c.X - from.X) * (segment.End.Y - from.Y)) - ((segment.End.X - from.X) * (c.Y - from.Y)));
                }

                from = segment.End;
            }

            return sum / 2;
        })) * Math.Pow(size / font.Metrics.UnitsPerEm, 2);

        var edgePixels = image.Pixels.Where((value, index) => index % 4 == 0 && value is > 0 and < 255).Count();
        Ink(image).Should().BeApproximately(area, (edgePixels * 0.5 / 255) + (edgePixels * FlatteningTolerance), $"'{character}' covers {area:F2} square pixels");
    }

    /// <summary>The flattening tolerance of Ave.Extensions.Documents.Graphics (1/64 pixel), as a bound on lost area per
    /// pixel of outline.</summary>
    private const double FlatteningTolerance = 1.0 / 64;

    [Fact]
    public void A_glyph_moved_by_whole_pixels_gives_the_same_pixels_moved()
    {
        var font = TestFonts.Regular;
        var at = Draw(PageOf(40, 40, Run(font, 20, new Point(10.3, 30.6), "e")), 72);
        var moved = Draw(PageOf(40, 40, Run(font, 20, new Point(13.3, 28.6), "e")), 72);

        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 30; x++)
            {
                Red(moved, x + 3, y).Should().Be(Red(at, x, y + 2), $"pixel ({x}, {y})");
            }
        }
    }

    [Fact]
    public void A_glyph_is_drawn_at_its_exact_position_without_snapping()
    {
        // Sixteen positions 1/16 pixel apart, across and down: no two give the same pixels, so no position is rounded to
        // a coarser grid (PDF viewers round glyph positions to 1/8 or 1/4 pixel, or to whole pixels); the ink stays the
        // glyph's area, to the 8-bit rounding of the partly covered pixels.
        var font = TestFonts.Regular;
        var images = Enumerable.Range(0, 16)
            .Select(step => Draw(PageOf(40, 40, Run(font, 20, new Point(10 + (step / 16.0), 30 + (step / 32.0)), "e")), 72))
            .ToList();

        images.Select(image => Convert.ToHexString(image.Pixels.AsSpan())).Distinct().Should().HaveCount(16);
        foreach (var image in images)
        {
            var edgePixels = image.Pixels.Where((value, index) => index % 4 == 0 && value is > 0 and < 255).Count();
            Ink(image).Should().BeApproximately(Ink(images[0]), 2 * edgePixels * 0.5 / 255);
        }
    }

    [Fact]
    public void An_image_fills_its_rectangle_pixel_for_pixel()
    {
        // rgba8.png is 7 × 5 pixels; drawn into 7 × 5 points at 72 dpi on a transparent page, every pixel is the image's.
        var image = TestImages.Load("rgba8.png");
        var raster = (RasterImage)image;
        var drawn = PageRasterizer.Render(PageOf(9, 7, new DrawImage(new Rect(new Point(1, 2), new Size(7, 5)), image)), new RasterOptions(72) { Background = null }).Value;

        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                var offset = (((y + 2) * drawn.Width) + x + 1) * 4;
                var alpha = raster.Alpha!.Value[(y * 7) + x];
                drawn.Pixels[offset + 3].Should().Be(alpha);
                if (alpha > 0)
                {
                    drawn.Pixels.Skip(offset).Take(3).Should().Equal(raster.Samples.Skip(((y * 7) + x) * 3).Take(3), $"pixel ({x}, {y})");
                }
            }
        }
    }

    [Fact]
    public void An_image_drawn_many_times_is_decoded_once_per_render()
    {
        // 20 small draws of one JPEG cost about what one draw costs: the decoded pixels are kept for the render call.
        var jpeg = TestImages.Load("baseline-rgb.jpg");
        long Allocated(int draws)
        {
            var page = PageOf(100, 100, [.. Enumerable.Range(0, draws).Select(index => (DrawItem)new DrawImage(new Rect(new Point(index * 4, 10), new Size(4, 4)), jpeg))]);
            Draw(page);
            var before = GC.GetAllocatedBytesForCurrentThread();
            Draw(page);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var once = Allocated(1);
        var decode = Allocated(2) - once;

        (Allocated(20) - once).Should().BeLessThan(once / 2, $"one draw allocates {once} bytes; a second draw of the same image {decode} more");
    }

    [Fact]
    public void A_JPEG_is_decoded_and_drawn()
    {
        var jpeg = TestImages.Load("baseline-rgb.jpg");
        var samples = ImageDecoder.DecodeSamples(jpeg).Value;

        var drawn = Draw(PageOf(40, 30, new DrawImage(new Rect(new Point(0, 0), new Size(40, 30)), jpeg)), 72);

        drawn.Pixels.Where((_, index) => index % 4 != 3).Should().Equal(samples.Samples);
    }

    [Fact]
    public void A_JPEG_with_a_component_no_scan_covers_is_an_error_of_the_page_not_an_exception()
    {
        // The image layer's refusal reaches the caller of the page API as the page's error.
        var jpeg = TestImages.Load("jpeg-unscanned-undefined-table.jpg");

        var result = PageRasterizer.Render(PageOf(40, 30, new DrawImage(new Rect(new Point(0, 0), new Size(40, 30)), jpeg)), new RasterOptions(72));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ImageErrorCodes.Invalid);
    }

    [Fact]
    public void Markers_draw_nothing()
    {
        var start = new StructureStart(new NodeId(1), SemanticRole.Paragraph, new SourceLocation("x.cs", 1));

        var image = Draw(PageOf(3, 3, new ArtifactStart(ArtifactKind.Layout), ArtifactEnd.Instance, start, new StructureEnd(new NodeId(1))));

        image.Pixels.Should().OnlyContain(value => value == 255);
    }

    [Fact]
    public void A_glyph_whose_outline_cannot_be_decoded_is_the_font_layer_s_error()
    {
        var bytes = FontFiles.WithGlyphs(TestFonts.Bytes("Lato-Regular.ttf"), [[], [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5]]);
        var font = FontParser.Parse(bytes).Value;
        var run = new GlyphRun(font, 10, Color.Black, new Point(1, 8), [new PositionedGlyph(new GlyphId(1), 0, "x")]);

        var result = PageRasterizer.Render(PageOf(10, 10, run), new RasterOptions());

        result.IsFailure.Should().BeTrue();
        result.Error.Is(FontErrorCodes.Parse._).Should().BeTrue();
        result.Error.Metadata!.Should().ContainKey(ErrorMetadataKeys.FontName);
    }
}
