using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
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
    /// <param name="fonts">The used fonts, to find each glyph run's resource name.</param>
    /// <returns>The stream data, or an error for a number that is not finite.</returns>
    public static Result<ImmutableArray<byte>, Error> Build(Page page, IReadOnlyDictionary<TrueTypeFont, UsedFont> fonts) =>
        ContentStream.Serialize(Operations(page, fonts));

    private static IEnumerable<ContentOperation> Operations(Page page, IReadOnlyDictionary<TrueTypeFont, UsedFont> fonts)
    {
        foreach (var item in page.Items)
        {
            var operations = item switch
            {
                FillRectangle fill => Rectangle(fill, page.Size.Height),
                GlyphRun run => Text(run, fonts[run.Font].ResourceName, page.Size.Height),

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
    /// A glyph run: one text object (§9.4) that selects the font and size, sets the colour, moves the text
    /// origin to the start of the baseline and shows the glyphs.
    /// </summary>
    private static ImmutableArray<ContentOperation> Text(GlyphRun run, PdfName fontResource, double pageHeight) =>
    [
        ContentOperators.SaveState(),
        ContentOperators.BeginText(),
        ContentOperators.SetFont(fontResource, run.FontSize),
        FillColor(run.Color),
        ContentOperators.MoveTextTo(run.Origin.X, pageHeight - run.Origin.Y),
        ContentOperators.ShowGlyphsWithAdjustments(GlyphArray(run)),
        ContentOperators.EndText(),
        ContentOperators.RestoreState(),
    ];

    /// <summary>
    /// The TJ array (§9.4.3). After each glyph a viewer advances the pen by the glyph's /W width. Where the page
    /// model puts a glyph somewhere else (kerning, in a later stage), a number is inserted that moves the pen:
    /// by the formula of §9.4.4, a number n moves it by −n / 1000 × font size. Numbers smaller than 0.001 (a
    /// millionth of the font size) are rounding noise from summing widths, and are left out. Glyph codes are
    /// two bytes, big-endian: the glyph id under Identity-H.
    /// </summary>
    private static ImmutableArray<PdfObject> GlyphArray(GlyphRun run)
    {
        var items = ImmutableArray.CreateBuilder<PdfObject>();
        var codes = new List<byte>();
        var pen = 0.0;
        foreach (var glyph in run.Glyphs)
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
