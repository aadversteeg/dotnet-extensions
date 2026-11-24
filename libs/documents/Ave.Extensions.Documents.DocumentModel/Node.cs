using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Images;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// A node of the document tree: a <see cref="Box"/>, an <see cref="InlineRun"/>, an <see cref="InlineGroup"/> or an
/// <see cref="ImageNode"/>.
/// Every node has an id (its pre-order position) and the source location of the code or markup that produced it.
/// </summary>
/// <param name="Id">The node id, unique in the document.</param>
/// <param name="Source">Where the node was written; reported with every error about it.</param>
public abstract record Node(NodeId Id, SourceLocation Source);

/// <summary>
/// A box: a rectangle that is laid out by the algorithm <see cref="Layout"/> names and that contains child
/// nodes. In CSS terms a block-level box (CSS Display 3 §2.1); <see cref="Layout"/> is its inner display type.
/// </summary>
/// <remarks>
/// Which children a box may have depends on its algorithm: <see cref="LayoutAlgorithmId.Flow"/> stacks child
/// boxes, <see cref="LayoutAlgorithmId.Inline"/> sets inline runs in lines (a block container whose content is an
/// inline formatting context, CSS 2.1 §9.2.1), grouped or not in <see cref="InlineGroup"/>s, and among them boxes laid out by <see cref="LayoutAlgorithmId.Image"/>, each
/// an atomic inline (an image in a line of text, CSS Display 3, Appendix A), <see cref="LayoutAlgorithmId.Tracks"/> lays out row groups of rows of
/// cells, <see cref="LayoutAlgorithmId.Image"/> holds one image. The algorithm reports an error for children it
/// cannot lay out.
/// </remarks>
/// <param name="Id">The node id.</param>
/// <param name="Role">What the box means (paragraph, heading, …), independent of how it is laid out.</param>
/// <param name="Layout">The layout algorithm for its content.</param>
/// <param name="Style">The computed box properties.</param>
/// <param name="Children">The child nodes, in document order.</param>
/// <param name="Source">Where the box was written.</param>
public sealed record Box(
    NodeId Id,
    SemanticRole Role,
    LayoutAlgorithmId Layout,
    BoxStyle Style,
    ValueList<Node> Children,
    SourceLocation Source) : Node(Id, Source)
{
    /// <summary>The natural language of the box's content as a language tag (ISO 32000-1 §14.9.2; THE RULE FOR A LANGUAGE
    /// TAG, <see cref="LanguageTags"/>), or null when it is that of its parent. A tagged PDF writes it on the box's
    /// structure element where it differs from its parent's.</summary>
    public string? Language { get; init; }
}

/// <summary>
/// A piece of text in one style, laid out by its parent's inline algorithm: literal text
/// (<see cref="TextRun"/>) or text computed at layout time (<see cref="CounterRun"/>). Beside the runs, an inline box may
/// hold image boxes (atomic inlines); inline layout reports any other child as invalid content.
/// </summary>
/// <param name="Id">The node id.</param>
/// <param name="Style">The computed text properties.</param>
/// <param name="Source">Where the run was written.</param>
public abstract record InlineRun(NodeId Id, TextStyle Style, SourceLocation Source) : Node(Id, Source);

/// <summary>A run of literal text; a leaf of the tree.</summary>
/// <param name="Id">The node id.</param>
/// <param name="Text">The text as written; white space is collapsed during layout (CSS Text 3 §4.1).</param>
/// <param name="Style">The computed text properties.</param>
/// <param name="Source">Where the text was written.</param>
public sealed record TextRun(NodeId Id, string Text, TextStyle Style, SourceLocation Source) : InlineRun(Id, Style, Source);

/// <summary>
/// Text whose value is known only during layout: the number of the page it lands on, or the number of pages
/// (CSS Paged Media 3 §6.1, the page-based counters <c>page</c> and <c>pages</c>, shown as <c>counter(page)</c> and
/// <c>counter(pages)</c> in decimal). The paginator settles the page count with a whole-document fixpoint.
/// </summary>
/// <param name="Id">The node id.</param>
/// <param name="Counter">Which counter.</param>
/// <param name="Style">The computed text properties.</param>
/// <param name="Source">Where the reference was written; reported when the page count does not settle.</param>
public sealed record CounterRun(NodeId Id, PageCounter Counter, TextStyle Style, SourceLocation Source) : InlineRun(Id, Style, Source);

/// <summary>
/// THE INLINE GROUPING of the model: a part of the content of an inline box (its runs, its images in the line and other
/// groups) that means something as a whole: a link (<see cref="SemanticRole.Link"/>) or a span
/// (<see cref="SemanticRole.Span"/>). It is transparent for layout: the lines are broken and painted as if its children
/// stood in its place, it adds no margin, padding, border or line height (CSS 2.1 §10.8 counts an inline box's line
/// height; a front-end that needs it adds an empty run in the element's style, as for any inline element). Groups nest,
/// a link inside a link excepted, and only in the children of an inline box. Each part of a group on one line is one
/// line part (the layout's <c>InlineGroupFragment</c>); its structure element is opened around each line part, which
/// tagged PDF joins into one element. When inline boxes with their own border and background come, they extend this
/// node.
/// </summary>
/// <param name="Id">The node id.</param>
/// <param name="Role">What the group means: <see cref="SemanticRole.Link"/> or <see cref="SemanticRole.Span"/>.</param>
/// <param name="Style">The computed text properties of the element: its font gives the height of a link's area on each
/// line (its ascent above and descent below the baseline), as Chromium draws a link's rectangle from the font of the
/// <c>a</c> element itself.</param>
/// <param name="Children">Its runs, images (boxes laid out by <see cref="LayoutAlgorithmId.Image"/>) and groups, in
/// document order.</param>
/// <param name="Source">Where the group was written.</param>
public sealed record InlineGroup(NodeId Id, SemanticRole Role, TextStyle Style, ValueList<Node> Children, SourceLocation Source) : Node(Id, Source)
{
    /// <summary>The natural language of the group's content as a language tag (ISO 32000-1 §14.9.2; THE RULE FOR A
    /// LANGUAGE TAG, <see cref="LanguageTags"/>), or null when it is that of its parent. A tagged PDF writes it on the
    /// group's structure element where it differs from its parent's.</summary>
    public string? Language { get; init; }
}

/// <summary>The page-based counters (CSS Paged Media 3 §6.1).</summary>
public enum PageCounter
{
    /// <summary>The number of the current page, starting at 1 (<c>counter(page)</c>).</summary>
    PageNumber,

    /// <summary>The total number of pages of the document (<c>counter(pages)</c>).</summary>
    PageCount,
}

/// <summary>
/// An image: a replaced element (CSS 2.1 §10.3.2), the only child of a box laid out by
/// <see cref="LayoutAlgorithmId.Image"/>. Its size comes from <see cref="Width"/> and <see cref="Height"/> and, for
/// what they leave open, from the image's natural size and aspect ratio.
/// </summary>
/// <param name="Id">The node id.</param>
/// <param name="Image">The decoded image.</param>
/// <param name="Width">The specified width, or null for <c>auto</c>.</param>
/// <param name="Height">The specified height, or null for <c>auto</c>.</param>
/// <param name="Source">Where the image was written.</param>
public sealed record ImageNode(NodeId Id, ImageData Image, Length? Width, Length? Height, SourceLocation Source) : Node(Id, Source);
