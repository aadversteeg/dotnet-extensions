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
/// veraPDF; the raster back-end draws the line where the page model puts it. Under <c>text-decoration-skip-ink: auto</c>, the
/// initial value, each piece left between the gaps around the glyphs' ink is such a rectangle in an artifact of its own.
/// </summary>
public class TextDecorationOutputTests
{
    private static readonly Color Red = new(0xCC, 0x00, 0x00);

    private static Document Decorated(TextDecorationSkipInk skipInk = TextDecorationSkipInk.None) =>
        Document(
            [
                Paragraph("Above the line."),
                Paragraph([Text("Underlined text, "), Text("struck through").LineThrough(), Text(" and underlined again.")]).FontSize(24.Pt()).Underline(Red),
            ],
            Page(400.Pt(), 200.Pt()).Margin(20.Pt()),
            title: "Text decoration",
            language: "en").FontFamily("Lato").TextDecorationSkipInk(skipInk);

    private static ImmutableArray<(string Name, Document Document)> Cases { get; } =
        [("text-decoration", Decorated()), ("text-decoration-skip-ink", Decorated(TextDecorationSkipInk.Auto))];

    private static Page Laid(TextDecorationSkipInk skipInk = TextDecorationSkipInk.None) =>
        DocumentTranslator.ToModel(Decorated(skipInk)).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).Value.Pages[0];

    private static List<DrawItem> SecondParagraph(TextDecorationSkipInk skipInk) =>
        [.. Laid(skipInk).Items.SkipWhile(item => item is not StructureStart { Role.StandardStructureType: "P" }).Skip(1)
            .SkipWhile(item => item is not StructureStart { Role.StandardStructureType: "P" }).Skip(1)
            .TakeWhile(item => item is not StructureEnd)];

    private static void EachRectangleIsAnArtifact(List<DrawItem> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is FillRectangle)
            {
                items[index - 1].Should().Be(new ArtifactStart(ArtifactKind.Layout));
                items[index + 1].Should().Be(ArtifactEnd.Instance);
            }
        }
    }

    [Fact]
    public void Each_piece_left_between_the_gaps_around_ink_is_a_filled_rectangle_in_a_layout_artifact_and_a_line_through_stays_whole()
    {
        var (whole, cut) = (SecondParagraph(TextDecorationSkipInk.None), SecondParagraph(TextDecorationSkipInk.Auto));

        cut.OfType<FillRectangle>().Count().Should().BeGreaterThan(whole.OfType<FillRectangle>().Count(), "the descenders of \"p\", \"g\" and \"y\" cut the underline");
        cut.OfType<FillRectangle>().Where(fill => fill.Color == Color.Black).Should().ContainSingle("one whole line-through");
        EachRectangleIsAnArtifact(cut);
        cut.OfType<GlyphRun>().Should().HaveCount(3);
    }

    [Fact]
    public void Each_line_is_a_filled_rectangle_in_a_layout_artifact_inside_the_paragraph()
    {
        var items = SecondParagraph(TextDecorationSkipInk.None);

        items.OfType<FillRectangle>().Should().HaveCount(4, "three underlined pieces and one line-through");
        EachRectangleIsAnArtifact(items);

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
    public void An_underlined_paragraph_passes_veraPDF_with_whole_lines_and_with_lines_cut_around_ink(string flavour)
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
