using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>
/// The area a filled path of the page model paints, by the same sampling rule as the read-back of Chromium's print
/// (<c>tools/reference/chromium/paths.py</c>), so the two sides of a case comparison write a curve in one form
/// (<see cref="PrintedPath"/>). The rule, step for step as there: curves are flattened to straight segments so few that no
/// point of a segment lies more than <see cref="Tolerance"/> from the curve; every contour is closed; the whole pixels
/// are those of <see cref="Pixel"/> from the page's top left; each pixel has <see cref="Samples"/> × <see cref="Samples"/>
/// sample points at the centres of a regular grid inside it, and a point is painted where it lies inside the path by its
/// fill rule and inside the rectangle the clips leave (left and top edges in, right and bottom edges out); a pixel's
/// share is its painted points in fifteenths, rounded half up, and the box is the smallest of whole pixels that holds
/// every pixel whose share is not 0. The arithmetic is the script's, in the same order, so both give the same digits for
/// the same shape (<c>PathSamplerTests</c> pins three shapes to the script's numbers).
/// </summary>
public static class PathSampler
{
    /// <summary>The side of a whole pixel, in points: one CSS px.</summary>
    public const double Pixel = 0.75;

    /// <summary>The sample points per pixel side.</summary>
    public const int Samples = 15;

    /// <summary>The largest distance between a curve and its flattened segments, in points.</summary>
    public const double Tolerance = 0.0005;

    /// <summary>The area a filled path paints, inside a clip.</summary>
    /// <param name="page">The page number the line belongs to.</param>
    /// <param name="path">The filled path.</param>
    /// <param name="clip">The rectangle the clips around it leave, or null where none is open.</param>
    /// <returns>The painted area, or null where no pixel is painted.</returns>
    public static PrintedPath? Sample(int page, FillPath path, Rect? clip)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = Segments(path);
        if (segments.Count == 0)
        {
            return null;
        }

        var (left, top) = (segments.Min(segment => Math.Min(segment.Ax, segment.Bx)), segments.Min(segment => Math.Min(segment.Ay, segment.By)));
        var (right, bottom) = (segments.Max(segment => Math.Max(segment.Ax, segment.Bx)), segments.Max(segment => Math.Max(segment.Ay, segment.By)));
        var (clipLeft, clipTop, clipRight, clipBottom) = clip is { } area
            ? (area.Origin.X, area.Origin.Y, area.Origin.X + area.Size.Width, area.Origin.Y + area.Size.Height)
            : (double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);
        if (clip is not null)
        {
            (left, top, right, bottom) = (Math.Max(left, clipLeft), Math.Max(top, clipTop), Math.Min(right, clipRight), Math.Min(bottom, clipBottom));
            if (left >= right || top >= bottom)
            {
                return null;
            }
        }

        var (firstColumn, endColumn) = ((long)Math.Floor(left / Pixel), (long)Math.Ceiling(right / Pixel));
        var (firstRow, endRow) = ((long)Math.Floor(top / Pixel), (long)Math.Ceiling(bottom / Pixel));
        var step = Pixel / Samples;
        var evenOdd = path.Rule == PathFillRule.EvenOdd;
        var counts = new Dictionary<(long Column, long Row), int>();
        for (var row = firstRow; row < endRow; row++)
        {
            for (var sampleRow = 0; sampleRow < Samples; sampleRow++)
            {
                var y = ((row * Samples) + sampleRow + 0.5) * step;
                var intervals = Inside(segments, y, evenOdd);
                for (var column = firstColumn; column < endColumn; column++)
                {
                    for (var sampleColumn = 0; sampleColumn < Samples; sampleColumn++)
                    {
                        var x = ((column * Samples) + sampleColumn + 0.5) * step;
                        if (!(clipLeft <= x && x < clipRight && clipTop <= y && y < clipBottom) || !intervals.Exists(interval => interval.X0 <= x && x < interval.X1))
                        {
                            continue;
                        }

                        counts[(column, row)] = counts.GetValueOrDefault((column, row)) + 1;
                    }
                }
            }
        }

        var digits = counts.Select(cell => (cell.Key, Digit: (cell.Value + (Samples / 2)) / Samples)).Where(cell => cell.Digit > 0).ToDictionary(cell => cell.Key, cell => cell.Digit);
        if (digits.Count == 0)
        {
            return null;
        }

        var (c0, c1) = (digits.Keys.Min(cell => cell.Column), digits.Keys.Max(cell => cell.Column));
        var (r0, r1) = (digits.Keys.Min(cell => cell.Row), digits.Keys.Max(cell => cell.Row));
        var rows = ImmutableArray.CreateBuilder<string>();
        for (var row = r0; row <= r1; row++)
        {
            var text = new System.Text.StringBuilder();
            for (var column = c0; column <= c1; column++)
            {
                text.Append(Hex[digits.GetValueOrDefault((column, row))]);
            }

            rows.Add(text.ToString());
        }

        return new PrintedPath(page, c0 * Pixel, r0 * Pixel, (c0 + (c1 - c0 + 1)) * Pixel, (r0 + (r1 - r0 + 1)) * Pixel,
            FormattableString.Invariant($"#{path.Color.Red:x2}{path.Color.Green:x2}{path.Color.Blue:x2}"), rows.ToImmutable());
    }

    /// <summary>The painted share per pixel that rectangles give the whole pixels of a path's box, by the same sample points
    /// and the same rounding as <see cref="Sample"/>: a point is painted where it lies inside one of the rectangles (left
    /// and top edges in, right and bottom edges out), so rectangles that paint the area a path paints give the path's rows
    /// digit for digit. A rectangle's part outside the box is not counted.</summary>
    /// <param name="rectangles">The rectangles, as they are painted: not snapped to whole pixels.</param>
    /// <param name="box">The path whose box of whole pixels the rows are of.</param>
    /// <returns>The rows of the box from the top, one hexadecimal digit per pixel from the left.</returns>
    public static ImmutableArray<string> Rows(IReadOnlyList<PrintedBox> rectangles, PrintedPath box)
    {
        ArgumentNullException.ThrowIfNull(rectangles);
        ArgumentNullException.ThrowIfNull(box);
        var (firstColumn, endColumn) = ((long)Math.Round(box.X0 / Pixel), (long)Math.Round(box.X1 / Pixel));
        var (firstRow, endRow) = ((long)Math.Round(box.Y0 / Pixel), (long)Math.Round(box.Y1 / Pixel));
        var step = Pixel / Samples;
        var rows = ImmutableArray.CreateBuilder<string>();
        for (var row = firstRow; row < endRow; row++)
        {
            var text = new System.Text.StringBuilder();
            for (var column = firstColumn; column < endColumn; column++)
            {
                var count = 0;
                for (var sampleRow = 0; sampleRow < Samples; sampleRow++)
                {
                    var y = ((row * Samples) + sampleRow + 0.5) * step;
                    for (var sampleColumn = 0; sampleColumn < Samples; sampleColumn++)
                    {
                        var x = ((column * Samples) + sampleColumn + 0.5) * step;
                        if (rectangles.Any(rectangle => rectangle.X0 <= x && x < rectangle.X1 && rectangle.Y0 <= y && y < rectangle.Y1))
                        {
                            count++;
                        }
                    }
                }

                text.Append(Hex[(count + (Samples / 2)) / Samples]);
            }

            rows.Add(text.ToString());
        }

        return rows.ToImmutable();
    }

    private const string Hex = "0123456789abcdef";

    /// <summary>A straight segment of a flattened contour.</summary>
    private readonly record struct Segment(double Ax, double Ay, double Bx, double By);

    /// <summary>A stretch of a sample row that is inside the path: from <c>X0</c> (in) to <c>X1</c> (out).</summary>
    private readonly record struct Interval(double X0, double X1);

    /// <summary>The flattened, closed contours of the path as straight segments.</summary>
    private static List<Segment> Segments(FillPath path)
    {
        var segments = new List<Segment>();
        foreach (var contour in path.Contours)
        {
            var points = new List<(double X, double Y)> { (contour.Start.X, contour.Start.Y) };
            var from = contour.Start;
            foreach (var segment in contour.Segments)
            {
                if (segment is PathCubic cubic)
                {
                    points.AddRange(Cubic(from, cubic.Control1, cubic.Control2, cubic.End));
                }
                else
                {
                    points.Add((segment.End.X, segment.End.Y));
                }

                from = segment.End;
            }

            // A contour closes back to its start, unless its last point is its start already (within 10^-6 pt).
            if (!(points.Count > 2 && Distance(points[0], points[^1]) <= 1e-6))
            {
                points.Add(points[0]);
            }

            for (var index = 0; index + 1 < points.Count; index++)
            {
                segments.Add(new Segment(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y));
            }
        }

        return segments;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => double.Hypot(a.X - b.X, a.Y - b.Y);

    /// <summary>The cubic curve p0 p1 p2 p3 as points from p1's side on, p0 not included: n pieces of equal parameter
    /// length, where the distance between the curve and its chord polygon is at most 3/4 M / n², with M the larger norm of
    /// the control polygon's second differences; at most 1 000 pieces.</summary>
    private static IEnumerable<(double X, double Y)> Cubic(Point p0, Point p1, Point p2, Point p3)
    {
        var second = Math.Max(double.Hypot(p0.X - (2 * p1.X) + p2.X, p0.Y - (2 * p1.Y) + p2.Y), double.Hypot(p1.X - (2 * p2.X) + p3.X, p1.Y - (2 * p2.Y) + p3.Y));
        var count = (int)Math.Max(1, Math.Min(1000, Math.Ceiling(Math.Sqrt(0.75 * second / Tolerance))));
        for (var step = 1; step <= count; step++)
        {
            var t = (double)step / count;
            var u = 1 - t;
            var (a, b, c, d) = (u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t);
            yield return ((a * p0.X) + (b * p1.X) + (c * p2.X) + (d * p3.X), (a * p0.Y) + (b * p1.Y) + (c * p2.Y) + (d * p3.Y));
        }
    }

    /// <summary>The stretches of the sample row at y that are inside the path by its fill rule: the crossings of the row
    /// with the segments (a segment counts from its upper end, which is in, to its lower end, which is out), in order of x,
    /// and the winding number after each.</summary>
    private static List<Interval> Inside(List<Segment> segments, double y, bool evenOdd)
    {
        var crossings = new List<(double X, int Direction)>();
        foreach (var (ax, ay, bx, by) in segments)
        {
            if ((ay <= y && y < by) || (by <= y && y < ay))
            {
                crossings.Add((ax + ((y - ay) * (bx - ax) / (by - ay)), by > ay ? 1 : -1));
            }
        }

        crossings.Sort();
        var intervals = new List<Interval>();
        var winding = 0;
        for (var index = 0; index < crossings.Count; index++)
        {
            winding += crossings[index].Direction;
            var inside = evenOdd ? Math.Abs(winding) % 2 == 1 : winding != 0;
            if (inside && index + 1 < crossings.Count)
            {
                intervals.Add(new Interval(crossings[index].X, crossings[index + 1].X));
            }
        }

        return intervals;
    }
}
