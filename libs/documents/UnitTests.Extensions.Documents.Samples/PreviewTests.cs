using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// Preview images through the composition package: the two-step pipeline (<see cref="DocumentRendering.ToPages"/>, then a
/// back-end) gives the same output as the one-call entry points, and the samples' pages are pinned as golden PNG files,
/// compared by their pixels (update with <c>UPDATE_GOLDEN=1</c> and review the images). The tests run on Linux and
/// Windows, x64 and arm64, so the goldens also show that the pixels do not depend on the platform. The PNG bytes depend
/// on the runtime's zlib as well; one test pins them (<c>PngEncoderTests</c>).
/// </summary>
public class PreviewTests
{
    private static readonly RasterOptions Screen = new(96);

    [Theory]
    [InlineData("sample-a-letter", 1)]
    [InlineData("sample-a-letter", 2)]
    [InlineData("timesheet-normal.nl", 1)]
    [InlineData("timesheet-normal.nl", 2)]
    public void A_sample_page_at_96_dpi_has_the_pixels_of_its_golden_PNG(string name, int page)
    {
        var document = SampleFiles.Documents.Single(sample => sample.Name == name).Document;

        var images = document.ToImages(TestFonts.SetWithItalic, Screen);

        images.IsSuccess.Should().BeTrue(images.IsFailure ? images.Error.ToString() : string.Empty);
        var image = images.Value[page - 1];
        GoldenImages.ComparePixels($"{name}.page{page}.96dpi.png", image.Width, image.Height, image.Pixels.AsSpan()).Should().BeNull();
    }

    [Fact]
    public void Laying_out_once_gives_the_same_PDF_and_images_as_the_one_call_entry_points()
    {
        var document = SampleFiles.Timesheet("normal", "nl-NL");
        var fonts = TestFonts.SetWithItalic;

        var pages = document.ToPages(fonts).Value;

        pages.ToPdf(SampleOutput.Options).Value.Should().Equal(document.ToPdf(fonts, SampleOutput.Options).Value);
        var images = pages.ToImages(Screen).Value;
        var pngs = pages.ToPngPages(Screen).Value;
        images.Should().HaveCount(pages.Pages.Count);
        pngs.Should().HaveCount(pages.Pages.Count);
        pngs[1].Should().Equal(pages.ToPng(2, Screen).Value);
        pngs[1].Should().Equal(ImageEncoder.EncodePng(images[1].Width, images[1].Height, images[1].Pixels.AsSpan()).Value);
        document.ToPngPages(fonts, Screen).Value.Select(png => png.Length).Should().Equal(pngs.Select(png => png.Length));
        document.ToImages(fonts, Screen).Value[0].Pixels.Should().Equal(images[0].Pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void A_page_the_document_does_not_have_is_refused(int page)
    {
        var result = SampleFiles.Timesheet("normal", "nl-NL").ToPng(TestFonts.SetWithItalic, page, Screen);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.PageNotFound);
    }

    [Fact]
    public void An_error_of_the_document_comes_before_the_raster_back_end()
    {
        var result = Ave.Extensions.Documents.Fluent.Elements.Document([Ave.Extensions.Documents.Fluent.Elements.Paragraph("x")])
            .ToPng(new Ave.Extensions.Documents.Fonts.FontSet([]), 1, Screen);

        result.IsFailure.Should().BeTrue();
        result.Error.Is(RasterErrorCodes._).Should().BeFalse("the document fails in the front-end or the layout first");
    }
}
