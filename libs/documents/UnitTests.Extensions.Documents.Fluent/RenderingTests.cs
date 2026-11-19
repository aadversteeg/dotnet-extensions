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

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
