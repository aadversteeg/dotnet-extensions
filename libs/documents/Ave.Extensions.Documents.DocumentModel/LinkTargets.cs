using System.Collections.Generic;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// Sets the targets of links once the whole tree is built. A link inside the document may lead to a node that comes after
/// it (a table of contents before its headings), whose id a front-end knows only when it has built that node, so both
/// front-ends build such a link with a provisional target and then give every link its node here, in one pass.
/// </summary>
public static class LinkTargets
{
    /// <summary>
    /// The tree with the target of every link group named in <paramref name="targets"/> (by the group's id) replaced, and
    /// every link group named with no target (null: a link a front-end refuses) replaced by its content, which stays where
    /// it is as content of the group's parent; every other node as it is, the same instance where nothing below it changes. Recursive, one level per box or group;
    /// a front-end bounds the depth of what it builds (<see cref="ModelLimits.MaxNestingDepth"/>) before it calls this.
    /// </summary>
    /// <param name="root">The tree.</param>
    /// <param name="targets">Per link group's id, its target.</param>
    /// <returns>The tree with the targets set.</returns>
    public static Box Resolve(Box root, IReadOnlyDictionary<NodeId, LinkTarget?> targets) =>
        targets.Count == 0 ? root : (Box)Resolved(root, targets);

    private static Node Resolved(Node node, IReadOnlyDictionary<NodeId, LinkTarget?> targets)
    {
        switch (node)
        {
            case Box box:
                var children = Children(box.Children, targets);
                return children is null ? box : box with { Children = children.Value };
            case InlineGroup group:
                var content = Children(group.Children, targets);
                var role = group.Role is SemanticRole.LinkRole link && targets.TryGetValue(group.Id, out var target) && target is not null
                    ? link with { Target = target }
                    : group.Role;
                return content is null && ReferenceEquals(role, group.Role) ? group : group with { Role = role, Children = content ?? group.Children };
            default:
                return node;
        }
    }

    /// <summary>The children with their targets set and the links without a target replaced by their content, or null when
    /// none of them changes.</summary>
    private static ValueList<Node>? Children(ValueList<Node> children, IReadOnlyDictionary<NodeId, LinkTarget?> targets)
    {
        var changed = false;
        var resolved = new List<Node>(children.Count);
        foreach (var child in children)
        {
            var node = Resolved(child, targets);
            if (node is InlineGroup { Role: SemanticRole.LinkRole } group && targets.TryGetValue(group.Id, out var target) && target is null)
            {
                resolved.AddRange(group.Children);
                changed = true;
            }
            else
            {
                resolved.Add(node);
                changed |= !ReferenceEquals(node, child);
            }
        }

        return changed ? resolved.ToValueList() : null;
    }
}
