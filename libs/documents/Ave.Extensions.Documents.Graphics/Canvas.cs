using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// A drawing surface of 8-bit RGBA pixels (<see cref="RgbaImage"/>'s layout): paths are filled and images drawn onto it
/// in order, each painted over what is there with source-over compositing. Pixel (i, j) is the unit square from (i, j) to
/// (i + 1, j + 1) of device space, y down. Mutable while drawing; <see cref="ToImage"/> takes the result.
/// </summary>
/// <remarks>
/// <para><b>Anti-aliasing.</b> A pixel's coverage is the share of its square that the shape covers
/// (<see cref="AreaRasterizer"/>: exact for outlines that do not overlap themselves), rounded to 8 bits; it is the alpha
/// the colour is painted with.</para>
/// <para><b>Compositing.</b> Source over (Porter and Duff, "Compositing Digital Images", 1984), with coverage α and the
/// pixel's alpha β: alpha' = α + β(1 − α), colour' = (C·α + D·β(1 − α)) / alpha'. Computed on the encoded 8-bit sRGB
/// values in integers and rounded to the nearest. Blending in linear light would be more correct in theory; it is not
/// done on purpose, because every PDF viewer the output is compared with (PDFium, MuPDF, poppler) blends the encoded
/// values, and a page would look visibly different from them at every anti-aliased edge (thin text gets lighter).</para>
/// <para><b>Seams.</b> Two shapes painted one after the other that share an edge each get their exact coverage there, so
/// together they cover each pixel on the edge exactly once; but source over lets a little of the pixel beneath show
/// through (with coverage ½ and ½, a quarter). Every renderer that composites shape by shape does this (it is called
/// conflation); a shape that must not show the seam is one path.</para>
/// <para><b>Arguments.</b> One rule for every public member of this library: a value it cannot draw with (not finite, out
/// of range, a size that does not fit) is answered with a <see cref="Result{T, E}"/> error with a
/// <see cref="GraphicsErrorCodes"/> code, never with an exception; a null reference throws
/// <see cref="ArgumentNullException"/>, and <see cref="PathBuilder"/> called out of order throws
/// <see cref="InvalidOperationException"/>, both bugs of the caller. The ranges: every point of a path, after the
/// transform, lies within ±<see cref="MaxCoordinate"/> in both directions, so sums and squares of a few coordinates stay
/// finite in double precision; a clip box of <see cref="Coverage"/> lies within ±<see cref="MaxPixelCoordinate"/>.
/// Inside these ranges the work of a fill is bounded by the pixels it can touch, never by the size of the shape: a shape
/// is clipped to the box in floating point before any number becomes an integer.</para>
/// </remarks>
public sealed class Canvas
{
    /// <summary>The most pixels a canvas or a coverage mask may have: 2^26 (64 Mi, about 8192 × 8192), 256 MiB of
    /// pixels. An A4 page at 600 dpi has 34.8 Mi.</summary>
    public const long MaxPixels = 1L << 26;

    /// <summary>The largest distance from the origin, in each direction, of a point of a path after its transform:
    /// 10^150. A difference or a sum of two such numbers, and the square of that, are still finite doubles.</summary>
    public const double MaxCoordinate = 1e150;

    /// <summary>The largest distance from the origin, in each direction, of a clip box of <see cref="Coverage"/>, in
    /// pixels: 2^30, so every pixel index fits a 32-bit integer.</summary>
    public const double MaxPixelCoordinate = 1 << 30;

    private readonly byte[] _pixels;
    private readonly AreaRasterizer _rasterizer = new();
    private readonly AreaRasterizer _sampler = new();
    private readonly List<Vertex> _preimage = new(4);
    private readonly List<Vertex> _clipFirst = new(16);
    private readonly List<Vertex> _clipSecond = new(16);
    private readonly List<List<Vertex>> _preimagePolygons = new(1);

    private Canvas(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The whole canvas as a box.</summary>
    public PixelBox Bounds => new(0, 0, Width, Height);

    /// <summary>Creates a canvas filled with a background.</summary>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="background">The opaque background colour, or null for transparent pixels.</param>
    /// <returns>The canvas, <c>Documents.Graphics.InvalidValue</c> for a side below 1, or <c>Documents.Graphics.TooLarge</c>
    /// for more than <see cref="MaxPixels"/> pixels.</returns>
    public static Result<Canvas, Error> Create(int width, int height, Color? background)
    {
        if (width < 1 || height < 1)
        {
            return Invalid<Canvas>($"A canvas is at least 1 x 1 pixels; asked for {width} x {height}.", $"{width}x{height}", "size");
        }

        if ((long)width * height > MaxPixels)
        {
            return TooLarge<Canvas>(width, height);
        }

        var pixels = new byte[(long)width * height * 4];
        if (background is { } color)
        {
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = color.Red;
                pixels[index + 1] = color.Green;
                pixels[index + 2] = color.Blue;
                pixels[index + 3] = 255;
            }
        }

        return Result<Canvas, Error>.Success(new Canvas(width, height, pixels));
    }

    /// <summary>Fills a path with an opaque colour, anti-aliased by exact area coverage.</summary>
    /// <param name="path">The path; open subpaths are closed.</param>
    /// <param name="transform">From the path's coordinates to device pixels.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="color">The colour.</param>
    /// <param name="clip">Only what lies inside this box is painted; null for the whole canvas.</param>
    /// <returns>Unit, or <c>Documents.Graphics.InvalidValue</c> for a value out of range (see the class remarks).</returns>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    public Result<Unit, Error> Fill(Path path, Affine transform, FillRule rule, Color color, PixelBox? clip = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Box(clip).OnSuccessBind(box => CoverageRows(_rasterizer, path, transform, rule, box, (row, first, coverage) =>
        {
            var offset = (((long)row * Width) + first) * 4;
            for (var index = 0; index < coverage.Length; index++, offset += 4)
            {
                Composite(offset, color.Red, color.Green, color.Blue, ToByte(coverage[index]));
            }
        }));
    }

    /// <summary>
    /// Draws an image through an affine transform. Image space has pixel (i, j) of the image as the unit square from
    /// (i, j) to (i + 1, j + 1), row 0 at the top; <paramref name="imageToDevice"/> maps it to device pixels.
    /// </summary>
    /// <remarks>
    /// <para><b>Resampling rule: an exact box filter.</b> The image is a grid of uniformly coloured squares, as PDF draws
    /// an image that does not ask to be interpolated (ISO 32000-1 §8.9.5.3, Interpolate false; the PDF/A profiles forbid
    /// asking for interpolation, ISO 19005-3 §6.2.8): each device pixel shows the mean of the image over the part of the
    /// image its square covers. That part is the pixel's preimage, a parallelogram in image space; its exact overlap with
    /// every image pixel is the weight of that pixel (<see cref="AreaRasterizer"/> again, on the image's grid). The
    /// colours are averaged weighted by their alpha (premultiplied), so a transparent pixel adds no colour; the alpha
    /// painted is the mean alpha over the whole preimage, so a device pixel half outside the image gets half its alpha,
    /// and the image's edges are anti-aliased like a filled path. For any magnification this shows the image's pixels as
    /// sharp squares; for any reduction each image pixel counts by the area it covers.</para>
    /// </remarks>
    /// <param name="image">The image.</param>
    /// <param name="imageToDevice">From image space to device pixels.</param>
    /// <param name="clip">Only what lies inside this box is painted; null for the whole canvas.</param>
    /// <returns>Unit, or <c>Documents.Graphics.InvalidValue</c> for a value out of range (see the class remarks).</returns>
    /// <exception cref="ArgumentNullException">The image is null.</exception>
    public Result<Unit, Error> DrawImage(RgbaImage image, Affine imageToDevice, PixelBox? clip = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var box = Box(clip);
        if (box.IsFailure)
        {
            return Result<Unit, Error>.Failure(box.Error);
        }

        if (!imageToDevice.IsFinite)
        {
            return Invalid<Unit>("The image's transform is not finite.", imageToDevice.ToString(), "transform");
        }

        if (imageToDevice.Inverse() is not { } toImage || box.Value.IsEmpty)
        {
            // A transform that flattens the image to a line or a point gives it no area: nothing to paint.
            return Result<Unit, Error>.Success(Unit.Value);
        }

        var imageBox = new PixelBox(0, 0, image.Width, image.Height);
        var preimageArea = 1 / Math.Abs(imageToDevice.Determinant);
        var weights = new Weights(image);
        CoverageRow sample = weights.Add;
        var clipBox = box.Value;
        return CoverageRows(_rasterizer, Path.Rectangle(0, 0, image.Width, image.Height), imageToDevice, FillRule.NonZero, clipBox, (row, first, coverage) =>
        {
            for (var index = 0; index < coverage.Length; index++)
            {
                if (coverage[index] == 0)
                {
                    continue;
                }

                var x = first + index;
                weights.Clear();
                SamplePreimage(new PixelBox(x, row, x + 1, row + 1).Intersect(clipBox), toImage, imageBox, sample);
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
    /// <param name="clip">The box, within ±<see cref="MaxPixelCoordinate"/>; the mask covers the pixels from ⌊left⌋,
    /// ⌊top⌋ to ⌈right⌉, ⌈bottom⌉, at most <see cref="MaxPixels"/>.</param>
    /// <returns>The mask, or <c>Documents.Graphics.InvalidValue</c> / <c>TooLarge</c>.</returns>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    public static Result<CoverageMask, Error> Coverage(Path path, Affine transform, FillRule rule, PixelBox clip)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!clip.IsValid || Math.Abs(clip.Left) > MaxPixelCoordinate || Math.Abs(clip.Top) > MaxPixelCoordinate
            || Math.Abs(clip.Right) > MaxPixelCoordinate || Math.Abs(clip.Bottom) > MaxPixelCoordinate)
        {
            return Invalid<CoverageMask>($"The clip box must be a finite rectangle within ±{MaxPixelCoordinate.ToString(CultureInfo.InvariantCulture)} pixels.", clip.ToString(), "clip");
        }

        var left = (int)Math.Floor(clip.Left);
        var top = (int)Math.Floor(clip.Top);
        var width = (int)Math.Ceiling(clip.Right) - left;
        var height = (int)Math.Ceiling(clip.Bottom) - top;
        if ((long)width * height > MaxPixels)
        {
            return TooLarge<CoverageMask>(width, height);
        }

        var values = new byte[(long)width * height];
        return CoverageRows(new AreaRasterizer(), path, transform, rule, clip, (row, first, coverage) =>
        {
            for (var index = 0; index < coverage.Length; index++)
            {
                values[((long)(row - top) * width) + first + index - left] = ToByte(coverage[index]);
            }
        }).OnSuccessMap(_ => new CoverageMask(left, top, width, height, ImmutableArray.Create(values)));
    }

    /// <summary>Checks the values, then hands each row of the coverage of a path inside a box to <paramref name="row"/>.</summary>
    private static Result<Unit, Error> CoverageRows(AreaRasterizer rasterizer, Path path, Affine transform, FillRule rule, PixelBox box, CoverageRow row)
    {
        if (!Enum.IsDefined(rule))
        {
            return Invalid<Unit>($"{(int)rule} is not a fill rule.", ((int)rule).ToString(CultureInfo.InvariantCulture), "rule");
        }

        if (!transform.IsFinite)
        {
            return Invalid<Unit>("The transform is not finite.", transform.ToString(), "transform");
        }

        if (OutOfRange(path, transform) is { } point)
        {
            return Invalid<Unit>(
                $"A point of the path lies at ({point.X.ToString(CultureInfo.InvariantCulture)}, {point.Y.ToString(CultureInfo.InvariantCulture)}) after the transform; points must be finite and within ±{MaxCoordinate.ToString(CultureInfo.InvariantCulture)}.",
                $"{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)}",
                "path");
        }

        if (!box.IsEmpty)
        {
            rasterizer.Rasterize(Clipping.ToBox(Flattening.Polygons(path, transform, box), box), rule, row);
        }

        return Result<Unit, Error>.Success(Unit.Value);
    }

    /// <summary>The first point of the path (end or control point) that is not finite or out of range after the
    /// transform, or null.</summary>
    private static Vertex? OutOfRange(Path path, Affine transform)
    {
        foreach (var command in path.Commands)
        {
            var point = command switch
            {
                MoveTo move => Check(transform, move.X, move.Y),
                LineTo line => Check(transform, line.X, line.Y),
                QuadraticTo quadratic => Check(transform, quadratic.ControlX, quadratic.ControlY) ?? Check(transform, quadratic.X, quadratic.Y),
                CubicTo cubic => Check(transform, cubic.Control1X, cubic.Control1Y) ?? Check(transform, cubic.Control2X, cubic.Control2Y) ?? Check(transform, cubic.X, cubic.Y),
                _ => null,
            };
            if (point is not null)
            {
                return point;
            }
        }

        return null;
    }

    private static Vertex? Check(Affine transform, double x, double y)
    {
        var (mappedX, mappedY) = (transform.MapX(x, y), transform.MapY(x, y));
        return Math.Abs(mappedX) <= MaxCoordinate && Math.Abs(mappedY) <= MaxCoordinate ? null : new Vertex(mappedX, mappedY);
    }

    /// <summary>
    /// The image samples under one device box: its four corners mapped back to image space (a parallelogram), clipped to
    /// the image, and rasterised on the image's grid into <paramref name="sample"/>. Uses the canvas's buffers, so drawing
    /// an image allocates nothing per pixel.
    /// </summary>
    private void SamplePreimage(PixelBox cell, Affine toImage, PixelBox imageBox, CoverageRow sample)
    {
        _preimage.Clear();
        _preimage.Add(new Vertex(toImage.MapX(cell.Left, cell.Top), toImage.MapY(cell.Left, cell.Top)));
        _preimage.Add(new Vertex(toImage.MapX(cell.Right, cell.Top), toImage.MapY(cell.Right, cell.Top)));
        _preimage.Add(new Vertex(toImage.MapX(cell.Right, cell.Bottom), toImage.MapY(cell.Right, cell.Bottom)));
        _preimage.Add(new Vertex(toImage.MapX(cell.Left, cell.Bottom), toImage.MapY(cell.Left, cell.Bottom)));
        var clipped = Clipping.ToBox(_preimage, imageBox, _clipFirst, _clipSecond);
        if (clipped.Count < 3)
        {
            return;
        }

        _preimagePolygons.Clear();
        _preimagePolygons.Add(clipped);
        _sampler.Rasterize(_preimagePolygons, FillRule.NonZero, sample);
    }

    private Result<PixelBox, Error> Box(PixelBox? clip) =>
        clip is { IsValid: false } invalid
            ? Invalid<PixelBox>("The clip box is not a finite rectangle with its edges in order.", invalid.ToString(), "clip")
            : Result<PixelBox, Error>.Success(clip is { } box ? box.Intersect(Bounds) : Bounds);

    private static Result<T, Error> Invalid<T>(string message, string value, string property) =>
        Result<T, Error>.Failure(new Error(GraphicsErrorCodes.InvalidValue, message)
            .With(ErrorMetadataKeys.Value, value)
            .With(ErrorMetadataKeys.Property, property));

    private static Result<T, Error> TooLarge<T>(long width, long height) =>
        Result<T, Error>.Failure(new Error(GraphicsErrorCodes.TooLarge, $"{width} x {height} pixels is more than the {MaxPixels} pixels a canvas or a mask may have.")
            .With(ErrorMetadataKeys.Value, $"{width}x{height}"));

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
/// covered. Made only by <see cref="Canvas.Coverage"/>, so its values always match its size.
/// </summary>
public sealed class CoverageMask
{
    internal CoverageMask(int left, int top, int width, int height, ImmutableArray<byte> values)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Values = values;
    }

    /// <summary>The column of the first value.</summary>
    public int Left { get; }

    /// <summary>The row of the first value.</summary>
    public int Top { get; }

    /// <summary>Values per row.</summary>
    public int Width { get; }

    /// <summary>Rows.</summary>
    public int Height { get; }

    /// <summary>Width × height values, row by row.</summary>
    public ImmutableArray<byte> Values { get; }

    /// <summary>The coverage of pixel (x, y), 0 outside the mask.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The coverage.</returns>
    public byte At(int x, int y) =>
        (long)x >= Left && (long)x < (long)Left + Width && (long)y >= Top && (long)y < (long)Top + Height
            ? Values[(int)(((long)(y - Top) * Width) + x - Left)]
            : (byte)0;
}
