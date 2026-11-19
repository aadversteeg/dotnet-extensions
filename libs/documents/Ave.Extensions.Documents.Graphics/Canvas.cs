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
/// (<see cref="AreaRasterizer"/>, exact also where contours overlap or cross; see <see cref="MaxFillWork"/> for the
/// work that takes), rounded to 8 bits; it is the alpha the colour is painted with.</para>
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
/// <see cref="GraphicsErrorCodes"/> code, never with an exception; only a null reference throws
/// <see cref="ArgumentNullException"/>, a bug of the caller (a path cannot be built out of order: see
/// <see cref="PathBuilder"/>). The ranges: every point of a path, after the
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

    /// <summary>
    /// The farthest the grid of a drawn image may reach from the device origin along x or along y, in pixels: 2^40
    /// (about 1.1 × 10^12). Measured as |e| + |a|·width + |c|·height (and |f| + |b|·width + |d|·height), the largest sum
    /// of the terms from which a position of the image's grid is computed.
    /// </summary>
    /// <remarks>
    /// Where the edges of the image's pixels fall is computed in double precision from those terms, and each product and
    /// sum rounds by at most 2^-53 of its size; with the rounding of a caller who built the transform from a rectangle
    /// (a scale and an offset, two roundings more), an edge lies at most 6 · 2^-53 · reach from its exact place, which
    /// within this limit is 2^-10.4 pixel: for one edge, under a fifth of an 8-bit step where the colours on either side
    /// differ the most. A pixel that several edges cross takes that from each: the worst measured is an image squeezed
    /// nearly flat, thousands of its pixels inside one device pixel, at 0.822 of a step from the exact value (0.32 more
    /// than the rounding to 8 bits alone); every other kind of transform measured at most 0.53. The same limit keeps the
    /// preimage of a device pixel at least 2^-40 image pixels wide, where the rounding of its corners (2^-52 of the image
    /// coordinates) is at most 2^-12 of its size. Beyond it the error grows with the reach (measured: a pixel edge 0.08
    /// pixel off at a reach of 4.9 × 10^15, 5.3 pixels at 4.9 × 10^17), so an image reaching farther is refused, not
    /// drawn wrong.
    /// </remarks>
    public const double MaxImageReach = 1L << 40;

    /// <summary>
    /// The most work the exact coverage of one fill may take: 2^27 units. A pixel where the winding number may take more
    /// than two values (contours that overlap or cross in it, see <see cref="AreaRasterizer"/>) with m pieces of edges in
    /// it costs m·(2m + m(m − 1)/2) + m² units, unless its pieces are two sides apart (the sides of a thin stroke, which
    /// cost nothing); a shape whose pixels together cost more is refused with <c>Documents.Graphics.TooMuchWork</c>,
    /// before any of it is painted.
    /// </summary>
    /// <remarks>
    /// The exact area of such a pixel takes its pieces' crossings, so the work grows with the cube of the pieces in it, and
    /// a few kilobytes of path can put thousands of edges through one pixel. Text and drawings use far less. Measured on
    /// the development host (2026-11-19): of eight fonts at 12 pixels per em, every glyph filled on its own, the largest
    /// glyph takes 248,985 units (DejaVu Sans 2.37), 539 times under this limit; of the engine's samples the largest page
    /// takes 6.83 × 10^5 units at 96 dpi (<see cref="MaxCanvasWork"/> is 393 times that). About 11 ns a unit there: a fill
    /// at this limit takes about 1.7 s, a canvas at its limit about 3.6 s. Refused, for example: a thousand thin bars
    /// through one pixel (7.4 × 10^9 units), one contour crossing itself in one pixel with 1,001 edges (5.0 × 10^8); a
    /// refusal comes before the work, in milliseconds. The PDF back-end writes such a page; a viewer draws it with its
    /// own rasteriser.
    /// </remarks>
    public const long MaxFillWork = 1L << 27;

    /// <summary>The most work the exact coverage of all fills on one canvas may take together: 2^28 units (see
    /// <see cref="MaxFillWork"/>); the fill that would go beyond it is refused with <c>Documents.Graphics.TooMuchWork</c>
    /// and not painted, the fills before it stay.</summary>
    public const long MaxCanvasWork = 1L << 28;

    /// <summary>The most pieces of edges the pixels of one row where contours may overlap may hold in one fill: 2^18
    /// (the pieces of a row are kept at once). A shape with more is refused with <c>Documents.Graphics.TooMuchWork</c>,
    /// before any of it is painted.</summary>
    public const int MaxRowPieces = AreaRasterizer.MaxRowPieces;

    private readonly byte[] _pixels;
    private readonly AreaRasterizer _rasterizer = new();
    private readonly AreaRasterizer _sampler = new();
    private readonly List<Vertex> _preimage = new(4);
    private readonly List<Vertex> _clipFirst = new(16);
    private readonly List<Vertex> _clipSecond = new(16);
    private readonly List<List<Vertex>> _preimagePolygons = new(1);
    private long _work;
    private double[] _columnShares = new double[4];
    private double[] _rowShares = new double[4];

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
    /// <returns>Unit, or <c>Documents.Graphics.InvalidValue</c> for a value out of range (see the class remarks), or
    /// <c>Documents.Graphics.TooMuchWork</c> for a shape whose exact coverage takes more work than
    /// <see cref="MaxFillWork"/>, <see cref="MaxCanvasWork"/> or <see cref="MaxRowPieces"/> allow (nothing painted), or
    /// <c>Documents.Graphics.InexactCoverage</c>, a guard no shape is known to reach: the fill stopped part way, and after
    /// it the canvas's content is not defined and the canvas is not to be used.</returns>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    public Result<Unit, Error> Fill(Path path, Affine transform, FillRule rule, Color color, PixelBox? clip = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var box = Box(clip);
        if (box.IsFailure)
        {
            return Result<Unit, Error>.Failure(box.Error);
        }

        var painted = CoverageRows(_rasterizer, path, transform, rule, box.Value, Admit, (row, first, coverage) =>
        {
            var offset = (((long)row * Width) + first) * 4;
            for (var index = 0; index < coverage.Length; index++, offset += 4)
            {
                Composite(offset, color.Red, color.Green, color.Blue, ToByte(coverage[index]));
            }
        });
        if (painted.IsFailure)
        {
            return Result<Unit, Error>.Failure(painted.Error);
        }

        _work += painted.Value;
        return Result<Unit, Error>.Success(Unit.Value);
    }

    /// <summary>The fill's charge against <see cref="MaxFillWork"/> and, with the fills before it, against
    /// <see cref="MaxCanvasWork"/>.</summary>
    private Error? Admit(long charge) =>
        charge > MaxFillWork
            ? AreaRasterizer.TooMuchWork($"The fill's exact coverage takes {charge} units of work; a fill may take at most {MaxFillWork} (Canvas.MaxFillWork).", charge, MaxFillWork)
            : _work + charge > MaxCanvasWork
                ? AreaRasterizer.TooMuchWork($"The fills on this canvas take {_work + charge} units of work with this one; a canvas may take at most {MaxCanvasWork} (Canvas.MaxCanvasWork).", _work + charge, MaxCanvasWork)
                : null;

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
    /// <para><b>How far.</b> An image whose grid reaches farther from the device origin than <see cref="MaxImageReach"/>
    /// is refused with <c>Documents.Graphics.InvalidValue</c>: beyond it the rounding of the positions of its pixel edges
    /// is no longer below the 8-bit rounding of the result (the limit's remarks give the arithmetic). Within it the rule
    /// above holds for any transform. An image drawn without rotation or shear is sampled per axis, relative to the
    /// image's grid line nearest to the pixel; a rotated or sheared one by mapping the pixel's corners back to the
    /// image.</para>
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

        var reachX = Math.Abs(imageToDevice.E) + (Math.Abs(imageToDevice.A) * image.Width) + (Math.Abs(imageToDevice.C) * image.Height);
        var reachY = Math.Abs(imageToDevice.F) + (Math.Abs(imageToDevice.B) * image.Width) + (Math.Abs(imageToDevice.D) * image.Height);
        if (!(reachX <= MaxImageReach && reachY <= MaxImageReach))
        {
            var reach = double.Max(reachX, reachY).ToString(CultureInfo.InvariantCulture);
            return Invalid<Unit>(
                $"The image's grid reaches {reach} pixels from the origin; an image is drawn exactly only within {MaxImageReach.ToString(CultureInfo.InvariantCulture)} pixels (Canvas.MaxImageReach).",
                reach,
                "transform");
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
        var axisAligned = imageToDevice.B == 0 && imageToDevice.C == 0;
        return CoverageRows(_rasterizer, Path.Rectangle(0, 0, image.Width, image.Height), imageToDevice, FillRule.NonZero, clipBox, admit: null, (row, first, coverage) =>
        {
            for (var index = 0; index < coverage.Length; index++)
            {
                if (coverage[index] == 0)
                {
                    continue;
                }

                var x = first + index;
                weights.Clear();
                var cell = new PixelBox(x, row, x + 1, row + 1).Intersect(clipBox);
                if (axisAligned)
                {
                    SampleAxisAligned(cell, imageToDevice, weights);
                }
                else
                {
                    SamplePreimage(cell, toImage, imageBox, sample);
                }

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
        }).OnSuccessMap(_ => Unit.Value);
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
    /// ⌊top⌋ to ⌈right⌉, ⌈bottom⌉, at most <see cref="MaxPixels"/> in all and on each side.</param>
    /// <returns>The mask, or <c>Documents.Graphics.InvalidValue</c> / <c>TooLarge</c>, or
    /// <c>Documents.Graphics.TooMuchWork</c> for a shape whose exact coverage takes more work than
    /// <see cref="MaxFillWork"/> or <see cref="MaxRowPieces"/> allow.</returns>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    public static Result<CoverageMask, Error> Coverage(Path path, Affine transform, FillRule rule, PixelBox clip)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!clip.IsValid || Math.Abs(clip.Left) > MaxPixelCoordinate || Math.Abs(clip.Top) > MaxPixelCoordinate
            || Math.Abs(clip.Right) > MaxPixelCoordinate || Math.Abs(clip.Bottom) > MaxPixelCoordinate)
        {
            return Invalid<CoverageMask>($"The clip box must be a finite rectangle within ±{MaxPixelCoordinate.ToString(CultureInfo.InvariantCulture)} pixels.", clip.ToString(), "clip");
        }

        // In 64 bits: a box as wide as the range is 2^31 pixels, one more than a 32-bit integer holds.
        var (left, top) = ((long)Math.Floor(clip.Left), (long)Math.Floor(clip.Top));
        var (width, height) = ((long)Math.Ceiling(clip.Right) - left, (long)Math.Ceiling(clip.Bottom) - top);
        if (width * height > MaxPixels || width > MaxPixels || height > MaxPixels)
        {
            return TooLarge<CoverageMask>(width, height);
        }

        // The rasteriser reports only cells of the box (AreaRasterizer.Rasterize), so every index below is in the mask.
        var values = new byte[width * height];
        return CoverageRows(new AreaRasterizer(), path, transform, rule, clip, AdmitOne, (row, first, coverage) =>
        {
            var start = ((row - top) * width) + first - left;
            for (var index = 0; index < coverage.Length; index++)
            {
                values[start + index] = ToByte(coverage[index]);
            }
        }).OnSuccessMap(_ => new CoverageMask((int)left, (int)top, (int)width, (int)height, ImmutableArray.Create(values)));
    }

    /// <summary>A charge against <see cref="MaxFillWork"/> alone (a mask has no canvas).</summary>
    private static Error? AdmitOne(long charge) =>
        charge > MaxFillWork
            ? AreaRasterizer.TooMuchWork($"The shape's exact coverage takes {charge} units of work; a fill may take at most {MaxFillWork} (Canvas.MaxFillWork).", charge, MaxFillWork)
            : null;

    /// <summary>Checks the values, then hands each row of the coverage of a path inside a box to <paramref name="row"/>;
    /// the work charged, or the rasteriser's error. A null <paramref name="admit"/> is for an image's outline, a
    /// rectangle under an affine transform: one convex polygon, which winds around every point 0 times or once (one
    /// way), so the winding number in a cell takes 0 and one other value only and the integral is its exact coverage
    /// (<see cref="AreaRasterizer.RasterizeConvex"/>): no flags, no arrangement and no charge.</summary>
    private static Result<long, Error> CoverageRows(AreaRasterizer rasterizer, Path path, Affine transform, FillRule rule, PixelBox box, Func<long, Error?>? admit, CoverageRow row)
    {
        if (!Enum.IsDefined(rule))
        {
            return Invalid<long>($"{(int)rule} is not a fill rule.", ((int)rule).ToString(CultureInfo.InvariantCulture), "rule");
        }

        if (!transform.IsFinite)
        {
            return Invalid<long>("The transform is not finite.", transform.ToString(), "transform");
        }

        if (OutOfRange(path, transform) is { } point)
        {
            return Invalid<long>(
                $"A point of the path lies at ({point.X.ToString(CultureInfo.InvariantCulture)}, {point.Y.ToString(CultureInfo.InvariantCulture)}) after the transform; points must be finite and within ±{MaxCoordinate.ToString(CultureInfo.InvariantCulture)}.",
                $"{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)}",
                "path");
        }

        if (box.IsEmpty)
        {
            return Result<long, Error>.Success(0);
        }

        var polygons = Clipping.ToBox(Flattening.Polygons(path, transform, box), box);
        if (admit is null)
        {
            rasterizer.RasterizeConvex(polygons, rule, box, row);
            return Result<long, Error>.Success(0);
        }

        return rasterizer.Rasterize(polygons, rule, box, row, admit);
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

        // The preimage is a parallelogram, clipped to the image's box still convex: winding 0 or ±1 everywhere, so the
        // integral is its exact overlap with each image pixel (AreaRasterizer.RasterizeConvex), with no flags and no charge.
        _preimagePolygons.Clear();
        _preimagePolygons.Add(clipped);
        _sampler.RasterizeConvex(_preimagePolygons, FillRule.NonZero, imageBox, sample);
    }

    /// <summary>
    /// The image samples under one device box for an image drawn without rotation or shear (b = c = 0): the preimage is a
    /// rectangle, so the share of each image pixel is the product of its overlap along x and its overlap along y, each
    /// computed on its own axis.
    /// </summary>
    /// <remarks>
    /// Exact within <see cref="MaxImageReach"/>, far from the image's origin too. Along an axis, the box's two ends are
    /// taken relative to the image's grid line nearest to the box, mapped forward to the device as the image's own edges
    /// are; a box that lies between two grid lines gets its whole length in that pixel, computed from the box's own
    /// width, not as a difference of two positions. It is a faster path for the same result: mapping the box's corners
    /// back to the image (<see cref="SamplePreimage"/>) gives the same pixels within the reach, measured to one 8-bit
    /// step at most.
    /// </remarks>
    private void SampleAxisAligned(PixelBox cell, Affine imageToDevice, Weights weights)
    {
        var (firstColumn, columns) = Overlaps(cell.Left, cell.Right, imageToDevice.A, imageToDevice.E, weights.Image.Width, ref _columnShares);
        var (firstRow, rows) = Overlaps(cell.Top, cell.Bottom, imageToDevice.D, imageToDevice.F, weights.Image.Height, ref _rowShares);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                weights.AddPixel(firstColumn + column, firstRow + row, _columnShares[column] * _rowShares[row]);
            }
        }
    }

    /// <summary>
    /// Along one axis, the overlap of the device interval [from, to] with each image pixel, the image mapped by
    /// <c>scale · u + offset</c> and <paramref name="size"/> pixels long: the first pixel and the number of shares written.
    /// </summary>
    private static (int First, int Count) Overlaps(double from, double to, double scale, double offset, int size, ref double[] shares)
    {
        // The grid line nearest to the interval, at its device position: u relative to that line is small where a pixel
        // edge can fall inside the interval, so it is exact there.
        var line = Nearest((((from + to) / 2) - offset) / scale, size);
        var position = (scale * line) + offset;
        var (low, high) = ((from - position) / scale, (to - position) / scale);
        if (scale < 0)
        {
            (low, high) = (high, low);
        }

        if (high <= -line || low >= size - line)
        {
            return (0, 0);
        }

        // Far from the image's origin both ends may round to the same number; the interval still has its length.
        var (clippedLow, clippedHigh) = (double.Max(low, -line), double.Min(high, size - line));

        var first = (int)Math.Floor(clippedLow);
        var count = int.Max(1, (int)Math.Ceiling(clippedHigh) - first);
        if (shares.Length < count)
        {
            shares = new double[int.Max(count, 2 * shares.Length)];
        }

        if (count == 1)
        {
            // Inside one pixel: the whole length, from the interval's own width when the image's edge does not cut it.
            shares[0] = clippedLow == low && clippedHigh == high ? (to - from) / Math.Abs(scale) : clippedHigh - clippedLow;
        }
        else
        {
            for (var index = 0; index < count; index++)
            {
                shares[index] = double.Min(clippedHigh, first + index + 1) - double.Max(clippedLow, first + index);
            }
        }

        return (line + first, count);
    }

    /// <summary>The grid line nearest to an image coordinate, 0 to <paramref name="size"/>.</summary>
    private static int Nearest(double value, int size) => double.IsNaN(value) ? 0 : (int)double.Clamp(Math.Floor(value + 0.5), 0, size);

    private Result<PixelBox, Error> Box(PixelBox? clip) =>
        clip is { IsValid: false } invalid
            ? Invalid<PixelBox>("The clip box is not a finite rectangle with its edges in order.", invalid.ToString(), "clip")
            : Result<PixelBox, Error>.Success(clip is { } box ? box.Intersect(Bounds) : Bounds);

    private static Result<T, Error> Invalid<T>(string message, string value, string property) =>
        Result<T, Error>.Failure(new Error(GraphicsErrorCodes.InvalidValue, message)
            .With(ErrorMetadataKeys.Value, value)
            .With(ErrorMetadataKeys.Property, property));

    private static Result<T, Error> TooLarge<T>(long width, long height) =>
        Result<T, Error>.Failure(new Error(GraphicsErrorCodes.TooLarge, $"{width} x {height} pixels is more than a canvas or a mask may have: at most {MaxPixels} pixels, and at most that many on a side.")
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

        public RgbaImage Image => image;

        public void Clear() => (Alpha, Red, Green, Blue) = (0, 0, 0, 0);

        /// <summary>Adds one image pixel with its share of the preimage.</summary>
        public void AddPixel(int column, int row, double share)
        {
            var offset = ((row * image.Width) + column) * 4;
            var weight = share * image.Pixels[offset + 3];
            Alpha += weight;
            Red += weight * image.Pixels[offset];
            Green += weight * image.Pixels[offset + 1];
            Blue += weight * image.Pixels[offset + 2];
        }

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
