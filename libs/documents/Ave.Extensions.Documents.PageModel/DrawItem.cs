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
/// <para><b>THE NESTING RULE FOR MARKERS</b>, for all three kinds — structure (<see cref="StructureStart"/>,
/// <see cref="StructureEnd"/>), artifact (<see cref="ArtifactStart"/>, <see cref="ArtifactEnd"/>) and clip
/// (<see cref="ClipStart"/>, <see cref="ClipEnd"/>): the markers of a page nest as one sequence of pairs. Every end
/// closes the innermost marker still open, which is a start of its own kind (and for a structure end, of its node);
/// every start ends on the same page. Inside an artifact there is no other artifact and no structure marker: an
/// artifact is not part of the structure. A clip may open anywhere, also inside an artifact or another clip, up to 27
/// clips one inside the other (<see cref="PageModelRules.MaxClipDepth"/>, from the 28 levels of <c>q</c> a PDF may nest). This is
/// how the PDF writer can keep its graphics-state pairs (<c>q</c>/<c>Q</c>, ISO 32000-1 §8.4.2) and its marked-content
/// pairs (<c>BDC</c>/<c>EMC</c>, §14.6) inside each other. A back-end checks the rule and refuses a page that breaks
/// it (<see cref="PageModelRules"/>, for every back-end).</para>
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
/// <see cref="StructureEnd"/> on the same page (the nesting rule, <see cref="DrawItem"/>); a node split over two pages
/// has a pair on each page, with the same id. Markers nest like the document tree, and content between a start and its
/// end belongs to that node. Tagged
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

    /// <summary>The natural language of the node's content (<see cref="LanguageTags"/>), or null when it is its parent's.</summary>
    public string? Language { get; init; }
}

/// <summary>Ends the content started by the <see cref="StructureStart"/> with the same node id.</summary>
/// <param name="Node">The document node.</param>
public sealed record StructureEnd(NodeId Node) : DrawItem;

/// <summary>
/// Starts content that is not part of the document's structure: decoration or page furniture (an "artifact", ISO
/// 32000-1 §14.8.2.2). Ends at its <see cref="ArtifactEnd"/>; artifacts do not nest, and contain no structure markers
/// (the nesting rule, <see cref="DrawItem"/>). Content outside every artifact belongs to the innermost open structure
/// marker.
/// </summary>
/// <param name="Kind">What kind of artifact.</param>
public sealed record ArtifactStart(ArtifactKind Kind) : DrawItem;

/// <summary>Ends the artifact started by the last <see cref="ArtifactStart"/>.</summary>
public sealed record ArtifactEnd : DrawItem
{
    /// <summary>The one end marker.</summary>
    public static ArtifactEnd Instance { get; } = new();
}

/// <summary>
/// Starts a clip: the items up to the matching <see cref="ClipEnd"/> are drawn only inside the rectangle (and inside
/// every clip around it), as the slices of content taller than a page are. A rectangle only, in page coordinates; the
/// pair nests with the other markers (the nesting rule, <see cref="DrawItem"/>). A clip says nothing about structure:
/// the content inside it belongs where it would without it.
/// </summary>
/// <param name="Area">The rectangle.</param>
public sealed record ClipStart(Rect Area) : DrawItem;

/// <summary>Ends the clip started by the innermost open <see cref="ClipStart"/>.</summary>
public sealed record ClipEnd : DrawItem
{
    /// <summary>The one end marker.</summary>
    public static ClipEnd Instance { get; } = new();
}

/// <summary>
/// The area a reader clicks to follow a link: one part of a link on one line, or an image inside the link (ISO 32000-1
/// §12.5.6.5, the <c>/Rect</c> of a link annotation). It draws nothing. It stands inside the structure marker of its link
/// (a <see cref="StructureStart"/> whose role is <see cref="SemanticRole.LinkRole"/>, which carries the target and the
/// description), with that marker as the innermost open structure marker and no artifact open (<see cref="PageModelRules"/>):
/// a link in an artifact, such as a page header or a repeated header row, has no area, and its text is only drawn. The PDF
/// back-end writes one link annotation per area; the raster back-end draws nothing for it.
/// </summary>
/// <param name="Link">The link's node.</param>
/// <param name="Area">The area, in page coordinates.</param>
public sealed record LinkArea(NodeId Link, Rect Area) : DrawItem;

/// <summary>
/// The place a link inside the document leads to (a link whose target is <see cref="LinkTarget.Node"/>): the top left
/// of the first fragment of <see cref="Node"/>, on the page that fragment is on. It draws nothing. The layout writes one
/// for every node a link leads to, once in the document, at the node's first fragment (a box's border box, an inline
/// group's first line part), outside every artifact; every link to a node needs that node's anchor
/// (<see cref="PageModelRules"/>). The PDF back-end writes it as the destination of each link to the node; the raster
/// back-end draws nothing for it.
/// </summary>
/// <param name="Node">The node the place belongs to.</param>
/// <param name="At">The top left of its first fragment, in page coordinates.</param>
public sealed record Anchor(NodeId Node, Point At) : DrawItem;

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

    /// <summary>
    /// A later slice of a line of text taller than the page area (<c>MonolithicOverflow.Slice</c>; <c>/Pagination</c>).
    /// Not a repeat: each page shows another part of the same glyphs. A stated choice: the first slice is the line's
    /// content and delivers its text (it draws the whole line, clipped; ISO 32000-1 §14.8.2.2.3); the later pages draw the same glyphs again, shifted, and as content their text
    /// would be read again. ISO 32000-1 §14.8.2.2.1 defines artifacts as "graphics objects that are not part of the
    /// author's original content but rather are generated by the conforming writer in the course of pagination, layout,
    /// or other strictly mechanical processes"; the lower part of a glyph is the author's content, so by the letter of
    /// the first half this marks real content as an artifact, and the engine accepts that for the text to be read once.
    /// The later slices of an image are not artifacts: they are content of the same figure.
    /// </summary>
    SliceContinuation,
}
