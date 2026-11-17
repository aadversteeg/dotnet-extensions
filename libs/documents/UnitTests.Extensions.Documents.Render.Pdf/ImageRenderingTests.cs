using System.Text;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using UglyToad.PdfPig;

namespace UnitTests.Ave.Documents.Render.Pdf;

/// <summary>Images in the PDF: image XObjects with a soft mask for alpha, JPEG embedded unchanged, each image once.</summary>
public class ImageRenderingTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    private static PageDocument Images() => new(
        new DocumentMetadata("Images", null),
        [
            new Page(new Size(300, 200),
            [
                new DrawImage(new Rect(new Point(20, 30), new Size(70, 50)), TestImages.Load("rgba8.png")),
                new DrawImage(new Rect(new Point(120, 30), new Size(80, 60)), TestImages.Load("baseline-rgb.jpg")),
            ]),
            new Page(new Size(300, 200), [new DrawImage(new Rect(new Point(10, 10), new Size(35, 25)), TestImages.Load("rgba8.png"))]),
        ]);

    private static byte[] Render() => [.. PdfRenderer.Render(Images(), Options).Value];

    [Fact]
    public void Each_image_is_drawn_into_its_rectangle_as_an_independent_reader_sees_it()
    {
        using var pdf = PdfDocument.Open(Render());

        var first = pdf.GetPage(1).GetImages().OrderBy(image => image.BoundingBox.Left).ToArray();
        first.Should().HaveCount(2);

        // PdfPig reports bounds in PDF user space (origin bottom-left): y = 200 − 30 − height.
        first[0].BoundingBox.Left.Should().BeApproximately(20, 1e-6);
        first[0].BoundingBox.Bottom.Should().BeApproximately(200 - 30 - 50, 1e-6);
        first[0].BoundingBox.Width.Should().BeApproximately(70, 1e-6);
        first[0].WidthInSamples.Should().Be(7);
        first[1].BoundingBox.Width.Should().BeApproximately(80, 1e-6);
        first[1].WidthInSamples.Should().Be(40);
        pdf.GetPage(2).GetImages().Should().ContainSingle();
    }

    [Fact]
    public void Alpha_becomes_a_soft_mask_and_a_JPEG_keeps_its_DCT_data()
    {
        var text = Encoding.Latin1.GetString(Render());

        Occurrences.Count(text, "/Subtype /Image").Should().Be(3, "the PNG, its soft mask and the JPEG; the PNG used twice is embedded once");
        text.Should().Contain("/SMask ").And.Contain("/Filter /DCTDecode");
        text.Should().Contain(Encoding.Latin1.GetString(TestImages.Bytes("baseline-rgb.jpg")), "the JPEG file is embedded byte for byte");
    }

    [Fact]
    public void Output_with_images_is_deterministic() => Render().Should().Equal(Render());

    private static class Occurrences
    {
        public static int Count(string text, string literal) => (text.Length - text.Replace(literal, string.Empty, StringComparison.Ordinal).Length) / literal.Length;
    }
}
