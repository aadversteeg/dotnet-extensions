using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// How the raster back-end's pages are compared with the renders of reference PDF viewers (MuPDF, poppler) of the
/// engine's own PDF of the same page model: the metrics of the distance between two images, and the rule that decides.
/// One place for all of them, like <c>ChromiumLayout</c> for layout numbers. Only the sample tests use it today; a second
/// user (the HTML front-end's visual checks) moves it to Ave.Extensions.Documents.TestSupport.
/// </summary>
/// <remarks>
/// <para><b>The metrics</b>, over the pixels both images have, on the red, green and blue channels (all renders are
/// opaque):</para>
/// <list type="bullet">
/// <item><see cref="Distance.LargestAwayFromEdges"/>: the largest difference of one channel over the pixels away from edges
/// (<see cref="EdgeMask"/>), where both images should show the same colour.</item>
/// <item><see cref="Distance.MeanSquaredError"/>: Σ d² / (3 × pixels), over every pixel and channel, in 8-bit steps
/// squared.</item>
/// <item><see cref="Distance.Over32"/>, <see cref="Distance.Over64"/>, <see cref="Distance.Over128"/>: the pixels where
/// some channel differs by more than 32, 64 or 128 steps.</item>
/// </list>
/// <para><b>The rule</b> (<see cref="Breaches"/>). The references differ from each other at every glyph edge: they sample
/// coverage on different grids (MuPDF 17 × 15 sub-pixels, poppler 4 × 4 with a gamma on vector edges), round glyph
/// positions to different fractions of a pixel, and poppler snaps thin rectangles to whole pixels. So a bare tolerance
/// would be a guess; the references' distance to each other is the bound:</para>
/// <list type="number">
/// <item>away from edges, mean squared error, and pixels over 128 (a wrong pixel, not an edge drawn a little differently):
/// the engine's distance to EACH reference is not larger than the references' distance to each other;</item>
/// <item>pixels over 32 and over 64, which measure how an edge is drawn: the engine is not the outlier — its distance to
/// the NEARER reference is not larger than the references' distance to each other. Poppler draws glyph edges heavier and
/// snaps hairlines, so the engine and MuPDF are about equally far from poppler, and either can be a little farther by
/// chance;</item>
/// <item>no other tolerance; the golden PNG files catch any change over time.</item>
/// </list>
/// <para><b>Images.</b> Both viewers fit an image to whole pixels before they draw it and smooth some enlargements, and the
/// engine maps it exactly onto its rectangle as sample squares, as the PDF defines it (ISO 32000-1 §8.9.5.3; PDF/A forbids
/// asking for interpolation, ISO 19005-3 §6.2.8). So image rectangles, grown by one pixel, are left out of the
/// away-from-edges metric (<see cref="EdgeMask"/>). The image rule is proven exactly in Graphics; decoding and colour are
/// compared with MuPDF on images at 1:1 on the pixel grid (<c>RasterReferenceTests</c>).</para>
/// </remarks>
internal static class RasterComparison
{
    /// <summary>The distance between two images by the metrics.</summary>
    /// <param name="LargestAwayFromEdges">The largest channel difference away from edges.</param>
    /// <param name="MeanSquaredError">The mean squared difference per channel.</param>
    /// <param name="Over32">Pixels with a channel difference above 32.</param>
    /// <param name="Over64">Pixels with a channel difference above 64.</param>
    /// <param name="Over128">Pixels with a channel difference above 128.</param>
    public sealed record Distance(int LargestAwayFromEdges, double MeanSquaredError, int Over32, int Over64, int Over128)
    {
        /// <summary>The numbers, for a table.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => FormattableString.Invariant($"{LargestAwayFromEdges} | {MeanSquaredError:F2} | {Over32} / {Over64} / {Over128}");
    }

    /// <summary>The breaches of the rule (see the remarks) by the engine's distances to two references; empty when it holds.</summary>
    /// <param name="toFirst">The engine's distance to the first reference.</param>
    /// <param name="toSecond">The engine's distance to the second reference.</param>
    /// <param name="between">The references' distance to each other.</param>
    /// <returns>One line per breach.</returns>
    public static IReadOnlyList<string> Breaches(Distance toFirst, Distance toSecond, Distance between)
    {
        var breaches = new List<string>();
        foreach (var (name, distance) in new[] { ("first", toFirst), ("second", toSecond) })
        {
            if (distance.LargestAwayFromEdges > between.LargestAwayFromEdges)
            {
                breaches.Add($"away from edges, to the {name} reference: {distance.LargestAwayFromEdges} > {between.LargestAwayFromEdges}");
            }

            if (distance.MeanSquaredError > between.MeanSquaredError)
            {
                breaches.Add(FormattableString.Invariant($"mean squared error, to the {name} reference: {distance.MeanSquaredError:F2} > {between.MeanSquaredError:F2}"));
            }

            if (distance.Over128 > between.Over128)
            {
                breaches.Add($"pixels over 128, to the {name} reference: {distance.Over128} > {between.Over128}");
            }
        }

        if (Math.Min(toFirst.Over32, toSecond.Over32) > between.Over32)
        {
            breaches.Add($"pixels over 32, to the nearer reference: {Math.Min(toFirst.Over32, toSecond.Over32)} > {between.Over32}");
        }

        if (Math.Min(toFirst.Over64, toSecond.Over64) > between.Over64)
        {
            breaches.Add($"pixels over 64, to the nearer reference: {Math.Min(toFirst.Over64, toSecond.Over64)} > {between.Over64}");
        }

        return breaches;
    }

    /// <summary>
    /// The pixels at an edge: a pixel is at an edge of an item when that item's coverage is not the same in all pixels of
    /// the pixel's 3 × 3 neighbourhood, and at an edge when it is at an edge of any item of the page (a glyph, a filled
    /// rectangle, an image's rectangle), whatever is drawn over or under it, and every pixel within one pixel of an image's
    /// rectangle is at an edge too. Coverage is the exact coverage of
    /// Ave.Extensions.Documents.Graphics, of each item on its own: so a partly covered pixel, a pixel next to one, and a
    /// pixel next to an edge that falls exactly between two pixels are at an edge; everything else is away from edges.
    /// Each item's geometry is worked out here from the page model, independently of the raster back-end's painter: a
    /// rectangle is its area; a glyph is its outline scaled by font size / units per em, y flipped, at the run's origin
    /// plus the glyph's offset.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="resolution">Pixels per inch.</param>
    /// <returns>Width × height flags, row by row, with the size of the engine's image of the page.</returns>
    public static (int Width, int Height, bool[] AtEdge) EdgeMask(Page page, double resolution)
    {
        var (longWidth, longHeight) = PageRasterizer.PixelSize(page.Size, resolution);
        var (width, height) = ((int)longWidth, (int)longHeight);
        var scale = resolution / 72;
        var atEdge = new bool[width * height];
        foreach (var (path, transform, left, top, right, bottom) in Shapes(page, scale))
        {
            // Two pixels of margin: the coverage is 0 around the shape, so every pixel next to it is inside the mask.
            var box = new PixelBox(Math.Floor(left) - 2, Math.Floor(top) - 2, Math.Ceiling(right) + 2, Math.Ceiling(bottom) + 2).Intersect(new PixelBox(0, 0, width, height));
            if (box.IsEmpty)
            {
                continue;
            }

            var mask = Canvas.Coverage(path, transform, FillRule.NonZero, box).Value;
            for (var y = mask.Top; y < mask.Top + mask.Height; y++)
            {
                for (var x = mask.Left; x < mask.Left + mask.Width; x++)
                {
                    var value = mask.At(x, y);
                    for (var dy = -1; dy <= 1 && !atEdge[(y * width) + x]; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (mask.At(x + dx, y + dy) != value)
                            {
                                atEdge[(y * width) + x] = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        // Image rectangles, grown by one pixel, count as edges (see the class remarks).
        foreach (var image in page.Items.OfType<DrawImage>())
        {
            var area = image.Area;
            var left = (int)Math.Floor(Math.Min(area.Origin.X, area.Origin.X + area.Size.Width) * scale) - 1;
            var top = (int)Math.Floor(Math.Min(area.Origin.Y, area.Origin.Y + area.Size.Height) * scale) - 1;
            var right = (int)Math.Ceiling(Math.Max(area.Origin.X, area.Origin.X + area.Size.Width) * scale) + 1;
            var bottom = (int)Math.Ceiling(Math.Max(area.Origin.Y, area.Origin.Y + area.Size.Height) * scale) + 1;
            for (var y = Math.Max(0, top); y < Math.Min(height, bottom); y++)
            {
                for (var x = Math.Max(0, left); x < Math.Min(width, right); x++)
                {
                    atEdge[(y * width) + x] = true;
                }
            }
        }

        return (width, height, atEdge);
    }

    /// <summary>Every item of the page that draws, as a path, its transform to pixels and its bounds in pixels.</summary>
    private static IEnumerable<(Path Path, Affine Transform, double Left, double Top, double Right, double Bottom)> Shapes(Page page, double scale)
    {
        foreach (var item in page.Items)
        {
            if (item is FillRectangle or DrawImage)
            {
                var area = item is FillRectangle fill ? fill.Area : ((DrawImage)item).Area;
                var (x0, x1) = (Math.Min(area.Origin.X, area.Origin.X + area.Size.Width) * scale, Math.Max(area.Origin.X, area.Origin.X + area.Size.Width) * scale);
                var (y0, y1) = (Math.Min(area.Origin.Y, area.Origin.Y + area.Size.Height) * scale, Math.Max(area.Origin.Y, area.Origin.Y + area.Size.Height) * scale);
                yield return (Path.Rectangle(area.Origin.X, area.Origin.Y, area.Size.Width, area.Size.Height), Affine.Scale(scale, scale), x0, y0, x1, y1);
            }
            else if (item is GlyphRun run)
            {
                var size = run.FontSize / run.Font.Metrics.UnitsPerEm * scale;
                foreach (var glyph in run.Glyphs)
                {
                    var shape = run.Font.Shape(glyph.Glyph).Value;
                    if (shape.Contours.IsEmpty)
                    {
                        continue;
                    }

                    var transform = new Affine(size, 0, 0, -size, (run.Origin.X + glyph.X) * scale, (run.Origin.Y + glyph.Y) * scale);
                    var points = shape.Contours.SelectMany(contour => contour.Points).ToList();
                    var builder = new PathBuilder();
                    foreach (var contour in shape.Contours)
                    {
                        var segments = contour.Segments();
                        var subpath = builder.MoveTo(segments.Start.X, segments.Start.Y);
                        foreach (var segment in segments.Segments)
                        {
                            _ = segment.Control is { } control
                                ? subpath.QuadraticTo(control.X, control.Y, segment.End.X, segment.End.Y)
                                : subpath.LineTo(segment.End.X, segment.End.Y);
                        }

                        subpath.Close();
                    }

                    yield return (
                        builder.ToPath(),
                        transform,
                        points.Min(point => transform.MapX(point.X, point.Y)),
                        points.Min(point => transform.MapY(point.X, point.Y)),
                        points.Max(point => transform.MapX(point.X, point.Y)),
                        points.Max(point => transform.MapY(point.X, point.Y)));
                }
            }
        }
    }

    /// <summary>The distance between two images of the same size by the metrics; a grey image (1 or 2 channels) counts
    /// its grey value as red, green and blue.</summary>
    /// <param name="first">The first image: width, height, channels per pixel (1 to 4) and pixels.</param>
    /// <param name="second">The second image.</param>
    /// <param name="edges">The edge mask of the page (<see cref="EdgeMask"/>).</param>
    /// <returns>The distance.</returns>
    /// <exception cref="ArgumentException">The images and the mask are not all the same size.</exception>
    public static Distance Compare((int Width, int Height, int Channels, byte[] Pixels) first, (int Width, int Height, int Channels, byte[] Pixels) second, (int Width, int Height, bool[] AtEdge) edges)
    {
        if ((first.Width, first.Height) != (second.Width, second.Height) || (first.Width, first.Height) != (edges.Width, edges.Height))
        {
            throw new ArgumentException($"The images are {first.Width} x {first.Height} and {second.Width} x {second.Height} pixels and the edge mask {edges.Width} x {edges.Height}: a comparison needs one size.");
        }

        var (width, height) = (first.Width, first.Height);
        var largest = 0;
        var squares = 0.0;
        var (over32, over64, over128) = (0, 0, 0);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixelLargest = 0;
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = Math.Abs(Sample(first, x, y, channel) - Sample(second, x, y, channel));
                    squares += difference * difference;
                    pixelLargest = Math.Max(pixelLargest, difference);
                }

                over32 += pixelLargest > 32 ? 1 : 0;
                over64 += pixelLargest > 64 ? 1 : 0;
                over128 += pixelLargest > 128 ? 1 : 0;
                if (!edges.AtEdge[(y * edges.Width) + x])
                {
                    largest = Math.Max(largest, pixelLargest);
                }
            }
        }

        return new Distance(largest, squares / (3.0 * width * height), over32, over64, over128);
    }

    private static byte Sample((int Width, int Height, int Channels, byte[] Pixels) image, int x, int y, int channel) =>
        image.Pixels[(((y * image.Width) + x) * image.Channels) + (image.Channels < 3 ? 0 : channel)];
}
