using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// A border with dashed sides (<see cref="BorderStyle.Dashed"/>, CSS Backgrounds 3 §3.2) on a block box, as Chromium 159
/// prints it (cases <c>bd-*</c>).
/// <para>THE RULE FOR A DASHED SIDE: a side of width w (in CSS pixels) has dashes of d = 3w and an ideal gap of g = 2w below
/// 3 px, and d = 2w and g = w from 3 px; the side runs along the whole edge of the border box, over both corners, so it is
/// L long, the border box's width or height. Of m = ⌊(L + g) / (d + g)⌋ dashes and m + 1 dashes the count is the one whose
/// gap (L − n·d) / (n − 1) lies nearer g, m + 1 on a tie; the first dash starts at the side's start and the last ends at its
/// end, every gap the same. Measured on 47 sides: widths of 1, 2, 3 and 4 px, lengths from 11 to 368 px (cases
/// <c>bd-lengths-thin</c>, <c>bd-lengths-thick</c>: 1 px and 232 px is 47 dashes with gaps of 1.978 px, 2 px and 231 px 24
/// dashes, where the gap of 23 dashes, 4.227 px, lies 0.010 px further from 4 than that of 24; <c>bd-sides</c>: 2 px and
/// 30 px is the tie, 4 dashes and gaps of 2 px, where 3 dashes would leave gaps of 6 px; <c>bd-widths</c>,
/// <c>bd-fraction</c>, <c>bd-break</c>). The dashes are not on whole pixels (1 px and 151 px: a dash every 4.933 px).</para>
/// <para>Each dashed side is one filled path of one rectangle per dash, the sides in the order top, bottom, right, left, a
/// solid side beside them one filled rectangle along its whole edge after them, as <see cref="PagePainter"/> paints cut
/// borders (<c>bd-sides</c>: Chromium draws such a solid side under a clipping path that leaves its half of the corner,
/// which a side of the same colour covers anyway). The border box is on whole CSS pixels of the page, each edge rounded to
/// the nearest (<c>bd-fraction</c>: a box from 16.53 px to 118.83 px is dashed from 17 px to 119 px); the background
/// under it fills the whole border box, the gaps too (<c>bd-sides</c>). A box over a page break dashes the sides of each of
/// its parts along that part's own length (<c>bd-break</c>: 340 px on the first page, 89 px on the next).</para>
/// <para>A side of length 0 is not drawn (<c>bd-lengths-*</c>). A side shorter than two dashes and a gap is refused
/// (<see cref="LayoutErrorCodes.DashedBorder"/>): Chromium draws a side shorter than two dashes as one solid line (a stroke,
/// measured at 2 and 5 px for 1 px, 9 px for 2 px, 10 px for 3 px and 13 px for 4 px), and between two dashes and two dashes
/// and a gap it was not measured.</para>
/// <para>Bounded work: a dash is a contour of three segments, so a side holds at most <see cref="MaxDashes"/> dashes, the
/// page model's limit of segments per path over three; a side with more is refused. A 1 px dashed border around the page
/// area of an A4 page (about 718 × 1,047 px) is 144 dashes along the top and the bottom and 210 down each side, 708 in all.
/// The work is linear in the number of dashes.</para>
/// </summary>
internal static class DashedBorder
{
    /// <summary>The most dashes on one side: one path of at most <see cref="PageModelRules.MaxPathSegments"/> segments,
    /// three a dash.</summary>
    internal const int MaxDashes = PageModelRules.MaxPathSegments / 3;

    /// <summary>The four sides in the order they are painted.</summary>
    private enum Edge { Top, Bottom, Right, Left }

    /// <summary>True when any visible side of the border is dashed.</summary>
    /// <param name="border">The border.</param>
    /// <returns>True for a dashed border.</returns>
    internal static bool IsDashed(Border border) =>
        Array.Exists([border.Top, border.Right, border.Bottom, border.Left], side => side.IsVisible && side.Style == BorderStyle.Dashed);

    /// <summary>The dash and the ideal gap of a side of a width, in CSS pixels (THE RULE FOR A DASHED SIDE).</summary>
    private static (double Dash, double Gap) Pattern(double width)
    {
        var w = width / CssPixels.Points;
        return w < 3 ? (3 * w, 2 * w) : (2 * w, w);
    }

    /// <summary>The dashes of a side of a length and a width (THE RULE FOR A DASHED SIDE): their count, length and the gap
    /// between them, in points; a count of 0 for a side of length 0, and null for a side too short or too long to dash.</summary>
    /// <param name="length">The side's length, in points.</param>
    /// <param name="width">The side's width, in points.</param>
    /// <returns>The count, the dash and the gap, or null.</returns>
    internal static (int Count, double Dash, double Gap)? Dashes(double length, double width)
    {
        var (dash, ideal) = Pattern(width);
        var l = length / CssPixels.Points;
        if (l <= 1e-9)
        {
            return (0, 0, 0);
        }

        if (l < (2 * dash) + ideal - 1e-9)
        {
            return null;
        }

        var m = (int)Math.Min(Math.Floor(((l + ideal) / (dash + ideal)) + 1e-9), MaxDashes + 1);
        double Gap(int count) => (l - (count * dash)) / (count - 1);
        var count = m + 1 <= MaxDashes + 1 && Gap(m + 1) >= 0 && Math.Abs(Gap(m + 1) - ideal) <= Math.Abs(Gap(m) - ideal) + 1e-9 ? m + 1 : m;
        return count > MaxDashes ? null : (count, dash * CssPixels.Points, Gap(count) * CssPixels.Points);
    }

    /// <summary>The border of a box with dashed sides, in its border box on the page (THE RULE FOR A DASHED SIDE): the dashed
    /// sides, one path each, then the solid ones, one rectangle each, every side along its whole edge.</summary>
    /// <param name="border">The border; its visible sides of one width and one colour, solid or dashed (the model's check).</param>
    /// <param name="box">The border box on the page.</param>
    /// <param name="area">The top left corner of the page area the box is in, on the page.</param>
    /// <returns>The fills.</returns>
    internal static IEnumerable<DrawItem> Paint(Border border, Rect box, Point area)
    {
        var (x0, y0) = (CssPixels.Round(box.Origin.X), CssPixels.Round(box.Origin.Y));
        var (x1, y1) = (CssPixels.Round(box.Origin.X + box.Size.Width), CssPixels.Round(box.Origin.Y + box.Size.Height));
        BorderSide SideOf(Edge edge) => edge switch
        {
            Edge.Top => border.Top,
            Edge.Bottom => border.Bottom,
            Edge.Right => border.Right,
            _ => border.Left,
        };
        Rect Along(Edge edge, double from, double length, double width) => edge switch
        {
            Edge.Top => new Rect(new Point(x0 + from, y0), new Size(length, width)),
            Edge.Bottom => new Rect(new Point(x0 + from, y1 - width), new Size(length, width)),
            Edge.Right => new Rect(new Point(x1 - width, y0 + from), new Size(width, length)),
            _ => new Rect(new Point(x0, y0 + from), new Size(width, length)),
        };

        // Chromium writes each dash's ends in single precision, in CSS pixels from the page area's corner: the side's start
        // as a float plus the dash's distance along the side as a float, the sum rounded to a float again (bd-widths: the
        // seventh dash of a 1 px side of 102 px starts at 29.700001 px, the fourth at 14.8500004 px; the fifth of a side of
        // 32 px from 28 px at 47.333336 px), and a dash's edge can lie on a sample of the comparison; so do the dashes here.
        IEnumerable<(double From, double To)> Places(Edge edge, (int Count, double Dash, double Gap) dashes)
        {
            var (start, origin) = edge is Edge.Top or Edge.Bottom ? (x0, area.X) : (y0, area.Y);
            var at = (start - origin) / CssPixels.Points;
            var (dash, step) = (dashes.Dash / CssPixels.Points, (dashes.Dash + dashes.Gap) / CssPixels.Points);
            double OnPage(double along) => ((double)(float)((float)at + (float)along) * CssPixels.Points) + origin - start;
            for (var index = 0; index < dashes.Count; index++)
            {
                yield return (OnPage(index * step), OnPage((index * step) + dash));
            }
        }

        Edge[] order = [Edge.Top, Edge.Bottom, Edge.Right, Edge.Left];
        foreach (var dashed in new[] { true, false })
        {
            foreach (var edge in order)
            {
                var side = SideOf(edge);
                if (!side.IsVisible || (side.Style == BorderStyle.Dashed) != dashed)
                {
                    continue;
                }

                var length = edge is Edge.Top or Edge.Bottom ? x1 - x0 : y1 - y0;
                if (!dashed)
                {
                    yield return new FillRectangle(Along(edge, 0, length, side.Width.Points), side.Color);
                    continue;
                }

                // Too short or too long: refused before painting (Check); a side of length 0 draws nothing.
                if (Dashes(length, side.Width.Points) is not { Count: > 0 } dashes)
                {
                    continue;
                }

                var contours = new List<PathContour>(dashes.Count);
                foreach (var (from, to) in Places(edge, dashes))
                {
                    var place = Along(edge, from, to - from, side.Width.Points);
                    var (left, top) = (place.Origin.X, place.Origin.Y);
                    var (right, bottom) = (left + place.Size.Width, top + place.Size.Height);
                    contours.Add(new PathContour(place.Origin, [new PathLine(new Point(right, top)), new PathLine(new Point(right, bottom)), new PathLine(new Point(left, bottom))]));
                }

                yield return new FillPath([.. contours], PathFillRule.NonZero, side.Color);
            }
        }
    }

    /// <summary>The first box with a dashed side too short or too long to dash where it lies on its page, as an error
    /// (<see cref="LayoutErrorCodes.DashedBorder"/>), or null. One walk over every page's body and areas with an explicit
    /// stack, linear in the number of fragments; each side measured on the border box put on whole pixels of the page, as
    /// it is painted.</summary>
    /// <param name="pages">The fragment tree.</param>
    /// <returns>The error, or null.</returns>
    internal static Error? Check(ValueList<PageFragment> pages)
    {
        var pending = new Stack<(Fragment Fragment, Point Origin)>();
        foreach (var page in pages)
        {
            foreach (var area in new[] { page.Header, page.Root, page.Footer })
            {
                if (area is not null)
                {
                    pending.Push((area.Fragment, area.Offset));
                }
            }
        }

        while (pending.Count > 0)
        {
            var (fragment, origin) = pending.Pop();
            if (fragment is BoxFragment box && IsDashed(box.Border))
            {
                var width = CssPixels.Round(origin.X + box.Size.Width) - CssPixels.Round(origin.X);
                var height = CssPixels.Round(origin.Y + box.Size.Height) - CssPixels.Round(origin.Y);
                foreach (var (side, length) in new[] { (box.Border.Top, width), (box.Border.Bottom, width), (box.Border.Right, height), (box.Border.Left, height) })
                {
                    if (side.IsVisible && side.Style == BorderStyle.Dashed && Dashes(length, side.Width.Points) is null)
                    {
                        var (dash, gap) = Pattern(side.Width.Points);
                        return LayoutErrors.DashedBorder(box.Box, length / CssPixels.Points, side.Width.Points / CssPixels.Points, length / CssPixels.Points >= (2 * dash) + gap);
                    }
                }
            }

            var children = fragment switch
            {
                BoxFragment parent => parent.Children,
                LineFragment line => line.Children,
                SliceFragment slice => [slice.Content],
                _ => ValueList<PlacedFragment>.Empty,
            };
            foreach (var child in children)
            {
                pending.Push((child.Fragment, origin + child.Offset));
            }
        }

        return null;
    }
}
