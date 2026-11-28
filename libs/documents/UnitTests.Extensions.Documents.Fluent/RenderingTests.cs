using System.Runtime.CompilerServices;
using System.Text;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>The whole pipeline through <see cref="DocumentRendering.ToPdf(Document, FontSet, PdfRenderOptions)"/>: errors from later layers still
/// point at the caller's line.</summary>
public class RenderingTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.Zero));

    [Fact]
    public void A_valid_document_becomes_a_pdf()
    {
        var pdf = Document([Heading(1, "Hello"), Paragraph("World")]).FontFamily("Lato").FontSize(11.Pt()).ToPdf(TestFonts.Set, Options);

        pdf.IsSuccess.Should().BeTrue();
        Encoding.ASCII.GetString(pdf.Value.AsSpan(0, 8)).Should().Be("%PDF-1.7");
        PdfReadBack.Letters(pdf.Value).Single().Select(letter => letter.Value).Should().Equal("HelloWorld".Select(character => character.ToString()));
    }

    [Fact]
    public void A_character_the_font_lacks_is_an_error_at_the_line_of_its_text()
    {
        var line = CurrentLine() + 1;
        var span = Text("Price in yen: ¥ and kanji 漢");

        var error = Document([Paragraph([Bold("Note: "), span])]).FontFamily("Lato").ToPdf(TestFonts.Set, Options).Error;

        error.Code.Should().Be(FontErrorCodes.GlyphMissing);
        error.Metadata![ErrorMetadataKeys.Character].Should().Be("漢");
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void A_block_that_can_never_fit_a_page_is_an_error_at_the_line_of_the_block()
    {
        var line = CurrentLine() + 1;
        var tall = Block([Paragraph("x")]).PaddingTop(30.Cm());

        var error = Document([Paragraph("Before"), tall]).FontFamily("Lato").ToPdf(TestFonts.Set, Options).Error;

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void A_weight_the_font_set_does_not_have_is_an_error_at_the_line_of_the_text()
    {
        var line = CurrentLine() + 1;
        var span = Text("semi-bold").FontWeight(new FontWeight(600));

        var error = Document([Paragraph([span])]).FontFamily("Lato").ToPdf(TestFonts.Set, Options).Error;

        error.Code.Should().Be(FontErrorCodes.NotFound);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void A_default_or_incomplete_font_set_is_an_error_not_an_exception()
    {
        var document = Document([Paragraph("World")]).FontFamily("Lato");

        document.ToPdf(new FontSet(default), Options).Error.Code.Should().Be(FontErrorCodes.NotFound);
        document.ToPdf(new FontSet([TestFonts.Regular, null!]), Options).Error.Code.Should().Be(FontErrorCodes.NotFound);
        document.ToPng(new FontSet([null!]), 1, new()).Error.Code.Should().Be(FontErrorCodes.NotFound);
    }

    [Fact]
    public void A_font_whose_licence_forbids_embedding_is_drawn_to_a_png_and_refused_in_a_pdf()
    {
        // OS/2 fsType 0x0002 (offset 8 of the table), "Restricted License embedding": a PDF would embed the font, a PNG
        // holds only the pixels of its glyphs.
        var os2 = FontFiles.Table(TestFonts.Bytes("Lato-Regular.ttf"), "OS/2");
        (os2[8], os2[9]) = (0x00, 0x02);
        var restricted = FontParser.Parse(FontFiles.WithTable(TestFonts.Bytes("Lato-Regular.ttf"), "OS/2", os2)).Value;
        var document = Document([Paragraph("World")]).FontFamily("Lato");
        var fonts = new FontSet([restricted]);

        document.ToPdf(fonts, Options).Error.Code.Should().Be(PdfRenderErrorCodes.Write.FontEmbeddingNotPermitted);
        document.ToPng(fonts, 1, new RasterOptions(72)).IsSuccess.Should().BeTrue();
        document.ToPng(TestFonts.Set, 1, new RasterOptions(72)).Value.Should().Equal(document.ToPng(fonts, 1, new RasterOptions(72)).Value, "the licence bits draw nothing");
    }

    [Theory]
    [InlineData(0, 1.09e12, true)]
    [InlineData(-3.65e11, 7.3e11, true)]
    [InlineData(-3.7e11, 7.4e11, false)]
    [InlineData(-5e11, 1e12, false)]
    public void An_image_s_reach_is_counted_from_the_page_s_origin(double x, double width, bool drawn)
    {
        // At 72 dpi the reach along x is |x| + width (Canvas.MaxImageReach, 2^40 ≈ 1.0995 × 10^12): an image that starts
        // at the origin is drawn up to that width, one centred on the origin reaches 1.5 times its width and is refused
        // from two thirds of it, though none of its points is as far from the origin.
        var page = new PageDocument(new DocumentMetadata("Reach", null), [new Page(new Size(100, 100), [new DrawImage(new Rect(new Point(x, 0), new Size(width, 10)), TestImages.Load("rgb8.png"))])]);

        var png = page.ToPng(1, new RasterOptions(72));

        png.IsSuccess.Should().Be(drawn);
        if (!drawn)
        {
            png.Error.Code.Should().Be(RasterErrorCodes.ImageOutOfReach);
        }
    }

    [Fact]
    public void An_image_wholly_off_the_page_beyond_the_reach_is_refused_where_a_rectangle_is_drawn()
    {
        // Both lie 2 × 10^12 pt to the right of the page and show nothing; the image's reach is beyond the limit at any
        // resolution of 72 dpi or more. The PDF back-end writes both pages.
        Page PageWith(DrawItem item) => new(new Size(100, 100), [item]);
        var area = new Rect(new Point(2e12, 10), new Size(10, 10));
        var image = new PageDocument(new DocumentMetadata("Off the page", null), [PageWith(new DrawImage(area, TestImages.Load("rgb8.png")))]);
        var rectangle = new PageDocument(new DocumentMetadata("Off the page", null), [PageWith(new FillRectangle(area, new Color(0, 0, 0)))]);

        image.ToPng(1, new RasterOptions(72)).Error.Code.Should().Be(RasterErrorCodes.ImageOutOfReach);
        rectangle.ToPng(1, new RasterOptions(72)).IsSuccess.Should().BeTrue();
        image.ToPdf(Options).IsSuccess.Should().BeTrue();
        rectangle.ToPdf(Options).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_page_with_a_glyph_whose_exact_coverage_takes_too_much_work_is_refused_by_the_raster_back_end_and_written_to_pdf()
    {
        // Glyph 1: the star polygon {1001/500}, one contour of 1,001 points crossing itself, in design units around the
        // glyph's origin; at 0.05625 pt and 72 dpi its radius of 16,000 units is 0.45 pixel, so the whole star lies in
        // pixel (10, 10) and asks about 5.0 × 10^8 units of work there, above Canvas.MaxFillWork (2^27).
        var points = Enumerable.Range(0, 1001)
            .Select(index => 2 * Math.PI * (index * 500L % 1001) / 1001)
            .Select(angle => ((int)Math.Round(16000 * Math.Cos(angle)), (int)Math.Round(16000 * Math.Sin(angle))))
            .ToArray();
        var star = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(0).UInt16(0).UInt16(0).UInt16(points.Length - 1).UInt16(0);
        star.Bytes(points.Select(_ => (byte)0x01));
        var previous = (X: 0, Y: 0);
        foreach (var (x, _) in points)
        {
            star.UInt16((x - previous.X) & 0xFFFF);
            previous.X = x;
        }

        foreach (var (_, y) in points)
        {
            star.UInt16((y - previous.Y) & 0xFFFF);
            previous.Y = y;
        }

        var font = FontParser.Parse(FontFiles.WithGlyphs(TestFonts.Bytes("Lato-Regular.ttf"), [[], star.ToArray()])).Value;
        var run = new GlyphRun(font, 0.05625, new Color(0, 0, 0), new Point(10.5, 10.5), [new PositionedGlyph(new GlyphId(1), 0, "x")]);
        var page = new PageDocument(new DocumentMetadata("Heavy glyph", null), [new Page(new Size(21, 21), [run])]);

        PageRasterizer.Render(page.Pages[0], new RasterOptions(72)).Error.Code.Should().Be(Ave.Extensions.Documents.Graphics.GraphicsErrorCodes.TooMuchWork);
        page.ToPng(1, new RasterOptions(72)).Error.Code.Should().Be(Ave.Extensions.Documents.Graphics.GraphicsErrorCodes.TooMuchWork);
        page.ToPdf(Options).IsSuccess.Should().BeTrue();
    }

    /// <summary>A document of the given number of pages, one short paragraph on each.</summary>
    private static Document Pages(int count) =>
        Document([.. Enumerable.Range(0, count).SelectMany(page => page == 0 ? [Paragraph("p")] : new Element[] { PageBreak(), Paragraph("p") })]).FontFamily("Lato");

    /// <summary>What every call on a document gives under a page limit: null when it renders, else the error.</summary>
    private static IEnumerable<(string Entry, Ave.Extensions.ErrorPaths.Error? Error)> EveryEntry(Document document, int? maxPages, bool pngPages = true)
    {
        var raster = new RasterOptions(9);
        static Ave.Extensions.ErrorPaths.Error? ErrorOf<T>(Ave.Extensions.Functional.Result<T, Ave.Extensions.ErrorPaths.Error> result) => result.IsSuccess ? null : result.Error;
        yield return ("ToPages", ErrorOf(maxPages is { } m1 ? document.ToPages(TestFonts.Set, m1) : document.ToPages(TestFonts.Set)));
        yield return ("ToPdf", ErrorOf(maxPages is { } m2 ? document.ToPdf(TestFonts.Set, Options, m2) : document.ToPdf(TestFonts.Set, Options)));
        yield return ("ToPng", ErrorOf(maxPages is { } m3 ? document.ToPng(TestFonts.Set, 1, raster, m3) : document.ToPng(TestFonts.Set, 1, raster)));
        if (pngPages)
        {
            yield return ("ToPngPages", ErrorOf(maxPages is { } m4 ? document.ToPngPages(TestFonts.Set, raster, m4) : document.ToPngPages(TestFonts.Set, raster)));
        }

        yield return ("ToImages", ErrorOf(maxPages is { } m5 ? document.ToImages(TestFonts.Set, raster, m5) : document.ToImages(TestFonts.Set, raster)));
    }

    [Fact]
    public void Every_call_on_a_document_renders_up_to_its_page_limit_and_refuses_one_page_past_it_naming_its_parameter()
    {
        EveryEntry(Pages(3), 3).Should().OnlyContain(entry => entry.Error == null, "just inside the limit");

        foreach (var (entry, error) in EveryEntry(Pages(3), 2))
        {
            error!.Value.Code.Should().Be(LayoutErrorCodes.TooManyPages, entry);
            error.Value.Message.Should().Contain("more than 2 pages", entry).And.Contain("the maxPages parameter of DocumentRendering (default 10000)", entry)
                .And.NotContain("HtmlRenderOptions", entry).And.NotContain("LayoutEnvironment", entry);
        }
    }

    [Fact]
    public void The_calls_on_a_document_stop_at_the_default_page_limit_and_render_past_it_when_it_is_raised()
    {
        // One page past the default: refused without the parameter, rendered with a raised limit.
        var document = Pages(LayoutEnvironment.DefaultMaxPages + 1);

        EveryEntry(document, null, pngPages: false).Should().OnlyContain(entry => entry.Error != null && entry.Error.Value.Code == LayoutErrorCodes.TooManyPages);
        EveryEntry(document, LayoutEnvironment.DefaultMaxPages + 1, pngPages: false).Should().OnlyContain(entry => entry.Error == null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public void A_page_limit_below_one_is_refused_by_every_call_on_a_document(int maxPages)
    {
        foreach (var (entry, error) in EveryEntry(Pages(1), maxPages))
        {
            error!.Value.Code.Should().Be(LayoutErrorCodes.InvalidOptions, entry);
            error.Value.Message.Should().Be($"The maxPages parameter of DocumentRendering is {maxPages}; a document has at least one page, so the page limit must be at least 1.", entry);
        }
    }

    [Fact]
    public void The_raster_refusal_of_an_image_names_the_place_of_the_image_in_the_code()
    {
        // A JPEG whose header claims 16000 x 16000 pixels: embedded in a PDF as it is, too many pixels for the raster output.
        var jpeg = TestImages.Bytes("jpeg-rgb.jpg");
        var frame = Enumerable.Range(0, jpeg.Length - 1).First(index => jpeg[index] == 0xFF && jpeg[index + 1] == 0xC0);
        jpeg[frame + 5] = jpeg[frame + 7] = 16000 >> 8;
        jpeg[frame + 6] = jpeg[frame + 8] = 16000 & 0xFF;
        var line = CurrentLine() + 1;
        var document = Document([Image(jpeg).Width(100.Pt()).Alt("x")]).FontFamily("Lato");

        document.ToPdf(TestFonts.Set, Options).IsSuccess.Should().BeTrue();
        var refused = document.ToPng(TestFonts.Set, 1, new RasterOptions(72)).Error;

        refused.Code.Should().Be(Ave.Extensions.Documents.Images.ImageErrorCodes.TooLarge);
        refused.Message.Should().StartWith($"The image at RenderingTests.cs:{line} (jpeg 16000x16000 rgb) is not drawn: the raster output decodes each image to pixels");
        refused.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
