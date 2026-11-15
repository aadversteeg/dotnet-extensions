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
    /// Builds each table's grid from its structure elements, as a reader does, with <see cref="TableSlots"/> — the
    /// same slot-filling rule layout uses: rows in order; row groups (THead, TBody) end row spans, and consecutive
    /// rows directly under the table form one group, as in the HTML table model; each cell takes the first free slot
    /// of its row and covers what its span covers. A row with a free slot left of the widest row's last column is a
    /// violation.
    /// </summary>
    /// <param name="tree">The structure tree.</param>
    /// <returns>One error per row that does not fill the table's columns, at the row's source location.</returns>
    public static IEnumerable<Error> Violations(StructureTree tree) =>
        tree.Elements.Where(element => element.Type.Value == "Table").SelectMany(table => Violations(tree, table));

    private static IEnumerable<Error> Violations(StructureTree tree, StructureElement table)
    {
        var groups = Groups(tree, table);
        var rows = groups.SelectMany(group => group).ToImmutableArray();
        var grid = TableSlots.Fill([.. groups.Select(group => (IReadOnlyList<IReadOnlyList<CellSpan>>)[.. group.Select(row => (IReadOnlyList<CellSpan>)[.. Children(tree, row).Select(cell => cell.Start.Span)])])]);
        return grid.IncompleteRows.Select(index =>
        {
            var (row, filled) = (rows[index], grid.Slots[index].Count(slot => slot >= 0));
            return new Error(
                    DocumentErrorCodes.Pdf.Conformance.PdfUa1.IrregularTable,
                    $"PDF/UA-1 needs every table row to fill all {grid.ColumnCount} columns; this row fills {filled} (ISO 14289-1 §7.2) ({row.Start.Source}).")
                .With(ErrorMetadataKeys.Node, row.Start.Node.ToString())
                .With(ErrorMetadataKeys.Value, $"{filled}/{grid.ColumnCount}")
                .WithSource(row.Start.Source);
        });
    }

    /// <summary>The table's row groups: each THead or TBody, and each run of rows directly under the table.</summary>
    private static ImmutableArray<ImmutableArray<StructureElement>> Groups(StructureTree tree, StructureElement table)
    {
        var groups = ImmutableArray.CreateBuilder<ImmutableArray<StructureElement>>();
        var loose = ImmutableArray.CreateBuilder<StructureElement>();
        foreach (var child in Children(tree, table))
        {
            if (child.Type.Value == "TR")
            {
                loose.Add(child);
                continue;
            }

            if (loose.Count > 0)
            {
                groups.Add(loose.ToImmutable());
                loose.Clear();
            }

            groups.Add([.. Children(tree, child).Where(row => row.Type.Value == "TR")]);
        }

        if (loose.Count > 0)
        {
            groups.Add(loose.ToImmutable());
        }

        return groups.ToImmutable();
    }

    private static IEnumerable<StructureElement> Children(StructureTree tree, StructureElement element) =>
        element.Kids.OfType<ElementKid>().Select(kid => tree.Elements[kid.Index]);
}
