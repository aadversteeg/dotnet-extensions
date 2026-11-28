using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Translates tables and rows of blocks into tracks boxes (<see cref="LayoutAlgorithmId.Tracks"/>): the table box,
/// its row groups, their rows and the cells, as in the CSS table model (CSS 2.1 §17.2). Both elements share this
/// structure; a row of blocks is a one-row table whose parts have the generic role, so tagged PDF will not call it a
/// table.
/// </summary>
internal static class TableTranslator
{
    /// <summary>One row group as the front-end forms it: the header rows, an explicit <see cref="RowGroupElement"/>, or
    /// the body rows between two explicit groups (a group the front-end forms itself, so that every row has one).</summary>
    private sealed record Group(bool IsHeader, RowGroupElement? Explicit, ImmutableArray<TableRowElement> Rows);

    /// <summary>How many nodes a table becomes: itself, its groups, rows, cells and the cells' content.</summary>
    public static int NodeCount(TableElement table) =>
        1 + Groups(table).Groups.Sum(GroupNodeCount);

    /// <summary>How many nodes a row of blocks becomes: the tracks box, one group, one row, a cell per item.</summary>
    public static int NodeCount(RowElement row) => 3 + row.Items.Sum(item => 1 + DocumentTranslator.NodeCount(item));

    private static int GroupNodeCount(Group group) => 1 + group.Rows.Sum(RowNodeCount);

    private static int RowNodeCount(TableRowElement row) => 1 + row.Cells.Sum(CellNodeCount);

    private static int CellNodeCount(CellElement cell) => 1 + cell.Content.Sum(DocumentTranslator.NodeCount);

    /// <summary>
    /// A table: validates its columns and parts, and builds table → groups → rows → cells. The table's text values
    /// are inherited by its rows and cells; a cell takes padding and border from the table's cell defaults when it does
    /// not set its own. A table has no padding in the collapsing border model (CSS 2.1 §17.6.2: "a table does not have
    /// padding"), so padding on a table is reported as not applicable.
    /// </summary>
    public static Result<Node, Error> Table(TableElement table, NodeId id, bool pageBreakBefore, DocumentTranslator.Inherited inherited)
    {
        var box = Cascade.Box(table.Format, table.Style);
        var text = Cascade.Text(table.Format, table.Style);
        var (groups, misplaced) = Groups(table);
        var problems = DocumentTranslator.BoxProblems(box, table.Source)
            .Concat(InputChecks.Text(text, table.Source))
            .Concat(InputChecks.Columns(table.Columns, table.Source))
            .Concat(InputChecks.NotApplicable(box.Padding, "padding", "table", table.Source))
            .Concat(misplaced.Select(InputChecks.MisplacedHeaderRow))
            .Concat(table.Columns.Count > 0 ? InputChecks.TableShape([.. groups.Select(group => (IReadOnlyList<TableRowElement>)group.Rows)], table.Columns.Count, table.Source) : [])
            .ToImmutableArray();
        var own = inherited.With(text);
        var children = Parts(groups, new NodeId(id.Value + 1), own, table.CellDefaults, isLayoutOnly: false);
        return DocumentTranslator.WithProblems(problems, children).OnSuccessMap(nodes => (Node)new Box(
            id,
            SemanticRole.Table,
            LayoutAlgorithmId.Tracks,
            DocumentTranslator.BoxStyleOf(box, pageBreakBefore, own) with { Columns = table.Columns, DecorationBreak = table.LinesAtBreaks, Width = table.Width },
            nodes,
            table.Source));
    }

    /// <summary>
    /// A row of blocks: a tracks box with one group of one row, each item in a cell of its own, all with the generic
    /// role. Without column sizes every item gets an equal fill share.
    /// </summary>
    public static Result<Node, Error> Row(RowElement row, NodeId id, bool pageBreakBefore, DocumentTranslator.Inherited inherited)
    {
        var box = Cascade.Box(row.Format, row.Style);
        var text = Cascade.Text(row.Format, row.Style);
        var columns = row.EffectiveColumns;
        var problems = DocumentTranslator.BoxProblems(box, row.Source)
            .Concat(InputChecks.Text(text, row.Source))
            .Concat(row.Items.Count == 0 ? InputChecks.RowItems(0, row.Source) : InputChecks.Columns(columns, row.Source))
            .Concat(row.ColumnSizes.Count > 0 ? InputChecks.ColumnCount(row.ColumnSizes.Count, row.Items.Count, row.Source) : [])
            .ToImmutableArray();
        var own = inherited.With(text);
        var cells = row.Items.Select(item => new CellElement([item], CellSpan.Single, row.VerticalAlignment, Style.Empty, Style.Empty, item.Source));
        var tableRow = new TableRowElement([.. cells], IsHeader: false, Style.Empty, Style.Empty, row.Source);
        var children = Parts([new Group(false, null, [tableRow])], new NodeId(id.Value + 1), own, BoxFormat.None, isLayoutOnly: true);
        return DocumentTranslator.WithProblems(problems, children).OnSuccessMap(nodes => (Node)new Box(
            id,
            SemanticRole.Generic,
            LayoutAlgorithmId.Tracks,
            DocumentTranslator.BoxStyleOf(box, pageBreakBefore, own) with { Columns = columns },
            nodes,
            row.Source));
    }

    /// <summary>
    /// The groups of a table's row list: the header rows at the start form the header group; each explicit row group
    /// is a group; the body rows between explicit groups form an anonymous group. A header row anywhere else is
    /// returned as misplaced.
    /// </summary>
    private static (ImmutableArray<Group> Groups, ImmutableArray<SourceLocation> Misplaced) Groups(TableElement table)
    {
        var groups = ImmutableArray.CreateBuilder<Group>();
        var misplaced = ImmutableArray.CreateBuilder<SourceLocation>();
        var loose = ImmutableArray.CreateBuilder<TableRowElement>();
        var header = table.Rows.TakeWhile(part => part is TableRowElement { IsHeader: true }).Cast<TableRowElement>().ToImmutableArray();
        if (!header.IsEmpty)
        {
            groups.Add(new Group(true, null, header));
        }

        void FlushLoose()
        {
            if (loose.Count > 0)
            {
                groups.Add(new Group(false, null, loose.ToImmutable()));
                loose.Clear();
            }
        }

        foreach (var part in table.Rows.Skip(header.Length))
        {
            switch (part)
            {
                case TableRowElement row:
                    if (row.IsHeader)
                    {
                        misplaced.Add(row.Source);
                    }

                    loose.Add(row);
                    break;
                case RowGroupElement group:
                    FlushLoose();
                    misplaced.AddRange(group.Rows.Where(row => row.IsHeader).Select(row => row.Source));
                    groups.Add(new Group(false, group, [.. group.Rows]));
                    break;
            }
        }

        FlushLoose();
        return (groups.ToImmutable(), misplaced.ToImmutable());
    }

    /// <summary>Translates the groups, handing out ids in pre-order.</summary>
    private static Result<ValueList<Node>, Error> Parts(
        ImmutableArray<Group> groups, NodeId firstId, DocumentTranslator.Inherited inherited, BoxFormat cellDefaults, bool isLayoutOnly)
    {
        var next = firstId.Value;
        var placed = new List<(Group Group, NodeId Id)>();
        foreach (var group in groups)
        {
            placed.Add((group, new NodeId(next)));
            next += GroupNodeCount(group);
        }

        var hasBody = groups.Any(group => !group.IsHeader);
        return placed
            .TraverseAll(item => GroupNode(item.Group, item.Id, inherited, cellDefaults, isLayoutOnly, hasBody))
            .OnSuccessMap(nodes => nodes.ToValueList());
    }

    /// <summary>A row group box. An explicit group carries its own text, border, background and keep rule; it has no
    /// margin or padding (CSS 2.1 §17.5: "Internal table elements do not have margins"; only cells have padding).
    /// Header rows are a table head (THead) only when the table has body rows for them to head: a table head without
    /// a body is a structure PDF/UA refuses (veraPDF rule 7.2-14). The header rows of a table that has only header rows
    /// are its body; their cells stay header cells.</summary>
    private static Result<Node, Error> GroupNode(Group group, NodeId id, DocumentTranslator.Inherited inherited, BoxFormat cellDefaults, bool isLayoutOnly, bool hasBody)
    {
        var (format, style, source) = group.Explicit is { } explicitGroup
            ? (explicitGroup.Format, explicitGroup.Style, explicitGroup.Source)
            : (Style.Empty, Style.Empty, group.Rows[0].Source);
        var box = Cascade.Box(format, style);
        var text = Cascade.Text(format, style);
        var problems = PartProblems(box, text, "row group", source);
        var own = inherited.With(text);
        var next = id.Value + 1;
        var rows = new List<(TableRowElement Row, NodeId Id)>();
        foreach (var row in group.Rows)
        {
            rows.Add((row, new NodeId(next)));
            next += RowNodeCount(row);
        }

        var children = rows
            .TraverseAll(item => RowNode(item.Row, item.Id, own, cellDefaults, isLayoutOnly))
            .OnSuccessMap(nodes => nodes.ToValueList());
        var role = isLayoutOnly ? SemanticRole.Generic : group.IsHeader && hasBody ? SemanticRole.TableHead : SemanticRole.TableBody;
        var part = group.IsHeader ? TableDisplay.HeaderGroup : TableDisplay.RowGroup;
        return DocumentTranslator.WithProblems(problems, children).OnSuccessMap(nodes =>
            (Node)new Box(id, role, LayoutAlgorithmId.Tracks, DocumentTranslator.BoxStyleOf(box, false, own) with { TableDisplay = part }, nodes, source));
    }

    /// <summary>A row box with its cells; the row's text values are inherited by its cells.</summary>
    private static Result<Node, Error> RowNode(TableRowElement row, NodeId id, DocumentTranslator.Inherited inherited, BoxFormat cellDefaults, bool isLayoutOnly)
    {
        var box = Cascade.Box(row.Format, row.Style);
        var text = Cascade.Text(row.Format, row.Style);
        var problems = PartProblems(box, text, "row", row.Source);
        var own = inherited.With(text);
        var next = id.Value + 1;
        var cells = new List<(CellElement Cell, NodeId Id)>();
        foreach (var cell in row.Cells)
        {
            cells.Add((cell, new NodeId(next)));
            next += CellNodeCount(cell);
        }

        var role = isLayoutOnly ? SemanticRole.Generic : row.IsHeader ? SemanticRole.TableHeaderCell : SemanticRole.TableCell;
        var children = cells
            .TraverseAll(item => CellNode(item.Cell, item.Id, own, cellDefaults, role, isLayoutOnly ? "row" : "table cell"))
            .OnSuccessMap(nodes => nodes.ToValueList());
        return DocumentTranslator.WithProblems(problems, children).OnSuccessMap(nodes => (Node)new Box(
            id, isLayoutOnly ? SemanticRole.Generic : SemanticRole.TableRow, LayoutAlgorithmId.Tracks, DocumentTranslator.BoxStyleOf(box, false, own) with { TableDisplay = TableDisplay.Row }, nodes, row.Source));
    }

    /// <summary>A cell box: a flow box with the cell's content; padding and border fall back to the table's cell
    /// defaults; span and vertical alignment set. <paramref name="container"/> names what the author wrote, a table
    /// cell or an item of a row of blocks, for the page-break check.</summary>
    private static Result<Node, Error> CellNode(
        CellElement cell, NodeId id, DocumentTranslator.Inherited inherited, BoxFormat cellDefaults, SemanticRole role, string container)
    {
        var box = Cascade.Box(cell.Format, cell.Style, cellDefaults);
        var text = Cascade.Text(cell.Format, cell.Style);
        var problems = DocumentTranslator.BoxProblems(box, cell.Source)
            .Concat(InputChecks.Text(text, cell.Source))
            .Concat(InputChecks.NotApplicable(box.Margin, "margin", "cell", cell.Source))
            .Concat(InputChecks.PageBreakIn(cell.Content, container))
            .ToImmutableArray();
        var own = inherited.With(text);
        var children = DocumentTranslator.Children(cell.Content, new NodeId(id.Value + 1), own);
        return DocumentTranslator.WithProblems(problems, children).OnSuccessMap(nodes => (Node)new Box(
            id,
            role,
            LayoutAlgorithmId.Flow,
            DocumentTranslator.BoxStyleOf(box, false, own) with { Span = cell.Span, VerticalAlign = cell.VerticalAlignment ?? VerticalAlign.Top },
            nodes,
            cell.Source));
    }

    /// <summary>A row or row group: valid text and border, no margin or padding.</summary>
    private static ImmutableArray<Error> PartProblems(Cascade.BoxValues box, TextFormat text, string element, SourceLocation source) =>
    [
        .. InputChecks.Text(text, source),
        .. InputChecks.Border(box.Border, source),
        .. InputChecks.NotApplicable(box.Margin, "margin", element, source),
        .. InputChecks.NotApplicable(box.Padding, "padding", element, source),
    ];
}
