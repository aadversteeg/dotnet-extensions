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
            .. page.Header is { } header ? Artifact(ArtifactKind.PageHeader, Items(header.Fragment, header.Offset, inArtifact: true, page.PageSize.Width, anchors)) : [],
            .. page.BodyClip is { } body
                ? Clipped(body, Items(page.Root.Fragment, page.Root.Offset, inArtifact: false, page.PageSize.Width, anchors).Select(item => item is LinkArea link ? link with { Area = Inside(link.Area, body) } : item))
                : Items(page.Root.Fragment, page.Root.Offset, inArtifact: false, page.PageSize.Width, anchors),
            .. page.Footer is { } footer ? Artifact(ArtifactKind.PageFooter, Items(footer.Fragment, footer.Offset, inArtifact: true, page.PageSize.Width, anchors)) : [],
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
    /// (<paramref name="pageWidth"/>). The slices of an image are all content of its figure, as the pages of a paragraph
    /// are content of the paragraph: each page shows another part of the image, and its alternative text is on the
    /// figure, read once. Every slice of a line of text after the first is an artifact
    /// (<see cref="ArtifactKind.SliceContinuation"/>, a stated choice): the first slice delivers the line's text, and the
    /// later ones draw the same glyphs again.
    /// </remarks>
    private static IEnumerable<DrawItem> Items(Fragment fragment, Point origin, bool inArtifact, double pageWidth, Anchors anchors) => fragment switch
    {
        BoxFragment box => Box(box, origin, inArtifact, pageWidth, anchors),
        LineFragment line => Line(line, origin, inArtifact, pageWidth, anchors),
        TextFragment text => Glyphs(text, origin) is { Glyphs.Count: > 0 } run ? [run] : [],
        ImageFragment image => [new DrawImage(new Rect(origin, image.Size), image.Image.Image)],
        RuleFragment rule => Decoration([new FillRectangle(new Rect(origin, rule.Size), rule.Color)], inArtifact),
        SliceFragment slice => Clipped(new Rect(new Point(0, origin.Y), new Size(pageWidth, slice.Size.Height)),
            slice.IsContinuation && !inArtifact && slice.Content.Fragment is LineFragment
                ? Artifact(ArtifactKind.SliceContinuation, Items(slice.Content.Fragment, origin + slice.Content.Offset, inArtifact: true, pageWidth, anchors))
                : Items(slice.Content.Fragment, origin + slice.Content.Offset, inArtifact, pageWidth, anchors)),
        _ => throw new InvalidOperationException($"Unknown fragment type {fragment.GetType().Name}."),
    };

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

    private static IEnumerable<DrawItem> Box(BoxFragment box, Point origin, bool inArtifact, double pageWidth, Anchors anchors)
    {
        if (inArtifact)
        {
            return [.. Background(box, origin), .. Border(box, origin), .. Children(box.Children, origin, inArtifact: true, pageWidth, anchors)];
        }

        if (box.IsRepeated || box.Box.Role == SemanticRole.Decorative)
        {
            return Artifact(box.IsRepeated ? ArtifactKind.Repeated : ArtifactKind.Layout, Box(box, origin, inArtifact: true, pageWidth, anchors));
        }

        // The box's own background and border go inside its markers, before its children: they are a layout
        // artifact, not the box's logical content, but they are part of what a reader sees of it, so a figure's or a
        // table's /BBox (ElementBounds) encloses them. The painting order is the same either way.
        return
        [
            new StructureStart(box.Box.Id, box.Box.Role, box.Box.Source) { Span = box.Box.Style.Span, Language = box.Box.Language },
            .. anchors.At(box.Box.Id, origin),
            .. Decoration([.. Background(box, origin), .. Border(box, origin)], inArtifact: false),
            .. Children(box.Children, origin, inArtifact: false, pageWidth, anchors),
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
    private static IEnumerable<DrawItem> Line(LineFragment line, Point origin, bool inArtifact, double pageWidth, Anchors anchors)
    {
        if (line.Groups.Count == 0 || (inArtifact && !line.Groups.Any(part => part.Box is not null)))
        {
            return Children(line.Children, origin, inArtifact, pageWidth, anchors);
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
            items.AddRange(Items(placed.Fragment, origin + placed.Offset, inArtifact, pageWidth, anchors));
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

    private static IEnumerable<DrawItem> Children(ValueList<PlacedFragment> children, Point origin, bool inArtifact, double pageWidth, Anchors anchors) =>
        children.SelectMany(child => Items(child.Fragment, origin + child.Offset, inArtifact, pageWidth, anchors));

    /// <summary>Decoration outside an artifact becomes a layout artifact of its own; inside one it is drawn as is.</summary>
    private static IEnumerable<DrawItem> Decoration(ImmutableArray<DrawItem> items, bool inArtifact) =>
        inArtifact ? items : Artifact(ArtifactKind.Layout, items);

    /// <summary>The items between an artifact start and end; nothing at all when there are no items.</summary>
    private static IEnumerable<DrawItem> Artifact(ArtifactKind kind, IEnumerable<DrawItem> items)
    {
        ImmutableArray<DrawItem> content = [.. items];
        return content.IsEmpty ? [] : [new ArtifactStart(kind), .. content, ArtifactEnd.Instance];
    }

    private static IEnumerable<DrawItem> Background(BoxFragment box, Point origin) =>
        box.Box.Style.Background is { } color ? [new FillRectangle(new Rect(origin, box.Size), color)] : [];

    /// <summary>
    /// The border sides the fragment paints, each a filled rectangle along its edge of the border box (solid borders
    /// only, CSS Backgrounds 3 §3). The top and bottom sides run the full width; the left and right sides fill the
    /// height between them. LIMIT: where two sides of different colours meet, CSS joins them on the diagonal of the
    /// corner (CSS Backgrounds 3, colour transitions at corners); here the top and bottom sides cover the corners.
    /// </summary>
    private static IEnumerable<DrawItem> Border(BoxFragment box, Point origin) => Sides(box.Border, origin, box.Size);

    private static IEnumerable<DrawItem> Sides(Border border, Point origin, Size size)
    {
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
