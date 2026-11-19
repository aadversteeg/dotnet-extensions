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
    /// <summary>An item of a paragraph's lines: an inline run with its glyphs (<see cref="ShapedRun"/>) or an atomic inline
    /// (<see cref="AtomicInline"/>).</summary>
    public abstract record Item;

    /// <summary>An inline run with its resolved font and shaped glyphs.</summary>
    public sealed record ShapedRun(InlineRun Run, TrueTypeFont Font, ValueList<ShapedGlyph> Glyphs) : Item;

    /// <summary>
    /// An atomic inline (CSS Display 3, Appendix A): a child box of the paragraph laid out by its own algorithm, one item of
    /// the line that is never broken. On the line it is its margin box (CSS 2.1 §10.8: "For replaced elements, inline-block
    /// elements, and inline-table elements, this is the height of their margin box").
    /// </summary>
    /// <param name="Box">The child box.</param>
    /// <param name="Fragment">Its fragment, once laid out at the paragraph's width; null while only measured.</param>
    /// <param name="Width">The width of its margin box, in points: its advance on the line.</param>
    public sealed record AtomicInline(Box Box, BoxFragment? Fragment, double Width) : Item
    {
        /// <summary>Its horizontal margins, in points.</summary>
        public double Margins => Box.Style.Margin.Horizontal.Points;
    }

    /// <summary>The glyphs an item puts on the line breaker's runs: a run's own glyphs, or for an atomic inline one glyph
    /// as wide as its margin box (<see cref="ShapedGlyph.AtomicInline"/>).</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its glyphs.</returns>
    public static ValueList<ShapedGlyph> GlyphsOf(Item item) => item switch
    {
        ShapedRun run => run.Glyphs,
        AtomicInline atomic => [ShapedGlyph.AtomicInline(atomic.Width)],
        _ => throw new InvalidOperationException($"Unknown line item {item.GetType().Name}."),
    };

    /// <summary>The strut of a paragraph's lines (CSS 2.1 §10.8.1): the paragraph's own font, resolved, and its text
    /// properties (font size and line height).</summary>
    /// <param name="Font">The paragraph's font.</param>
    /// <param name="Style">The paragraph's computed text properties.</param>
    public sealed record Strut(TrueTypeFont Font, TextStyle Style);

    /// <summary>
    /// The vertical extent of one inline box around the baseline (CSS 2.1 §10.8.1, "Leading and half-leading"; CSS
    /// Inline 3 §5.3). A is the font's ascent and D its descent, from its line metrics (<see cref="Fonts.FontMetrics.Line"/>:
    /// 'OS/2' typographic or 'hhea', as Chromium chooses), at the box's font size; the leading L is added
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
            var metrics = font.Metrics.Line;
            var ascent = Metric(metrics.Ascender);
            var descent = Metric(-metrics.Descender);
            var leading = lineHeight.HeightFor(fontSize) is { } height ? height - (ascent + descent) : Metric(metrics.LineGap);
            var above = whole ? CssPixels.Floor(leading / 2) : leading / 2;
            return new InlineExtent(ascent, descent, above, leading - above);
        }

        /// <summary>The extent of an atomic inline's margin box: all of it above the baseline (<see cref="Build"/>, THE RULE
        /// FOR AN ATOMIC INLINE ON THE LINE).</summary>
        public static InlineExtent OfMarginBox(AtomicInline atomic) =>
            new(atomic.Box.Style.Margin.Vertical.Points + (atomic.Fragment?.Size.Height ?? 0), 0, 0, 0);
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
    /// moved right or to the centre, or justified by widening its word separators; an atomic inline is none (case
    /// <c>ii-align</c>: in "one", image, "two" the justified line widens only the space after "two").
    /// <para>THE RULE FOR AN ATOMIC INLINE ON THE LINE: its margin box sits with its bottom on the baseline and reaches as
    /// high above it as it is tall (CSS 2.1 §10.8, the margin box; §10.8.1, 'vertical-align': "baseline: Align the baseline
    /// of the box with the baseline of the parent box. If the box does not have a baseline, align the bottom margin edge
    /// with the parent's baseline"), as Chromium synthesizes the baseline of an image off its margin box's bottom edge (Blink
    /// <c>LogicalBoxFragment::BaselineMetrics</c>, core/layout/logical_box_fragment.cc). So it makes the line taller when it
    /// is taller than the strut's part above the baseline, never below it; its margins count above, below and beside it
    /// (case <c>ii-baseline</c>).</para>
    /// </remarks>
    /// <param name="broken">The broken line.</param>
    /// <param name="runs">The paragraph's runs; <see cref="LineSegment.Run"/> indexes into it.</param>
    /// <param name="width">The width of the line box (the paragraph's content width).</param>
    /// <param name="align">The paragraph's text-align.</param>
    /// <param name="isLastLine">True for the paragraph's last line, which is never justified.</param>
    /// <param name="strut">The paragraph's strut, or null when the model gives the paragraph no text properties.</param>
    /// <param name="rounding">How the fonts' metrics are taken (the document's profile).</param>
    /// <param name="empty">The paragraph's runs that put no glyph on any line and lie on this one (<see cref="EmptyRunsByLine"/>):
    /// their inline boxes count for the line's height all the same.</param>
    /// <returns>The line fragment with its text fragments.</returns>
    public static LineFragment Build(TextLine broken, IReadOnlyList<Item> runs, double width, TextAlign align, bool isLastLine, Strut? strut, FontMetricRounding rounding, IReadOnlyList<ShapedRun>? empty = null)
    {
        var (line, offset) = LineJustification.Place(broken, width, align, isLastLine);
        var pieces = line.Segments
            .Select(segment => (Segment: segment, Run: runs[segment.Run]))
            .Select(piece => (piece.Segment, piece.Run, Extent: piece.Run switch
            {
                ShapedRun run => InlineExtent.Of(run.Font, run.Run.Style.FontSize.Points, run.Run.Style.LineHeight, rounding),
                AtomicInline atomic => InlineExtent.OfMarginBox(atomic),
                _ => throw new InvalidOperationException($"Unknown line item {piece.Run.GetType().Name}."),
            }))
            .ToImmutableArray();
        ImmutableArray<InlineExtent> extents = [.. pieces.Select(piece => piece.Extent),
            .. (empty ?? []).Select(run => InlineExtent.Of(run.Font, run.Run.Style.FontSize.Points, run.Run.Style.LineHeight, rounding)),
            .. strut is { } own ? [InlineExtent.Of(own.Font, own.Style.FontSize.Points, own.Style.LineHeight, rounding)] : Array.Empty<InlineExtent>()];

        var above = extents.Max(extent => extent.Above);
        var below = extents.Max(extent => extent.Below);
        var children = ImmutableArray.CreateBuilder<PlacedFragment>(pieces.Length);
        var x = offset;
        foreach (var (segment, item, extent) in pieces)
        {
            // The baseline lies `above` below the line's top; this piece's top edge is its ascent above that. An atomic
            // inline's border box lies inside its margin box, whose bottom is on the baseline.
            children.Add(item switch
            {
                AtomicInline { Fragment: { } fragment } atomic => new PlacedFragment(
                    new Point(x + atomic.Box.Style.Margin.Left.Points, above - atomic.Box.Style.Margin.Bottom.Points - fragment.Size.Height), fragment),
                ShapedRun run => new PlacedFragment(
                    new Point(x, above - extent.Ascent), new TextFragment(run.Run, run.Font, extent.Ascent, segment.Glyphs, new Size(segment.Width, extent.Ascent + extent.Descent))),
                _ => throw new InvalidOperationException("An atomic inline is placed on a line only once it is laid out."),
            });
            x += segment.Width;
        }

        return new LineFragment(new Size(x, above + below), children.MoveToImmutable());
    }

    /// <summary>
    /// The runs that put no glyph on any line (an empty span, a span whose only space collapses at a line edge), each with
    /// the line it lies on: the line of the last run before it that has glyphs, or the first line. "Empty inline elements
    /// generate empty inline boxes, but these boxes still have margins, padding, borders and a line height, and thus
    /// influence the line box height calculation just like elements with content" (CSS 2.1 §10.8), as Chromium counts
    /// them (case <c>fr-empty-span</c>).
    /// </summary>
    /// <param name="lines">The paragraph's lines.</param>
    /// <param name="runs">The paragraph's runs.</param>
    /// <returns>Per line, the runs without glyphs that lie on it.</returns>
    public static IReadOnlyList<IReadOnlyList<ShapedRun>> EmptyRunsByLine(IReadOnlyList<TextLine> lines, IReadOnlyList<Item> runs)
    {
        // One pass over the segments and one over the runs: linear in the paragraph.
        var lastLineOf = new int?[runs.Count];
        for (var line = 0; line < lines.Count; line++)
        {
            foreach (var segment in lines[line].Segments)
            {
                lastLineOf[segment.Run] = line;
            }
        }

        var byLine = Enumerable.Range(0, lines.Count).Select(_ => new List<ShapedRun>()).ToList();
        var current = 0;
        for (var run = 0; run < runs.Count && lines.Count > 0; run++)
        {
            if (lastLineOf[run] is { } line)
            {
                // A run on several lines: an empty run after it lies on its last line.
                current = line;
                continue;
            }

            // An atomic inline always has its glyph on a line.
            if (runs[run] is ShapedRun empty)
            {
                byLine[current].Add(empty);
            }
        }

        return byLine;
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
