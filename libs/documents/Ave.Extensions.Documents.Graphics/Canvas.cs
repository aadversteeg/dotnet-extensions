using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// A drawing surface of 8-bit RGBA pixels (<see cref="RgbaImage"/>'s layout): paths are filled and images drawn onto it
/// in order, each painted over what is there with source-over compositing. Pixel (i, j) is the unit square from (i, j) to
/// (i + 1, j + 1) of device space, y down. Mutable while drawing; <see cref="ToImage"/> takes the result.
/// </summary>
/// <remarks>
/// <para><b>Anti-aliasing.</b> A pixel's coverage is the exact share of its square that the shape covers
/// (<see cref="AreaRasterizer"/>), rounded to 8 bits; it is the alpha the colour is painted with.</para>
/// <para><b>Compositing.</b> Source over (Porter and Duff, "Compositing Digital Images", 1984), with coverage α and the
/// pixel's alpha β: alpha' = α + β(1 − α), colour' = (C·α + D·β(1 − α)) / alpha'. Computed on the encoded 8-bit sRGB
/// values in integers and rounded to the nearest. Blending in linear light would be more correct in theory; it is not
/// done on purpose, because every PDF viewer the output is compared with (PDFium, MuPDF, poppler) blends the encoded
/// values, and a page would look visibly different from them at every anti-aliased edge (thin text gets lighter).</para>
/// <para><b>Seams.</b> Two shapes painted one after the other that share an edge each get their exact coverage there, so
/// together they cover each pixel on the edge exactly once; but source over lets a little of the pixel beneath show
/// through (with coverage ½ and ½, a quarter). Every renderer that composites shape by shape does this (it is called
/// conflation); a shape that must not show the seam is one path.</para>
/// </remarks>
public sealed class Canvas
{
    /// <summary>The most pixels a canvas may have: 2^26 (64 Mi, about 8192 × 8192), 256 MiB of pixels. An A4 page at 600
    /// dpi has 34.8 Mi.</summary>
    public const long MaxPixels = 1L << 26;

    private readonly byte[] _pixels;
    private readonly AreaRasterizer _rasterizer = new();
    private readonly AreaRasterizer _sampler = new();

    /// <summary>Creates a canvas filled with a background.</summary>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="background">The opaque background colour, or null for transparent pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A side is below 1, or the canvas has more than
    /// <see cref="MaxPixels"/> pixels.</exception>
    public Canvas(int width, int height, Color? background)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)width * height, MaxPixels, "width × height");
        Width = width;
        Height = height;
        _pixels = new byte[(long)width * height * 4];
        if (background is { } color)
        {
            for (var index = 0; index < _pixels.Length; index += 4)
            {
                _pixels[index] = color.Red;
                _pixels[index + 1] = color.Green;
                _pixels[index + 2] = color.Blue;
                _pixels[index + 3] = 255;
            }
        }
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The whole canvas as a box.</summary>
    public PixelBox Bounds => new(0, 0, Width, Height);

    /// <summary>Fills a path with an opaque colour, anti-aliased by exact area coverage.</summary>
    /// <param name="path">The path; open subpaths are closed.</param>
    /// <param name="transform">From the path's coordinates to device pixels.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="color">The colour.</param>
    /// <param name="clip">Only what lies inside this box is painted; null for the whole canvas.</param>
    /// <exception cref="ArgumentException">The transform, a coordinate of the path or the clip box is not finite.</exception>
    public void Fill(Path path, Affine transform, FillRule rule, Color color, PixelBox? clip = null)
    {
        CoverageRows(_rasterizer, path, transform, rule, Box(clip), (row, first, coverage) =>
        {
            var offset = (((long)row * Width) + first) * 4;
            for (var index = 0; index < coverage.Length; index++, offset += 4)
            {
                Composite(offset, color.Red, color.Green, color.Blue, ToByte(coverage[index]));
            }
        });
    }

    /// <summary>
    /// Draws an image through an affine transform. Image space has pixel (i, j) of the image as the unit square from
    /// (i, j) to (i + 1, j + 1), row 0 at the top; <paramref name="imageToDevice"/> maps it to device pixels.
    /// </summary>
    /// <remarks>
    /// <para><b>Resampling rule: an exact box filter.</b> The image is a grid of uniformly coloured squares, as PDF draws
    /// an image that does not ask to be interpolated (ISO 32000-1 §8.9.5.3, Interpolate false): each device pixel shows
    /// the mean of the image over the part of the image its square covers. That part is the pixel's preimage, a
    /// parallelogram in image space; its exact overlap with every image pixel is the weight of that pixel
    /// (<see cref="AreaRasterizer"/> again, on the image's grid). The colours are averaged weighted by their alpha
    /// (premultiplied), so a transparent pixel adds no colour; the alpha painted is the mean alpha over the whole
    /// preimage, so a device pixel half outside the image gets half its alpha, and the image's edges are anti-aliased
    /// like a filled path. For any magnification this shows the image's pixels as sharp squares; for any reduction each
    /// image pixel counts by the area it covers.</para>
    /// </remarks>
    /// <param name="image">The image.</param>
    /// <param name="imageToDevice">From image space to device pixels.</param>
    /// <param name="clip">Only what lies inside this box is painted; null for the whole canvas.</param>
    /// <exception cref="ArgumentException">The transform or the clip box is not finite.</exception>
    public void DrawImage(RgbaImage image, Affine imageToDevice, PixelBox? clip = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!imageToDevice.IsFinite)
        {
            throw new ArgumentException("The transform is not finite.", nameof(imageToDevice));
        }

        var box = Box(clip);
        if (imageToDevice.Inverse() is not { } toImage || box.IsEmpty)
        {
            // A transform that flattens the image to a line or a point gives it no area: nothing to paint.
            return;
        }

        var outline = Path.Rectangle(0, 0, image.Width, image.Height);
        var imageBox = new PixelBox(0, 0, image.Width, image.Height);
        var preimageArea = 1 / Math.Abs(imageToDevice.Determinant);
        var weights = new Weights(image);
        CoverageRow sample = weights.Add;
        CoverageRows(_rasterizer, outline, imageToDevice, FillRule.NonZero, box, (row, first, coverage) =>
        {
            for (var index = 0; index < coverage.Length; index++)
            {
                if (coverage[index] == 0)
                {
                    continue;
                }

                var x = first + index;
                var cell = new PixelBox(x, row, x + 1, row + 1).Intersect(box);
                var preimage = Flattening.Polygons(Path.Rectangle(cell.Left, cell.Top, cell.Right - cell.Left, cell.Bottom - cell.Top), toImage, imageBox);
                weights.Clear();
                _sampler.Rasterize(Clipping.ToBox(preimage, imageBox), FillRule.NonZero, sample);
                if (weights.Alpha > 0)
                {
                    Composite(
                        (((long)row * Width) + x) * 4,
                        ToByte(weights.Red / weights.Alpha / 255),
                        ToByte(weights.Green / weights.Alpha / 255),
                        ToByte(weights.Blue / weights.Alpha / 255),
                        ToByte(weights.Alpha / 255 / preimageArea));
                }
            }
        });
    }

    /// <summary>The pixels drawn so far.</summary>
    /// <returns>An image with a copy of the pixels.</returns>
    public RgbaImage ToImage() => new(Width, Height, ImmutableArray.Create(_pixels));

    /// <summary>
    /// The 8-bit coverage of a path, without painting it: for each pixel of the box's pixel range, the share of the pixel
    /// a fill with this path, transform, rule and clip would cover. The same computation <see cref="Fill"/> paints with.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="transform">From the path's coordinates to device pixels.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="clip">The box; the mask covers the pixels from ⌊left⌋, ⌊top⌋ to ⌈right⌉, ⌈bottom⌉.</param>
    /// <returns>The mask.</returns>
    /// <exception cref="ArgumentException">The transform, a coordinate of the path or the clip box is not finite.</exception>
    public static CoverageMask Coverage(Path path, Affine transform, FillRule rule, PixelBox clip)
    {
        if (!clip.IsValid)
        {
            throw new ArgumentException("The clip box is not a finite rectangle.", nameof(clip));
        }

        var left = (int)Math.Floor(clip.Left);
        var top = (int)Math.Floor(clip.Top);
        var width = (int)Math.Ceiling(clip.Right) - left;
        var height = (int)Math.Ceiling(clip.Bottom) - top;
        var values = new byte[(long)width * height];
        CoverageRows(new AreaRasterizer(), path, transform, rule, clip, (row, first, coverage) =>
        {
            for (var index = 0; index < coverage.Length; index++)
            {
                values[((long)(row - top) * width) + first + index - left] = ToByte(coverage[index]);
            }
        });

        return new CoverageMask(left, top, width, height, ImmutableArray.Create(values));
    }

    /// <summary>Hands each row of the coverage of a path inside a box to <paramref name="row"/>.</summary>
    private static void CoverageRows(AreaRasterizer rasterizer, Path path, Affine transform, FillRule rule, PixelBox box, CoverageRow row)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!transform.IsFinite)
        {
            throw new ArgumentException("The transform is not finite.", nameof(transform));
        }

        var polygons = Flattening.Polygons(path, transform, box);
        foreach (var polygon in polygons)
        {
            foreach (var vertex in polygon)
            {
                if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
                {
                    throw new ArgumentException("A point of the path is not finite after the transform.", nameof(path));
                }
            }
        }

        if (!box.IsEmpty)
        {
            rasterizer.Rasterize(Clipping.ToBox(polygons, box), rule, row);
        }
    }

    private PixelBox Box(PixelBox? clip)
    {
        if (clip is { IsValid: false })
        {
            throw new ArgumentException("The clip box is not a finite rectangle.", nameof(clip));
        }

        return clip is { } box ? box.Intersect(Bounds) : Bounds;
    }

    /// <summary>A share 0 to 1 as 8 bits, rounded to the nearest (halves up).</summary>
    private static byte ToByte(double share) => (byte)(int)((double.Clamp(share, 0, 1) * 255) + 0.5);

    /// <summary>Source over, in integers (see the class remarks), of an opaque colour with alpha α onto one pixel.</summary>
    private void Composite(long offset, byte red, byte green, byte blue, byte alpha)
    {
        if (alpha == 0)
        {
            return;
        }

        int beta = _pixels[offset + 3];

        // Both alphas scaled to 0..255; alpha' and the colour weights are kept as multiples of 1/(255·255).
        var below = beta * (255 - alpha);
        var total = (alpha * 255) + below;
        _pixels[offset] = Mix(red, _pixels[offset], alpha, below, total);
        _pixels[offset + 1] = Mix(green, _pixels[offset + 1], alpha, below, total);
        _pixels[offset + 2] = Mix(blue, _pixels[offset + 2], alpha, below, total);
        _pixels[offset + 3] = (byte)((total + 127) / 255);
    }

    private static byte Mix(byte source, byte destination, int alpha, int below, int total) =>
        (byte)(((source * alpha * 255) + (destination * below) + (total / 2)) / total);

    /// <summary>The weighted sums of an image's samples under a preimage: Σ w·a and Σ w·a·c per colour.</summary>
    private sealed class Weights(RgbaImage image)
    {
        public double Alpha { get; private set; }

        public double Red { get; private set; }

        public double Green { get; private set; }

        public double Blue { get; private set; }

        public void Clear() => (Alpha, Red, Green, Blue) = (0, 0, 0, 0);

        public void Add(int row, int first, ReadOnlySpan<double> share)
        {
            var pixels = image.Pixels.AsSpan();
            for (var index = 0; index < share.Length; index++)
            {
                var column = first + index;
                if (share[index] == 0 || column >= image.Width || row >= image.Height)
                {
                    continue;
                }

                var offset = ((row * image.Width) + column) * 4;
                var weight = share[index] * pixels[offset + 3];
                Alpha += weight;
                Red += weight * pixels[offset];
                Green += weight * pixels[offset + 1];
                Blue += weight * pixels[offset + 2];
            }
        }
    }
}

/// <summary>
/// The 8-bit coverage of a shape over a block of pixels (<see cref="Canvas.Coverage"/>): 0 not covered, 255 fully
/// covered.
/// </summary>
/// <param name="Left">The column of the first value.</param>
/// <param name="Top">The row of the first value.</param>
/// <param name="Width">Values per row.</param>
/// <param name="Height">Rows.</param>
/// <param name="Values">Width × height values, row by row.</param>
public sealed record CoverageMask(int Left, int Top, int Width, int Height, ImmutableArray<byte> Values)
{
    /// <summary>The coverage of pixel (x, y), 0 outside the mask.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The coverage.</returns>
    public byte At(int x, int y) =>
        x >= Left && x < Left + Width && y >= Top && y < Top + Height ? Values[((y - Top) * Width) + x - Left] : (byte)0;
}
