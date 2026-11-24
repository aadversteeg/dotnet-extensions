using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    /// <summary>
    /// THE RULE FOR A HEADER CELL'S SCOPE IN HTML: what a <c>th</c> heads, written as the <c>/Scope</c> of its <c>TH</c>
    /// element (<see cref="TableHeaderScope"/>).
    /// <list type="number">
    /// <item>A <c>scope</c> attribute of <c>row</c> or <c>rowgroup</c> makes it head its row, <c>col</c> or <c>colgroup</c>
    /// its column, each compared in any ASCII case (an enumerated attribute of the HTML Standard). PDF has no scope for
    /// a group of rows or columns, so a group scope heads the cell's own row or column only.</item>
    /// <item>Without a <c>scope</c>, or with any other value (the auto state), a <c>th</c> in a header row group
    /// (<c>thead</c>), or in a row whose cells are all <c>th</c>, heads its column; any other <c>th</c> heads its row.</item>
    /// </list>
    /// No cell heads both: the first cell of a row of headers heads its column, also with body rows below it and other
    /// header cells beside it. A <c>td</c>'s <c>scope</c> is not read, and neither is any cell's <c>headers</c> attribute.
    /// The HTML Standard's own algorithm for the auto state looks at the cells around the header too; this rule takes the
    /// row and its group only.
    /// </summary>
    private sealed partial class Run
    {
        /// <summary>The header cells without a scope of their own, whose scope their row decides.</summary>
        private readonly HashSet<NodeId> _autoScoped = [];

        /// <summary>The role of a <c>th</c> by its <c>scope</c> attribute; in the auto state the column for now, and noted
        /// for <see cref="WithHeaderScopes"/>.</summary>
        private SemanticRole HeaderCellRole(HtmlElement html, NodeId id)
        {
            var scope = html.Attribute("scope") switch
            {
                { } value when InfraAscii.EqualsIgnoreCase(value, "row") || InfraAscii.EqualsIgnoreCase(value, "rowgroup") => TableHeaderScope.Row,
                { } value when InfraAscii.EqualsIgnoreCase(value, "col") || InfraAscii.EqualsIgnoreCase(value, "colgroup") => TableHeaderScope.Column,
                _ => (TableHeaderScope?)null,
            };
            if (scope is null)
            {
                _autoScoped.Add(id);
            }

            return SemanticRole.TableHeaderCellFor(scope ?? TableHeaderScope.Column);
        }

        /// <summary>A row of a row group with the scope of its header cells in the auto state: the row's, unless the group
        /// is a header group or every cell of the row is a header cell.</summary>
        private Box WithHeaderScopes(Box row, bool inHeaderGroup)
        {
            var cells = row.Children.OfType<Box>().ToList();
            if (inHeaderGroup || cells.All(cell => cell.Role is SemanticRole.TableHeaderCellRole) || !cells.Any(cell => _autoScoped.Contains(cell.Id)))
            {
                return row;
            }

            return row with
            {
                Children = [.. row.Children.Select(child => child is Box cell && _autoScoped.Contains(cell.Id)
                    ? cell with { Role = SemanticRole.TableHeaderCellFor(TableHeaderScope.Row) }
                    : child)],
            };
        }
    }
}
