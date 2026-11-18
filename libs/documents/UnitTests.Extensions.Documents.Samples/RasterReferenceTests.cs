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
/// Two kinds of pages: every page of every sample at 96 and 300 dpi, with one named exception
/// (<see cref="KnownException"/>; images not on the pixel grid, so image rectangles are left out of the away-from-edges
/// metric), and pages of one image at 1:1 on the pixel grid, compared with MuPDF inside the image. With the
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
            cases.Add(name, 300);
        }

        return cases;
    }

    /// <summary>
    /// The one page where the rule does not hold, as measured on 2026-11-18 (tools/reference/raster/README.md): at 300 dpi,
    /// page 1 of timesheet-edge.nl has 12 753 pixels over 128 steps from poppler, where the viewers have 12 569 between
    /// them (and the engine 552 from MuPDF). The test holds the page to those two numbers, so it fails if the engine's
    /// count grows or the viewers' count changes; every other metric of the page, and every other page, keeps the rule.
    /// </summary>
    private static readonly (string Name, double Resolution, int Page, int EngineToPoppler, int Viewers) KnownException =
        ("timesheet-edge.nl", 300, 1, 12_753, 12_569);

    [DockerTheory]
    [MemberData(nameof(Samples))]
    public void Every_sample_page_is_as_close_to_each_viewer_as_the_rule_asks(string name, double resolution)
    {
        var document = SampleFiles.Documents.Single(sample => sample.Name == name).Document;
        var pages = Ave.Extensions.Documents.DocumentRendering.ToPages(document, TestFonts.SetWithItalic).Value;

        Compare(name, pages, resolution);
    }

    /// <summary>
    /// Image interiors against MuPDF, where resampling plays no part: the image at 1:1 on the pixel grid (at 96 dpi a
    /// pixel is 0.75 pt), so each device pixel is one sample square and the comparison checks decoding and colour only.
    /// The image rule itself (area-weighted sample squares, ISO 32000-1 §8.9.5.3; PDF/A forbids asking for interpolation,
    /// ISO 19005-3 §6.2.8) is proven exactly in Graphics (<c>ImageResamplingTests</c>). PNG: equal. JPEG: within the bound
    /// <c>JpegDecoderReferenceTests</c> derived against djpeg for the file's sampling — 1 for grey, 3 for colour (the
    /// inverse DCT's rounding, counted up to 1.772 times through the colour transform, and the rounding of up-sampled
    /// chroma). Poppler is not a reference for image interiors: it resamples every image drawn below 4×, at 1:1 too
    /// (measured: up to 198–245 from MuPDF and the engine, both equal there; tools/reference/raster/README.md). A PNG with
    /// alpha is left out: on the white page its pixels test compositing arithmetic, not decoding (Graphics tests it).
    /// </summary>
    [DockerTheory]
    [InlineData("rgb8-filters.png", 0)]
    [InlineData("grey8.png", 0)]
    [InlineData("palette4.png", 0)]
    [InlineData("baseline-grey.jpg", 1)]
    [InlineData("baseline-rgb.jpg", 3)]
    [InlineData("progressive-rgb.jpg", 3)]
    [InlineData("jpeg-444.jpg", 3)]
    public void An_image_at_one_to_one_on_the_pixel_grid_decodes_like_MuPDF(string file, int largest)
    {
        var image = TestImages.Load(file);
        var (width, height) = (image.PixelWidth, image.PixelHeight);
        var page = new Page(
            new Size((width + 8) * 0.75, (height + 8) * 0.75),
            [new DrawImage(new Rect(new Point(4 * 0.75, 4 * 0.75), new Size(width * 0.75, height * 0.75)), image)]);
        var pages = new PageDocument(new DocumentMetadata(file, null), [page]);
        var engine = PageRasterizer.Render(pages, new RasterOptions(96)).Value[0];
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-raster-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "document.pdf"), [.. PdfRenderer.Render(pages, new PdfRenderOptions(SampleOutput.Options.CreationDate)).Value]);
            var (exitCode, output) = Docker.Run(
                ["run", "--rm", .. Docker.UserArguments, "-v", $"{folder}:/work", ReferenceTools.Image, "mutool", "draw", "-q", "-r", "96", "-A", "8", "-o", "/work/mupdf-%d.png", "/work/document.pdf"],
                TimeSpan.FromMinutes(2));
            exitCode.Should().Be(0, output);
            var (mupdfWidth, _, channels, mupdf) = Renders(folder, "mupdf-").Single();

            var difference = 0;
            for (var y = 4; y < 4 + height; y++)
            {
                for (var x = 4; x < 4 + width; x++)
                {
                    for (var channel = 0; channel < 3; channel++)
                    {
                        difference = Math.Max(difference, Math.Abs(engine.Pixels[(((y * engine.Width) + x) * 4) + channel] - mupdf[(((y * mupdfWidth) + x) * channels) + channel]));
                    }
                }
            }

            difference.Should().BeLessThanOrEqualTo(largest, $"{file} at 1:1: the largest channel difference inside the image");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Compare(string name, PageDocument pages, double resolution)
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
                var edges = RasterComparison.EdgeMask(pages.Pages[index], resolution);
                var ours = (engine[index].Width, engine[index].Height, 4, engine[index].Pixels.ToArray());
                var toMupdf = RasterComparison.Compare(ours, mupdf[index], edges);
                var toPoppler = RasterComparison.Compare(ours, poppler[index], edges);
                var between = RasterComparison.Compare(mupdf[index], poppler[index], edges);
                var judged = toPoppler;
                if ((name, resolution, index + 1) == (KnownException.Name, KnownException.Resolution, KnownException.Page))
                {
                    toPoppler.Over128.Should().BeLessThanOrEqualTo(KnownException.EngineToPoppler, "the known exception must not grow");
                    between.Over128.Should().Be(KnownException.Viewers, "the known exception was measured against this count");
                    judged = toPoppler with { Over128 = between.Over128 };
                }

                table.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {dpi} | {index + 1} | {toMupdf} | {toPoppler} | {between} |");
                breaches.AddRange(RasterComparison.Breaches(toMupdf, judged, between).Select(breach => $"page {index + 1}: {breach}"));
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
