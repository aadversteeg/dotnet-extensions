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
/// Underlined and struck-through text through every back-end: in the page model each line is a filled rectangle inside a
/// layout artifact within the paragraph, never its content; the tagged PDF keeps the paragraph's text whole and passes
/// veraPDF; the raster back-end draws the line where the page model puts it.
/// </summary>
public class TextDecorationOutputTests
{
    private static readonly Color Red = new(0xCC, 0x00, 0x00);

    private static Document Decorated() =>
        Document(
            [
                Paragraph("Above the line."),
                Paragraph([Text("Underlined text, "), Text("struck through").LineThrough(), Text(" and underlined again.")]).FontSize(24.Pt()).Underline(Red),
            ],
            Page(400.Pt(), 200.Pt()).Margin(20.Pt()),
            title: "Text decoration",
            language: "en").FontFamily("Lato");

    private static ImmutableArray<(string Name, Document Document)> Cases { get; } = [("text-decoration", Decorated())];

    private static Page Laid() => DocumentTranslator.ToModel(Decorated()).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).Value.Pages[0];

    [Fact]
    public void Each_line_is_a_filled_rectangle_in_a_layout_artifact_inside_the_paragraph()
    {
        var items = Laid().Items.SkipWhile(item => item is not StructureStart { Role.StandardStructureType: "P" }).Skip(1)
            .SkipWhile(item => item is not StructureStart { Role.StandardStructureType: "P" }).Skip(1)
            .TakeWhile(item => item is not StructureEnd).ToList();

        items.OfType<FillRectangle>().Should().HaveCount(4, "three underlined pieces and one line-through");
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is FillRectangle)
            {
                items[index - 1].Should().Be(new ArtifactStart(ArtifactKind.Layout));
                items[index + 1].Should().Be(ArtifactEnd.Instance);
            }
        }

        items.OfType<GlyphRun>().Should().HaveCount(3);
    }

    [Fact]
    public void The_tagged_pdf_keeps_the_paragraph_s_text_whole()
    {
        var pdf = Decorated().ToPdf(TestFonts.SetWithItalic, SampleOutput.Options);

        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var paragraphs = PdfStructureReadBack.Read(pdf.Value).Single().DepthFirst().Where(element => element.Type == "P").ToList();
        paragraphs.Should().HaveCount(2);
        string.Concat(paragraphs[1].DepthFirst().Select(element => element.Text)).Should().Contain("struck through");
    }

    [Fact]
    public void The_raster_back_end_draws_the_line_where_the_page_model_puts_it()
    {
        var underline = Laid().Items.OfType<FillRectangle>().First(fill => fill.Color == Red).Area;

        var png = Decorated().ToPng(TestFonts.SetWithItalic, 1, new RasterOptions(72)).Value;
        var pixels = (RasterImage)ImageDecoder.DecodeSamples(ImageDecoder.Decode(png.AsMemory()).Value).Value;
        var channels = pixels.Samples.Length / (pixels.PixelWidth * pixels.PixelHeight);
        // At 72 dpi one point is one pixel; the line is 2.4 pt thick, so the pixel row below its top edge lies inside it.
        var (x, y) = ((int)(underline.Origin.X + 4), (int)Math.Ceiling(underline.Origin.Y));
        pixels.Samples.Skip((y * pixels.PixelWidth + x) * channels).Take(3).Should().Equal(new byte[] { 0xCC, 0, 0 }, $"a pixel of the underline at {underline}");
        pixels.Samples.Skip(((y + 4) * pixels.PixelWidth + x) * channels).Take(3).Should().Equal(new byte[] { 255, 255, 255 }, "the page below the underline");
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void An_underlined_paragraph_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-text-decoration-").FullName;
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
