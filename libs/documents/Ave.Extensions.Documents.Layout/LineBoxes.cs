using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Typesetting;

namespace Ave.Extensions.Documents.Layout;

/// <summary>Builds line boxes from broken lines (CSS 2.1 §10.8 "Line height calculations").</summary>
internal static class LineBoxes
{
    /// <summary>An inline run with its resolved font and shaped glyphs.</summary>
    public sealed record ShapedRun(InlineRun Run, TrueTypeFont Font, ValueList<ShapedGlyph> Glyphs);

    /// <summary>The strut of a paragraph's lines (CSS 2.1 §10.8.1): the paragraph's own font, resolved, and its text
    /// properties (font size and line height).</summary>
    /// <param name="Font">The paragraph's font.</param>
    /// <param name="Style">The paragraph's computed text properties.</param>
    public sealed record Strut(TrueTypeFont Font, TextStyle Style);

    /// <summary>
    /// The vertical extent of one inline box around the baseline (CSS 2.1 §10.8.1, "Leading and half-leading"; CSS
    /// Inline 3 §5.3). A is the font's ascent and D its descent ('hhea'), at the box's font size; the leading L is added
    /// half above A and half below D. With <c>line-height: normal</c> (the initial value) L is the font's line gap, so
    /// the box is ascent + descent + line gap high (Inline 3 §5.3: "the font's line gap metric may also be incorporated
    /// into A and D by adding half to each side as half-leading"); with any other value "L = 'line-height' - AD" (CSS 2.1
    /// §10.8.1), so the box is exactly the line height high ("Note. L may be negative."). Under
    /// <see cref="FontMetricRounding.WholePixels"/> A, D and the line gap are each rounded to whole pixels and the half
    /// above is floored to a whole pixel, the rest of L going below, as Chromium does.
    /// </summary>
    private sealed record InlineExtent(double Ascent, double Descent, double LeadingAbove, double LeadingBelow)
    {
        public double Above => Ascent + LeadingAbove;
        public double Below => Descent + LeadingBelow;

        public static InlineExtent Of(TrueTypeFont font, double fontSize, LineHeight lineHeight, FontMetricRounding rounding)
        {
            var whole = rounding == FontMetricRounding.WholePixels;
            double Metric(int designUnits) =>
                whole ? CssPixels.Round(font.Metrics.ToPoints(designUnits, fontSize)) : font.Metrics.ToPoints(designUnits, fontSize);
            var ascent = Metric(font.Metrics.Ascender);
            var descent = Metric(-font.Metrics.Descender);
            var leading = lineHeight.HeightFor(fontSize) is { } height ? height - (ascent + descent) : Metric(font.Metrics.LineGap);
            var above = whole ? CssPixels.Floor(leading / 2) : leading / 2;
            return new InlineExtent(ascent, descent, above, leading - above);
        }
    }

    /// <summary>
    /// Builds the line box for one line. All inline boxes sit on one baseline (<c>vertical-align: baseline</c>);
    /// the line box reaches from the highest top to the lowest bottom among them (CSS 2.1 §10.8: "the line box
    /// height is the distance between the uppermost box top and the lowermost box bottom. (This includes the strut,
    /// as explained under 'line-height' below.)"): every line starts with the paragraph's strut, a box of no width with
    /// the paragraph's own font and line height, so no line is lower than the paragraph's line height.
    /// </summary>
    /// <remarks>
    /// Horizontally the line is placed by its paragraph's <c>text-align</c> (<see cref="LineJustification.Place"/>):
    /// moved right or to the centre, or justified by widening its word separators.
    /// </remarks>
    /// <param name="broken">The broken line.</param>
    /// <param name="runs">The paragraph's runs; <see cref="LineSegment.Run"/> indexes into it.</param>
    /// <param name="width">The width of the line box (the paragraph's content width).</param>
    /// <param name="align">The paragraph's text-align.</param>
    /// <param name="isLastLine">True for the paragraph's last line, which is never justified.</param>
    /// <param name="strut">The paragraph's strut, or null when the model gives the paragraph no text properties.</param>
    /// <param name="rounding">How the fonts' metrics are taken (the document's profile).</param>
    /// <returns>The line fragment with its text fragments.</returns>
    public static LineFragment Build(TextLine broken, IReadOnlyList<ShapedRun> runs, double width, TextAlign align, bool isLastLine, Strut? strut, FontMetricRounding rounding)
    {
        var (line, offset) = LineJustification.Place(broken, width, align, isLastLine);
        var pieces = line.Segments
            .Select(segment => (Segment: segment, Run: runs[segment.Run]))
            .Select(piece => (piece.Segment, piece.Run, Extent: InlineExtent.Of(piece.Run.Font, piece.Run.Run.Style.FontSize.Points, piece.Run.Run.Style.LineHeight, rounding)))
            .ToImmutableArray();
        ImmutableArray<InlineExtent> extents = [.. pieces.Select(piece => piece.Extent),
            .. strut is { } own ? [InlineExtent.Of(own.Font, own.Style.FontSize.Points, own.Style.LineHeight, rounding)] : Array.Empty<InlineExtent>()];

        var above = extents.Max(extent => extent.Above);
        var below = extents.Max(extent => extent.Below);
        var children = ImmutableArray.CreateBuilder<PlacedFragment>(pieces.Length);
        var x = offset;
        foreach (var (segment, run, extent) in pieces)
        {
            var size = new Size(segment.Width, extent.Ascent + extent.Descent);
            var text = new TextFragment(run.Run, run.Font, extent.Ascent, segment.Glyphs, size);

            // The baseline lies `above` below the line's top; this piece's top edge is its ascent above that.
            children.Add(new PlacedFragment(new Point(x, above - extent.Ascent), text));
            x += segment.Width;
        }

        return new LineFragment(new Size(x, above + below), children.MoveToImmutable());
    }
}

/// <summary>
/// The CSS pixel, 1/96 inch = 0.75 pt (CSS Values 4 §6.2), and rounding lengths in points to whole pixels, as Chromium
/// does where it keeps whole pixels (<see cref="FontMetricRounding.WholePixels"/>).
/// </summary>
internal static class CssPixels
{
    /// <summary>Points per CSS pixel.</summary>
    public const double Points = 0.75;

    /// <summary>A length rounded to the nearest whole pixel, half away from zero (C's <c>lroundf</c>, which Chromium
    /// uses).</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The rounded length in points.</returns>
    public static double Round(double points) => Math.Round(points / Points, MidpointRounding.AwayFromZero) * Points;

    /// <summary>A length floored to a whole pixel (<c>LayoutUnit::Floor</c>). A length a hair below a whole pixel only
    /// because points do not hold every pixel value exactly counts as that pixel.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The floored length in points.</returns>
    public static double Floor(double points) => Math.Floor(points / Points + 1e-9) * Points;
}
