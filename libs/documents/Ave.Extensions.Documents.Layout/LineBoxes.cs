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
    /// Inline 3 §5.3). A is the font's ascent and D its descent, from its line metrics (<see cref="Fonts.FontMetrics.Line"/>:
    /// 'OS/2' typographic or 'hhea', as Chromium chooses), at the box's font size; the leading L is added
    /// half above A and half below D. With <c>line-height: normal</c> (the initial value) L is the font's line gap, so
    /// the box is ascent + descent + line gap high (Inline 3 §5.3: "the font's line gap metric may also be incorporated
    /// into A and D by adding half to each side as half-leading"); with any other value "L = 'line-height' - AD" (CSS 2.1
    /// §10.8.1), so the box is exactly the line height high ("Note. L may be negative.").
    /// </summary>
    private sealed record InlineExtent(double Ascent, double Descent, double HalfLeading)
    {
        public double Above => Ascent + HalfLeading;
        public double Below => Descent + HalfLeading;

        public static InlineExtent Of(TrueTypeFont font, double fontSize, LineHeight lineHeight)
        {
            var metrics = font.Metrics.Line;
            var ascent = font.Metrics.ToPoints(metrics.Ascender, fontSize);
            var descent = -font.Metrics.ToPoints(metrics.Descender, fontSize);
            var leading = lineHeight.HeightFor(fontSize) is { } height
                ? height - (ascent + descent)
                : font.Metrics.ToPoints(metrics.LineGap, fontSize);
            return new InlineExtent(ascent, descent, leading / 2);
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
    /// <param name="empty">The paragraph's runs that put no glyph on any line and lie on this one (<see cref="EmptyRunsByLine"/>):
    /// their inline boxes count for the line's height all the same.</param>
    /// <returns>The line fragment with its text fragments.</returns>
    public static LineFragment Build(TextLine broken, IReadOnlyList<ShapedRun> runs, double width, TextAlign align, bool isLastLine, Strut? strut, IReadOnlyList<ShapedRun>? empty = null)
    {
        var (line, offset) = LineJustification.Place(broken, width, align, isLastLine);
        var pieces = line.Segments
            .Select(segment => (Segment: segment, Run: runs[segment.Run]))
            .Select(piece => (piece.Segment, piece.Run, Extent: InlineExtent.Of(piece.Run.Font, piece.Run.Run.Style.FontSize.Points, piece.Run.Run.Style.LineHeight)))
            .ToImmutableArray();
        ImmutableArray<InlineExtent> extents = [.. pieces.Select(piece => piece.Extent),
            .. (empty ?? []).Select(run => InlineExtent.Of(run.Font, run.Run.Style.FontSize.Points, run.Run.Style.LineHeight)),
            .. strut is { } own ? [InlineExtent.Of(own.Font, own.Style.FontSize.Points, own.Style.LineHeight)] : Array.Empty<InlineExtent>()];

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
    public static IReadOnlyList<IReadOnlyList<ShapedRun>> EmptyRunsByLine(IReadOnlyList<TextLine> lines, IReadOnlyList<ShapedRun> runs)
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

            byLine[current].Add(runs[run]);
        }

        return byLine;
    }
}
