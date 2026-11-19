using System.Collections.Immutable;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// An image in a line of text through every back-end: the page model holds it inside the paragraph, the tagged PDF nests
/// its figure, with its alternative text, in the paragraph's structure element and passes veraPDF, and the raster back-end
/// draws it where the page model puts it.
/// </summary>
public class InlineImageOutputTests
{
    /// <summary>A solid 16 × 4 pixel image of (60, 90, 160) (tools/generate-test-images.py, cases/wide.png).</summary>
    private static byte[] Wide => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "wide.png"));

    private static Document Signed() =>
        Document(
            [
                Paragraph("Above the line."),
                Paragraph([Text("Signed "), InlineImage(Image(Wide).Alt("Signature").Width(48.Pt()).MarginLeft(2.Pt())), Text(" by the client, "), InlineImage(Image(Wide).Decorative().Width(16.Pt())), Text(" today.")]),
            ],
            Page(300.Pt(), 200.Pt()).Margin(20.Pt()),
            title: "Inline image",
            language: "en").FontFamily("Lato");

    private static ImmutableArray<(string Name, Document Document)> Cases { get; } = [("inline-image", Signed())];

    [Fact]
    public void The_page_model_holds_the_image_inside_the_paragraph_s_structure()
    {
        var page = DocumentTranslator.ToModel(Signed()).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).Value.Pages[0];

        // The second paragraph's structure start, then its figure's, then the image, inside both.
        var items = page.Items.SkipWhile(item => item is not StructureStart { Role.StandardStructureType: "P" }).Skip(1)
            .SkipWhile(item => item is not StructureStart { Role.StandardStructureType: "P" }).ToList();
        var figure = items.FindIndex(item => item is StructureStart { Role.StandardStructureType: "Figure" });
        var image = items.FindIndex(item => item is DrawImage);
        var paragraphEnd = items.FindIndex(item => item is StructureEnd);
        figure.Should().BePositive();
        image.Should().BeGreaterThan(figure);
        items.FindIndex(figure, item => item is StructureEnd).Should().BeGreaterThan(image, "the image is content of the figure");
        items.Count(item => item is StructureEnd).Should().BeGreaterThanOrEqualTo(2);
        paragraphEnd.Should().BeGreaterThan(image);
    }

    [Fact]
    public void The_tagged_pdf_nests_the_figure_with_its_alternative_text_in_the_paragraph()
    {
        var pdf = Signed().ToPdf(TestFonts.SetWithItalic, SampleOutput.Options);

        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var paragraphs = PdfStructureReadBack.Read(pdf.Value).Single().DepthFirst().Where(element => element.Type == "P").ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[1].Children.Where(child => child.Type == "Figure").Should().ContainSingle("the decorative image is an artifact, not a figure")
            .Which.Details.Should().Be("Alt=\"Signature\"");
        paragraphs[1].DepthFirst().Select(element => element.Text).Should().Contain(text => text.Contains("Signed"));
    }

    [Fact]
    public void The_raster_back_end_draws_the_image_where_the_page_model_puts_it()
    {
        var page = DocumentTranslator.ToModel(Signed()).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).Value.Pages[0];
        var areas = page.Items.OfType<DrawImage>().Select(image => image.Area).ToList();
        areas.Should().HaveCount(2);

        var png = Signed().ToPng(TestFonts.SetWithItalic, 1, new RasterOptions(72)).Value;
        var pixels = (RasterImage)ImageDecoder.DecodeSamples(ImageDecoder.Decode(png.AsMemory()).Value).Value;
        var channels = pixels.Samples.Length / (pixels.PixelWidth * pixels.PixelHeight);
        foreach (var area in areas)
        {
            // At 72 dpi one point is one pixel: the pixel at the centre of each image area shows the image's colour.
            var (x, y) = ((int)(area.Origin.X + area.Size.Width / 2), (int)(area.Origin.Y + area.Size.Height / 2));
            var at = (y * pixels.PixelWidth + x) * channels;
            pixels.Samples.Skip(at).Take(3).Should().Equal(new byte[] { 60, 90, 160 }, $"the centre of the image at {area}");
        }
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_paragraph_with_images_in_its_line_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-inline-image-").FullName;
        try
        {
            foreach (var (name, document) in Cases)
            {
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. SampleFiles.Pdf(document)]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(Cases.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
