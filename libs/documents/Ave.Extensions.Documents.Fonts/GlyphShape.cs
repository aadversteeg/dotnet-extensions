using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fonts;

/// <summary>
/// The outline of a glyph, decoded from 'glyf' (<see cref="TrueTypeFont.Shape"/>): closed contours of TrueType
/// points, in font design units with y up from the baseline, before any hinting. A composite glyph is already
/// resolved into the contours of its components, each moved and transformed as the composite says. Filled with the
/// non-zero winding rule (OpenType spec, "glyf — Glyph data": "the non-zero winding rule").
/// </summary>
/// <param name="Contours">The contours, in the order of the font's points; empty for a glyph without an outline, such
/// as the space.</param>
public sealed record GlyphShape(ValueList<GlyphContour> Contours)
{
    /// <summary>The outline of a glyph without contours.</summary>
    public static GlyphShape Empty { get; } = new(ValueList<GlyphContour>.Empty);
}

/// <summary>
/// One closed contour of a glyph: its points as the font stores them. An on-curve point is a point the outline
/// passes through; an off-curve point is the control point of a quadratic Bézier curve. Two off-curve points in a row
/// imply an on-curve point midway between them (OpenType spec, "glyf", simple glyph description), so a contour can
/// hold any number of them in a row. The last point connects back to the first.
/// </summary>
/// <param name="Points">The points, at least one.</param>
public sealed record GlyphContour(ValueList<OutlinePoint> Points)
{
    /// <summary>
    /// The contour as a start point and a closed run of segments, each a straight line or one quadratic Bézier curve,
    /// with every implied on-curve point made explicit. The curves stay quadratic. The start is the first on-curve
    /// point; a contour of off-curve points only starts at the point implied between its last and its first point.
    /// A contour of one point has no segments: it encloses nothing.
    /// </summary>
    /// <returns>The segments, which end where they start.</returns>
    public ContourSegments Segments()
    {
        var points = Points.Items;
        var count = points.Length;
        if (count == 1)
        {
            return new ContourSegments(points[0] with { OnCurve = true }, ValueList<OutlineSegment>.Empty);
        }

        var first = -1;
        for (var index = 0; index < count && first < 0; index++)
        {
            first = points[index].OnCurve ? index : -1;
        }

        // From an on-curve start, the walk visits every other point and ends on the start itself; from an implied
        // start, it visits every point and the last curve closes on the start.
        var start = first >= 0 ? points[first] : Midpoint(points[count - 1], points[0]);
        var segments = ImmutableArray.CreateBuilder<OutlineSegment>();
        OutlinePoint? control = null;
        for (var step = 1; step <= count; step++)
        {
            var point = first >= 0 ? points[(first + step) % count] : points[step - 1];
            if (point.OnCurve)
            {
                segments.Add(new OutlineSegment(point, control));
                control = null;
            }
            else
            {
                if (control is { } previous)
                {
                    segments.Add(new OutlineSegment(Midpoint(previous, point), previous));
                }

                control = point;
            }
        }

        if (control is { } last)
        {
            segments.Add(new OutlineSegment(start, last));
        }

        return new ContourSegments(start, segments.ToImmutable());
    }

    private static OutlinePoint Midpoint(OutlinePoint a, OutlinePoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2, OnCurve: true);
}

/// <summary>A point of a glyph contour, in font design units, y up from the baseline.</summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Y">The vertical position.</param>
/// <param name="OnCurve">True for a point on the outline, false for the control point of a quadratic curve.</param>
public readonly record struct OutlinePoint(double X, double Y, bool OnCurve);

/// <summary>A contour as explicit segments (<see cref="GlyphContour.Segments"/>).</summary>
/// <param name="Start">The on-curve point the first segment starts from and the last one ends on.</param>
/// <param name="Segments">The segments in order.</param>
public sealed record ContourSegments(OutlinePoint Start, ValueList<OutlineSegment> Segments);

/// <summary>One segment of a contour: from the end of the previous segment (or the start) to <paramref name="End"/>.</summary>
/// <param name="End">The on-curve point the segment ends on.</param>
/// <param name="Control">The control point of a quadratic Bézier curve; null for a straight line.</param>
public readonly record struct OutlineSegment(OutlinePoint End, OutlinePoint? Control);
