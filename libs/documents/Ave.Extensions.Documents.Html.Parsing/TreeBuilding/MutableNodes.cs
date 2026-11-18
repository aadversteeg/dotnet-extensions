using System.Collections.Generic;
using System.Text;
using Ave.Extensions.Documents.Html.Parsing.Tokenizing;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>
/// The tree that tree construction builds and rearranges (foster parenting, the adoption agency algorithm move nodes
/// around), before it is frozen into the immutable <see cref="HtmlNode"/> tree. Children are a doubly linked list, so
/// inserting before a reference child, removing a node and finding the previous sibling are constant work, whatever
/// the number of children.
/// </summary>
internal abstract class MutableNode
{
    /// <summary>The parent, or null when the node is not in a tree.</summary>
    public MutableParent? Parent { get; set; }

    /// <summary>The previous sibling.</summary>
    public MutableNode? Previous { get; set; }

    /// <summary>The next sibling.</summary>
    public MutableNode? Next { get; set; }

    /// <summary>Where the node starts in the source.</summary>
    public int Start { get; init; }

    /// <summary>Removes the node from its parent, if it has one.</summary>
    public void Remove()
    {
        if (Parent is not { } parent)
        {
            return;
        }

        if (Previous is null)
        {
            parent.First = Next;
        }
        else
        {
            Previous.Next = Next;
        }

        if (Next is null)
        {
            parent.Last = Previous;
        }
        else
        {
            Next.Previous = Previous;
        }

        Parent = null;
        Previous = null;
        Next = null;
    }
}

/// <summary>A node that has children: a document, a document fragment or an element.</summary>
internal abstract class MutableParent : MutableNode
{
    /// <summary>The first child.</summary>
    public MutableNode? First { get; set; }

    /// <summary>The last child.</summary>
    public MutableNode? Last { get; set; }

    /// <summary>Inserts a node before a reference child, or at the end when the reference is null (DOM "insert").</summary>
    /// <param name="node">The node; it is removed from its old parent first.</param>
    /// <param name="before">The reference child, or null.</param>
    public void Insert(MutableNode node, MutableNode? before)
    {
        node.Remove();
        node.Parent = this;
        if (before is null)
        {
            node.Previous = Last;
            if (Last is null)
            {
                First = node;
            }
            else
            {
                Last.Next = node;
            }

            Last = node;
            return;
        }

        node.Next = before;
        node.Previous = before.Previous;
        if (before.Previous is null)
        {
            First = node;
        }
        else
        {
            before.Previous.Next = node;
        }

        before.Previous = node;
    }

    /// <summary>Whether a child is an element (a document may have only one).</summary>
    public bool HasElementChild()
    {
        for (var child = First; child is not null; child = child.Next)
        {
            if (child is MutableElement)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The children, first to last (a copy, so the caller may move them).</summary>
    public List<MutableNode> Children()
    {
        var children = new List<MutableNode>();
        for (var child = First; child is not null; child = child.Next)
        {
            children.Add(child);
        }

        return children;
    }
}

/// <summary>The document.</summary>
internal sealed class MutableDocument : MutableParent
{
    /// <summary>The document's mode.</summary>
    public DocumentMode Mode { get; set; } = DocumentMode.NoQuirks;
}

/// <summary>A document fragment: a template's contents, or the result of a fragment parse.</summary>
internal sealed class MutableFragment : MutableParent
{
}

/// <summary>An element.</summary>
internal sealed class MutableElement : MutableParent
{
    /// <summary>Its namespace.</summary>
    public required ElementNamespace Namespace { get; init; }

    /// <summary>Its local name.</summary>
    public required string LocalName { get; init; }

    /// <summary>Its attributes, in order.</summary>
    public List<HtmlAttribute> Attributes { get; } = [];

    /// <summary>The token it was created for (to create more elements for the same token, §13.2.4.3).</summary>
    public required Token Token { get; init; }

    /// <summary>For an HTML template: its contents.</summary>
    public MutableFragment? Content { get; init; }

    /// <summary>Whether it is on the stack of open elements now (kept by the stack, so the check is constant work).</summary>
    public bool OnStack { get; set; }

    /// <summary>Whether it is the HTML element of that name.</summary>
    public bool IsHtml(string localName) => Namespace == ElementNamespace.Html && LocalName == localName;

    /// <summary>Whether it is in that namespace with that local name.</summary>
    public bool Is(ElementNamespace ns, string localName) => Namespace == ns && LocalName == localName;
}

/// <summary>Text.</summary>
internal sealed class MutableText : MutableNode
{
    /// <summary>The characters.</summary>
    public StringBuilder Data { get; } = new();
}

/// <summary>A comment.</summary>
internal sealed class MutableComment : MutableNode
{
    /// <summary>The comment's text.</summary>
    public required string Data { get; init; }
}

/// <summary>A processing instruction.</summary>
internal sealed class MutableProcessingInstruction : MutableNode
{
    /// <summary>Its target.</summary>
    public required string Target { get; init; }

    /// <summary>Its data.</summary>
    public required string Data { get; init; }
}

/// <summary>A doctype.</summary>
internal sealed class MutableDocumentType : MutableNode
{
    /// <summary>The name, or empty.</summary>
    public required string Name { get; init; }

    /// <summary>The public identifier, or empty.</summary>
    public required string PublicId { get; init; }

    /// <summary>The system identifier, or empty.</summary>
    public required string SystemId { get; init; }
}
