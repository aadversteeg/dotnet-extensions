using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Model;
using Ave.Documents.Page;

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
    public static PageDocument Paint(DocumentMetadata metadata, ImmutableArray<PageFragment> pages) =>
        new(new DocumentInfo(metadata.Title, metadata.Author), [.. pages.Select(PaintPage)]);

    private static PageContent PaintPage(PageFragment page) =>
        new(page.PageSize, [.. Items(page.Root.Fragment, page.Root.Offset)]);

    /// <summary>
    /// Walks the tree depth-first, in document order. For each box: its background, then a structure start,
    /// its content, a structure end. CSS 2.1 Appendix E.2 paints all block backgrounds of a stacking context
    /// (step 4) before all inline content (step 7). For normal flow without overlapping boxes — all that stage
    /// 0 can produce, since it has no negative margins, floats or positioning — painting in document order
    /// gives the same picture. The background is outside the structure markers because it is decoration, not
    /// content (an "artifact" in tagged PDF).
    /// </summary>
    private static IEnumerable<DrawItem> Items(Fragment fragment, Point origin) => fragment switch
    {
        BoxFragment box =>
        [
            .. Background(box, origin),
            new StructureStart(box.Box.Id, box.Box.Role),
            .. Children(box.Children, origin),
            new StructureEnd(box.Box.Id),
        ],
        LineFragment line => Children(line.Children, origin),
        TextFragment text => [Glyphs(text, origin)],
        _ => throw new InvalidOperationException($"Unknown fragment type {fragment.GetType().Name}."),
    };

    private static IEnumerable<DrawItem> Children(ImmutableArray<PlacedFragment> children, Point origin) =>
        children.SelectMany(child => Items(child.Fragment, origin + child.Offset));

    private static IEnumerable<DrawItem> Background(BoxFragment box, Point origin) =>
        box.Box.Style.Background is { } color ? [new FillRectangle(new Rect(origin, box.Size), color)] : [];

    /// <summary>
    /// A glyph run starts on the baseline, <see cref="TextFragment.Ascent"/> below the fragment's top edge;
    /// each glyph's position is the sum of the advances before it.
    /// </summary>
    private static GlyphRun Glyphs(TextFragment text, Point origin)
    {
        var glyphs = ImmutableArray.CreateBuilder<PositionedGlyph>(text.Glyphs.Length);
        var x = 0.0;
        foreach (var glyph in text.Glyphs)
        {
            glyphs.Add(new PositionedGlyph(glyph.Glyph, x, glyph.Text));
            x += glyph.Advance;
        }

        var style = text.Run.Style;
        return new GlyphRun(text.Font, style.FontSize.Points, style.Color, origin + new Point(0, text.Ascent), glyphs.MoveToImmutable());
    }
}
