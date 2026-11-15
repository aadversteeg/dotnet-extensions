using System.Collections.Immutable;
using Ave.Documents.Core;

namespace Ave.Documents.Model;

/// <summary>
/// A node of the document tree: a <see cref="Box"/> or a <see cref="TextRun"/>. Every node has an id (its
/// pre-order position) and the source location of the code or markup that produced it.
/// </summary>
/// <param name="Id">The node id, unique in the document.</param>
/// <param name="Source">Where the node was written; reported with every error about it.</param>
public abstract record Node(NodeId Id, SourceLocation Source);

/// <summary>
/// A box: a rectangle that is laid out by the algorithm <see cref="Layout"/> names and that contains child
/// nodes. In CSS terms a block-level box (CSS Display 3 §2); <see cref="Layout"/> is its inner display type.
/// </summary>
/// <remarks>
/// Which children a box may have depends on its algorithm: <see cref="LayoutAlgorithmId.Flow"/> stacks child
/// boxes, <see cref="LayoutAlgorithmId.Inline"/> sets text runs in lines (a block container whose content is an
/// inline formatting context, CSS 2.1 §9.2.1). The algorithm reports an error for children it cannot lay out.
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
    ImmutableArray<Node> Children,
    SourceLocation Source) : Node(Id, Source);

/// <summary>A run of text in one style; a leaf of the tree, laid out by its parent's inline algorithm.</summary>
/// <param name="Id">The node id.</param>
/// <param name="Text">The text as written; white space is collapsed during layout (CSS Text 3 §4.1).</param>
/// <param name="Style">The computed text properties.</param>
/// <param name="Source">Where the text was written.</param>
public sealed record TextRun(NodeId Id, string Text, TextStyle Style, SourceLocation Source) : Node(Id, Source);
