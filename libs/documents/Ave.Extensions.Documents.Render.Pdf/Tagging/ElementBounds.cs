using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;

namespace Ave.Extensions.Documents.Render.Pdf.Tagging;

/// <summary>
/// The bounding boxes of the structure elements that need one. ISO 32000-1 §14.8.5.4.3, Table 344, <c>BBox</c>:
/// "(Optional for Annot; required for any figure or table appearing in its entirety on a single page; not
/// inheritable)" —
/// "the element's bounding box (the rectangle that completely encloses its visible content)", in default user space.
/// Of the illustration types (Figure, Formula and Form, §14.8.4.5, Table 340) the semantic roles give only Figure.
/// </summary>
internal static class ElementBounds
{
    /// <summary>The structure types that carry a box: the figure and the table, the types the roles of
    /// <see cref="SemanticRole"/> map to that Table 344 names.</summary>
    private static readonly FrozenSet<string> BoxedTypes = FrozenSet.Create("Figure", "Table");

    /// <summary>
    /// Per element (by index), its box when it is an illustration or a table whose visible content is all on one page;
    /// null otherwise — for other types, for an element with content on two pages, and for one with no visible content.
    /// Visible content is everything drawn between the element's markers on the page, including layout artifacts such
    /// as a table's grid lines, cell backgrounds and the element's own background and border (the painter draws those
    /// inside the element's markers): they are not the element's logical content, but they are what a reader sees of it.
    /// Only what lies inside the clips around it counts (<see cref="ContentRun.Clips"/>).
    /// </summary>
    /// <param name="tree">The structure tree built from the pages' events.</param>
    /// <param name="events">The events of every page.</param>
    /// <param name="pages">The pages, for their heights (user space has its origin at the bottom-left).</param>
    /// <returns>One entry per element of <paramref name="tree"/>.</returns>
    public static ImmutableArray<UserSpaceBox?> Of(StructureTree tree, IReadOnlyList<ImmutableArray<PageEvent>> events, IReadOnlyList<Page> pages)
    {
        var indexOfNode = tree.Elements.Select((element, index) => (element.Start.Node, index)).ToDictionary(entry => entry.Node, entry => entry.index);
        var boxed = tree.Elements.Select((element, index) => (element, index))
            .Where(entry => BoxedTypes.Contains(entry.element.Type.Value))
            .Select(entry => entry.index)
            .ToHashSet();
        var ink = new Dictionary<int, (int Page, Extent Extent)>();
        var onSeveralPages = new HashSet<int>();

        for (var page = 0; page < events.Count; page++)
        {
            var open = new Stack<int>();
            foreach (var pageEvent in events[page])
            {
                switch (pageEvent)
                {
                    case StructureOpened opened:
                        open.Push(indexOfNode[opened.Start.Node]);
                        break;
                    case StructureClosed:
                        open.Pop();
                        break;
                    case ContentRun run when Clipped(InkOf(run.Items), run.Clips) is { } extent:
                        foreach (var element in open.Where(boxed.Contains))
                        {
                            if (!ink.TryGetValue(element, out var seen))
                            {
                                ink[element] = (page, extent);
                            }
                            else if (seen.Page != page)
                            {
                                onSeveralPages.Add(element);
                            }
                            else
                            {
                                ink[element] = (page, seen.Extent.Union(extent));
                            }
                        }

                        break;
                }
            }
        }

        return [.. tree.Elements.Select((_, index) =>
            !onSeveralPages.Contains(index) && ink.TryGetValue(index, out var found)
                ? found.Extent.InUserSpace(pages[found.Page].Size.Height)
                : (UserSpaceBox?)null)];
    }

    /// <summary>The part of an extent inside every clip (<see cref="ContentRun.Clips"/>): what a reader sees of it;
    /// null when nothing of it is left.</summary>
    private static Extent? Clipped(Extent? ink, ImmutableArray<Rect> clips) =>
        clips.Aggregate(ink, (left, clip) => left?.Intersect(Extent.Of(clip)));

    /// <summary>The extent of the ink of some items, or null when they put none on the page.</summary>
    private static Extent? InkOf(IEnumerable<DrawItem> items) => Union(items.Select(InkOf));

    /// <summary>
    /// The extent of one item: the rectangle of an image or a fill, the box of a filled path; for glyphs, the union of each glyph's outline box,
    /// placed where the content stream draws the glyph.
    /// </summary>
    private static Extent? InkOf(DrawItem item) => item switch
    {
        DrawImage image => Extent.Of(image.Area),
        FillRectangle fill => Extent.Of(fill.Area),
        FillPath path => PathExtent(path),
        GlyphRun run => Union(run.Glyphs.Select(glyph => InkOf(run, glyph))),
        _ => throw new System.InvalidOperationException($"No extent for the draw item {item.GetType().Name}."),
    };

    /// <summary>
    /// The extent of a filled path: the smallest rectangle that holds every contour, its curves included. A cubic curve
    /// lies inside the box of its ends and of the points where its derivative is zero in x or in y (ISO 32000-1 §8.5.2.2
    /// gives the curve as a polynomial of degree 3 in t; its derivative is a quadratic, solved for t in (0, 1) per axis).
    /// </summary>
    private static Extent PathExtent(FillPath path)
    {
        var (left, top, right, bottom) = (double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        void Add(Point point) => (left, top, right, bottom) = (System.Math.Min(left, point.X), System.Math.Min(top, point.Y), System.Math.Max(right, point.X), System.Math.Max(bottom, point.Y));
        foreach (var contour in path.Contours)
        {
            Add(contour.Start);
            var from = contour.Start;
            foreach (var segment in contour.Segments)
            {
                if (segment is PathCubic cubic)
                {
                    foreach (var t in Extrema(from.X, cubic.Control1.X, cubic.Control2.X, cubic.End.X).Concat(Extrema(from.Y, cubic.Control1.Y, cubic.Control2.Y, cubic.End.Y)))
                    {
                        Add(At(from, cubic, t));
                    }
                }

                Add(segment.End);
                from = segment.End;
            }
        }

        return new Extent(left, top, right, bottom);
    }

    /// <summary>The t in (0, 1) where one coordinate of the cubic curve p0 p1 p2 p3 has a zero derivative: the roots of
    /// a t² + b t + c with a = 3(−p0 + 3p1 − 3p2 + p3), b = 6(p0 − 2p1 + p2), c = 3(p1 − p0).</summary>
    private static IEnumerable<double> Extrema(double p0, double p1, double p2, double p3)
    {
        var (a, b, c) = (3 * (-p0 + (3 * p1) - (3 * p2) + p3), 6 * (p0 - (2 * p1) + p2), 3 * (p1 - p0));
        double[] roots = a == 0
            ? b == 0 ? [] : [-c / b]
            : (b * b) - (4 * a * c) is var discriminant && discriminant >= 0
                ? [(-b + System.Math.Sqrt(discriminant)) / (2 * a), (-b - System.Math.Sqrt(discriminant)) / (2 * a)]
                : [];
        return roots.Where(t => t is > 0 and < 1);
    }

    /// <summary>The point of a cubic curve from <paramref name="from"/> at t (§8.5.2.2).</summary>
    private static Point At(Point from, PathCubic cubic, double t)
    {
        var u = 1 - t;
        var (a, b, c, d) = (u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t);
        return new Point(
            (a * from.X) + (b * cubic.Control1.X) + (c * cubic.Control2.X) + (d * cubic.End.X),
            (a * from.Y) + (b * cubic.Control1.Y) + (c * cubic.Control2.Y) + (d * cubic.End.Y));
    }

    /// <summary>
    /// The box of a glyph's outline (<see cref="Fonts.TrueTypeFont.GlyphBounds"/>) on the page: the font's y grows up
    /// from the baseline, the page's down, so the top is the baseline minus yMax. Null for a glyph without an outline.
    /// </summary>
    private static Extent? InkOf(GlyphRun run, PositionedGlyph glyph)
    {
        var outline = run.Font.GlyphBounds(glyph.Glyph);
        if (outline.HasNoValue)
        {
            return null;
        }

        var bounds = outline.Value;
        var scale = run.FontSize / run.Font.Metrics.UnitsPerEm;
        var x = run.Origin.X + glyph.X;
        var baseline = run.Origin.Y + glyph.Y;
        return new Extent(x + bounds.XMin * scale, baseline - bounds.YMax * scale, x + bounds.XMax * scale, baseline - bounds.YMin * scale);
    }

    /// <summary>The union of some extents, skipping the nulls; null when all are.</summary>
    private static Extent? Union(IEnumerable<Extent?> extents) =>
        extents.Aggregate((Extent?)null, (all, next) => all is { } a && next is { } b ? a.Union(b) : all ?? next);

    /// <summary>A rectangle in page coordinates (origin top-left, y down) by its edges.</summary>
    private readonly record struct Extent(double Left, double Top, double Right, double Bottom)
    {
        public static Extent Of(Rect area) =>
            new(area.Origin.X, area.Origin.Y, area.Origin.X + area.Size.Width, area.Origin.Y + area.Size.Height);

        public Extent Union(Extent other) => new(
            System.Math.Min(Left, other.Left),
            System.Math.Min(Top, other.Top),
            System.Math.Max(Right, other.Right),
            System.Math.Max(Bottom, other.Bottom));

        /// <summary>The part of this rectangle inside another one, or null when they do not overlap.</summary>
        public Extent? Intersect(Extent other) =>
            new Extent(System.Math.Max(Left, other.Left), System.Math.Max(Top, other.Top), System.Math.Min(Right, other.Right), System.Math.Min(Bottom, other.Bottom))
                is var part && part.Left < part.Right && part.Top < part.Bottom ? part : null;

        /// <summary>The same rectangle in default user space, where y grows upwards from the page's bottom edge.</summary>
        public UserSpaceBox InUserSpace(double pageHeight) => new(Left, pageHeight - Bottom, Right, pageHeight - Top);
    }
}

/// <summary>A rectangle in default user space (ISO 32000-1 §8.3.2.3): origin at the bottom-left of the page, y up.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Bottom">The bottom edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Top">The top edge.</param>
internal readonly record struct UserSpaceBox(double Left, double Bottom, double Right, double Top);
