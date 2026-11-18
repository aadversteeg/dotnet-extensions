using System.Globalization;
using System.IO;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;
using RgbaImage = Ave.Extensions.Documents.Graphics.RgbaImage;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The raster back-end against two PDF viewers: each page drawn by the engine from its page model, and the engine's PDF of
/// the same page model rendered by MuPDF 1.25.1 (<c>mutool draw -A 8</c>, unhinted when anti-aliased) and by poppler
/// 25.03.0 (<c>pdftoppm</c>, FreeType unhinted), at the same resolution, compared by the rule and the metrics of
/// <see cref="RasterComparison"/>. PDFium is not a reference: it hints TrueType glyphs and has no switch to stop it.
/// Needs Docker (trait <c>Category=Reference</c>).
/// </summary>
/// <remarks>
/// Two kinds of pages: every page of every sample (images not on the pixel grid, so image rectangles are left out of the
/// away-from-edges metric), and pages of images placed exactly on the pixel grid at 1:1, at whole-number enlargements and
/// at whole-number reductions, PNG and JPEG, grey and colour, where the image's interior is compared too. With the
/// environment variable <c>RASTER_REFERENCE_OUT</c> set to a folder, the test writes the table of distances
/// (<c>distances.md</c>) and, for every timesheet page, the three images side by side (engine, MuPDF, poppler);
/// <c>tools/reference/raster/README.md</c> keeps the table measured last.
/// </remarks>
[Trait("Category", "Reference")]
public sealed class RasterReferenceTests
{
    public static TheoryData<string, double> Samples()
    {
        var cases = new TheoryData<string, double>();
        foreach (var (name, _) in SampleFiles.Documents)
        {
            cases.Add(name, 96);
        }

        cases.Add("timesheet-normal.nl", 300);
        cases.Add("sample-a-letter", 300);
        return cases;
    }

    /// <summary>Images and the scales they are drawn at on the pixel grid: 1 is 1:1, 2 and 3 enlargements, 0.5 and 0.25
    /// reductions (only where the image's size divides).</summary>
    public static TheoryData<string, double> GridImages()
    {
        var cases = new TheoryData<string, double>();
        foreach (var (file, scales) in new[]
        {
            ("rgb8-filters.png", new[] { 1, 2, 3, 0.5, 0.25 }),
            ("grey8.png", new[] { 1, 2, 3.0 }),
            ("rgba8.png", new[] { 1, 2, 3.0 }),
            ("baseline-rgb.jpg", new[] { 1, 2, 3, 0.5 }),
            ("baseline-grey.jpg", new[] { 1, 2, 3, 0.5 }),
        })
        {
            foreach (var scale in scales)
            {
                cases.Add(file, scale);
            }
        }

        return cases;
    }

    [DockerTheory]
    [MemberData(nameof(Samples))]
    public void Every_sample_page_is_as_close_to_each_viewer_as_the_rule_asks(string name, double resolution)
    {
        var document = SampleFiles.Documents.Single(sample => sample.Name == name).Document;
        var pages = Ave.Extensions.Documents.DocumentRendering.ToPages(document, TestFonts.SetWithItalic).Value;

        Compare(name, pages, resolution, imagesAsEdges: true);
    }

    [DockerTheory(Skip = "Waiting for the rule for images on the pixel grid: at 2x both viewers interpolate and the engine does not, and poppler resamples every image below 4x (tools/reference/raster/README.md, \"Images on the pixel grid\").")]
    [MemberData(nameof(GridImages))]
    public void An_image_on_the_pixel_grid_is_as_close_to_each_viewer_as_the_rule_asks(string file, double scale)
    {
        // At 96 dpi a pixel is 0.75 pt: the image starts on a whole pixel, 4 pixels from the page's edges, and covers whole
        // pixels.
        const double resolution = 96;
        var image = TestImages.Load(file);
        var (width, height) = (image.PixelWidth * scale, image.PixelHeight * scale);
        var page = new Page(
            new Size((width + 8) * 0.75, (height + 8) * 0.75),
            [new DrawImage(new Rect(new Point(4 * 0.75, 4 * 0.75), new Size(width * 0.75, height * 0.75)), image)]);

        Compare($"{file} at {scale.ToString(CultureInfo.InvariantCulture)}", new PageDocument(new DocumentMetadata(file, null), [page]), resolution, imagesAsEdges: false);
    }

    private static void Compare(string name, PageDocument pages, double resolution, bool imagesAsEdges)
    {
        var engine = PageRasterizer.Render(pages, new RasterOptions(resolution)).Value;
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-raster-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "document.pdf"), [.. PdfRenderer.Render(pages, new PdfRenderOptions(SampleOutput.Options.CreationDate)).Value]);
            var dpi = resolution.ToString(CultureInfo.InvariantCulture);
            var (exitCode, output) = Docker.Run(
                ["run", "--rm", .. Docker.UserArguments, "-v", $"{folder}:/work", ReferenceTools.Image, "bash", "-c",
                    $"set -eu && mutool draw -q -r {dpi} -A 8 -o /work/mupdf-%d.png /work/document.pdf && pdftoppm -r {dpi} -png /work/document.pdf /work/poppler"],
                TimeSpan.FromMinutes(5));
            exitCode.Should().Be(0, output);

            var mupdf = Renders(folder, "mupdf-");
            var poppler = Renders(folder, "poppler-");
            mupdf.Should().HaveCount(engine.Length);
            poppler.Should().HaveCount(engine.Length);

            var breaches = new List<string>();
            var table = new StringBuilder();
            for (var index = 0; index < engine.Length; index++)
            {
                var edges = RasterComparison.EdgeMask(pages.Pages[index], resolution, imagesAsEdges);
                var ours = (engine[index].Width, engine[index].Height, 4, engine[index].Pixels.ToArray());
                var toMupdf = RasterComparison.Compare(ours, mupdf[index], edges);
                var toPoppler = RasterComparison.Compare(ours, poppler[index], edges);
                var between = RasterComparison.Compare(mupdf[index], poppler[index], edges);
                table.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {dpi} | {index + 1} | {toMupdf} | {toPoppler} | {between} |");
                breaches.AddRange(RasterComparison.Breaches(toMupdf, toPoppler, between).Select(breach => $"page {index + 1}: {breach}"));
                Write(name, dpi, index, engine[index], mupdf[index], poppler[index]);
            }

            if (Environment.GetEnvironmentVariable("RASTER_REFERENCE_OUT") is { Length: > 0 } outFolder)
            {
                File.AppendAllText(Path.Combine(outFolder, "distances.md"), table.ToString());
            }

            breaches.Should().BeEmpty($"{name} at {dpi} dpi (first reference MuPDF, second poppler):\n{table}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The PNG files a renderer wrote, in page order (poppler pads page numbers with zeros).</summary>
    private static List<(int Width, int Height, int Channels, byte[] Pixels)> Renders(string folder, string prefix) =>
        [.. Directory.GetFiles(folder, prefix + "*.png")
            .OrderBy(path => int.Parse(Path.GetFileNameWithoutExtension(path)[prefix.Length..], CultureInfo.InvariantCulture))
            .Select(path => (RasterImage)ImageDecoder.Decode(File.ReadAllBytes(path)).Value)
            .Select(image => (image.PixelWidth, image.PixelHeight, image.ColorSpace == ImageColorSpace.Gray ? 1 : 3, image.Samples.ToArray()))];

    /// <summary>With RASTER_REFERENCE_OUT set: for timesheets, the three images side by side.</summary>
    private static void Write(string name, string dpi, int index, RgbaImage engine, (int Width, int Height, int Channels, byte[] Pixels) mupdf, (int Width, int Height, int Channels, byte[] Pixels) poppler)
    {
        if (Environment.GetEnvironmentVariable("RASTER_REFERENCE_OUT") is not { Length: > 0 } folder || !name.StartsWith("timesheet", StringComparison.Ordinal))
        {
            return;
        }

        var parts = new[] { (engine.Width, engine.Height, 4, engine.Pixels.ToArray()), mupdf, poppler };
        var height = parts.Max(part => part.Item2);
        var width = parts.Sum(part => part.Item1);
        var sheet = new byte[width * height * 4];
        Array.Fill(sheet, (byte)255);
        var left = 0;
        foreach (var (partWidth, partHeight, channels, pixels) in parts)
        {
            for (var y = 0; y < partHeight; y++)
            {
                for (var x = 0; x < partWidth; x++)
                {
                    var target = ((y * width) + left + x) * 4;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        sheet[target + channel] = pixels[(((y * partWidth) + x) * channels) + (channels == 1 ? 0 : channel)];
                    }
                }
            }

            left += partWidth;
        }

        File.WriteAllBytes(Path.Combine(folder, $"{name}.{dpi}dpi.page{index + 1}.engine-mupdf-poppler.png"), [.. ImageEncoder.EncodePng(width, height, sheet)]);
    }
}
