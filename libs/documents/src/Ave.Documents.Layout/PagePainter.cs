using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.PageModel;

namespace Ave.Documents.Layout;

/// <summary>
/// Turns the fragment tree into the page model: absolute positions, and the display list in painting order.
/// </summary>
public static class PagePainter
{
    /// <summary>Paints every page.</summary>
    /// <param name="metadata">The document's metadata.</param>
    /// <param name="pages">The fragment tree, one entry per page.</param>
    /// <returns>The page document.</returns>
    public static PageDocument Paint(DocumentMetadata metadata, ValueList<PageFragment> pages) =>
        new(new DocumentInfo(metadata.Title, metadata.Author), [.. pages.Select(PaintPage)]);

    /// <summary>
    /// A page: header area, body, footer area. The areas repeat on every page; in tagged PDF they are pagination
    /// artifacts (ISO 32000-1 §14.8.2.2), so they are painted without structure markers.
    /// </summary>
    private static Page PaintPage(PageFragment page) =>
        new(page.PageSize,
        [
            .. page.Header is { } header ? Items(header.Fragment, header.Offset, isArtifact: true) : [],
            .. Items(page.Root.Fragment, page.Root.Offset, isArtifact: false),
            .. page.Footer is { } footer ? Items(footer.Fragment, footer.Offset, isArtifact: true) : [],
        ]);

    /// <summary>
    /// Walks the tree depth-first, in document order. For each box: its background and border, then a structure
    /// start, its content, a structure end. CSS 2.1 Appendix E.2 paints all block backgrounds and borders of a stacking
    /// context (step 4) before all inline content (step 7). For normal flow without overlapping boxes — all this engine
    /// can produce, since it has no negative margins, floats or positioning — painting in document order gives the
    /// same picture. Background and border are outside the structure markers because they are decoration, not content
    /// (an "artifact" in tagged PDF). A table's grid lines (<see cref="RuleFragment"/>) come after its cells in the
    /// fragment tree, so they are painted over the cell backgrounds, as the collapsing border model asks (CSS 2.1
    /// §17.5.1 table layers, then §17.6.2 borders). Repeated content (<see cref="BoxFragment.IsRepeated"/>) and the
    /// page areas get no structure markers: they are artifacts, the document structure has them once.
    /// </summary>
    private static IEnumerable<DrawItem> Items(Fragment fragment, Point origin, bool isArtifact) => fragment switch
    {
        BoxFragment box => Box(box, origin, isArtifact || box.IsRepeated),
        LineFragment line => Children(line.Children, origin, isArtifact),
        TextFragment text => [Glyphs(text, origin)],
        ImageFragment image => [new DrawImage(new Rect(origin, image.Size), image.Image.Image)],
        RuleFragment rule => [new FillRectangle(new Rect(origin, rule.Size), rule.Color)],
        _ => throw new InvalidOperationException($"Unknown fragment type {fragment.GetType().Name}."),
    };

    private static IEnumerable<DrawItem> Box(BoxFragment box, Point origin, bool isArtifact) =>
    [
        .. Background(box, origin),
        .. Border(box, origin),
        .. isArtifact ? [] : new DrawItem[] { new StructureStart(box.Box.Id, box.Box.Role) },
        .. Children(box.Children, origin, isArtifact),
        .. isArtifact ? [] : new DrawItem[] { new StructureEnd(box.Box.Id) },
    ];

    private static IEnumerable<DrawItem> Children(ValueList<PlacedFragment> children, Point origin, bool isArtifact) =>
        children.SelectMany(child => Items(child.Fragment, origin + child.Offset, isArtifact));

    private static IEnumerable<DrawItem> Background(BoxFragment box, Point origin) =>
        box.Box.Style.Background is { } color ? [new FillRectangle(new Rect(origin, box.Size), color)] : [];

    /// <summary>
    /// The border sides the fragment paints, each a filled rectangle along its edge of the border box (solid borders
    /// only, CSS Backgrounds 3 §4). The top and bottom sides run the full width; the left and right sides fill the
    /// height between them. LIMIT: where two sides of different colours meet, CSS joins them on the diagonal of the
    /// corner (CSS Backgrounds 3, colour transitions at corners); here the top and bottom sides cover the corners.
    /// </summary>
    private static IEnumerable<DrawItem> Border(BoxFragment box, Point origin)
    {
        var (border, size) = (box.Border, box.Size);
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
    /// "down".
    /// </summary>
    private static GlyphRun Glyphs(TextFragment text, Point origin)
    {
        var glyphs = ImmutableArray.CreateBuilder<PositionedGlyph>(text.Glyphs.Count);
        var x = 0.0;
        foreach (var glyph in text.Glyphs)
        {
            glyphs.Add(new PositionedGlyph(glyph.Glyph, x + glyph.XOffset, glyph.Text, -glyph.YOffset));
            x += glyph.Advance;
        }

        var style = text.Run.Style;
        return new GlyphRun(text.Font, style.FontSize.Points, style.Color, origin + new Point(0, text.Ascent), glyphs.MoveToImmutable());
    }
}
