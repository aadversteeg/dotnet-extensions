using System;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>A point of a polygon in device space.</summary>
/// <param name="X">The x coordinate, in pixels.</param>
/// <param name="Y">The y coordinate, in pixels.</param>
internal readonly record struct Vertex(double X, double Y);

/// <summary>
/// Turns a path into closed polygons in device space: each subpath is transformed and its curves are replaced by
/// straight lines, close enough that the polygon stays within <see cref="Tolerance"/> of the curve.
/// </summary>
/// <remarks>
/// <para><b>Flattening rule.</b> A curve is cut into n pieces of equal parameter length, and the polyline through the
/// curve's points at t = k/n replaces it. Between two such points the distance from the curve to the chord is at most
/// h²/8 · max|B''| with h = 1/n (the error of linear interpolation). For a quadratic curve B'' = 2(p0 − 2p1 + p2), so
/// n = ⌈√(|p0 − 2p1 + p2| / (4τ))⌉; for a cubic |B''| ≤ 6·max(|p0 − 2p1 + p2|, |p1 − 2p2 + p3|) = 6M, so
/// n = ⌈√(3M / (4τ))⌉, with τ = <see cref="Tolerance"/>. Curves are transformed before they are flattened (an affine map
/// keeps a Bézier curve a Bézier curve of its control points), so the bound holds in pixels.</para>
/// <para><b>Bounded work.</b> n is at most <see cref="MaxPiecesPerCurve"/>; the bound above holds while
/// M ≤ 4τ·n²/3, that is for every curve that bends by less than about 2^18 pixels — only a glyph about a million pixels
/// tall bends more, and it is then drawn with that many pieces. A curve whose control points all lie outside the clip
/// box on one side is replaced by its chord: the curve and the chord lie in the convex hull of the control points, so
/// the loop they form winds around no point of the clip box, and the coverage inside the box is exactly the same.
/// So a huge curve that the box does not reach costs one line.</para>
/// </remarks>
internal static class Flattening
{
    /// <summary>The largest distance between a curve and the polyline that replaces it: 1/64 of a pixel.</summary>
    public const double Tolerance = 1.0 / 64;

    /// <summary>The most straight pieces one curve becomes: 4 096.</summary>
    public const int MaxPiecesPerCurve = 4096;

    /// <summary>The closed polygons of a path, transformed to device space; subpaths of fewer than three points are
    /// left out (they enclose nothing).</summary>
    /// <param name="path">The path.</param>
    /// <param name="transform">From the path's space to device pixels.</param>
    /// <param name="clip">The box that will be drawn into; curves that cannot reach it become chords.</param>
    /// <returns>The polygons.</returns>
    public static List<List<Vertex>> Polygons(Path path, Affine transform, PixelBox clip)
    {
        var polygons = new List<List<Vertex>>();
        List<Vertex>? current = null;
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case MoveTo move:
                    Finish(polygons, current);
                    current = [Map(transform, move.X, move.Y)];
                    break;
                case LineTo line:
                    current!.Add(Map(transform, line.X, line.Y));
                    break;
                case QuadraticTo quadratic:
                    Quadratic(current!, Map(transform, quadratic.ControlX, quadratic.ControlY), Map(transform, quadratic.X, quadratic.Y), clip);
                    break;
                case CubicTo cubic:
                    Cubic(current!, Map(transform, cubic.Control1X, cubic.Control1Y), Map(transform, cubic.Control2X, cubic.Control2Y), Map(transform, cubic.X, cubic.Y), clip);
                    break;
                case ClosePath:
                    // The polygon is closed anyway; a new subpath after Close starts with its own MoveTo, or, as in PDF
                    // (§8.5.2.1), at the start of the closed one.
                    Finish(polygons, current);
                    current = current is null ? null : [current[0]];
                    break;
                default:
                    throw new InvalidOperationException($"Unknown path command {command.GetType().Name}.");
            }
        }

        Finish(polygons, current);
        return polygons;
    }

    private static void Finish(List<List<Vertex>> polygons, List<Vertex>? polygon)
    {
        if (polygon is { Count: >= 3 })
        {
            polygons.Add(polygon);
        }
    }

    private static Vertex Map(Affine transform, double x, double y) => new(transform.MapX(x, y), transform.MapY(x, y));

    private static void Quadratic(List<Vertex> polygon, Vertex control, Vertex end, PixelBox clip)
    {
        var start = polygon[^1];
        if (Misses(clip, start, control, end))
        {
            polygon.Add(end);
            return;
        }

        var bend = Length(start.X - (2 * control.X) + end.X, start.Y - (2 * control.Y) + end.Y);
        var pieces = Pieces(bend / (4 * Tolerance));
        for (var piece = 1; piece < pieces; piece++)
        {
            var t = (double)piece / pieces;
            var u = 1 - t;
            polygon.Add(new Vertex(
                (u * u * start.X) + (2 * u * t * control.X) + (t * t * end.X),
                (u * u * start.Y) + (2 * u * t * control.Y) + (t * t * end.Y)));
        }

        polygon.Add(end);
    }

    private static void Cubic(List<Vertex> polygon, Vertex control1, Vertex control2, Vertex end, PixelBox clip)
    {
        var start = polygon[^1];
        if (Misses(clip, start, control1, control2, end))
        {
            polygon.Add(end);
            return;
        }

        var bend = double.Max(
            Length(start.X - (2 * control1.X) + control2.X, start.Y - (2 * control1.Y) + control2.Y),
            Length(control1.X - (2 * control2.X) + end.X, control1.Y - (2 * control2.Y) + end.Y));
        var pieces = Pieces(3 * bend / (4 * Tolerance));
        for (var piece = 1; piece < pieces; piece++)
        {
            var t = (double)piece / pieces;
            var u = 1 - t;
            polygon.Add(new Vertex(
                (u * u * u * start.X) + (3 * u * u * t * control1.X) + (3 * u * t * t * control2.X) + (t * t * t * end.X),
                (u * u * u * start.Y) + (3 * u * u * t * control1.Y) + (3 * u * t * t * control2.Y) + (t * t * t * end.Y)));
        }

        polygon.Add(end);
    }

    /// <summary>n = ⌈√q⌉, at least 1 and at most <see cref="MaxPiecesPerCurve"/>.</summary>
    private static int Pieces(double square) =>
        square >= (double)MaxPiecesPerCurve * MaxPiecesPerCurve ? MaxPiecesPerCurve : int.Max(1, (int)Math.Ceiling(Math.Sqrt(square)));

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

    /// <summary>True when every control point lies beyond the same side of the box.</summary>
    private static bool Misses(PixelBox clip, params ReadOnlySpan<Vertex> points)
    {
        bool left = true, right = true, above = true, below = true;
        foreach (var point in points)
        {
            left &= point.X <= clip.Left;
            right &= point.X >= clip.Right;
            above &= point.Y <= clip.Top;
            below &= point.Y >= clip.Bottom;
        }

        return left || right || above || below;
    }
}
