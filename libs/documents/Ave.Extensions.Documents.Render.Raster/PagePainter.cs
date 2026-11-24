using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace Ave.Extensions.Documents.Render.Raster;

/// <summary>
/// Walks a page's display list in painting order and draws each item on a canvas. The one place where page coordinates
/// (points, origin top-left, y down) become pixels: multiply by <c>scale</c> (= resolution / 72).
/// </summary>
/// <remarks>
/// <para><b>Clipping.</b> Every item is drawn through the walk's current clip box: the whole canvas, intersected with the
/// rectangle of every clip open around the item (<see cref="ClipStart"/> … <see cref="ClipEnd"/>, as PDF's
/// <c>q x y w h re W n … Q</c>). At a clip's start the current box is pushed and intersected with the clip's rectangle in
/// pixels (<see cref="ClipBox"/>, <see cref="PixelBox.Intersect"/>); at its end it is popped. The clips nest, as the
/// page-model rules guarantee (<see cref="PageModelRules"/>). The box is always an axis-parallel rectangle in pixels: a
/// clip of the page model is a rectangle with sides along the page's axes, the map from points to pixels is a scale
/// only (no rotation or shear), and the intersection of axis-parallel rectangles is one. The draw calls clip the
/// geometry to the box before they compute coverage (<see cref="Canvas.Fill"/>, <see cref="Canvas.DrawImage"/>), so a
/// pixel a clip edge crosses shows the exact area of the shape inside the clip. A clip of no area, or one beside the page,
/// lets nothing through.</para>
/// <para><b>Glyphs.</b> Each glyph is its outline (<see cref="TrueTypeFont.Shape"/>), in design units with y up, mapped to
/// the page by font size / units per em with y flipped, placed at its exact position (run origin plus the glyph's offset),
/// and filled with the non-zero rule (TrueType's). One fill per glyph, as PDF viewers draw them: where two glyphs overlap,
/// the second is painted over the first.</para>
/// <para><b>Filled paths.</b> Each contour is a closed subpath of the same lines and cubic curves, mapped to pixels by the
/// same scale, and filled with the item's rule (non-zero or even-odd) through the same exact coverage as a rectangle.</para>
/// <para><b>Images.</b> The image's pixels (decoded once per render call, however often the image is drawn: JPEG by the
/// image layer's decoder) are mapped onto the item's rectangle, row 0 at the top, as PDF maps an image's unit square
/// (ISO 32000-1 §8.9.4), and drawn with the canvas's exact box filter.</para>
/// </remarks>
internal static class PagePainter
{
    /// <summary>Draws the page's items; the first glyph or image that cannot be decoded ends the walk with its error.</summary>
    public static Result<Unit, Error> Paint(Page page, Canvas canvas, double scale, Images images)
    {
        var clip = canvas.Bounds;
        var outer = new Stack<PixelBox>();
        foreach (var item in page.Items)
        {
            switch (item)
            {
                case ClipStart start:
                    outer.Push(clip);
                    clip = clip.Intersect(ClipBox(start.Area, scale));
                    continue;
                case ClipEnd:
                    clip = outer.Pop();
                    continue;
            }

            var drawn = item switch
            {
                FillRectangle fill => Rectangle(canvas, fill, scale, clip),
                FillPath path => FilledPath(canvas, path, scale, clip),
                GlyphRun run => Glyphs(canvas, run, scale, clip),
                DrawImage image => Image(canvas, image, scale, clip, images),
                // Markers, and a link's area, draw nothing: a page as pixels has no structure and nothing to click.
                StructureStart or StructureEnd or ArtifactStart or ArtifactEnd or LinkArea or Anchor => Result<Unit, Error>.Success(Unit.Value),
                _ => throw new InvalidOperationException($"The raster back-end cannot draw a {item.GetType().Name} yet."),
            };

            if (drawn.IsFailure)
            {
                return drawn;
            }
        }

        return Result<Unit, Error>.Success(Unit.Value);
    }

    /// <summary>The decoded pixels of the images one render call draws, by image id: each distinct image is decoded and
    /// converted once, however often it is drawn.</summary>
    internal sealed class Images
    {
        private readonly Dictionary<ImageId, RgbaImage> _decoded = [];

        public Result<RgbaImage, Error> Of(ImageData image)
        {
            if (_decoded.TryGetValue(image.Id, out var known))
            {
                return Result<RgbaImage, Error>.Success(known);
            }

            var decoded = ImageDecoder.DecodeSamples(image).OnSuccessMap(Rgba);
            if (decoded.IsSuccess)
            {
                _decoded[image.Id] = decoded.Value;
            }

            return decoded;
        }
    }

    /// <summary>A rectangle of the page in pixels.</summary>
    internal static PixelBox ToPixels(Rect area, double scale) =>
        new(area.Origin.X * scale, area.Origin.Y * scale, (area.Origin.X + area.Size.Width) * scale, (area.Origin.Y + area.Size.Height) * scale);

    /// <summary>A clip's rectangle in pixels, by the same map as every item (<see cref="ToPixels"/>), with its edges in
    /// order: a negative width or height spans the same rectangle from the other corner, as PDF's <c>re</c> reads it.</summary>
    private static PixelBox ClipBox(Rect area, double scale)
    {
        var box = ToPixels(area, scale);
        return new PixelBox(double.Min(box.Left, box.Right), double.Min(box.Top, box.Bottom), double.Max(box.Left, box.Right), double.Max(box.Top, box.Bottom));
    }

    private static Result<Unit, Error> Rectangle(Canvas canvas, FillRectangle fill, double scale, PixelBox clip)
    {
        var area = fill.Area;
        return canvas.Fill(Path.Rectangle(area.Origin.X, area.Origin.Y, area.Size.Width, area.Size.Height), Affine.Scale(scale, scale), FillRule.NonZero, fill.Color, clip);
    }

    /// <summary>A filled path: its contours as closed subpaths of lines and cubic curves, in page coordinates scaled to
    /// pixels, filled by the item's rule (the same two rules as PDF's <c>f</c> and <c>f*</c>, ISO 32000-1 §8.5.3.3) with
    /// the canvas's exact coverage. The canvas charges the fill's flattened edges against its work limits
    /// (<see cref="Canvas.MaxFillWork"/>) and refuses a fill beyond them.</summary>
    private static Result<Unit, Error> FilledPath(Canvas canvas, FillPath fill, double scale, PixelBox clip)
    {
        var builder = new PathBuilder();
        foreach (var contour in fill.Contours)
        {
            var subpath = builder.MoveTo(contour.Start.X, contour.Start.Y);
            foreach (var segment in contour.Segments)
            {
                if (segment is PathCubic cubic)
                {
                    subpath.CubicTo(cubic.Control1.X, cubic.Control1.Y, cubic.Control2.X, cubic.Control2.Y, cubic.End.X, cubic.End.Y);
                }
                else
                {
                    subpath.LineTo(segment.End.X, segment.End.Y);
                }
            }

            subpath.Close();
        }

        var rule = fill.Rule == PathFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;
        return canvas.Fill(builder.ToPath(), Affine.Scale(scale, scale), rule, fill.Color, clip);
    }

    private static Result<Unit, Error> Glyphs(Canvas canvas, GlyphRun run, double scale, PixelBox clip)
    {
        var size = run.FontSize / run.Font.Metrics.UnitsPerEm * scale;
        foreach (var glyph in run.Glyphs)
        {
            var shape = run.Font.Shape(glyph.Glyph);
            if (shape.IsFailure)
            {
                return Result<Unit, Error>.Failure(shape.Error);
            }

            if (shape.Value.Contours.IsEmpty)
            {
                continue;
            }

            // Design units (y up) → pixels (y down): x' = size·x + e, y' = −size·y + f, with (e, f) the glyph's origin.
            var placement = new Affine(size, 0, 0, -size, (run.Origin.X + glyph.X) * scale, (run.Origin.Y + glyph.Y) * scale);
            var filled = canvas.Fill(Outline(shape.Value), placement, FillRule.NonZero, run.Color, clip);
            if (filled.IsFailure)
            {
                return filled;
            }
        }

        return Result<Unit, Error>.Success(Unit.Value);
    }

    /// <summary>A glyph's contours as a path: each contour a closed subpath of lines and quadratic curves.</summary>
    private static Path Outline(GlyphShape shape)
    {
        var builder = new PathBuilder();
        foreach (var contour in shape.Contours)
        {
            var segments = contour.Segments();
            var subpath = builder.MoveTo(segments.Start.X, segments.Start.Y);
            foreach (var segment in segments.Segments)
            {
                if (segment.Control is { } control)
                {
                    subpath.QuadraticTo(control.X, control.Y, segment.End.X, segment.End.Y);
                }
                else
                {
                    subpath.LineTo(segment.End.X, segment.End.Y);
                }
            }

            subpath.Close();
        }

        return builder.ToPath();
    }

    private static Result<Unit, Error> Image(Canvas canvas, DrawImage image, double scale, PixelBox clip, Images images) =>
        images.Of(image.Image).OnSuccessBind(pixels =>
        {
            var area = image.Area;

            // Image space (pixel (i, j) is the unit square at (i, j), row 0 at the top) → the rectangle → pixels.
            var placement = new Affine(
                area.Size.Width / pixels.Width * scale,
                0,
                0,
                area.Size.Height / pixels.Height * scale,
                area.Origin.X * scale,
                area.Origin.Y * scale);
            // The only value the canvas can refuse here is the reach: every number is within the page model's range.
            return canvas.DrawImage(pixels, placement, clip).MapError(error =>
                new Error(RasterErrorCodes.ImageOutOfReach, $"An image on the page cannot be drawn exactly at this resolution: {error.Message}")
                    .With(ErrorMetadataKeys.Value, error.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Value, out var reach) ? reach : string.Empty));
        });

    /// <summary>Grey or RGB samples and an optional alpha channel as RGBA pixels.</summary>
    private static RgbaImage Rgba(RasterImage image)
    {
        var count = image.PixelWidth * image.PixelHeight;
        var pixels = new byte[(long)count * 4];
        var grey = image.ColorSpace == ImageColorSpace.Gray;
        var samples = image.Samples.AsSpan();
        var alpha = image.Alpha;
        for (var index = 0; index < count; index++)
        {
            var offset = index * 4;
            if (grey)
            {
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = samples[index];
            }
            else
            {
                samples.Slice(index * 3, 3).CopyTo(pixels.AsSpan(offset));
            }

            pixels[offset + 3] = alpha is { } values ? values[index] : (byte)255;
        }

        // The size matches the pixels by construction; a RasterImage has at least one pixel.
        return RgbaImage.Create(image.PixelWidth, image.PixelHeight, ImmutableArray.Create(pixels)).Value;
    }
}
