using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace Ave.Extensions.Documents.Render.Raster;

/// <summary>
/// Walks a page's display list in painting order and draws each item on a canvas. The one place where page coordinates
/// (points, origin top-left, y down) become pixels: multiply by <c>scale</c> (= resolution / 72).
/// </summary>
/// <remarks>
/// <para><b>Clipping.</b> Every item is drawn through the walk's current clip box, which is the whole canvas today. A clip
/// that the page model may state later (a rectangle that brackets items, as PDF's <c>q … re W n … Q</c>) is two more cases
/// of the walk: at its start, push the current box and intersect it with the rectangle in pixels
/// (<see cref="ToPixels(Rect, double)"/>, <see cref="PixelBox.Intersect"/>); at its end, pop it. Nothing else changes: the
/// draw calls already take the box.</para>
/// <para><b>Glyphs.</b> Each glyph is its outline (<see cref="TrueTypeFont.Shape"/>), in design units with y up, mapped to
/// the page by font size / units per em with y flipped, placed at its exact position (run origin plus the glyph's offset),
/// and filled with the non-zero rule (TrueType's). One fill per glyph, as PDF viewers draw them: where two glyphs overlap,
/// the second is painted over the first.</para>
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
        foreach (var item in page.Items)
        {
            var drawn = item switch
            {
                FillRectangle fill => Rectangle(canvas, fill, scale, clip),
                GlyphRun run => Glyphs(canvas, run, scale, clip),
                DrawImage image => Image(canvas, image, scale, clip, images),
                StructureStart or StructureEnd or ArtifactStart or ArtifactEnd => Result<Unit, Error>.Success(Unit.Value),
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

    private static Result<Unit, Error> Rectangle(Canvas canvas, FillRectangle fill, double scale, PixelBox clip)
    {
        var area = fill.Area;
        return canvas.Fill(Path.Rectangle(area.Origin.X, area.Origin.Y, area.Size.Width, area.Size.Height), Affine.Scale(scale, scale), FillRule.NonZero, fill.Color, clip);
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
            builder.MoveTo(segments.Start.X, segments.Start.Y);
            foreach (var segment in segments.Segments)
            {
                if (segment.Control is { } control)
                {
                    builder.QuadraticTo(control.X, control.Y, segment.End.X, segment.End.Y);
                }
                else
                {
                    builder.LineTo(segment.End.X, segment.End.Y);
                }
            }

            builder.Close();
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
            return canvas.DrawImage(pixels, placement, clip);
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
