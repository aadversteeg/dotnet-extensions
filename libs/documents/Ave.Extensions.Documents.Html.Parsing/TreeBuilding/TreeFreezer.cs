using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>
/// Turns the mutable tree into the immutable <see cref="HtmlNode"/> tree. The depth of the result is checked against
/// <see cref="HtmlInputLimits.MaxDepth"/> while it is walked: the adoption agency algorithm can nest elements deeper than
/// the stack of open elements ever was, and every later stage walks the tree recursively.
/// </summary>
internal sealed class TreeFreezer
{
    private readonly Func<int, TextPosition> _position;

    /// <summary>Creates a freezer.</summary>
    /// <param name="position">Converts a source index to a position.</param>
    public TreeFreezer(Func<int, TextPosition> position) => _position = position;

    /// <summary>The first element deeper than the limit, or null.</summary>
    public MutableElement? TooDeepAt { get; private set; }

    /// <summary>Freezes a document.</summary>
    public HtmlDocument Document(MutableDocument document) => new(document.Mode, Children(document, 0));

    /// <summary>Freezes a fragment.</summary>
    public HtmlDocumentFragment Fragment(MutableFragment fragment, int depth = 0) =>
        new(Children(fragment, depth), _position(fragment.Start));

    private ValueList<HtmlNode> Children(MutableParent parent, int depth)
    {
        var children = new List<HtmlNode>();
        for (var child = parent.First; child is not null && TooDeepAt is null; child = child.Next)
        {
            children.Add(Node(child, depth + 1));
        }

        return [.. children];
    }

    private HtmlNode Node(MutableNode node, int depth)
    {
        switch (node)
        {
            case MutableElement element:
                if (depth > HtmlInputLimits.MaxDepth)
                {
                    TooDeepAt ??= element;
                    return new HtmlElement(element.Namespace, element.LocalName, [], [], null, _position(element.Start));
                }

                return new HtmlElement(
                    element.Namespace,
                    element.LocalName,
                    [.. element.Attributes],
                    Children(element, depth),
                    element.Content is null ? null : Fragment(element.Content, depth),
                    _position(element.Start));
            case MutableText text:
                return new HtmlText(text.Data.ToString(), _position(text.Start));
            case MutableComment comment:
                return new HtmlComment(comment.Data, _position(comment.Start));
            case MutableProcessingInstruction instruction:
                return new HtmlProcessingInstruction(instruction.Target, instruction.Data, _position(instruction.Start));
            case MutableDocumentType doctype:
                return new HtmlDocumentType(doctype.Name, doctype.PublicId, doctype.SystemId, _position(doctype.Start));
            default:
                throw new InvalidOperationException($"A {node.GetType().Name} cannot be a child.");
        }
    }
}
