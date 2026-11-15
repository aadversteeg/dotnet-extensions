using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.DocumentModel;
using Ave.Documents.Typesetting;

namespace Ave.Documents.Layout;

/// <summary>Builds line boxes from broken lines (CSS 2.1 §10.8 "Line height calculations").</summary>
internal static class LineBoxes
{
    /// <summary>A text run with its resolved font and shaped glyphs.</summary>
    public sealed record ShapedRun(TextRun Run, TrueTypeFont Font, ImmutableArray<ShapedGlyph> Glyphs);

    /// <summary>
    /// The vertical extent of one inline box around the baseline. With <c>line-height: normal</c> (the
    /// initial value) the line height is the font's ascent + descent + line gap ('hhea'), so the leading
    /// L = line gap and half of it goes above and half below (CSS 2.1 §10.8.1, "half-leading").
    /// </summary>
    private sealed record InlineExtent(double Ascent, double Descent, double HalfLeading)
    {
        public double Above => Ascent + HalfLeading;
        public double Below => Descent + HalfLeading;

        public static InlineExtent Of(TrueTypeFont font, double fontSize) => new(
            Ascent: font.Metrics.ToPoints(font.Metrics.Ascender, fontSize),
            Descent: -font.Metrics.ToPoints(font.Metrics.Descender, fontSize),
            HalfLeading: font.Metrics.ToPoints(font.Metrics.LineGap, fontSize) / 2);
    }

    /// <summary>
    /// Builds the line box for one line. All inline boxes sit on one baseline (<c>vertical-align: baseline</c>);
    /// the line box reaches from the highest top to the lowest bottom among them (CSS 2.1 §10.8: "the line box
    /// height is the distance between the uppermost box top and the lowermost box bottom"). Stage 0 has no
    /// "strut" of the paragraph's own font (§10.8.1); line height comes from the runs on the line only.
    /// </summary>
    /// <param name="line">The broken line.</param>
    /// <param name="runs">The paragraph's runs; <see cref="LineSegment.Run"/> indexes into it.</param>
    /// <returns>The line fragment with its text fragments.</returns>
    public static LineFragment Build(TextLine line, IReadOnlyList<ShapedRun> runs)
    {
        var pieces = line.Segments
            .Select(segment => (Segment: segment, Run: runs[segment.Run]))
            .Select(piece => (piece.Segment, piece.Run, Extent: InlineExtent.Of(piece.Run.Font, piece.Run.Run.Style.FontSize.Points)))
            .ToImmutableArray();

        var above = pieces.Max(piece => piece.Extent.Above);
        var below = pieces.Max(piece => piece.Extent.Below);
        var children = ImmutableArray.CreateBuilder<PlacedFragment>(pieces.Length);
        var x = 0.0;
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
