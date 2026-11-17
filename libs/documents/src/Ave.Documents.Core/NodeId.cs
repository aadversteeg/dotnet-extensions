namespace Ave.Documents.Core;

/// <summary>
/// Identifies one node of the document model. Assigned by the front-end in document (pre-order) order, so the
/// same document always gets the same ids. The id travels through layout into the page model, where the
/// structure markers of a node split over two pages carry the same id.
/// </summary>
/// <param name="Value">The node's position in a pre-order walk of the document tree, starting at 0.</param>
public readonly record struct NodeId(int Value)
{
    /// <summary>Formats as <c>#7</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"#{Value}";
}
