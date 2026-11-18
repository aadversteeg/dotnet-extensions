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
    public HtmlDocument Document(MutableDocument document) => new(document.Mode, Freeze(document, 0));

    /// <summary>Freezes a fragment.</summary>
    public HtmlDocumentFragment Fragment(MutableFragment fragment) => new(Freeze(fragment, 0), _position(fragment.Start));

    /// <summary>One parent being frozen: its children so far, and the next child to freeze. An element passes twice:
    /// its children, then (for a template) its contents.</summary>
    private sealed class Frame(MutableParent parent, int depth)
    {
        /// <summary>The parent being frozen.</summary>
        public MutableParent Parent { get; } = parent;

        /// <summary>Its depth in the tree, for the limit on depth.</summary>
        public int Depth { get; } = depth;

        /// <summary>The next child to freeze; null once all are.</summary>
        public MutableNode? Next { get; set; } = parent.First;

        /// <summary>The frozen children so far.</summary>
        public List<HtmlNode> Children { get; } = [];

        /// <summary>For a template, its frozen children, kept while its contents are frozen.</summary>
        public ValueList<HtmlNode>? ElementChildren { get; set; }

        /// <summary>Whether the template's contents, not its children, are being frozen.</summary>
        public bool InContent { get; set; }
    }

    /// <summary>The children of a parent, frozen with an explicit stack (no recursion: the depth a hand-built or
    /// adoption-agency tree reaches cannot overflow the thread's stack).</summary>
    private ValueList<HtmlNode> Freeze(MutableParent root, int depth)
    {
        var stack = new Stack<Frame>();
        stack.Push(new Frame(root, depth));
        ValueList<HtmlNode> finished = [];
        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (frame.Next is { } child && TooDeepAt is null)
            {
                frame.Next = child.Next;
                if (child is MutableElement element)
                {
                    if (frame.Depth + 1 > HtmlInputLimits.MaxDepth)
                    {
                        TooDeepAt ??= element;
                        frame.Children.Add(new HtmlElement(element.Namespace, element.LocalName, [], [], null, _position(element.Start)));
                        continue;
                    }

                    stack.Push(new Frame(element, frame.Depth + 1));
                    continue;
                }

                frame.Children.Add(Leaf(child));
                continue;
            }

            // The frame's children are done.
            if (frame.Parent is MutableElement { Content: { } content } template && !frame.InContent && TooDeepAt is null)
            {
                // A template: its contents next, at the template's depth (§4.12.3), then the template itself.
                frame.ElementChildren = [.. frame.Children];
                frame.Children.Clear();
                frame.InContent = true;
                frame.Next = content.First;
                continue;
            }

            stack.Pop();
            ValueList<HtmlNode> children = [.. frame.Children];
            if (stack.Count == 0)
            {
                finished = children;
                break;
            }

            var owner = (MutableElement)frame.Parent;
            var frozen = frame.InContent
                ? new HtmlElement(owner.Namespace, owner.LocalName, owner.FrozenAttributes(), frame.ElementChildren!.Value,
                    new HtmlDocumentFragment(children, _position(owner.Content!.Start)), _position(owner.Start))
                : new HtmlElement(owner.Namespace, owner.LocalName, owner.FrozenAttributes(), children,
                    owner.Content is null ? null : new HtmlDocumentFragment([], _position(owner.Content.Start)), _position(owner.Start));
            stack.Peek().Children.Add(frozen);
        }

        return finished;
    }

    private HtmlNode Leaf(MutableNode node) => node switch
    {
        MutableText text => new HtmlText(text.Data.ToString(), _position(text.Start)),
        MutableComment comment => new HtmlComment(comment.Data, _position(comment.Start)),
        MutableProcessingInstruction instruction => new HtmlProcessingInstruction(instruction.Target, instruction.Data, _position(instruction.Start)),
        MutableDocumentType doctype => new HtmlDocumentType(doctype.Name, doctype.PublicId, doctype.SystemId, _position(doctype.Start)),
        _ => throw new InvalidOperationException($"A {node.GetType().Name} cannot be a child."),
    };
}
