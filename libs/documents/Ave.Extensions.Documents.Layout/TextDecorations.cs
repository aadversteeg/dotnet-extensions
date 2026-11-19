using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// THE RULE FOR TEXT DECORATION LINES: where the lines a run's decorations draw lie on one piece of the run on a line
/// (CSS Text Decoration 3 §2). Each decoration of the piece (<see cref="TextStyle.Decorations"/>) is one filled rectangle
/// (<see cref="RuleFragment"/>), as Chromium paints a solid line (Blink <c>DecorationLinePainter::Paint</c>,
/// <c>DrawLineAsRect</c>, core/paint/decoration_line_painter.cc), measured against Chromium's print (cases <c>dl-*</c>).
/// </summary>
/// <remarks>
/// <para>Thickness: a tenth of the decorating box's font size, at least one pixel (Blink <c>ComputeDecorationThickness</c>,
/// core/paint/text_decoration_info.cc: <c>text-decoration-thickness: auto</c> is "used_font_size / 10.f";
/// <c>TextDecorationInfo::ComputeThickness</c>: at least 1). The font's own underline metrics ('post') are not used:
/// Chromium reads them only for <c>from-font</c>.</para>
/// <para>The spec leaves thickness and position to the user agent (CSS Text Decoration 3 §2.5: "The exact position and
/// thickness of line decorations is UA-defined in this level"), so they are Chromium's.</para>
/// <para>Position, with A the decorating font's ascent and T the thickness (Blink <c>TextDecorationInfo::ComputeUnderlineLineData</c>,
/// <c>ComputeLineThroughLineData</c>, <c>ComputeOverlineLineData</c>; core/layout/text_decoration_offset.cc
/// <c>ComputeUnderlineOffsetAuto</c>, <c>ComputeUnderlineOffsetForUnder</c>): an underline's top lies a gap below the
/// baseline, the gap being half the thickness rounded up and at least one pixel ("use at least one pixel gap. If underline
/// is thick then use a bigger gap"), and it is placed from the decorating box, so it is one straight line across text of
/// other sizes (case <c>dl-whose</c>); a line-through's top lies 2A/3 − T/2 below the top of the piece's own text, an
/// overline's top T above it, so these two follow the text of each piece (<c>dl-whose</c>: they step where the size
/// changes). Under <see cref="FontMetricRounding.WholePixels"/> the thickness is floored to whole pixels, the overline's
/// offset is floored T, and each top is rounded to a whole pixel ("Round to nearest pixel to match text", Blink
/// <c>SnapYAxis</c>), counted from the line box's top, where Chromium paints the baseline at a whole pixel of the page and the line from
/// there; equal where line tops lie on whole pixels, else the line moves with the unrounded baseline (case <c>dl-fraction</c>,
/// listed in docs/architecture.md). Under <see cref="FontMetricRounding.Exact"/> nothing is rounded.</para>
/// <para>Extent: from the piece's start across its advances, a widened space of a justified line included and a hanging
/// preserved space with its whole advance (<see cref="ShapedGlyph.Overhang"/>, case <c>dl-space</c>: underlined past the
/// line's end); a collapsible space removed at a line's end is not part of the piece, so it is not decorated. (CSS Text Decoration 3 §2
/// leaves out "spacing … at the beginning and end of a line"; Chromium draws across a hanging space all the same, and so
/// does the engine.) Neighbouring
/// pieces of one decorating box touch (<c>dl-join</c>). An image among the runs gets no line (<c>dl-image</c>; §2: "Atomic
/// inlines, such as images and inline blocks, are not decorated"): only text pieces are decorated. The lines take no part in layout: no line's height and no break moves (CSS Text Decoration 3
/// §5.2: "Text decorations that leak outside a box are considered ink overflow"; case <c>dl-height</c>).</para>
/// <para>Paint order: for each piece, its underlines and overlines before its glyphs, its line-throughs after them, each
/// in the order of <see cref="TextStyle.Decorations"/> (CSS Text Decoration 3 §5.1: "underlines · overlines · text · emphasis marks (text-emphasis) · line-through"; CSS 2.1
/// Appendix E.2, step 7.2.1.4.1.1; Blink
/// <c>TextFragmentPainter::Paint</c>: <c>PaintExceptLineThrough</c>, the text, <c>PaintOnlyLineThrough</c>; case
/// <c>dl-lines</c>). Chromium paints the underline and the overline of one decorating box together, outermost box first;
/// CSS 2.1 paints every underline before every overline. The two differ only where nested boxes draw both, and the order
/// here is Chromium's.</para>
/// <para>LIMIT: <c>text-decoration-skip-ink: auto</c>, Chromium's initial value, leaves gaps in an underline or overline where
/// a glyph crosses it; here every line is whole (docs/architecture.md, "Known limits").</para>
/// </remarks>
internal static class TextDecorations
{
    /// <summary>A decoration of a piece, with the decorating box's font resolved.</summary>
    /// <param name="Decoration">The decoration.</param>
    /// <param name="Font">The decorating box's font.</param>
    public sealed record Resolved(TextDecoration Decoration, TrueTypeFont Font);

    /// <summary>The rectangles one piece's decorations draw, in line coordinates: those painted before the piece's glyphs
    /// (underlines, overlines) and those painted after them (line-throughs).</summary>
    /// <param name="decorations">The piece's decorations, in order.</param>
    /// <param name="x">The piece's start.</param>
    /// <param name="glyphs">The piece's glyphs.</param>
    /// <param name="baseline">The line's baseline, from the line box's top.</param>
    /// <param name="ascent">The ascent of the piece's own font, as the line box takes it.</param>
    /// <param name="rounding">How the fonts' metrics are taken (the document's profile).</param>
    /// <returns>The rectangles before and after the glyphs; none when the piece has no width.</returns>
    public static (IReadOnlyList<PlacedFragment> Before, IReadOnlyList<PlacedFragment> After) Of(
        IReadOnlyList<Resolved> decorations, double x, IReadOnlyList<ShapedGlyph> glyphs, double baseline, double ascent, FontMetricRounding rounding)
    {
        if (decorations.Count == 0)
        {
            return ([], []);
        }

        // One pass over the piece's glyphs, made only for a decorated piece; then one rectangle per decoration.
        var width = glyphs.Sum(glyph => glyph.Advance + glyph.Overhang);
        if (width <= 0)
        {
            return ([], []);
        }

        var whole = rounding == FontMetricRounding.WholePixels;
        PlacedFragment Rule(Resolved resolved)
        {
            var size = resolved.Decoration.FontSize.Points;
            var thickness = System.Math.Max(size / 10, CssPixels.Points);
            var shown = whole ? CssPixels.Floor(thickness) : thickness;
            var textTop = baseline - ascent;
            var top = resolved.Decoration.Line switch
            {
                TextDecorationLine.Underline => baseline + System.Math.Max(1, System.Math.Ceiling(thickness / CssPixels.Points / 2)) * CssPixels.Points,
                TextDecorationLine.LineThrough => textTop + 2 * Ascent(resolved.Font, size, whole) / 3 - thickness / 2,
                _ => textTop - shown,
            };
            return new PlacedFragment(new Point(x, whole ? Snapped(top) : top), new RuleFragment(resolved.Decoration.Color, new Size(width, shown)));
        }

        return ([.. decorations.Where(resolved => resolved.Decoration.Line != TextDecorationLine.LineThrough).Select(Rule)],
            [.. decorations.Where(resolved => resolved.Decoration.Line == TextDecorationLine.LineThrough).Select(Rule)]);
    }

    /// <summary>A line's top rounded to the nearest whole pixel, a half pixel down (Blink <c>SnapYAxis</c>: "floorf(y + 0.5f)").</summary>
    private static double Snapped(double top) => System.Math.Floor(top / CssPixels.Points + 0.5) * CssPixels.Points;

    /// <summary>A font's ascent at a size, as the line box takes it (<see cref="FontMetrics.Line"/>), rounded to a whole pixel
    /// under <see cref="FontMetricRounding.WholePixels"/>, as Chromium's float ascent is on Linux.</summary>
    private static double Ascent(TrueTypeFont font, double size, bool whole)
    {
        var ascent = font.Metrics.ToPoints(font.Metrics.Line.Ascender, size);
        return whole ? CssPixels.Round(ascent) : ascent;
    }
}
