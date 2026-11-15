using Ave.Documents.Core;
using Ave.Documents.Fonts;

namespace Ave.Documents.PageModel;

/// <summary>
/// One entry of a page's display list. Coordinates are page coordinates in points: origin top-left, y down.
/// </summary>
/// <remarks>
/// How this grows to "everything PDF can draw" without breaking existing users: new cases are ADDED next to
/// these — a filled or stroked path with a paint (of which <see cref="FillRectangle"/> stays the common special
/// case), an image, and a group with a transform, a clip and compositing (opacity, blend mode, soft mask) that
/// holds its own items. Existing cases keep their meaning; a back-end that meets a case it does not know
/// reports it instead of skipping it.
/// </remarks>
public abstract record DrawItem
{
    // Only the cases in this assembly exist, so back-ends can switch over them completely.
    private protected DrawItem()
    {
    }
}

/// <summary>A rectangle filled with one colour (a box background).</summary>
/// <param name="Area">The rectangle.</param>
/// <param name="Color">The fill colour.</param>
public sealed record FillRectangle(Rect Area, Color Color) : DrawItem;

/// <summary>
/// Glyphs of one font, size and colour on one baseline. Glyphs are referenced by glyph id in a font program,
/// never as "text plus a font name": the PDF writer needs glyph ids for composite fonts, and a raster back-end
/// needs the outlines of exactly these glyphs.
/// </summary>
/// <param name="Font">The font the glyph ids belong to.</param>
/// <param name="FontSize">The font size in points.</param>
/// <param name="Color">The fill colour of the glyphs.</param>
/// <param name="Origin">The start of the baseline: the origin of the first glyph.</param>
/// <param name="Glyphs">The glyphs, each with its position along the baseline.</param>
public sealed record GlyphRun(TrueTypeFont Font, double FontSize, Color Color, Point Origin, ValueList<PositionedGlyph> Glyphs) : DrawItem;

/// <summary>A glyph in a <see cref="GlyphRun"/>.</summary>
/// <param name="Glyph">The glyph id.</param>
/// <param name="X">The glyph origin's distance from the run's origin along the baseline, in points.</param>
/// <param name="Text">The source characters the glyph shows (for copy, search and accessibility).</param>
public sealed record PositionedGlyph(GlyphId Glyph, double X, string Text);

/// <summary>
/// Starts the content of a document node with a meaning (a paragraph, a heading). Every start has a matching
/// <see cref="StructureEnd"/> on the same page; a node split over two pages has a pair on each page, with the
/// same id. Tagged PDF (a later stage) builds its structure tree from these markers.
/// </summary>
/// <param name="Node">The document node.</param>
/// <param name="Role">Its role.</param>
public sealed record StructureStart(NodeId Node, SemanticRole Role) : DrawItem;

/// <summary>Ends the content started by the <see cref="StructureStart"/> with the same node id.</summary>
/// <param name="Node">The document node.</param>
public sealed record StructureEnd(NodeId Node) : DrawItem;
