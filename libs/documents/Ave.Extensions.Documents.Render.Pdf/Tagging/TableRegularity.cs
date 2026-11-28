using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Render.Pdf.Tagging;

/// <summary>
/// PDF/UA's rule that a table is a regular grid: "Table rows shall have the same number of columns" and "Table columns
/// shall have the same number of rows", counting column and row spans (ISO 14289-1 §7.2, veraPDF rules 7.2-42 and
/// 7.2-41). The layout engine allows a row with fewer cells than columns — the rest of the row is simply empty — so a
/// PDF/UA profile has to refuse such a table instead of writing a structure a screen reader cannot map onto columns.
/// </summary>
internal static class TableRegularity
{
    /// <summary>
    /// The most slots the check fills for one table: 4 194 304 (2^22), a thousand columns of four thousand rows, far
    /// beyond a table a reader can take in. Each slot is one number (16 MiB at the cap); with the slot lists that grow
    /// as cells are placed and the grid copied from them, filling a grid at the cap allocates about 51 MiB, whatever its
    /// spans (measured, and pinned below 64 MiB by a Core TableSlotsTests test). A larger table is refused
    /// (<c>Documents.Pdf.Conformance.PdfUa1.TableTooLarge</c>) rather than checked with memory that the page model's
    /// spans, not its size, decide.
    /// </summary>
    public const int MaxGridSlots = 1 << 22;

    /// <summary>
    /// Builds each table's grid from its structure elements, as a reader does, with <see cref="TableSlots"/> — the
    /// same slot-filling rule layout uses: rows in order; row groups (THead, TBody) end row spans, and consecutive
    /// rows directly under the table form one group, as in the HTML table model; each cell takes the first free slot
    /// of its row and covers what its span covers. A regular grid has as many columns as its first row covers (the sum
    /// of that row's column spans; nothing spans into the first row from above), so the grid is filled with that column
    /// count: a row with a free slot is a violation, and so is a cell that reaches past the last column. The grid is the
    /// first row's width times the number of rows, whatever the spans of later rows, and at most
    /// <see cref="MaxGridSlots"/>.
    /// </summary>
    /// <param name="tree">The structure tree.</param>
    /// <returns>One error per row that does not fill the table's columns, at the row's source location.</returns>
    public static IEnumerable<Error> Violations(StructureTree tree) =>
        tree.Elements.Where(element => element.Type.Value == "Table").SelectMany(table => Violations(tree, table));

    private static IEnumerable<Error> Violations(StructureTree tree, StructureElement table)
    {
        var groups = Groups(tree, table);
        var rows = groups.SelectMany(group => group).ToImmutableArray();
        var spans = groups.Select(group => (IReadOnlyList<IReadOnlyList<CellSpan>>)[.. group.Select(row => (IReadOnlyList<CellSpan>)[.. Children(tree, row).Select(cell => cell.Start.Span)])]).ToList();
        var columnCount = spans.SelectMany(group => group).Select(row => row.Sum(span => (long)span.Columns)).FirstOrDefault();
        if (columnCount * rows.Length > MaxGridSlots || columnCount > TableSlots.MaxColumns)
        {
            return [new Error(
                    PdfRenderErrorCodes.Conformance.PdfUa1.TableTooLarge,
                    $"The PDF/UA-1 check of a regular table grid fills at most {MaxGridSlots} slots and {TableSlots.MaxColumns} columns; this table of {columnCount} columns and {rows.Length} rows has more ({table.Start.Source}).")
                .With(ErrorMetadataKeys.Node, table.Start.Node.ToString())
                .With(ErrorMetadataKeys.Value, $"{columnCount}x{rows.Length}")
                .WithSource(table.Start.Source)];
        }

        var grid = TableSlots.Fill(spans, (int)columnCount);
        var cellProblems = grid.Problems
            .Where(problem => problem.Kind is TableSlots.ProblemKind.SpanPastGroup or TableSlots.ProblemKind.Overlap or TableSlots.ProblemKind.PastLastColumn)
            .Select(problem => (Row: rows[problem.Row], Cell: Children(tree, rows[problem.Row]).ElementAt(problem.Cell)))
            .Select(found => new Error(
                    PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable,
                    $"PDF/UA-1 needs a regular table grid; this cell's row span runs past its row group, it overlaps another cell, or it reaches past the {grid.ColumnCount} columns of the first row (ISO 14289-1 §7.2) ({found.Cell.Start.Source}).")
                .With(ErrorMetadataKeys.Node, found.Cell.Start.Node.ToString())
                .With(ErrorMetadataKeys.Value, found.Cell.Start.Span.ToString())
                .WithSource(found.Cell.Start.Source));
        var kidTypes = Children(tree, table).Select(kid => kid.Type.Value).ToList();
        var headWithoutBody = kidTypes.Contains("THead") && !kidTypes.Contains("TBody")
            ? [new Error(
                    PdfRenderErrorCodes.Conformance.PdfUa1.TableHeadWithoutBody,
                    $"PDF/UA-1 needs body rows in a table that has a head (veraPDF rule 7.2-14) ({table.Start.Source}).")
                .With(ErrorMetadataKeys.Node, table.Start.Node.ToString())
                .WithSource(table.Start.Source)]
            : Array.Empty<Error>();
        return headWithoutBody.Concat(cellProblems).Concat(grid.IncompleteRows.Select(index =>
        {
            var (row, filled) = (rows[index], grid.Slots[index].Count(slot => slot >= 0));
            return new Error(
                    PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable,
                    $"PDF/UA-1 needs every table row to fill all {grid.ColumnCount} columns; this row fills {filled} (ISO 14289-1 §7.2) ({row.Start.Source}).")
                .With(ErrorMetadataKeys.Node, row.Start.Node.ToString())
                .With(ErrorMetadataKeys.Value, $"{filled}/{grid.ColumnCount}")
                .WithSource(row.Start.Source);
        }));
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
