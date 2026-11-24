using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.PageModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Turns the fragment tree into the page model: absolute positions, and the display list in painting order.
/// </summary>
public static class PagePainter
{
    /// <summary>Paints every page.</summary>
    /// <param name="metadata">The document's metadata, passed on unchanged.</param>
    /// <param name="pages">The fragment tree, one entry per page.</param>
    /// <returns>The page document.</returns>
    public static PageDocument Paint(DocumentMetadata metadata, ValueList<PageFragment> pages)
    {
        var anchors = new Anchors(Targets(pages));
        return new(metadata, [.. pages.Select(page => PaintPage(page, anchors))]);
    }

    /// <summary>The nodes the links inside the document lead to (<see cref="LinkTarget.Node"/>): the targets of the link
    /// groups on every line of every page, read with an explicit stack.</summary>
    private static HashSet<NodeId> Targets(ValueList<PageFragment> pages)
    {
        var targets = new HashSet<NodeId>();
        var pending = new Stack<Fragment>();
        foreach (var page in pages)
        {
            pending.Push(page.Root.Fragment);
        }

        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case BoxFragment box:
                    foreach (var child in box.Children)
                    {
                        pending.Push(child.Fragment);
                    }

                    break;
                case LineFragment line:
                    foreach (var part in line.Groups)
                    {
                        if (part.Group.Role is SemanticRole.LinkRole { Target: LinkTarget.NodeTarget target })
                        {
                            targets.Add(target.Value);
                        }
                    }

                    foreach (var child in line.Children)
                    {
                        pending.Push(child.Fragment);
                    }

                    break;
                case SliceFragment slice:
                    pending.Push(slice.Content.Fragment);
                    break;
            }
        }

        return targets;
    }

    /// <summary>
    /// THE RULE FOR A LINK TARGET'S PLACE: the anchor (<see cref="Anchor"/>) of a node a link leads to is the top left of
    /// the node's first fragment outside every artifact, in document order: for a box its border box on the first page it
    /// is on, for an inline group its first line part (the top of that part's area: the ascent of the group's own font
    /// above the baseline). Each target gets one anchor, the first time the painter meets it.
    /// </summary>
    private sealed class Anchors(HashSet<NodeId> pending)
    {
        public IEnumerable<DrawItem> At(NodeId node, Point at) => pending.Remove(node) ? [new Anchor(node, at)] : [];
    }

    /// <summary>
    /// A page: header area, body, footer area. The areas repeat on every page; in tagged PDF they are pagination
    /// artifacts (ISO 32000-1 §14.8.2.2.2: "running heads and folios"), so they are painted as artifacts, without
    /// structure markers. The body is drawn under a clip to its area where the page has one
    /// (<see cref="PageFragment.BodyClip"/>, <see cref="BodyClip.BodyArea"/>); the header and footer areas never are.
    /// </summary>
    private static Page PaintPage(PageFragment page, Anchors anchors) =>
        new(page.PageSize,
        [
            .. page.Header is { } header ? Artifact(ArtifactKind.PageHeader, Items(header.Fragment, header.Offset, inArtifact: true, new Sheet(page.PageSize.Width, null), anchors)) : [],
            .. page.BodyClip is { } body
                ? Clipped(body, Items(page.Root.Fragment, page.Root.Offset, inArtifact: false, new Sheet(page.PageSize.Width, body), anchors).Select(item => item is LinkArea link ? link with { Area = Inside(link.Area, body) } : item))
                : Items(page.Root.Fragment, page.Root.Offset, inArtifact: false, new Sheet(page.PageSize.Width, null), anchors),
            .. page.Footer is { } footer ? Artifact(ArtifactKind.PageFooter, Items(footer.Fragment, footer.Offset, inArtifact: true, new Sheet(page.PageSize.Width, null), anchors)) : [],
        ]);

    /// <summary>
    /// Walks the tree depth-first, in document order. For each box: its background and border, then a structure
    /// start, its content, a structure end. CSS 2.1 Appendix E.2 paints all block backgrounds and borders of a stacking
    /// context (step 4) before all inline content (step 7). For normal flow without overlapping boxes — all this engine
    /// can produce, since it has no negative margins, floats or positioning — painting in document order gives the
    /// same picture. A table's grid lines (<see cref="RuleFragment"/>) come after its cells in the fragment tree, so
    /// they are painted over the cell backgrounds, as the collapsing border model asks (CSS 2.1 §17.5.1 table layers,
    /// then §17.6.2 borders).
    /// </summary>
    /// <remarks>
    /// What is content and what is decoration (ISO 32000-1 §14.8.2.2.1: artifacts "are not part of the author's original
    /// content but rather are generated by the conforming writer in the course of pagination, layout, or other strictly
    /// mechanical processes"):
    /// backgrounds, borders and grid lines are layout artifacts; a box with the <see cref="SemanticRole.Decorative"/>
    /// role is a layout artifact as a whole; repeated content (<see cref="BoxFragment.IsRepeated"/>, a table's repeated
    /// header rows) is a pagination artifact, since the document structure has it once. Everything else is content of
    /// the innermost structure marker around it. <paramref name="inArtifact"/> is true inside an artifact, where no
    /// further markers are written: artifacts do not nest and hold no structure. The slices of content taller than a page
    /// (<see cref="SliceFragment"/>) are clipped, each to its own top and bottom across the whole width of the page
    /// (<c>sheet.Width</c>). The slices of an image are all content of its figure, as the pages of a paragraph
    /// are content of the paragraph: each page shows another part of the image, and its alternative text is on the
    /// figure, read once. Every slice of a line of text after the first is an artifact
    /// (<see cref="ArtifactKind.SliceContinuation"/>, a stated choice): the first slice delivers the line's text, and the
    /// later ones draw the same glyphs again.
    /// </remarks>
    private static IEnumerable<DrawItem> Items(Fragment fragment, Point origin, bool inArtifact, Sheet sheet, Anchors anchors) => fragment switch
    {
        BoxFragment box => Box(box, origin, inArtifact, sheet, anchors),
        LineFragment line => Line(line, origin, inArtifact, sheet, anchors),
        TextFragment text => Glyphs(text, origin) is { Glyphs.Count: > 0 } run ? [run] : [],
        ImageFragment image => [new DrawImage(new Rect(origin, image.Size), image.Image.Image)],
        MarkerFragment marker => Bullet(marker, origin),
        RuleFragment rule => Decoration([new FillRectangle(new Rect(origin, rule.Size), rule.Color)], inArtifact),
        SliceFragment slice => Clipped(new Rect(new Point(0, origin.Y), new Size(sheet.Width, slice.Size.Height)),
            slice.IsContinuation && !inArtifact && slice.Content.Fragment is LineFragment
                ? Artifact(ArtifactKind.SliceContinuation, Items(slice.Content.Fragment, origin + slice.Content.Offset, inArtifact: true, sheet, anchors))
                : Items(slice.Content.Fragment, origin + slice.Content.Offset, inArtifact, sheet, anchors)),
        _ => throw new InvalidOperationException($"Unknown fragment type {fragment.GetType().Name}."),
    };

    /// <summary>The page being painted: its width, for the clips of slices, and the rectangle its body is clipped to, or
    /// null for a page area or a page whose body is not clipped.</summary>
    private sealed record Sheet(double Width, Rect? BodyClip);

    private static bool Overlaps(Rect a, Rect b) =>
        a.Origin.X < b.Origin.X + b.Size.Width && b.Origin.X < a.Origin.X + a.Size.Width
        && a.Origin.Y < b.Origin.Y + b.Size.Height && b.Origin.Y < a.Origin.Y + a.Size.Height;

    /// <summary>
    /// A list item's bullet (<see cref="MarkerFragment"/>, Chromium 159's shapes, case <c>li-symbol-sizes</c>): a disc is the
    /// circle inscribed in its box, four cubic curves, filled; a circle a ring one CSS pixel wide centred on that circle, two
    /// circles filled under the even-odd rule (Chromium strokes the circle with a line one pixel wide; the ring paints the
    /// same area); a square its box, filled. A bullet of no size draws nothing.
    /// </summary>
    private static IEnumerable<DrawItem> Bullet(MarkerFragment marker, Point origin)
    {
        var size = marker.Size;
        if (size.Width <= 0)
        {
            return [];
        }

        var color = marker.Symbol.Style.Color;
        var (cx, cy, r) = (origin.X + size.Width / 2, origin.Y + size.Height / 2, size.Width / 2);
        const double halfPixel = 0.375;
        return marker.Symbol.Shape switch
        {
            MarkerShape.Disc => [new FillPath([Circle(cx, cy, r)], PathFillRule.NonZero, color)],
            MarkerShape.Circle => [new FillPath([Circle(cx, cy, r + halfPixel), Circle(cx, cy, r - halfPixel)], PathFillRule.EvenOdd, color)],
            _ => [new FillRectangle(new Rect(origin, size), color)],
        };
    }

    /// <summary>A circle as four cubic curves, each a quarter, with the control points at κ = 0.5522847498 times the
    /// radius along the tangents (the usual approximation, radial error below 0.03 %).</summary>
    private static PathContour Circle(double cx, double cy, double r)
    {
        const double kappa = 0.5522847498;
        var k = kappa * r;
        return new PathContour(new Point(cx + r, cy),
        [
            new PathCubic(new Point(cx + r, cy + k), new Point(cx + k, cy + r), new Point(cx, cy + r)),
            new PathCubic(new Point(cx - k, cy + r), new Point(cx - r, cy + k), new Point(cx - r, cy)),
            new PathCubic(new Point(cx - r, cy - k), new Point(cx - k, cy - r), new Point(cx, cy - r)),
            new PathCubic(new Point(cx + k, cy - r), new Point(cx + r, cy - k), new Point(cx + r, cy)),
        ]);
    }

    /// <summary>A link area under the body's clip (<see cref="BodyClip.BodyArea"/>): the part of it that the clip shows, so a
    /// reader clicks only where the link is drawn. As Chromium 159 writes a link whose box reaches above the page area at
    /// the top of a page: the rectangle starts at the page area's top, 18 pt, where the box's border box starts at 16.008 pt
    /// (cases <c>ib-link-button</c>, <c>ib-link-button-break</c>). An area wholly outside the clip keeps no width or height.</summary>
    private static Rect Inside(Rect area, Rect clip)
    {
        var (left, top) = (Math.Max(area.Origin.X, clip.Origin.X), Math.Max(area.Origin.Y, clip.Origin.Y));
        var right = Math.Max(left, Math.Min(area.Origin.X + area.Size.Width, clip.Origin.X + clip.Size.Width));
        var bottom = Math.Max(top, Math.Min(area.Origin.Y + area.Size.Height, clip.Origin.Y + clip.Size.Height));
        return new Rect(new Point(left, top), new Size(right - left, bottom - top));
    }

    /// <summary>The items between a clip start and end; nothing at all when there are no items.</summary>
    private static IEnumerable<DrawItem> Clipped(Rect area, IEnumerable<DrawItem> items)
    {
        ImmutableArray<DrawItem> content = [.. items];
        return content.IsEmpty ? [] : [new ClipStart(area), .. content, ClipEnd.Instance];
    }

    private static IEnumerable<DrawItem> Box(BoxFragment box, Point origin, bool inArtifact, Sheet sheet, Anchors anchors)
    {
        if (box.Box.Role is SemanticRole.ListLabelRole && sheet.BodyClip is { } clip && !Overlaps(new Rect(origin, box.Size), clip))
        {
            // Chromium 159 leaves out a list marker that lies wholly outside the clipped body, text or shape (case li-marker-clip).
            return [];
        }

        if (inArtifact)
        {
            return [.. Background(box, origin), .. Border(box, origin), .. Children(box.Children, origin, inArtifact: true, sheet, anchors)];
        }

        if (box.IsRepeated || box.Box.Role == SemanticRole.Decorative)
        {
            return Artifact(box.IsRepeated ? ArtifactKind.Repeated : ArtifactKind.Layout, Box(box, origin, inArtifact: true, sheet, anchors));
        }

        // The box's own background and border go inside its markers, before its children: they are a layout
        // artifact, not the box's logical content, but they are part of what a reader sees of it, so a figure's or a
        // table's /BBox (ElementBounds) encloses them. The painting order is the same either way.
        return
        [
            new StructureStart(box.Box.Id, box.Box.Role, box.Box.Source) { Span = box.Box.Style.Span, Language = box.Box.Language },
            .. anchors.At(box.Box.Id, origin),
            .. Decoration([.. Background(box, origin), .. Border(box, origin)], inArtifact: false),
            .. Children(box.Children, origin, inArtifact: false, sheet, anchors),
            new StructureEnd(box.Box.Id),
        ];
    }

    /// <summary>
    /// A line: its children, and around the children of each inline group's part on it (<see cref="LineGroupPart"/>) the
    /// group's structure marker, so a group over several lines or pages is one element in tagged PDF (the markers of one
    /// node are joined, as for a paragraph over two pages). Inside a link's marker, before its content, come its link
    /// areas (<see cref="LinkArea"/>): the part's area, and the border box of every image in the part (Chromium's print
    /// writes a rectangle for the link's line part and one for an image inside it). Inside an artifact a group is drawn
    /// without markers and a link gets no area: a link in a page header or footer, or in a repeated copy of a table's
    /// header rows, is only drawn (a known limit; the first, real occurrence of a header row keeps its links). A link with a
    /// box of its own is drawn as a box with a link inside the link's marker: its area, then its background and border.
    /// <para>THE RULE FOR PAINTING AN INLINE BOX: a group with a box (<see cref="InlineGroup.Box"/>) paints its background
    /// and then its border where its part opens, after the line's children before it and before its own content, as a layout
    /// artifact (inside the group's marker when it has a role; a group without a role opens no marker, its content stays
    /// content of the element around it), and inside an artifact as it is. So, as Chromium prints it (cases
    /// <c>ib-padding</c>, <c>ib-nested</c>), a box covers the text of the lines above it, the text of the lines below it is
    /// drawn over it, and a box inside a box is painted over the outer one's background.</para>
    /// </summary>
    private static IEnumerable<DrawItem> Line(LineFragment line, Point origin, bool inArtifact, Sheet sheet, Anchors anchors)
    {
        if (line.Groups.Count == 0 || (inArtifact && !line.Groups.Any(part => part.Box is not null)))
        {
            return Children(line.Children, origin, inArtifact, sheet, anchors);
        }

        var items = new List<DrawItem>();
        var open = new Stack<LineGroupPart>();
        var next = 0;
        void Close(LineGroupPart part)
        {
            if (!inArtifact && part.Group.Role is not null)
            {
                items.Add(new StructureEnd(part.Group.Id));
            }
        }

        void OpenAt(int index)
        {
            while (next < line.Groups.Count && line.Groups[next].First == index)
            {
                var part = line.Groups[next++];
                if (!inArtifact && part.Group.Role is { } role)
                {
                    items.Add(new StructureStart(part.Group.Id, role, part.Group.Source) { Language = part.Group.Language });
                    items.AddRange(anchors.At(part.Group.Id, origin + part.Area.Origin));
                    if (role is SemanticRole.LinkRole)
                    {
                        items.Add(new LinkArea(part.Group.Id, new Rect(origin + part.Area.Origin, part.Area.Size)));
                        for (var child = part.First; child < part.First + part.Count; child++)
                        {
                            if (line.Children[child].Fragment is BoxFragment image)
                            {
                                items.Add(new LinkArea(part.Group.Id, new Rect(origin + line.Children[child].Offset, image.Size)));
                            }
                        }
                    }
                }

                if (part is { Box: { } area, Group.Box: { } box })
                {
                    items.AddRange(Decoration([.. InlineBox(box, part, new Rect(origin + area.Origin, area.Size))], inArtifact));
                }

                if (part.Count == 0)
                {
                    Close(part);
                }
                else
                {
                    open.Push(part);
                }
            }
        }

        for (var index = 0; index < line.Children.Count; index++)
        {
            OpenAt(index);
            var placed = line.Children[index];
            items.AddRange(Items(placed.Fragment, origin + placed.Offset, inArtifact, sheet, anchors));
            while (open.Count > 0 && open.Peek().First + open.Peek().Count == index + 1)
            {
                Close(open.Pop());
            }
        }

        // Parts that hold no child and open after the line's last child: an empty inline box at the line's end.
        OpenAt(line.Children.Count);
        return items;
    }

    /// <summary>The background and then the border sides of an inline box's part, in its border box <paramref name="area"/>
    /// (THE RULE FOR PAINTING AN INLINE BOX): the sides as a box's (<see cref="Border(BoxFragment, Point)"/>), the left one
    /// only on the part that holds the start edge and the right one only on the part that holds the end edge (THE RULE FOR AN
    /// INLINE BOX OVER LINES).</summary>
    private static IEnumerable<DrawItem> InlineBox(InlineBoxStyle box, LineGroupPart part, Rect area) =>
        [.. box.Background is { } color ? [new FillRectangle(area, color)] : Array.Empty<DrawItem>(),
            .. Sides(box.Border with { Left = part.HoldsStart ? box.Border.Left : BorderSide.None, Right = part.HoldsEnd ? box.Border.Right : BorderSide.None }, area.Origin, area.Size)];

    private static IEnumerable<DrawItem> Children(ValueList<PlacedFragment> children, Point origin, bool inArtifact, Sheet sheet, Anchors anchors) =>
        children.SelectMany(child => Items(child.Fragment, origin + child.Offset, inArtifact, sheet, anchors));

    /// <summary>Decoration outside an artifact becomes a layout artifact of its own; inside one it is drawn as is.</summary>
    private static IEnumerable<DrawItem> Decoration(ImmutableArray<DrawItem> items, bool inArtifact) =>
        inArtifact ? items : Artifact(ArtifactKind.Layout, items);

    /// <summary>The items between an artifact start and end; nothing at all when there are no items.</summary>
    private static IEnumerable<DrawItem> Artifact(ArtifactKind kind, IEnumerable<DrawItem> items)
    {
        ImmutableArray<DrawItem> content = [.. items];
        return content.IsEmpty ? [] : [new ArtifactStart(kind), .. content, ArtifactEnd.Instance];
    }

    /// <summary>The background of a box under its border box; along the rounded border box where it has rounded corners
    /// (<see cref="RoundedBox"/>).</summary>
    private static IEnumerable<DrawItem> Background(BoxFragment box, Point origin) => box.Box.Style.Background switch
    {
        null => [],
        var color when !box.Box.Style.BorderRadius.IsZero => [RoundedBox.Background(box, origin, color.Value)],
        var color => [new FillRectangle(new Rect(origin, box.Size), color.Value)],
    };

    /// <summary>
    /// The border sides the fragment paints. A border of solid sides is painted as four filled rectangles along the edges
    /// of the border box (CSS Backgrounds 3 §3): the top and bottom sides run the full width, the left and right sides fill
    /// the height between them. A border with an inset or outset side, or with a corner where two visible solid sides of
    /// different colours meet, is painted as Chromium 159 paints it, with the sides cut at the corners where two colours
    /// meet (CSS Backgrounds 3 §4.4, colour transitions at corners; <see cref="CutBorder"/>); a border of one colour stays
    /// four rectangles (<c>bs-solid-one-colour</c>: Chromium fills the same pixels as one shape). A box with rounded corners
    /// paints its border as one ring (<see cref="RoundedBox"/>).
    /// </summary>
    private static IEnumerable<DrawItem> Border(BoxFragment box, Point origin) =>
        box.Box.Style.BorderRadius.IsZero ? Sides(box.Border, origin, box.Size) : RoundedBox.Border(box, origin);

    private static IEnumerable<DrawItem> Sides(Border border, Point origin, Size size)
    {
        if (border.Top.Style != BorderStyle.Solid || border.Right.Style != BorderStyle.Solid || border.Bottom.Style != BorderStyle.Solid || border.Left.Style != BorderStyle.Solid
            || MeetsAnotherColour(border))
        {
            return CutBorder(border, new Rect(origin, size));
        }

        var (top, bottom) = (border.Top.Width.Points, border.Bottom.Width.Points);
        IEnumerable<(BorderSide Side, Rect Area)> sides =
        [
            (border.Top, new Rect(origin, new Size(size.Width, top))),
            (border.Bottom, new Rect(origin + new Point(0, size.Height - bottom), new Size(size.Width, bottom))),
            (border.Left, new Rect(origin + new Point(0, top), new Size(border.Left.Width.Points, size.Height - top - bottom))),
            (border.Right, new Rect(origin + new Point(size.Width - border.Right.Width.Points, top), new Size(border.Right.Width.Points, size.Height - top - bottom))),
        ];
        return sides.Where(side => side.Side.IsVisible).Select(side => (DrawItem)new FillRectangle(side.Area, side.Side.Color));
    }

    /// <summary>Whether two visible sides of different colours meet at a corner of the border (a side of width 0 meets
    /// none): the only place where the cut differs from the rectangles.</summary>
    private static bool MeetsAnotherColour(Border border) =>
        new[] { (border.Top, border.Right), (border.Right, border.Bottom), (border.Bottom, border.Left), (border.Left, border.Top) }
            .Any(corner => corner.Item1.IsVisible && corner.Item2.IsVisible && corner.Item1.Color != corner.Item2.Color);

    /// <summary>The four sides of a border in the order <see cref="CutBorder"/> considers them: top, bottom, right, left.</summary>
    private enum Edge { Top, Bottom, Right, Left }

    /// <summary>
    /// A border with an inset or outset side or with solid sides of more than one colour, as Chromium 159 prints it (cases
    /// <c>bs-widths</c>, <c>bs-corners</c>, <c>bs-sides</c>, <c>hr-default</c>; solid sides: <c>bs-solid-colours</c>,
    /// <c>bs-solid-widths</c>, <c>bs-solid-pairs</c>, <c>bs-solid-break</c>): each visible side is one fill in its painted
    /// colour (<see cref="BorderShades.Painted"/>), the inset and outset sides in the order top, bottom, right, left, then
    /// the solid sides in the same order (<c>bs-corners</c>, <c>bs-solid-order</c>: a solid top, bottom, left, or top and
    /// bottom, beside inset or outset sides is painted after them). Each side runs along
    /// its whole edge of the border box, over both corners. At a corner where it meets a visible side painted before it in
    /// another colour it is cut on the line from the outer to the inner corner of the border (45 degrees where the two widths
    /// are equal), so each side shows its half of the corner; beside a side of its own colour, or a side of width 0, it covers
    /// the corner (<c>bs-sides</c>). A side cut at a corner is a filled path; one that is not stays a filled rectangle, never
    /// a path of four sides along the axes. The border box is painted with its edges on whole CSS pixels of the page,
    /// each rounded to the nearest (<c>bs-colours</c>: a box 28⅔ px high is painted 29 px high, its cut sides too;
    /// <c>bs-solid-colours</c>, box <c>cx</c>: a left edge at 16.53 px is painted at 17 px, a bottom at 202.53 px at 203 px);
    /// the widths of the sides stay as they are.
    /// </summary>
    private static IEnumerable<DrawItem> CutBorder(Border border, Rect box)
    {
        BorderSide SideOf(Edge edge) => edge switch
        {
            Edge.Top => border.Top,
            Edge.Bottom => border.Bottom,
            Edge.Right => border.Right,
            _ => border.Left,
        };
        Color ColorOf(Edge edge) => BorderShades.Painted(SideOf(edge), edge is Edge.Top or Edge.Left);
        static (Edge Start, Edge End) Neighbours(Edge edge) => edge is Edge.Top or Edge.Bottom ? (Edge.Left, Edge.Right) : (Edge.Top, Edge.Bottom);

        Edge[] edges = [Edge.Top, Edge.Bottom, Edge.Right, Edge.Left];
        var order = edges.Where(edge => SideOf(edge).Style != BorderStyle.Solid).Concat(edges.Where(edge => SideOf(edge).Style == BorderStyle.Solid))
            .Where(edge => SideOf(edge).IsVisible).ToArray();
        var (x0, y0) = (CssPixels.Round(box.Origin.X), CssPixels.Round(box.Origin.Y));
        var (x1, y1) = (CssPixels.Round(box.Origin.X + box.Size.Width), CssPixels.Round(box.Origin.Y + box.Size.Height));
        var (width, height) = (x1 - x0, y1 - y0);
        static Rect Area(double x, double y, double width, double height) => new(new Point(x, y), new Size(width, height));
        var (top, right, bottom, left) = (border.Top.Width.Points, border.Right.Width.Points, border.Bottom.Width.Points, border.Left.Width.Points);
        for (var index = 0; index < order.Length; index++)
        {
            var edge = order[index];
            var color = ColorOf(edge);
            bool Cut(Edge neighbour) => Array.IndexOf(order, neighbour, 0, index) >= 0 && ColorOf(neighbour) != color;
            var (start, end) = Neighbours(edge);
            var (cutStart, cutEnd) = (Cut(start), Cut(end));
            if (!cutStart && !cutEnd)
            {
                yield return new FillRectangle(edge switch
                {
                    Edge.Top => Area(x0, y0, width, top),
                    Edge.Bottom => Area(x0, y1 - bottom, width, bottom),
                    Edge.Right => Area(x1 - right, y0, right, height),
                    _ => Area(x0, y0, left, height),
                }, color);
                continue;
            }

            // The outer edge from its start corner to its end corner, then the inner edge back: at a cut end the inner
            // point is the inner corner of the border, else the point on the outer edge's line across the corner.
            Point[] corners = edge switch
            {
                Edge.Top => [new(x0, y0), new(x1, y0), new(cutEnd ? x1 - right : x1, y0 + top), new(cutStart ? x0 + left : x0, y0 + top)],
                Edge.Bottom => [new(x0, y1), new(x1, y1), new(cutEnd ? x1 - right : x1, y1 - bottom), new(cutStart ? x0 + left : x0, y1 - bottom)],
                Edge.Right => [new(x1, y0), new(x1, y1), new(x1 - right, cutEnd ? y1 - bottom : y1), new(x1 - right, cutStart ? y0 + top : y0)],
                _ => [new(x0, y0), new(x0, y1), new(x0 + left, cutEnd ? y1 - bottom : y1), new(x0 + left, cutStart ? y0 + top : y0)],
            };
            yield return new FillPath([new PathContour(corners[0], [.. corners.Skip(1).Select(corner => (PathSegment)new PathLine(corner))])], PathFillRule.NonZero, color);
        }
    }

    /// <summary>
    /// A glyph run starts on the baseline, <see cref="TextFragment.Ascent"/> below the fragment's top edge;
    /// each glyph's pen position is the sum of the advances before it, and the glyph is drawn at the pen plus its
    /// own offset from shaping (GPOS placement, a mark on its base); y flips from the font's "up" to the page's
    /// "down". An invisible glyph (a default-ignorable character, <see cref="Typesetting.ShapedGlyph.IsInvisible"/>) is not
    /// drawn.
    /// </summary>
    private static GlyphRun Glyphs(TextFragment text, Point origin)
    {
        var glyphs = ImmutableArray.CreateBuilder<PositionedGlyph>(text.Glyphs.Count);
        var x = 0.0;
        foreach (var glyph in text.Glyphs)
        {
            if (!glyph.IsInvisible)
            {
                glyphs.Add(new PositionedGlyph(glyph.Glyph, x + glyph.XOffset, glyph.Text, -glyph.YOffset));
            }

            x += glyph.Advance;
        }

        var style = text.Run.Style;
        return new GlyphRun(text.Font, style.FontSize.Points, style.Color, origin + new Point(0, text.Ascent), glyphs.ToImmutable());
    }
}
