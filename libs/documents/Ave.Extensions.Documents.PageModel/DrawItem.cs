using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Images;

namespace Ave.Extensions.Documents.PageModel;

/// <summary>
/// One entry of a page's display list. Coordinates are page coordinates in points: origin top-left, y down.
/// </summary>
/// <remarks>
/// How this grows to "everything PDF can draw" without breaking existing users: new cases are ADDED next to
/// these — a filled or stroked path with a paint (of which <see cref="FillRectangle"/> stays the common special
/// case) and a group with a transform, a clip and compositing (opacity, blend mode, soft mask) that
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

/// <summary>
/// An image drawn into a rectangle, scaled to fill it exactly (the layout keeps the aspect ratio). Images are
/// referenced as decoded data, not as a file name, like glyphs are referenced by glyph id.
/// </summary>
/// <param name="Area">Where the image goes on the page.</param>
/// <param name="Image">The image.</param>
public sealed record DrawImage(Rect Area, ImageData Image) : DrawItem;

/// <summary>A rectangle filled with one colour: a box background, one side of a border, or a table grid line.</summary>
/// <param name="Area">The rectangle.</param>
/// <param name="Color">The fill colour.</param>
public sealed record FillRectangle(Rect Area, Color Color) : DrawItem;

/// <summary>
/// Glyphs of one font, size and colour on one baseline. Glyphs are referenced by glyph id in a font program,
/// never as text with a font name: the PDF writer needs glyph ids for composite fonts, and a raster back-end
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
/// <param name="Text">The source characters the glyph shows (for copy, search and accessibility); several for a
/// ligature, empty for a glyph that only continues the previous one's text.</param>
/// <param name="Y">The glyph origin's distance BELOW the baseline, in points (page coordinates, y down); negative
/// for a mark raised above its base. Zero for almost every glyph.</param>
public sealed record PositionedGlyph(GlyphId Glyph, double X, string Text, double Y = 0);

/// <summary>
/// Starts the content of a document node with a meaning (a paragraph, a heading). Every start has a matching
/// <see cref="StructureEnd"/> on the same page; a node split over two pages has a pair on each page, with the
/// same id. Markers nest like the document tree, and content between a start and its end belongs to that node. Tagged
/// PDF builds its structure tree from these markers, in their order (reading order = document order).
/// </summary>
/// <param name="Node">The document node.</param>
/// <param name="Role">Its role.</param>
/// <param name="Source">Where the node was written, so a back-end can report a structure problem (a figure without
/// alternative text) at the author's line.</param>
public sealed record StructureStart(NodeId Node, SemanticRole Role, SourceLocation Source) : DrawItem
{
    /// <summary>For a table cell, the columns and rows it covers; <see cref="CellSpan.Single"/> otherwise.</summary>
    public CellSpan Span { get; init; } = CellSpan.Single;
}

/// <summary>Ends the content started by the <see cref="StructureStart"/> with the same node id.</summary>
/// <param name="Node">The document node.</param>
public sealed record StructureEnd(NodeId Node) : DrawItem;

/// <summary>
/// Starts content that is not part of the document's structure: decoration or page furniture (an "artifact", ISO
/// 32000-1 §14.8.2.2). Ends at the next <see cref="ArtifactEnd"/>; artifacts do not nest, and contain no structure
/// markers. Content outside every artifact belongs to the innermost open structure marker.
/// </summary>
/// <param name="Kind">What kind of artifact.</param>
public sealed record ArtifactStart(ArtifactKind Kind) : DrawItem;

/// <summary>Ends the artifact started by the last <see cref="ArtifactStart"/>.</summary>
public sealed record ArtifactEnd : DrawItem
{
    /// <summary>The one end marker.</summary>
    public static ArtifactEnd Instance { get; } = new();
}

/// <summary>The kinds of artifact (ISO 32000-1 §14.8.2.2.2, Table 330, the <c>/Type</c> and <c>/Subtype</c> entries).</summary>
public enum ArtifactKind
{
    /// <summary>Decoration such as a background, a border or a table rule (<c>/Layout</c>).</summary>
    Layout,

    /// <summary>A running header (<c>/Pagination /Header</c>).</summary>
    PageHeader,

    /// <summary>A running footer (<c>/Pagination /Footer</c>).</summary>
    PageFooter,

    /// <summary>Other content that repeats because of page breaks, such as a table's repeated header rows (<c>/Pagination</c>).</summary>
    Repeated,
}
