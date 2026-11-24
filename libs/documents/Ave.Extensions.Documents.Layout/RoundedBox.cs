using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The background and the border of a box with rounded corners (<see cref="BoxStyle.BorderRadius"/>, CSS Backgrounds 3
/// §5), as Chromium 159 prints them (cases <c>br-*</c>).
/// <para>THE RULE FOR A ROUNDED CORNER: a quarter of an ellipse whose radii are the corner's used radii (§5.5, below),
/// cut into <see cref="PartsPerQuarter"/> parts of equal angle; each part is the quadratic curve from its start to its
/// end on the ellipse, with its control point where the tangents at the two ends cross, written as the cubic curve of the
/// same shape (each cubic control point two thirds of the way from its end to the quadratic's). Chromium's print holds
/// this construction with 2 parts at the 0.5 px centre line of a 1 px ring, 4 at 3 and 4 px, 8 at 5.5 to 20 px and 16 at
/// 22 px and more (cases <c>br-bg-*</c>, <c>br-border-*</c>; the count is its own choice of how close a curve must be, and
/// where 8 turns into 16 between 20 and 22 px is not fixed by the cases); more parts paint the same pixels, and the comparison of painted
/// area finds no digit different with 16 parts at every radius measured: 3, 6, 8 and 20 px (<c>br-bg-radii</c>), the
/// clamped 22 px and 12 px and an unclamped 30 px (<c>br-bg-clamp</c>), a circle of 30 px and ellipses of 75 × 20 and 45 × 12 px
/// (<c>br-bg-percent</c>), four different corners (<c>br-bg-corners</c>) and the rings of <c>br-border-1px</c> and
/// <c>br-border-3px</c>. The usual four cubic curves of a circle paint other pixels on the larger ellipses.</para>
/// <para>THE RULE FOR THE USED RADII (§5.5): a corner's radius across is its length plus its percentage of the border
/// box's width, its radius down its length plus its percentage of the height; where the two radii along a side add up
/// to more than the side, every radius of the box is scaled by one factor, the smallest of side ÷ sum over the four
/// sides (<c>br-bg-clamp</c>: 40 px on a box 44 px high is 22 px; 999 px on a box 24 px high is 12 px, a pill; 30 px
/// on a box 62 px wide stays 30 px). The border box is put on whole CSS pixels first, each edge rounded to the nearest,
/// as a cut border is.</para>
/// <para>THE RULE FOR A ROUNDED BORDER: one filled path of two contours under the even-odd rule, the outer rounded border
/// box and inside it the box inset by the border width with each radius less the width, not below 0. Chromium strokes
/// the centre line where every radius is at least the width and fills such a ring otherwise; both paint the same area
/// (<c>br-border-1px</c>, <c>br-border-3px</c>: radii larger than, equal to and smaller than the width). The background
/// under a rounded border is filled to the border's centre line, inset by half the width with each radius less half the
/// width, not below 0, and as a rectangle where every such radius is 0 (the same cases, 1 px and 3 px borders: Chromium
/// fills the background along the line it strokes).</para>
/// <para>A box over a page break has its rounded corners only where its own edges are: a fragment that a break cuts at
/// its top or bottom has square corners there, its cut edge straight along the break (<c>br-break</c>). A rounded box
/// with a border that a break cuts is refused (<see cref="CutBorders"/>). The content is not clipped by the corners: text
/// that reaches into one is drawn (<c>br-text-corner</c>).</para>
/// <para>Bounded work: a rounded rectangle is one contour of at most 4 × <see cref="PartsPerQuarter"/> curves and 4
/// lines (68 segments), a ring two (136), far inside the page model's limit of segments per path.</para>
/// </summary>
internal static class RoundedBox
{
    /// <summary>The parts of equal angle a quarter ellipse is cut into (THE RULE FOR A ROUNDED CORNER).</summary>
    internal const int PartsPerQuarter = 16;

    /// <summary>The used radii of the four corners, across and down, in points, top left, top right, bottom right, bottom
    /// left: a corner that a page break cuts is square, the others resolved and scaled by one factor (THE RULE FOR THE
    /// USED RADII).</summary>
    /// <param name="radius">The corners as specified.</param>
    /// <param name="width">The border box's width.</param>
    /// <param name="height">The border box's height.</param>
    /// <param name="cutTop">True when a page break cuts the top edge.</param>
    /// <param name="cutBottom">True when a page break cuts the bottom edge.</param>
    /// <returns>The four corners' radii.</returns>
    internal static (double X, double Y)[] UsedRadii(BorderRadius radius, double width, double height, bool cutTop, bool cutBottom)
    {
        (double X, double Y) Resolve(CornerRadius corner, bool cut) =>
            cut ? (0, 0) : (corner.Length.Points + corner.Percent / 100 * width, corner.Length.Points + corner.Percent / 100 * height);
        (double X, double Y)[] corners =
        [
            Resolve(radius.TopLeft, cutTop), Resolve(radius.TopRight, cutTop), Resolve(radius.BottomRight, cutBottom), Resolve(radius.BottomLeft, cutBottom),
        ];
        static double Share(double side, double sum) => sum > side ? side / sum : 1;
        var factor = Math.Min(
            Math.Min(Share(width, corners[0].X + corners[1].X), Share(width, corners[3].X + corners[2].X)),
            Math.Min(Share(height, corners[0].Y + corners[3].Y), Share(height, corners[1].Y + corners[2].Y)));
        for (var index = 0; index < corners.Length; index++)
        {
            corners[index] = (corners[index].X * factor, corners[index].Y * factor);
        }

        return corners;
    }

    /// <summary>The background of a rounded box: to the border box, or with a border to the border's centre line (THE
    /// RULE FOR A ROUNDED BORDER).</summary>
    /// <param name="box">The fragment.</param>
    /// <param name="origin">Its top left on the page.</param>
    /// <param name="color">The background colour.</param>
    /// <returns>The fill.</returns>
    internal static DrawItem Background(BoxFragment box, Point origin, Color color)
    {
        var (area, radii) = Snapped(box, origin);
        var inset = box.Box.Style.Border.Top.IsVisible ? box.Box.Style.Border.Top.Width.Points / 2 : 0;
        if (inset > 0)
        {
            (area, radii) = Inset(area, radii, inset);
            if (Array.TrueForAll(radii, corner => corner.X == 0 || corner.Y == 0))
            {
                return new FillRectangle(area, color);
            }
        }

        return new FillPath([Contour(area, radii)], PathFillRule.NonZero, color);
    }

    /// <summary>The border of a rounded box: one ring of two contours, filled under the even-odd rule (THE RULE FOR A
    /// ROUNDED BORDER); nothing without a border.</summary>
    /// <param name="box">The fragment.</param>
    /// <param name="origin">Its top left on the page.</param>
    /// <returns>The fill, or nothing.</returns>
    internal static IEnumerable<DrawItem> Border(BoxFragment box, Point origin)
    {
        var side = box.Border.Top;
        if (!side.IsVisible || !box.Border.Bottom.IsVisible)
        {
            yield break;
        }

        var (area, radii) = Snapped(box, origin);
        var (inner, innerRadii) = Inset(area, radii, side.Width.Points);
        yield return new FillPath([Contour(area, radii), Contour(inner, innerRadii)], PathFillRule.EvenOdd, side.Color);
    }

    /// <summary>The first fragment of a rounded box with a border that a page break cuts, as an error
    /// (<see cref="LayoutErrorCodes.RoundedBorderBreak"/>), or null. Chromium 159 draws such a border's sides as rectangles
    /// under clipping paths, which the page model has not. One walk over every page's body and areas with an explicit
    /// stack, linear in the number of fragments.</summary>
    /// <param name="pages">The fragment tree.</param>
    /// <returns>The error, or null.</returns>
    internal static Error? CutBorders(ValueList<PageFragment> pages)
    {
        var pending = new Stack<Fragment>();
        foreach (var page in pages)
        {
            foreach (var area in new[] { page.Header, page.Root, page.Footer })
            {
                if (area is not null)
                {
                    pending.Push(area.Fragment);
                }
            }
        }

        while (pending.Count > 0)
        {
            var fragment = pending.Pop();
            if (fragment is BoxFragment { Box.Style.BorderRadius.IsZero: false, Box.Style.Border.Top.IsVisible: true } cut && (cut.ContinuesBefore || cut.ContinuesAfter))
            {
                return LayoutErrors.RoundedBorderBreak(cut.Box);
            }

            var children = fragment switch
            {
                BoxFragment box => box.Children,
                LineFragment line => line.Children,
                SliceFragment slice => [slice.Content],
                _ => ValueList<PlacedFragment>.Empty,
            };
            foreach (var child in children)
            {
                pending.Push(child.Fragment);
            }
        }

        return null;
    }

    /// <summary>The border box on whole CSS pixels of the page and its used radii.</summary>
    private static (Rect Area, (double X, double Y)[] Radii) Snapped(BoxFragment box, Point origin)
    {
        var (x0, y0) = (CssPixels.Round(origin.X), CssPixels.Round(origin.Y));
        var (x1, y1) = (CssPixels.Round(origin.X + box.Size.Width), CssPixels.Round(origin.Y + box.Size.Height));
        var area = new Rect(new Point(x0, y0), new Size(x1 - x0, y1 - y0));
        return (area, UsedRadii(box.Box.Style.BorderRadius, area.Size.Width, area.Size.Height, box.ContinuesBefore, box.ContinuesAfter));
    }

    /// <summary>The rectangle inset by a distance on every side, each radius less the distance, not below 0.</summary>
    private static (Rect Area, (double X, double Y)[] Radii) Inset(Rect area, (double X, double Y)[] radii, double by)
    {
        var inner = new Rect(new Point(area.Origin.X + by, area.Origin.Y + by), new Size(Math.Max(0, area.Size.Width - 2 * by), Math.Max(0, area.Size.Height - 2 * by)));
        return (inner, Array.ConvertAll(radii, corner => (Math.Max(0, corner.X - by), Math.Max(0, corner.Y - by))));
    }

    /// <summary>The rounded rectangle as one closed contour, clockwise on the page from the top edge's start: each side
    /// a line, each corner with both radii above 0 a quarter ellipse (THE RULE FOR A ROUNDED CORNER).</summary>
    private static PathContour Contour(Rect area, (double X, double Y)[] radii)
    {
        var (x0, y0) = (area.Origin.X, area.Origin.Y);
        var (x1, y1) = (x0 + area.Size.Width, y0 + area.Size.Height);
        (double X, double Y) Corner(int index) => radii[index].X > 0 && radii[index].Y > 0 ? radii[index] : (0, 0);
        var (topLeft, topRight, bottomRight, bottomLeft) = (Corner(0), Corner(1), Corner(2), Corner(3));
        var segments = new List<PathSegment>(4 * PartsPerQuarter + 4);
        segments.Add(new PathLine(new Point(x1 - topRight.X, y0)));
        Quarter(segments, x1 - topRight.X, y0 + topRight.Y, topRight, -90);
        segments.Add(new PathLine(new Point(x1, y1 - bottomRight.Y)));
        Quarter(segments, x1 - bottomRight.X, y1 - bottomRight.Y, bottomRight, 0);
        segments.Add(new PathLine(new Point(x0 + bottomLeft.X, y1)));
        Quarter(segments, x0 + bottomLeft.X, y1 - bottomLeft.Y, bottomLeft, 90);
        segments.Add(new PathLine(new Point(x0, y0 + topLeft.Y)));
        Quarter(segments, x0 + topLeft.X, y0 + topLeft.Y, topLeft, 180);
        return new PathContour(new Point(x0 + topLeft.X, y0), [.. segments]);
    }

    /// <summary>A quarter ellipse about a centre from an angle (degrees, clockwise on the page from the positive x axis)
    /// through 90 degrees, as <see cref="PartsPerQuarter"/> quadratic parts of equal angle written as cubics; nothing for
    /// a square corner.</summary>
    private static void Quarter(List<PathSegment> segments, double cx, double cy, (double X, double Y) radius, double from)
    {
        if (radius.X == 0)
        {
            return;
        }

        var step = Math.PI / 2 / PartsPerQuarter;
        var reach = 1 / Math.Cos(step / 2);
        Point At(double angle, double scale) => new(cx + radius.X * scale * Math.Cos(angle), cy + radius.Y * scale * Math.Sin(angle));
        var start = from * Math.PI / 180;
        var previous = At(start, 1);
        for (var part = 1; part <= PartsPerQuarter; part++)
        {
            var end = At(start + part * step, 1);
            var control = At(start + (part - 0.5) * step, reach);
            segments.Add(new PathCubic(
                new Point(previous.X + 2.0 / 3 * (control.X - previous.X), previous.Y + 2.0 / 3 * (control.Y - previous.Y)),
                new Point(end.X + 2.0 / 3 * (control.X - end.X), end.Y + 2.0 / 3 * (control.Y - end.Y)),
                end));
            previous = end;
        }
    }
}
