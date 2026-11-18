using System.Collections.Generic;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// A cascade layer (CSS Cascade 5 §6.4) of one origin: the root is the origin's unlayered declarations. Layers are
/// ordered by their first appearance, sublayers inside their parent; a layer's own declarations come after all its
/// sublayers (§6.4.4, "unlayered declarations within a layer"). Built while the rules are flattened; its rank is fixed
/// once, at the end, and read-only after.
/// </summary>
internal sealed class LayerNode
{
    private readonly Dictionary<string, LayerNode> _named = new(System.StringComparer.Ordinal);
    private readonly List<LayerNode> _children = [];

    private LayerNode(string? name) => Name = name;

    /// <summary>The layer's name part, or null for the root and for anonymous layers.</summary>
    public string? Name { get; }

    /// <summary>The precedence of normal declarations in the layer within its origin: higher wins. The root (unlayered)
    /// has the highest.</summary>
    public int Rank { get; private set; }

    /// <summary>A new root: the unlayered declarations of an origin.</summary>
    public static LayerNode Root() => new(null);

    /// <summary>The layer of a dotted name below this one, declared (in order) if it is new (§6.4.2).</summary>
    public LayerNode Resolve(string dottedName)
    {
        var node = this;
        foreach (var part in dottedName.Split('.'))
        {
            if (!node._named.TryGetValue(part, out var child))
            {
                child = new LayerNode(part);
                node._named[part] = child;
                node._children.Add(child);
            }

            node = child;
        }

        return node;
    }

    /// <summary>A new anonymous layer below this one (§6.4.2: each <c>@layer</c> block without a name is its own).</summary>
    public LayerNode Anonymous()
    {
        var child = new LayerNode(null);
        _children.Add(child);
        return child;
    }

    /// <summary>Gives every layer of the tree its rank: sublayers first, in order, then the layer itself. Iterative, so a
    /// long dotted name cannot exhaust the stack.</summary>
    public void AssignRanks()
    {
        var next = 0;
        var stack = new Stack<(LayerNode Node, int Child)>();
        stack.Push((this, 0));
        while (stack.Count > 0)
        {
            var (node, child) = stack.Pop();
            if (child < node._children.Count)
            {
                stack.Push((node, child + 1));
                stack.Push((node._children[child], 0));
            }
            else
            {
                node.Rank = next++;
            }
        }
    }
}
