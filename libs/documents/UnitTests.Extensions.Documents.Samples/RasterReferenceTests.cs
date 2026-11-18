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

    /// <summary>What separates the engine's page from a viewer's, removed one at a time for a named exception.</summary>
    [Flags]
    internal enum Cause
    {
        /// <summary>None.</summary>
        None = 0,

        /// <summary>Image interiors: the engine draws an image as exact sample squares, the viewers fit it to whole pixels
        /// and smooth it. Removed by leaving the drawn part of every image, grown by one pixel, out of the mean squared
        /// error and the counts (<see cref="RasterComparison.ImageInteriors"/>).</summary>
        ImageInterior = 1,

        /// <summary>Text baselines: the viewers draw a glyph's baseline on a whole pixel row (MuPDF the nearest, poppler the
        /// one above), the engine where the page model puts it. Removed by drawing the engine's page again with every
        /// baseline on the viewer's row (<see cref="RasterComparison.BaselinesOnPixelRows"/>).</summary>
        BaselineOnWholePixel = 2,
    }

    /// <summary>
    /// The rows of the sliced sample where the rule does not hold, measured on 2026-11-18 (tools/reference/raster/README.md,
    /// "The sliced sample"): each with its raw distances (engine–MuPDF, engine–poppler, MuPDF–poppler), pinned so a change is
    /// seen, and the causes that, removed, make the rule hold. These pages hold little but an image or a few lines of text,
    /// so the viewers' distance to each other, the allowance, is small, and two differences present on every sample show:
    /// image interiors and baselines on whole pixels (<see cref="Cause"/>).
    /// </summary>
    private static readonly (string Name, double Resolution, int Page, string Raw, Cause Causes)[] NamedExceptions =
    [
        ("sample-c-sliced", 96, 2, "0 | 39.60 | 835 / 669 / 270 | 0 | 76.09 | 1218 / 957 / 564 | 0 | 18.11 | 628 / 371 / 83", Cause.ImageInterior),
        ("sample-c-sliced", 96, 3, "0 | 79.33 | 1504 / 666 / 427 | 0 | 232.70 | 2323 / 1604 / 873 | 0 | 241.29 | 1895 / 1181 / 588", Cause.ImageInterior | Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 96, 5, "0 | 83.40 | 1170 / 721 / 25 | 0 | 93.84 | 1517 / 750 / 28 | 0 | 26.25 | 856 / 140 / 0", Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 96, 6, "0 | 9.25 | 121 / 81 / 4 | 0 | 45.33 | 862 / 813 / 4 | 0 | 42.72 | 757 / 736 / 0", Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 96, 7, "0 | 26.82 | 993 / 484 / 37 | 0 | 70.31 | 1769 / 1512 / 150 | 0 | 48.91 | 1358 / 979 / 3", Cause.ImageInterior | Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 96, 8, "0 | 21.53 | 572 / 211 / 4 | 0 | 55.59 | 1349 / 945 / 4 | 0 | 47.58 | 1212 / 742 / 0", Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 300, 6, "1 | 2.53 | 424 / 232 / 0 | 0 | 11.04 | 1758 / 418 / 163 | 1 | 10.77 | 1728 / 355 / 160", Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 300, 7, "1 | 12.42 | 3672 / 1637 / 885 | 0 | 21.79 | 6101 / 2611 / 849 | 1 | 21.97 | 6735 / 2250 / 757", Cause.ImageInterior | Cause.BaselineOnWholePixel),
        ("sample-c-sliced", 300, 8, "1 | 6.83 | 1973 / 623 / 147 | 0 | 22.27 | 2886 / 1996 / 511 | 1 | 18.63 | 2572 / 827 / 356", Cause.ImageInterior | Cause.BaselineOnWholePixel),
    ];

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
                    if (toPoppler.Over128 > KnownException.EngineToPoppler)
                    {
                        breaches.Add(FormattableString.Invariant($"page {index + 1}: the known exception grew: {toPoppler.Over128} pixels over 128 steps from poppler, pinned {KnownException.EngineToPoppler}"));
                    }

                    if (between.Over128 != KnownException.Viewers)
                    {
                        breaches.Add(FormattableString.Invariant($"page {index + 1}: the viewers' count of the known exception changed: {between.Over128}, pinned {KnownException.Viewers}"));
                    }

                    judged = toPoppler with { Over128 = between.Over128 };
                }

                table.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {dpi} | {index + 1} | {toMupdf} | {toPoppler} | {between} |");
                if (Environment.GetEnvironmentVariable("RASTER_REFERENCE_DIAGNOSE") is { Length: > 0 } diagnose)
                {
                    foreach (var cause in new[] { Cause.None, Cause.ImageInterior, Cause.BaselineOnWholePixel, Cause.ImageInterior | Cause.BaselineOnWholePixel })
                    {
                        var (m, p, b) = Adjusted(pages.Pages[index], resolution, cause, mupdf[index], poppler[index], edges);
                        File.AppendAllText(diagnose, FormattableString.Invariant($"| {name} | {dpi} | {index + 1} | {cause} | {m} | {p} | {b} | {string.Join("; ", RasterComparison.Breaches(m, p, b))} |\n"));
                    }
                }

                var named = NamedExceptions.Where(exception => (exception.Name, exception.Resolution, exception.Page) == (name, resolution, index + 1)).ToList();
                if (named.Count == 1)
                {
                    // The row as measured, then the rule with its named causes removed. A changed row is a breach, and the
                    // comparison goes on, so one run reports every page that moved.
                    var raw = $"{toMupdf} | {toPoppler} | {between}";
                    if (raw != named[0].Raw)
                    {
                        breaches.Add($"page {index + 1}: a named exception with other numbers than pinned: {raw}, pinned {named[0].Raw}");
                    }

                    var (m, p, b) = Adjusted(pages.Pages[index], resolution, named[0].Causes, mupdf[index], poppler[index], edges);
                    table.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {dpi} | {index + 1} ({named[0].Causes} removed) | {m} | {p} | {b} |");
                    breaches.AddRange(RasterComparison.Breaches(m, p, b).Select(breach => $"page {index + 1} with {named[0].Causes} removed: {breach}"));
                    Write(name, dpi, index, engine[index], mupdf[index], poppler[index]);
                    continue;
                }

                breaches.AddRange(RasterComparison.Breaches(toMupdf, judged, between).Select(breach => $"page {index + 1}: {breach}"));
                Write(name, dpi, index, engine[index], mupdf[index], poppler[index]);
            }

            if (Environment.GetEnvironmentVariable("RASTER_REFERENCE_OUT") is { Length: > 0 } outFolder)
            {
                File.AppendAllText(Path.Combine(outFolder, "distances.md"), table.ToString());
            }

            // Every breach in the message: the assertion itself names only the first.
            breaches.Should().BeEmpty($"{name} at {dpi} dpi (first reference MuPDF, second poppler) has {breaches.Count} breaches:\n{string.Join("\n", breaches)}\n{table}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The three distances of a page with the named causes removed (<see cref="Cause"/>): the engine's page drawn
    /// again with its baselines on each viewer's rows, and the image interiors left out of all three comparisons.</summary>
    private static (RasterComparison.Distance ToMupdf, RasterComparison.Distance ToPoppler, RasterComparison.Distance Between) Adjusted(
        Page page, double resolution, Cause causes,
        (int Width, int Height, int Channels, byte[] Pixels) mupdf, (int Width, int Height, int Channels, byte[] Pixels) poppler, (int Width, int Height, bool[] AtEdge) edges)
    {
        (int, int, int, byte[]) Engine(Func<double, double> row)
        {
            var drawn = causes.HasFlag(Cause.BaselineOnWholePixel) ? RasterComparison.BaselinesOnPixelRows(page, resolution, row) : page;
            var image = PageRasterizer.Render(drawn, new RasterOptions(resolution)).Value;
            return (image.Width, image.Height, 4, image.Pixels.ToArray());
        }

        var leftOut = causes.HasFlag(Cause.ImageInterior) ? RasterComparison.ImageInteriors(page, resolution) : new bool[edges.Width * edges.Height];
        return (
            RasterComparison.Compare(Engine(RasterComparison.MupdfRow), mupdf, edges, leftOut),
            RasterComparison.Compare(Engine(RasterComparison.PopplerRow), poppler, edges, leftOut),
            RasterComparison.Compare(mupdf, poppler, edges, leftOut));
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

        File.WriteAllBytes(Path.Combine(folder, $"{name}.{dpi}dpi.page{index + 1}.engine-mupdf-poppler.png"), [.. ImageEncoder.EncodePng(width, height, sheet).Value]);
    }
}
