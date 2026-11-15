using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// Writes a page's display list as a PDF content stream. This is the ONE place where page coordinates (origin
/// top-left, y down) become PDF user space (origin bottom-left, y up, ISO 32000-1 §8.3.2.3): y_pdf = page height − y.
/// </summary>
internal static class PageContentStream
{
    /// <summary>Builds the content stream of one page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="resources">The used fonts and images, to find each one's resource name.</param>
    /// <returns>The stream data, or an error for a number that is not finite.</returns>
    public static Result<ImmutableArray<byte>, Error> Build(Page page, ResourceNames resources) =>
        ContentStream.Serialize(Operations(page, resources));

    private static IEnumerable<ContentOperation> Operations(Page page, ResourceNames resources)
    {
        foreach (var item in page.Items)
        {
            var operations = item switch
            {
                FillRectangle fill => Rectangle(fill, page.Size.Height),
                GlyphRun run => Text(run, resources.Fonts[run.Font].ResourceName, page.Size.Height),
                DrawImage image => Image(image, resources.Images[image.Image].ResourceName, page.Size.Height),

                // Structure markers produce no drawing. Tagged PDF (marked content and a structure tree,
                // §14.6–§14.8) is built from them in the conformance stage.
                StructureStart or StructureEnd => [],
                _ => throw new InvalidOperationException($"The PDF back-end cannot draw a {item.GetType().Name} yet."),
            };

            foreach (var operation in operations)
            {
                yield return operation;
            }
        }
    }

    /// <summary>
    /// A filled rectangle: set the colour, add the rectangle (whose y is its BOTTOM edge in PDF space) and fill
    /// it, inside q/Q so the colour does not leak into later items (§8.4.2).
    /// </summary>
    private static ImmutableArray<ContentOperation> Rectangle(FillRectangle fill, double pageHeight) =>
    [
        ContentOperators.SaveState(),
        FillColor(fill.Color),
        ContentOperators.Rectangle(fill.Area.Origin.X, pageHeight - fill.Area.Origin.Y - fill.Area.Size.Height, fill.Area.Size.Width, fill.Area.Size.Height),
        ContentOperators.Fill(),
        ContentOperators.RestoreState(),
    ];

    /// <summary>
    /// An image: inside q/Q, map the unit square of image space onto the image's rectangle with <c>cm</c> (§8.9.4:
    /// "an image is mapped to … the unit square"), whose y is its BOTTOM edge in PDF space, then paint it with
    /// <c>Do</c> (§8.8).
    /// </summary>
    private static ImmutableArray<ContentOperation> Image(DrawImage image, PdfName resource, double pageHeight) =>
    [
        ContentOperators.SaveState(),
        ContentOperators.ConcatMatrix(image.Area.Size.Width, 0, 0, image.Area.Size.Height, image.Area.Origin.X, pageHeight - image.Area.Origin.Y - image.Area.Size.Height),
        ContentOperators.PaintXObject(resource),
        ContentOperators.RestoreState(),
    ];

    /// <summary>
    /// A glyph run: one text object (§9.4) that selects the font and size and sets the colour; then, for each
    /// stretch of glyphs at the same height (almost always the whole run; a raised or lowered mark makes its own
    /// stretch), it moves the text origin to the first glyph of the stretch and shows the glyphs. TJ can move the
    /// pen only horizontally (§9.4.3), hence one positioning per height.
    /// </summary>
    private static ImmutableArray<ContentOperation> Text(GlyphRun run, PdfName fontResource, double pageHeight) =>
    [
        ContentOperators.SaveState(),
        ContentOperators.BeginText(),
        ContentOperators.SetFont(fontResource, run.FontSize),
        FillColor(run.Color),
        .. SameHeightStretches(run.Glyphs).SelectMany(stretch => new[]
        {
            ContentOperators.MoveTextTo(run.Origin.X + stretch[0].X, pageHeight - (run.Origin.Y + stretch[0].Y)),
            ContentOperators.ShowGlyphsWithAdjustments(GlyphArray(run, stretch)),
        }),
        ContentOperators.EndText(),
        ContentOperators.RestoreState(),
    ];

    /// <summary>The glyphs split into consecutive stretches with the same <see cref="PositionedGlyph.Y"/>.</summary>
    private static IEnumerable<ImmutableArray<PositionedGlyph>> SameHeightStretches(ValueList<PositionedGlyph> glyphs)
    {
        var stretch = ImmutableArray.CreateBuilder<PositionedGlyph>();
        foreach (var glyph in glyphs)
        {
            if (stretch.Count > 0 && stretch[0].Y != glyph.Y)
            {
                yield return stretch.ToImmutable();
                stretch.Clear();
            }

            stretch.Add(glyph);
        }

        if (stretch.Count > 0)
        {
            yield return stretch.ToImmutable();
        }
    }

    /// <summary>
    /// The TJ array (§9.4.3) of one stretch, whose first glyph is at the text origin. After each glyph a viewer
    /// advances the pen by the glyph's /W width. Where the page model puts a glyph somewhere else (kerning,
    /// placement, justification), a number is inserted that moves the pen:
    /// by the formula of §9.4.4, a number n moves it by −n / 1000 × font size. Numbers smaller than 0.001 (a
    /// millionth of the font size) are rounding noise from summing widths, and are left out. Glyph codes are
    /// two bytes, big-endian: the glyph id under Identity-H.
    /// </summary>
    private static ImmutableArray<PdfObject> GlyphArray(GlyphRun run, ImmutableArray<PositionedGlyph> stretch)
    {
        var items = ImmutableArray.CreateBuilder<PdfObject>();
        var codes = new List<byte>();
        var pen = stretch[0].X;
        foreach (var glyph in stretch)
        {
            var adjustment = Math.Round((pen - glyph.X) * 1000 / run.FontSize, 3);
            if (adjustment != 0)
            {
                if (codes.Count > 0)
                {
                    items.Add(PdfString.FromBytes(codes));
                    codes.Clear();
                }

                items.Add(PdfNumber.From(adjustment));
                pen -= adjustment / 1000 * run.FontSize;
            }

            codes.Add((byte)(glyph.Glyph.Value >> 8));
            codes.Add((byte)(glyph.Glyph.Value & 0xFF));
            pen += FontEmbedding.GlyphSpace(run.Font, run.Font.AdvanceWidth(glyph.Glyph)) / 1000 * run.FontSize;
        }

        if (codes.Count > 0)
        {
            items.Add(PdfString.FromBytes(codes));
        }

        return items.ToImmutable();
    }

    private static ContentOperation FillColor(Color color) =>
        ContentOperators.SetFillRgb(color.Red / 255.0, color.Green / 255.0, color.Blue / 255.0);
}
