using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// THE RULE FOR THE GAPS AROUND A GLYPH'S INK: where an underline or an overline is cut so that it does not cross a glyph
/// (CSS Text Decoration 4 §2.10.4, <c>text-decoration-skip-ink: auto</c>: "UAs may interrupt underlines and overlines where the
/// line would cross glyph ink"). The spec leaves the exact gaps to the user agent; they are Chromium's, measured against its
/// print (cases <c>sk-*</c>, <c>dl-skip</c>).
/// </summary>
/// <remarks>
/// <para>Band: the line's rectangle as computed, its top and its unrounded thickness T, inset by half a pixel above and below
/// ("In order to ignore intersects less than 0.5px, inflate by -0.5", Blink <c>TextPainter::ClipDecorationLine</c>,
/// core/paint/text_painter.cc). Under <see cref="DocumentModel.FontMetricRounding.WholePixels"/> the top is the snapped top the line is
/// painted at, while T stays unrounded where the painted height is floored (Chromium's print: a 24 pt underline painted
/// 46.5–48.75 pt is cut by clips 46.125–49.275 pt, so its band is 46.875–48.9 pt).</para>
/// <para>One glyph's gap: the leftmost and the rightmost x of its outline inside the band (Blink <c>Font::GetTextIntercepts</c>,
/// platform/fonts/font.cc; Skia <c>SkTextBlob::getIntercepts</c>, <c>SkGlyph::ensureIntercepts</c> and
/// <c>calculate_path_gap</c>, src/core/SkGlyph.cpp): for every segment of the outline whose points reach the band, the points
/// where it crosses the band's top and its bottom edge, and every point of the segment strictly inside the band. A curve's
/// control point counts as such a point although the curve does not pass through it: Skia takes it, so the gap can be wider
/// than the ink, and the engine does what Chromium's print shows. A glyph whose outline gives no two different x has no gap.
/// The outline is the font's own (<see cref="TrueTypeFont.Shape"/>: design units, quadratic curves, no hinting), as Skia
/// reads it unhinted; Skia holds it at 64 px in FreeType's 1/64 px steps, the engine in design units.</para>
/// <para>Widening: each gap is widened on both sides by min(T, 13 px) ("kDecorationClipMaxDilation = 13"), and the line is
/// not painted inside it (Chromium's <c>ClipOut</c>; vertically the clip reaches a pixel past the band, so it covers the
/// line's whole height).</para>
/// <para>Joining: the visible ink of a line is its rectangle minus its own widened gaps over its whole height, and minus the
/// gaps of each underline or overline painted before it on the same piece over the rows that earlier line's clip reaches
/// (<see cref="ClipExtent"/>): within one piece of text Chromium keeps the clips of an earlier line while it paints the next
/// one (one <c>GraphicsContextStateSaver</c> around all of them, <c>TextDecorationPainter::PaintExceptLineThrough</c>; case
/// <c>sk-nest</c>: two equal underlines, the second under both sets of clips; case <c>sk-part</c>: an 8 pt underline inside a
/// 24 pt one keeps its upper rows where the 24 pt clips reach its lower half). It is written as rectangles, one per column of
/// equal rows (<see cref="Cut"/>). Each piece of
/// text is cut by its own glyphs only: a glyph of one piece never cuts the line of its neighbour (Blink clips per text
/// fragment; case <c>sk-touch</c>).</para>
/// <para>Whose glyphs and whose value: the glyphs of the piece the line is drawn across, in the piece's own font and size, while
/// the band comes from the decorating box's line (case <c>sk-size</c>); the property of the piece's text decides
/// (<see cref="DocumentModel.TextStyle.DecorationSkipInk"/>; Blink reads <c>TargetStyle().TextDecorationSkipInk()</c>; case
/// <c>sk-whose</c>). A line-through is never cut ("line-throughs are always continuous"). A glyph that is not drawn (a
/// default-ignorable character) has no ink; an image gets no line at all.</para>
/// </remarks>
internal static class InkGaps
{
    /// <summary>The widest a gap is widened on each side, in CSS pixels (Blink <c>kDecorationClipMaxDilation</c>).</summary>
    private const double MaxDilationPixels = 13;

    /// <summary>The vertical extent of the clip of a gap, from the line's top, in points: the band outset by one pixel, so half a
    /// pixel above and below the line's computed rectangle (Blink <c>clip_rect.Outset(gfx::OutsetsF::VH(1.0, dilation))</c>).</summary>
    /// <param name="top">The line's top.</param>
    /// <param name="thickness">The line's unrounded thickness.</param>
    /// <returns>The clip's top and bottom.</returns>
    public static (double Top, double Bottom) ClipExtent(double top, double thickness) =>
        (top - CssPixels.Points / 2, top + thickness + CssPixels.Points / 2);

    /// <summary>
    /// Adds the widened gaps the glyphs of one piece cut out of one line: one gap per glyph whose outline enters the band.
    /// One pass over the outline of each glyph; each outline is decoded once per font and glyph (<paramref name="outlines"/>).
    /// </summary>
    /// <param name="into">The gaps found so far; the new ones are added, as (left, right) in line coordinates.</param>
    /// <param name="outlines">The outlines decoded for the paragraph.</param>
    /// <param name="font">The piece's own font.</param>
    /// <param name="size">The piece's font size, in points.</param>
    /// <param name="x">The piece's start.</param>
    /// <param name="glyphs">The piece's glyphs.</param>
    /// <param name="baseline">The line's baseline, from the line box's top.</param>
    /// <param name="top">The decoration line's top, as painted.</param>
    /// <param name="thickness">The decoration line's unrounded thickness.</param>
    /// <returns>Success, or the error of an outline that cannot be decoded.</returns>
    public static Result<Unit, Error> Add(
        List<(double Left, double Right)> into, InkOutlines outlines, TrueTypeFont font, double size, double x, IReadOnlyList<ShapedGlyph> glyphs, double baseline, double top, double thickness)
    {
        var scale = size / font.Metrics.UnitsPerEm;
        var bandTop = top + CssPixels.Points / 2;
        var bandBottom = top + thickness - CssPixels.Points / 2;
        var dilation = System.Math.Min(thickness, MaxDilationPixels * CssPixels.Points);
        var pen = x;
        foreach (var glyph in glyphs)
        {
            if (!glyph.IsInvisible && !glyph.IsAtomicInline)
            {
                var outline = outlines.Of(font, glyph.Glyph);
                if (outline.IsFailure)
                {
                    return Result<Unit, Error>.Failure(outline.Error);
                }

                // The band from the glyph's own baseline, in design units with y down, as Skia's path has it.
                var origin = baseline - glyph.YOffset;
                if (GapOf(outline.Value, (bandTop - origin) / scale, (bandBottom - origin) / scale, outlines) is { } gap)
                {
                    var start = pen + glyph.XOffset;
                    into.Add((start + gap.Left * scale - dilation, start + gap.Right * scale + dilation));
                }
            }

            pen += glyph.Advance;
        }

        return Result<Unit, Error>.Success(Unit.Value);
    }

    /// <summary>
    /// What stays visible of a line from <paramref name="left"/> to <paramref name="right"/> once the gaps are cut out of it
    /// across its whole height (<see cref="Cut"/> with one clip).
    /// </summary>
    /// <param name="left">The line's start.</param>
    /// <param name="right">The line's end.</param>
    /// <param name="gaps">The gaps, in any order.</param>
    /// <returns>The visible pieces, left to right, each of positive width.</returns>
    public static IReadOnlyList<(double Left, double Right)> Visible(double left, double right, List<(double Left, double Right)> gaps) =>
        [.. Cut(left, right, 0, 1, [(0, 1, gaps)], new InkOutlines()).Select(piece => (piece.Left, piece.Right))];

    /// <summary>
    /// The visible rectangles of a line from (<paramref name="left"/>, <paramref name="top"/>) to (<paramref name="right"/>,
    /// <paramref name="bottom"/>) under the clips in force: each clip leaves out its gaps over the rows it reaches, the clip
    /// of the line itself its whole height, the clip of an earlier line of the same piece only the rows of its own extent
    /// (<see cref="ClipExtent"/>; case <c>sk-part</c>). The line is split along x at every gap end; in each column the visible
    /// rows are the line's rows minus the rows of the clips whose gaps cover that column; neighbouring columns with the same
    /// rows are one rectangle, so no rectangle has a seam along the line. One ordered pass over the gap ends after one sort;
    /// per column at most one row interval per clip is taken away.
    /// </summary>
    /// <param name="left">The line's start.</param>
    /// <param name="right">The line's end.</param>
    /// <param name="top">The line's top, as painted.</param>
    /// <param name="bottom">The line's bottom, as painted.</param>
    /// <param name="clips">The clips in force: each with the rows it reaches and its gaps.</param>
    /// <param name="work">Counts the columns and row cuts made (<see cref="InkOutlines.Cuts"/>).</param>
    /// <returns>The visible rectangles, column by column left to right and top to bottom within a column.</returns>
    public static IReadOnlyList<(double Left, double Top, double Right, double Bottom)> Cut(
        double left, double right, double top, double bottom, IReadOnlyList<(double Top, double Bottom, List<(double Left, double Right)> Gaps)> clips, InkOutlines work)
    {
        var ends = new List<(double X, int Change, int Clip)>();
        for (var clip = 0; clip < clips.Count; clip++)
        {
            if (clips[clip].Top < bottom && top < clips[clip].Bottom)
            {
                foreach (var (gapLeft, gapRight) in clips[clip].Gaps)
                {
                    ends.Add((gapLeft, 1, clip));
                    ends.Add((gapRight, -1, clip));
                }
            }
        }

        ends.Sort((a, b) => a.X.CompareTo(b.X));
        var open = new int[clips.Count];
        var rectangles = new List<(double Left, double Top, double Right, double Bottom)>();
        var columnStart = left;
        List<(double Top, double Bottom)>? columnRows = null;
        var x = left;
        var next = 0;
        while (x < right)
        {
            while (next < ends.Count && ends[next].X <= x)
            {
                open[ends[next].Clip] += ends[next].Change;
                next++;
            }

            var end = next < ends.Count ? System.Math.Min(ends[next].X, right) : right;
            var rows = Rows(top, bottom, clips, open, work);
            if (columnRows is null || !SameRows(columnRows, rows))
            {
                Close(rectangles, columnStart, x, columnRows);
                (columnStart, columnRows) = (x, rows);
            }

            x = end;
        }

        Close(rectangles, columnStart, right, columnRows);
        return rectangles;
    }

    /// <summary>The rows of a column left visible: the line's rows minus the rows of each clip with a gap open there.</summary>
    private static List<(double Top, double Bottom)> Rows(double top, double bottom, IReadOnlyList<(double Top, double Bottom, List<(double Left, double Right)> Gaps)> clips, int[] open, InkOutlines work)
    {
        var rows = new List<(double Top, double Bottom)> { (top, bottom) };
        work.Cuts++;
        for (var clip = 0; clip < clips.Count && rows.Count > 0; clip++)
        {
            if (open[clip] <= 0)
            {
                continue;
            }

            var (cutTop, cutBottom) = (clips[clip].Top, clips[clip].Bottom);
            var kept = new List<(double Top, double Bottom)>(rows.Count + 1);
            foreach (var (rowTop, rowBottom) in rows)
            {
                work.Cuts++;
                if (System.Math.Min(rowBottom, cutTop) > rowTop)
                {
                    kept.Add((rowTop, System.Math.Min(rowBottom, cutTop)));
                }

                if (rowBottom > System.Math.Max(rowTop, cutBottom))
                {
                    kept.Add((System.Math.Max(rowTop, cutBottom), rowBottom));
                }
            }

            rows = kept;
        }

        return rows;
    }

    private static bool SameRows(List<(double Top, double Bottom)> a, List<(double Top, double Bottom)> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var index = 0; index < a.Count; index++)
        {
            if (a[index] != b[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void Close(List<(double Left, double Top, double Right, double Bottom)> rectangles, double left, double right, List<(double Top, double Bottom)>? rows)
    {
        if (rows is null || right <= left)
        {
            return;
        }

        foreach (var (top, bottom) in rows)
        {
            rectangles.Add((left, top, right, bottom));
        }
    }

    /// <summary>The leftmost and rightmost x of an outline inside the band from <paramref name="top"/> to
    /// <paramref name="bottom"/> (design units, y down), or null when it gives no two different x (Skia
    /// <c>calculate_path_gap</c>).</summary>
    internal static (double Left, double Right)? GapOf(ImmutableArray<InkSegment> outline, double top, double bottom, InkOutlines outlines)
    {
        var left = double.MaxValue;
        var right = double.MinValue;
        foreach (var segment in outline)
        {
            outlines.Visited++;
            if (segment.IsCurve)
            {
                var (high, low) = (System.Math.Min(segment.Y0, System.Math.Min(segment.ControlY, segment.Y1)), System.Math.Max(segment.Y0, System.Math.Max(segment.ControlY, segment.Y1)));
                if (top <= low && high <= bottom)
                {
                    CurveCrossing(segment, top, ref left, ref right);
                    CurveCrossing(segment, bottom, ref left, ref right);
                    Inside(segment.X0, segment.Y0, top, bottom, ref left, ref right);
                    Inside(segment.ControlX, segment.ControlY, top, bottom, ref left, ref right);
                    Inside(segment.X1, segment.Y1, top, bottom, ref left, ref right);
                }
            }
            else
            {
                var (high, low) = (System.Math.Min(segment.Y0, segment.Y1), System.Math.Max(segment.Y0, segment.Y1));
                if (top <= low && high <= bottom)
                {
                    LineCrossing(segment, top, ref left, ref right);
                    LineCrossing(segment, bottom, ref left, ref right);
                    Inside(segment.X0, segment.Y0, top, bottom, ref left, ref right);
                    Inside(segment.X1, segment.Y1, top, bottom, ref left, ref right);
                }
            }
        }

        return left < right ? (left, right) : null;
    }

    /// <summary>A point strictly inside the band widens the gap to it.</summary>
    private static void Inside(double x, double y, double top, double bottom, ref double left, ref double right)
    {
        if (top < y && y < bottom)
        {
            Expand(x, ref left, ref right);
        }
    }

    /// <summary>Where a straight segment crosses the horizontal line at <paramref name="y"/>, its end point excluded (t in
    /// [0, 1)); a horizontal segment crosses nowhere.</summary>
    private static void LineCrossing(InkSegment segment, double y, ref double left, ref double right)
    {
        var t = (y - segment.Y0) / (segment.Y1 - segment.Y0);
        if (t >= 0 && t < 1)
        {
            Expand(segment.X0 + t * (segment.X1 - segment.X0), ref left, ref right);
        }
    }

    /// <summary>Where a quadratic curve crosses the horizontal line at <paramref name="y"/>, t in [0, 1]: the roots of
    /// A·t² − 2B·t + C = y with A = P0 − 2P1 + P2, B = P0 − P1, C = P0, as Skia solves them (<c>SkBezierQuad::Intersect</c>,
    /// <c>SkQuads::Roots</c>, src/base/: the root of larger magnitude from B ± √D, the other as C'/R; a t within a float's step
    /// of 0 or 1 taken as 0 or 1).</summary>
    private static void CurveCrossing(InkSegment segment, double y, ref double left, ref double right)
    {
        var ay = segment.Y0 - 2 * segment.ControlY + segment.Y1;
        var by = segment.Y0 - segment.ControlY;
        var cy = segment.Y0 - y;
        double first;
        double second;
        if (ay == 0)
        {
            if (by == 0)
            {
                return;
            }

            first = second = cy / (2 * by);
        }
        else
        {
            var discriminant = by * by - ay * cy;
            if (discriminant < 0 || double.IsNaN(discriminant))
            {
                return;
            }

            if (discriminant == 0)
            {
                first = second = by / ay;
            }
            else
            {
                var root = System.Math.Sqrt(discriminant);
                var r = by > 0 ? by + root : by - root;
                first = r / ay;
                second = cy / r;
            }
        }

        first = Pinned(first);
        second = Pinned(second);
        if (first >= 0 && first <= 1)
        {
            Expand(CurveX(segment, first), ref left, ref right);
        }

        if (second >= 0 && second <= 1 && second != first)
        {
            Expand(CurveX(segment, second), ref left, ref right);
        }
    }

    private static double CurveX(InkSegment segment, double t)
    {
        var ax = segment.X0 - 2 * segment.ControlX + segment.X1;
        var bx = segment.X0 - segment.ControlX;
        return ax * t * t - 2 * bx * t + segment.X0;
    }

    /// <summary>A t within a float's step of 0 or 1 is 0 or 1 (Skia <c>pinTRange</c>).</summary>
    private static double Pinned(double t) => (float)(t + 1.0) == 1f ? 0 : (float)t == 1f ? 1 : t;

    private static void Expand(double x, ref double left, ref double right)
    {
        left = System.Math.Min(left, x);
        right = System.Math.Max(right, x);
    }
}

/// <summary>One segment of a glyph's outline in design units with y down (as Skia's path): a straight line from (X0, Y0) to
/// (X1, Y1), or a quadratic curve through the control point.</summary>
/// <param name="X0">The start's x.</param>
/// <param name="Y0">The start's y.</param>
/// <param name="X1">The end's x.</param>
/// <param name="Y1">The end's y.</param>
/// <param name="ControlX">The control point's x (a curve only).</param>
/// <param name="ControlY">The control point's y (a curve only).</param>
/// <param name="IsCurve">True for a quadratic curve.</param>
internal readonly record struct InkSegment(double X0, double Y0, double X1, double Y1, double ControlX, double ControlY, bool IsCurve);

/// <summary>
/// The outlines of the glyphs whose ink cuts a decoration line, decoded once per font and glyph for one paragraph's layout,
/// and the work done on them: <see cref="Decoded"/> outlines decoded, <see cref="Visited"/> segments visited (the bound of
/// THE RULE FOR THE GAPS AROUND A GLYPH'S INK: one pass over each glyph's outline per decoration).
/// </summary>
internal sealed class InkOutlines
{
    private readonly Dictionary<TrueTypeFont, Dictionary<GlyphId, ImmutableArray<InkSegment>>> _fonts = new(ReferenceEqualityComparer.Instance);

    /// <summary>The number of outlines decoded.</summary>
    public int Decoded { get; private set; }

    /// <summary>The number of outline segments visited.</summary>
    public long Visited { get; set; }

    /// <summary>The number of columns and row cuts made in cutting lines (<see cref="InkGaps.Cut"/>).</summary>
    public long Cuts { get; set; }

    /// <summary>A glyph's outline as segments with y down, decoded on first use.</summary>
    /// <param name="font">The font.</param>
    /// <param name="glyph">The glyph.</param>
    /// <returns>The segments, or the decoder's error.</returns>
    public Result<ImmutableArray<InkSegment>, Error> Of(TrueTypeFont font, GlyphId glyph)
    {
        if (!_fonts.TryGetValue(font, out var glyphs))
        {
            glyphs = [];
            _fonts.Add(font, glyphs);
        }

        if (glyphs.TryGetValue(glyph, out var known))
        {
            return Result<ImmutableArray<InkSegment>, Error>.Success(known);
        }

        var shape = font.Shape(glyph);
        if (shape.IsFailure)
        {
            return Result<ImmutableArray<InkSegment>, Error>.Failure(shape.Error);
        }

        Decoded++;
        var segments = ImmutableArray.CreateBuilder<InkSegment>();
        foreach (var contour in shape.Value.Contours)
        {
            var walk = contour.Segments();
            var (x, y) = (walk.Start.X, -walk.Start.Y);
            foreach (var segment in walk.Segments)
            {
                var (endX, endY) = (segment.End.X, -segment.End.Y);
                segments.Add(segment.Control is { } control
                    ? new InkSegment(x, y, endX, endY, control.X, -control.Y, IsCurve: true)
                    : new InkSegment(x, y, endX, endY, 0, 0, IsCurve: false));
                (x, y) = (endX, endY);
            }
        }

        var outline = segments.ToImmutable();
        glyphs.Add(glyph, outline);
        return Result<ImmutableArray<InkSegment>, Error>.Success(outline);
    }
}
