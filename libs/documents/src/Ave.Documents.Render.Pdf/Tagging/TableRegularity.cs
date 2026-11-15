using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Render.Pdf.Tagging;

/// <summary>
/// PDF/UA's rule that a table is a regular grid: "Table rows shall have the same number of columns" and "Table columns
/// shall have the same number of rows", counting column and row spans (ISO 14289-1 §7.2, veraPDF rules 7.2-42 and
/// 7.2-41). The layout engine allows a row with fewer cells than columns — the rest of the row is simply empty — so a
/// PDF/UA profile has to refuse such a table instead of writing a structure a screen reader cannot map onto columns.
/// </summary>
internal static class TableRegularity
{
    /// <summary>
    /// Builds each table's grid from its structure elements, as a reader does: rows in order, row groups (THead, TBody)
    /// ending row spans, each cell taking the first free slot of its row and as many slots as its span covers (CSS 2.1
    /// §17.5 slot filling). A row with a free slot left of the widest row's last column is a violation.
    /// </summary>
    /// <param name="tree">The structure tree.</param>
    /// <returns>One error per row that does not fill the table's columns, at the row's source location.</returns>
    public static IEnumerable<Error> Violations(StructureTree tree) =>
        tree.Elements.Where(element => element.Type.Value == "Table").SelectMany(table => Violations(tree, table));

    private static IEnumerable<Error> Violations(StructureTree tree, StructureElement table)
    {
        var groups = Children(tree, table)
            .Select(child => child.Type.Value == "TR" ? [child] : Children(tree, child).Where(row => row.Type.Value == "TR").ToImmutableArray())
            .ToImmutableArray();
        var rows = groups.SelectMany(group => Slots(tree, group)).ToImmutableArray();
        var columns = rows.Select(row => row.Filled.Count == 0 ? 0 : row.Filled.Max() + 1).DefaultIfEmpty(0).Max();
        return rows
            .Where(row => row.Filled.Count < columns)
            .Select(row => new Error(
                    DocumentErrorCodes.Pdf.Conformance.PdfUa1.IrregularTable,
                    $"PDF/UA-1 needs every table row to fill all {columns} columns; this row fills {row.Filled.Count} (ISO 14289-1 §7.2) ({row.Row.Start.Source}).")
                .With(ErrorMetadataKeys.Node, row.Row.Start.Node.ToString())
                .With(ErrorMetadataKeys.Value, $"{row.Filled.Count}/{columns}")
                .WithSource(row.Row.Start.Source));
    }

    /// <summary>The slots each row of one row group fills; a row span never reaches past the group's last row.</summary>
    private static IEnumerable<(StructureElement Row, HashSet<int> Filled)> Slots(StructureTree tree, ImmutableArray<StructureElement> group)
    {
        var filled = group.Select(_ => new HashSet<int>()).ToArray();
        for (var row = 0; row < group.Length; row++)
        {
            var column = 0;
            foreach (var cell in Children(tree, group[row]))
            {
                while (filled[row].Contains(column))
                {
                    column++;
                }

                var span = cell.Start.Span;
                for (var spanned = row; spanned < System.Math.Min(row + span.Rows, group.Length); spanned++)
                {
                    filled[spanned].UnionWith(Enumerable.Range(column, span.Columns));
                }

                column += span.Columns;
            }
        }

        return group.Select((row, index) => (row, filled[index]));
    }

    private static IEnumerable<StructureElement> Children(StructureTree tree, StructureElement element) =>
        element.Kids.OfType<ElementKid>().Select(kid => tree.Elements[kid.Index]);
}
