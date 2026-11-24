using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>The table part a display type makes (CSS 2.1 §17.2; CSS Display 3 §2.4).</summary>
internal enum TablePart
{
    /// <summary>Not a table part.</summary>
    None,

    /// <summary><c>table</c>.</summary>
    Table,

    /// <summary><c>table-header-group</c>.</summary>
    HeaderGroup,

    /// <summary><c>table-row-group</c>.</summary>
    RowGroup,

    /// <summary><c>table-footer-group</c>.</summary>
    FooterGroup,

    /// <summary><c>table-row</c>.</summary>
    Row,

    /// <summary><c>table-cell</c>.</summary>
    Cell,

    /// <summary><c>table-column</c>.</summary>
    Column,

    /// <summary><c>table-column-group</c>.</summary>
    ColumnGroup,

    /// <summary><c>table-caption</c>.</summary>
    Caption,
}

/// <summary>
/// Tables (CSS 2.1 §17) as the document model has them: a box laid out by <see cref="LayoutAlgorithmId.Tracks"/> whose
/// children are row groups (<see cref="TableDisplay.HeaderGroup"/>, <see cref="TableDisplay.RowGroup"/>), whose children
/// are rows (<see cref="TableDisplay.Row"/>), whose children are the cells; in the collapsing border model (§17.6.2),
/// whose conflicts layout resolves from the borders of the cells, rows, groups and the table.
/// </summary>
/// <remarks>
/// <para>Only the form the model has is drawn; the rest is refused (<c>Documents.Html.Unsupported.Table</c>, or
/// <c>Value</c> for a computed value, <see cref="TableProperties"/>): a part outside the part it belongs in, which CSS
/// wraps in anonymous table boxes (§17.2.1), as text or an inline box directly in a row; a part made by another element
/// than the HTML element of its display type (HTML Standard §15.3.8), which would have no structure role of its own; a
/// caption, a header group after a row group; a table, group or row group without rows. A footer group is drawn after
/// the other row groups, as the table's last body rows (<c>DrawOrder</c>).</para>
/// <para>Column widths (§17.5.2), as Chromium lays them out: in the fixed table layout (<c>table-layout: fixed</c> on a
/// table of width <c>100%</c> or a length; at width <c>auto</c> Chromium lays the table out automatically) the lengths of the column
/// elements as fixed columns, and the width left shared equally over the columns without one (fill columns of weight
/// 1). In the automatic table layout a column element's length is the column's preferred width and a column without
/// one is sized by its content. The table's width is <c>auto</c> (as wide as its columns need), <c>100%</c> (its
/// container's width), a length (<see cref="BoxWidth.Fixed"/>) or another percentage of its container's width
/// (<see cref="BoxWidth.Percent"/>). A cell's length or percentage goes to the cell (<see cref="BoxWidth.Fixed"/>,
/// <see cref="BoxWidth.Percent"/>), and layout gives it its column (<c>CellWidths</c>). Percent columns are drawn in the
/// fixed layout of a table of any width and in the automatic layout (<c>PercentColumns</c>); a column group's width goes to
/// the columns of its span.</para>
/// <para>The parts follow CSS 2.1 §17.5: internal table elements have no margins; rows and row groups no padding; the
/// table none in the collapsing border model (§17.6.2: "a table does not have padding"). A table of the separated borders
/// model (<c>border-collapse: separate</c>, CSS 2.1 §17.6.1) keeps its padding and its <c>border-spacing</c>
/// (<see cref="BoxStyle.BorderModel"/>, <see cref="BoxStyle.BorderSpacing"/>); layout slices it at page breaks and refuses the breaks no
/// case measures (<c>Documents.Layout.TableBreak</c>). A row span ends with its row
/// group (HTML Standard §4.9.12.1). The table breaks with <see cref="BoxDecorationBreak.Slice"/>: layout splits a grid
/// line at a page break between rows at its centre, half on each page, where Chromium paints the line whole on one
/// page.</para>
/// </remarks>
public static partial class HtmlBoxGenerator
{
    /// <summary>The table part of a computed display.</summary>
    /// <param name="display">The computed value of <c>display</c>.</param>
    /// <returns>The part, or <see cref="TablePart.None"/>.</returns>
    internal static TablePart PartOf(CssValue display) => display is CssKeyword keyword ? keyword.Name switch
    {
        "table" => TablePart.Table,
        "table-header-group" => TablePart.HeaderGroup,
        "table-row-group" => TablePart.RowGroup,
        "table-footer-group" => TablePart.FooterGroup,
        "table-row" => TablePart.Row,
        "table-cell" => TablePart.Cell,
        "table-column" => TablePart.Column,
        "table-column-group" => TablePart.ColumnGroup,
        "table-caption" => TablePart.Caption,
        _ => TablePart.None,
    } : TablePart.None;

    /// <summary>The HTML elements whose display type each part is in the user-agent style sheet (HTML Standard §15.3.8).</summary>
    private static bool IsElementOf(TablePart part, HtmlElement element) =>
        element.Namespace == ElementNamespace.Html && (part, element.LocalName) switch
        {
            (TablePart.Table, "table") or (TablePart.HeaderGroup, "thead") or (TablePart.RowGroup, "tbody") or (TablePart.FooterGroup, "tfoot")
                or (TablePart.Row, "tr") or (TablePart.Caption, "caption")
                or (TablePart.Cell, "td" or "th") or (TablePart.Column, "col") or (TablePart.ColumnGroup, "colgroup") => true,
            _ => false,
        };

    private static string DisplayName(TablePart part) => part switch
    {
        TablePart.Table => "table",
        TablePart.HeaderGroup => "table-header-group",
        TablePart.RowGroup => "table-row-group",
        TablePart.FooterGroup => "table-footer-group",
        TablePart.Row => "table-row",
        TablePart.Cell => "table-cell",
        TablePart.Column => "table-column",
        TablePart.ColumnGroup => "table-column-group",
        TablePart.Caption => "table-caption",
        _ => "not a table part",
    };

    private sealed partial class Run
    {
        /// <summary>Per table: per column element, the width it gives (null for none, auto), its span and its start tag.</summary>
        private readonly Dictionary<StyledElement, List<(CssValue? Width, int Span, TextPosition Position)>> _columns = new(ReferenceEqualityComparer.Instance);

        /// <summary>The start tag of every cell with a width (<see cref="BoxWidth.Fixed"/>), where a width the table does not
        /// draw is refused (<see cref="CellWidths"/>).</summary>
        private readonly Dictionary<NodeId, TextPosition> _cellWidths = [];

        /// <summary>The boxes of the footer groups, which their table draws after its other row groups.</summary>
        private readonly HashSet<NodeId> _footers = [];

        private static TablePart PartOf(StyledElement element) => HtmlBoxGenerator.PartOf(element.Style[PropertyRegistry.Display]);

        private void RefuseTable(string message, string value, TextPosition position) =>
            Refuse(HtmlStyleErrorCodes.Unsupported.Table, message, value, position);

        /// <summary>
        /// A child of a block frame where tables decide it: inside a table, a row group or a row only the part that belongs
        /// there, pushed at the same level (a table is one level of the model with its groups, rows and cells); a column
        /// or column group is read into the table's columns; everything else is refused. Outside them a table part other
        /// than a table is refused. True when the child was handled here.
        /// </summary>
        private bool TableChild(Stack<object> stack, BlockFrame block, StyledNode node)
        {
            var parent = PartOf(block.Element);
            var element = node as StyledElement;
            var part = element is null ? TablePart.None : PartOf(element);
            var position = Position(node);
            if (parent is not (TablePart.Table or TablePart.HeaderGroup or TablePart.RowGroup or TablePart.FooterGroup or TablePart.Row))
            {
                if (part == TablePart.None || (part == TablePart.Table && IsElementOf(part, element!.Element)))
                {
                    return false;
                }

                RefuseTable(part == TablePart.Table
                    ? $"'display: table' on <{element!.Element.LocalName}> is not drawn in this version: only the table element is a table, with its structure role."
                    : $"The <{element!.Element.LocalName}> ({DisplayName(part)}) outside the table part it belongs in is not drawn in this version: CSS would wrap it in anonymous table boxes (CSS 2.1 §17.2.1).",
                    part == TablePart.Table ? $"display: table on {element.Element.LocalName}" : $"{element.Element.LocalName} outside a table", position);
                return true;
            }

            var host = block.Element.Element.LocalName;
            var belongs = parent switch
            {
                TablePart.Table => part is TablePart.Caption or TablePart.HeaderGroup or TablePart.RowGroup or TablePart.FooterGroup or TablePart.Column or TablePart.ColumnGroup,
                TablePart.Row => part == TablePart.Cell,
                _ => part == TablePart.Row,
            };
            if (!belongs)
            {
                var what = element is null ? "text" : $"<{element.Element.LocalName}> ({(part == TablePart.None ? "not a table part" : DisplayName(part))})";
                RefuseTable($"The {what} directly inside the <{host}> ({DisplayName(parent)}) is not drawn in this version: CSS would wrap it in an anonymous table box (CSS 2.1 §17.2.1).",
                    $"{element?.Element.LocalName ?? "text"} in {host}", position);
                return true;
            }

            if (!IsElementOf(part, element!.Element))
            {
                RefuseTable($"'display: {DisplayName(part)}' on <{element.Element.LocalName}> is not drawn in this version: only the HTML element of that display type is that table part, with its structure role.",
                    $"display: {DisplayName(part)} on {element.Element.LocalName}", position);
                return true;
            }

            if (part is TablePart.Column or TablePart.ColumnGroup)
            {
                AddColumns(block.Element, element, part);
                return true;
            }

            // A table has one caption in the model, which tagged PDF allows (ISO 32000-1 Table 337); Chromium draws every
            // caption element (case tc-two: the second above the grid too, below the first).
            if (part == TablePart.Caption && block.Nodes.OfType<Box>().Any(IsCaption))
            {
                RefuseTable($"A second table caption <{element.Element.LocalName}> is not drawn in this version: a table has one caption. Put its text in the first caption.",
                    $"second {element.Element.LocalName}", position);
                return true;
            }

            // CSS 2.1 §17.2: the header group is drawn before every other row group; the model's is its table's first child
            // after the caption.
            if (part == TablePart.HeaderGroup && block.Nodes.OfType<Box>().Any(node => !IsCaption(node)))
            {
                RefuseTable($"The header row group <{element.Element.LocalName}> after another row group is not drawn in this version: the document model's header rows come first.",
                    $"{element.Element.LocalName} after a row group", position);
                return true;
            }

            if (!Check(element, isBlock: true))
            {
                return true;
            }

            var frame = Frame(element, NewId(), block.Depth);
            frame.ForcedBreakBefore = block.ForcedBreakPending;
            block.ForcedBreakPending = false;
            stack.Push(frame);
            return true;
        }

        /// <summary>The columns of a column element, or of a column group: its column elements, or as many columns as its
        /// span (HTML Standard §4.9.3, §4.9.4), each with the width that applies to it: a group's own width goes to each
        /// column of its span (case <c>tpa-colgroup</c>: a group of span 2 at 25% or 60 pt makes two columns of 25% or 60 pt,
        /// in the automatic and the fixed layout); a group with column elements and a width of its own is refused. Each element is kept as one entry
        /// with its span, so the work is one step per element whatever its span; the columns are counted out only against
        /// the table's grid (<see cref="Columns"/>).</summary>
        private void AddColumns(StyledElement table, StyledElement element, TablePart part)
        {
            if (!_columns.TryGetValue(table, out var columns))
            {
                columns = [];
                _columns[table] = columns;
            }

            Check(element, isBlock: true);
            var children = part == TablePart.ColumnGroup
                ? element.Children.OfType<StyledElement>().Where(child => PartOf(child) == TablePart.Column && Display(child) != "none").ToList()
                : [];
            if (children.Count == 0)
            {
                columns.Add((Width(element), Span(element.Element.Attribute("span"), TableSlots.MaxColumnSpan, zero: 1), element.Element.Position));
                return;
            }

            if (Width(element) is { } groupWidth)
            {
                // Chromium gives a group's width to the columns of its span only (case tpa-colgroup a, b, c); beside col
                // elements one case shows it unused (d), which is not enough for a rule.
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'width: {groupWidth}' on a column group with column elements is not drawn in this version: give the width to its col elements.", groupWidth.ToString()!, element.Element.Position, "width");
            }

            foreach (var column in children)
            {
                Check(column, isBlock: true);
                columns.Add((Width(column), Span(column.Element.Attribute("span"), TableSlots.MaxColumnSpan, zero: 1), column.Element.Position));
            }
        }

        /// <summary>A computed width other than auto, or null (auto, or refused).</summary>
        private CssValue? Width(StyledElement element) => Value(element, "width") is { } width && width is not CssKeyword { Name: "auto" } ? width : null;

        /// <summary>
        /// A span attribute by the rules for parsing non-negative integers (HTML Standard §2.3.4.2), clamped to
        /// <paramref name="max"/>; 1 when it is missing or not a number; <paramref name="zero"/> for zero.
        /// </summary>
        private static int Span(string? attribute, int max, int zero)
        {
            if (attribute is null)
            {
                return 1;
            }

            var text = attribute.AsSpan().TrimStart([' ', '\t', '\n', '\f', '\r']);
            if (text.Length > 0 && text[0] == '+')
            {
                text = text[1..];
            }

            var digits = 0;
            while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            {
                digits++;
            }

            if (digits == 0)
            {
                return 1;
            }

            var value = 0L;
            foreach (var digit in text[..digits])
            {
                value = Math.Min(value * 10 + (digit - '0'), max);
            }

            return value == 0 ? zero : (int)value;
        }

        private static bool IsCaption(Box box) => box.Style.TableDisplay == TableDisplay.Caption;

        /// <summary>The box of a finished block frame, made the table part its element is.</summary>
        private Box TablePartBox(BlockFrame block, Box box) => PartOf(block.Element) switch
        {
            TablePart.Table => TableBox(block.Element, box),
            TablePart.Caption => CaptionBox(block.Element, box),
            TablePart.HeaderGroup or TablePart.RowGroup => GroupBox(block.Element, box),
            TablePart.FooterGroup => Footer(GroupBox(block.Element, box)),
            TablePart.Row => box with
            {
                Role = SemanticRole.TableRow,
                Layout = LayoutAlgorithmId.Tracks,
                Style = box.Style with { TableDisplay = TableDisplay.Row, Margin = Edges.Zero, Padding = Edges.Zero, Text = null },
            },
            TablePart.Cell => CellBox(block.Element, box),
            _ => box,
        };

        /// <summary>
        /// A table caption (CSS 2.1 §17.4): a block of text and inline elements, laid out by its table above or below the
        /// grid at the table's width (<c>caption-side</c>), its own margins, padding, border, background and text alignment
        /// drawn as a block's (cases <c>tc-*</c>). A caption with a block inside is refused: no case measures one.
        /// </summary>
        private Box CaptionBox(StyledElement element, Box box)
        {
            if (box.Layout != LayoutAlgorithmId.Inline && box.Children.Count > 0)
            {
                RefuseTable($"A block inside the table caption <{element.Element.LocalName}> is not drawn in this version: a caption holds text and inline elements.",
                    $"block in {element.Element.LocalName}", element.Element.Position);
            }

            return box with
            {
                Role = SemanticRole.Caption,
                Style = box.Style with
                {
                    TableDisplay = TableDisplay.Caption,
                    CaptionSide = Value(element, "caption-side") is CssKeyword { Name: "bottom" } ? CaptionSide.Bottom : CaptionSide.Top,
                },
            };
        }

        private Box CellBox(StyledElement element, Box box)
        {
            var html = element.Element;
            var rows = Span(html.Attribute("rowspan"), TableSlots.MaxRowSpan, zero: 0);
            if (rows == 0)
            {
                RefuseTable($"'rowspan=0' on <{html.LocalName}> (the cell spans the rest of its row group) is not drawn in this version.", "rowspan=0", html.Position);
                rows = 1;
            }

            var align = VerticalAlignOf(element);
            var width = Value(element, "width") switch
            {
                CssLength length => new BoxWidth.Fixed(length.Value),
                CssPercentage percent => new BoxWidth.Percent(percent.Value),
                _ => BoxWidth.Auto,
            };
            if (width != BoxWidth.Auto)
            {
                _cellWidths[box.Id] = html.Position;
            }

            return box with
            {
                Role = html.LocalName == "th" ? SemanticRole.TableHeaderCell : SemanticRole.TableCell,
                Style = box.Style with
                {
                    Margin = Edges.Zero,
                    Span = new CellSpan(Span(html.Attribute("colspan"), TableSlots.MaxColumnSpan, zero: 1), rows),
                    VerticalAlign = align,
                    Width = width,
                },
            };
        }

        /// <summary>The vertical alignment of a box's content in its height, for a table cell and a page-margin box (CSS 2.1
        /// §17.5.3, CSS Paged Media 3 §5.3): middle, bottom, else top.</summary>
        private VerticalAlign VerticalAlignOf(StyledElement element) => Value(element, "vertical-align") switch
        {
            CssKeyword { Name: "middle" } => VerticalAlign.Middle,
            CssKeyword { Name: "bottom" } => VerticalAlign.Bottom,
            _ => VerticalAlign.Top,
        };

        /// <summary>A row group; the row spans of its cells end with it (HTML Standard §4.9.12.1, "ending a row group").
        /// The header group's role is set by its table, which knows whether there are body rows.</summary>
        private Box GroupBox(StyledElement element, Box box)
        {
            var rows = box.Children.OfType<Box>().ToList();
            if (rows.Count == 0)
            {
                RefuseTable($"The row group <{element.Element.LocalName}> without rows is not drawn in this version.", $"{element.Element.LocalName} without rows", element.Element.Position);
            }

            ValueList<Node> children = [.. rows.Select((row, index) => (Node)(row with
            {
                Children = [.. row.Children.Select(cell => cell is Box { Style.Span.Rows: var span } spanning && span > rows.Count - index
                    ? spanning with { Style = spanning.Style with { Span = spanning.Style.Span with { Rows = rows.Count - index } } }
                    : cell)],
            }))];
            var isHeader = PartOf(element) == TablePart.HeaderGroup;
            return box with
            {
                Role = SemanticRole.TableBody,
                Layout = LayoutAlgorithmId.Tracks,
                Style = box.Style with
                {
                    TableDisplay = isHeader ? TableDisplay.HeaderGroup : TableDisplay.RowGroup,
                    Margin = Edges.Zero,
                    Padding = Edges.Zero,
                    Text = null,
                },
                Children = children,
            };
        }

        private Box Footer(Box group)
        {
            _footers.Add(group.Id);
            return group;
        }

        /// <summary>
        /// The table's row groups in the order they are drawn: the first footer group last, wherever it stands in the
        /// source; a later footer group is a row group in its place (CSS 2.1 §17.2, <c>table-footer-group</c>: "always
        /// displayed after all other rows and row groups"; Chromium: <c>TableGroupedChildren</c>, which takes the first
        /// footer group as the table's footer and the others as bodies). The model's table has no footer group, so the
        /// footer's rows are its last body rows: drawn once, after the body.
        /// </summary>
        private List<Box> DrawOrder(List<Box> groups)
        {
            var footer = groups.FindIndex(group => _footers.Contains(group.Id));
            if (footer < 0)
            {
                return groups;
            }

            var ordered = new List<Box>(groups);
            ordered.RemoveAt(footer);
            ordered.Add(groups[footer]);
            return ordered;
        }

        private Box TableBox(StyledElement element, Box box)
        {
            var html = element.Element;
            // A row group without rows was refused (GroupBox); under Warn it is left out, and a table without rows is an
            // empty block.
            var caption = box.Children.OfType<Box>().FirstOrDefault(IsCaption);
            var groups = DrawOrder([.. box.Children.OfType<Box>().Where(group => group.Children.Count > 0 && !IsCaption(group))]);
            if (groups.Count == 0)
            {
                RefuseTable($"The table <{html.LocalName}> without rows is not drawn in this version.", "table without rows", html.Position);
                return box with { Role = SemanticRole.Generic, Layout = LayoutAlgorithmId.Flow, Children = [] };
            }

            // A table head without a body is a structure PDF/UA refuses: then the header rows are the table's body, and
            // their cells stay header cells (as the code front-end does).
            var hasBody = groups.Any(group => group.Style.TableDisplay == TableDisplay.RowGroup);
            var grid = Grid(groups);
            if (grid.Problems.Any(problem => problem.Kind == TableSlots.ProblemKind.Overlap))
            {
                // Under Warn the spans are the model's initial value, one slot each, so no cells overlap.
                RefuseTable($"The table <{html.LocalName}> has cells that cover the same slot (HTML Standard §4.9.12.1, a table model error), which is not drawn in this version.", "cells that overlap", html.Position);
                groups = [.. groups.Select(group => group with
                {
                    Children = [.. Parts(group).Select(row => (Node)(row with { Children = [.. Parts(row).Select(cell => (Node)(cell with { Style = cell.Style with { Span = CellSpan.Single } }))] }))],
                })];
                grid = Grid(groups);
            }

            if (grid.ColumnCount == 0)
            {
                RefuseTable($"The table <{html.LocalName}> without cells is not drawn in this version.", "table without cells", html.Position);
                return box with { Role = SemanticRole.Generic, Layout = LayoutAlgorithmId.Flow, Children = [] };
            }

            var sizes = Columns(element, grid);
            groups = CellWidths(element, groups, grid, sizes.Width != BoxWidth.Auto && Value(element, "table-layout") is CssKeyword { Name: "fixed" });
            var separated = Value(element, "border-collapse") is not CssKeyword { Name: "collapse" };
            return box with
            {
                Role = SemanticRole.Table,
                Layout = LayoutAlgorithmId.Tracks,
                Style = box.Style with
                {
                    Columns = sizes.Columns,
                    Width = sizes.Width,
                    DecorationBreak = BoxDecorationBreak.Slice,
                    BorderModel = separated ? TableBorderModel.Separate : TableBorderModel.Collapse,
                    BorderSpacing = separated ? SpacingOf(element) : BorderSpacing.Zero,
                    Padding = separated ? box.Style.Padding : Edges.Zero,
                    Text = null,
                },
                Children = [.. caption is null ? [] : new[] { (Node)caption }, .. groups.Select(group => (Node)(group.Style.TableDisplay == TableDisplay.HeaderGroup && hasBody ? group with { Role = SemanticRole.TableHead } : group))],
            };
        }

        private static IEnumerable<Box> Parts(Box box) => box.Children.OfType<Box>();

        /// <summary>The computed <c>border-spacing</c> of a table with separated borders: horizontal, then vertical (CSS 2.1
        /// §17.6.1).</summary>
        private BorderSpacing SpacingOf(StyledElement element) =>
            Value(element, "border-spacing") is CssList { Items: [CssLength horizontal, CssLength vertical] }
                ? new BorderSpacing(horizontal.Value, vertical.Value)
                : BorderSpacing.Zero;

        /// <summary>The slots of the table's cells (HTML Standard §4.9.12.1).</summary>
        private static TableSlots.Grid Grid(List<Box> groups) =>
            TableSlots.Fill([.. groups.Select(group => (IReadOnlyList<IReadOnlyList<CellSpan>>)[.. Parts(group).Select(row => (IReadOnlyList<CellSpan>)[.. Parts(row).Select(cell => cell.Style.Span)])])]);

        /// <summary>
        /// The column sizes and the table's width (CSS 2.1 §17.5.2), as Chromium lays them out; after a refusal (drawn
        /// under <see cref="UnsupportedPolicy.Warn"/>) equal shares of the width. The fixed table layout is taken only by
        /// a table whose width is not auto (Chromium: <c>ComputedStyle::IsFixedTableLayout</c>); a table of width auto with
        /// <c>table-layout: fixed</c> is laid out automatically.
        /// </summary>
        private (ValueList<TrackSize> Columns, BoxWidth Width) Columns(StyledElement table, TableSlots.Grid grid)
        {
            var html = table.Element;
            var count = grid.ColumnCount;
            var width = Value(table, "width") switch
            {
                CssPercentage { Value: 100 } => BoxWidth.Stretch,
                CssPercentage percent => new BoxWidth.Percent(percent.Value),
                CssLength length => new BoxWidth.Fixed(length.Value),
                _ => BoxWidth.Auto,
            };
            var stretch = width != BoxWidth.Auto;
            var given = _columns.TryGetValue(table, out var columns) ? columns : [];
            var elements = given.Sum(column => (long)column.Span);
            if (elements > count)
            {
                RefuseTable($"The table <{html.LocalName}> has {elements} column elements for {count} columns of cells, which is not drawn in this version.", $"{elements} column elements for {count} columns", html.Position);
                return (Equal(count), width);
            }

            var starts = new bool[count];
            foreach (var cell in grid.Cells)
            {
                starts[cell.Column] = true;
            }

            var empty = Array.IndexOf(starts, false);
            if (empty >= 0)
            {
                RefuseTable($"Column {empty + 1} of <{html.LocalName}>, in which no cell starts, is not drawn in this version: only spanning cells give it a width.", $"column {empty + 1} without a cell that starts in it", html.Position);
                return (Equal(count), width);
            }

            var widths = new CssValue?[count];
            var places = new TextPosition[count];
            var next = 0;
            foreach (var (value, span, position) in given)
            {
                Array.Fill(widths, value, next, span);
                Array.Fill(places, position, next, span);
                next += span;
            }

            var isFixed = stretch && Value(table, "table-layout") is CssKeyword { Name: "fixed" };
            if (PercentColumns(widths, places, isFixed) is { } shares)
            {
                return (shares, width);
            }

            var lengths = widths.Select(value => value as CssLength).ToArray();
            if (isFixed)
            {
                // §17.5.2.1: the widths of the column elements, exact, and the width left over shared equally by the
                // columns without one (fill columns of weight 1); with no such column the table's width left over goes to
                // the fixed columns in proportion (BoxWidth.Stretch).
                return ([.. lengths.Select(length => length is null ? new TrackSize.Fill(1) : (TrackSize)new TrackSize.Fixed(length.Value))], width);
            }

            // §17.5.2.2: a column length is the column's preferred width, a column without one is sized by its content.
            return ([.. lengths.Select(length => length is null ? TrackSize.Auto.Instance : (TrackSize)new TrackSize.Preferred(length.Value))], width);
        }

        /// <summary>
        /// The widths of the table's cells (CSS 2.1 §17.5.2: a cell's <c>width</c>, a length of its content box or a
        /// percentage), which layout gives their columns (<see cref="BoxWidth.Fixed"/>, <see cref="BoxWidth.Percent"/>).
        /// Refused (<c>Value</c>, at the cell), and then left out: a length on a cell over several columns in the fixed
        /// table layout (case <c>twd-cell-span</c>, table <c>b</c>: Chromium's columns come out equal, which a width left out
        /// and a width shared over the columns both give, so the case shows no rule); a length on a cell of a table with
        /// percent columns in the fixed layout; and, not measured, a percentage on a cell in the fixed layout, on a cell
        /// over several columns, or in a column that has another width (a length, or another percentage, of its column
        /// element or another cell). Refused at the table: a cell over several columns of which one has a percentage (not
        /// measured), and percentages of 100 or more beside other columns (<see cref="PercentColumns"/>).
        /// </summary>
        private List<Box> CellWidths(StyledElement table, List<Box> groups, TableSlots.Grid grid, bool isFixed)
        {
            var given = _columns.TryGetValue(table, out var columns) ? columns : [];
            var percentColumns = given.Exists(column => column.Width is CssPercentage);
            var cells = groups.SelectMany(Parts).SelectMany(Parts).ToList();
            var count = grid.ColumnCount;

            // What gives each column a width: its column element's, then the cells over that column alone.
            var lengths = new bool[count];
            var percents = new List<double>[count];
            var next = 0;
            foreach (var (value, span, _) in given)
            {
                for (var index = next; index < Math.Min(count, next + span); index++)
                {
                    lengths[index] = value is CssLength;
                    percents[index] = value is CssPercentage percent ? [percent.Value] : [];
                }

                next += span;
            }

            for (var index = 0; index < count; index++)
            {
                percents[index] ??= [];
            }

            var placed = cells.Count == grid.Cells.Length;
            for (var index = 0; placed && index < cells.Count; index++)
            {
                if (grid.Cells[index].Span.Columns == 1)
                {
                    var column = grid.Cells[index].Column;
                    lengths[column] |= cells[index].Style.Width is BoxWidth.Fixed;
                    if (cells[index].Style.Width is BoxWidth.Percent { Value: var value } && !percents[column].Contains(value))
                    {
                        percents[column].Add(value);
                    }
                }
            }

            if (!isFixed && placed)
            {
                var spanning = grid.Cells.FirstOrDefault(cell => cell.Span.Columns > 1 && Enumerable.Range(cell.Column, cell.Span.Columns).Any(column => column < count && percents[column].Count > 0));
                if (spanning.Span.Columns > 1)
                {
                    RefuseTable($"The table <{table.Element.LocalName}> has a cell over several columns of which one has a percentage, which is not drawn in this version.", "a cell over a percent column", table.Element.Position);
                }

                var total = percents.Sum(values => values.Count > 0 ? values.Max() : 0);
                var first = cells.FindIndex(cell => cell.Style.Width is BoxWidth.Percent);
                if (total >= 100 && percents.Any(values => values.Count == 0) && first >= 0)
                {
                    Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'width: {cells[first].Style.Width}' on a table cell is not drawn: percent columns of 100% or more beside other columns are not drawn in the automatic table layout in this version.", cells[first].Style.Width.ToString()!, _cellWidths[cells[first].Id], "width");
                }
            }

            Box Checked(Box cell, int index)
            {
                if (cell.Style.Width == BoxWidth.Auto)
                {
                    return cell;
                }

                var column = placed ? grid.Cells[index].Column : 0;
                var reason = cell.Style.Width switch
                {
                    BoxWidth.Fixed when isFixed && percentColumns => "a width on a cell of a table with percent columns is not drawn in the fixed table layout in this version",
                    BoxWidth.Fixed when isFixed && cell.Style.Span.Columns > 1 => "a width on a cell over several columns is not drawn in the fixed table layout (table-layout: fixed) in this version: give the columns their widths with col elements",
                    BoxWidth.Fixed when placed && cell.Style.Span.Columns == 1 && percents[column].Count > 0 => "a length and a percentage in one column are not drawn in this version: give the column one width",
                    BoxWidth.Percent when isFixed => "a percentage on a cell is not drawn in the fixed table layout (table-layout: fixed) in this version: give the column its width with a col element",
                    BoxWidth.Percent when cell.Style.Span.Columns > 1 => "a percentage on a cell over several columns is not drawn in this version",
                    BoxWidth.Percent when !placed || lengths[column] || percents[column].Count > 1 => "a percentage beside another width in one column is not drawn in this version: give the column one width",
                    _ => null,
                };
                if (reason is null)
                {
                    return cell;
                }

                Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'width: {cell.Style.Width}' on a table cell is not drawn: {reason}.", cell.Style.Width.ToString()!, _cellWidths[cell.Id], "width");
                return cell with { Style = cell.Style with { Width = BoxWidth.Auto } };
            }

            var at = 0;
            return [.. groups.Select(group => group with
            {
                Children = [.. Parts(group).Select(row => (Node)(row with { Children = [.. Parts(row).Select(cell => (Node)Checked(cell, at++))] }))],
            })];
        }

        /// <summary>
        /// The columns of a table with percent columns (CSS 2.1 §17.5.2.1, where a percentage is of the table's width), as
        /// Chromium's fixed layout sizes them (<c>SynchronizeAssignableTableInlineSizeAndColumnsFixed</c>,
        /// table_layout_utils.cc): a percentage is of the width the columns share, the table's width less its outer half
        /// borders in the collapsing model (case <c>tp-outer-border</c>). Without a column of another kind the percent
        /// columns grow or shrink in proportion to fill that width (sums below and above 100, cases <c>tp-sum-below</c>,
        /// <c>tp-sum-above</c>); beside columns without a width each keeps its percentage and those columns share the rest
        /// equally (<c>tp-beside-auto</c>). Both are fill columns whose weights are the shares of the width, so layout
        /// divides the width exactly where Chromium cuts each share to 1/64 px and gives the remainder to the last column.
        /// A span repeats the percentage over its columns (<c>tp-col-span</c>). The same holds in a table of a length
        /// (<c>tp-length-table</c>, <c>tpa-length</c> e). In the automatic layout a percent column is a
        /// <see cref="TrackSize.Percent"/> column, a column length a preferred width and the others auto columns (cases
        /// <c>tpa-*</c>). Refused, at the column element, and then read as a column without a width: in the fixed layout a
        /// percent column beside a column of a length, which Chromium widens to what the percentages leave
        /// (<c>tp-beside-length</c>), and percentages of 100 or more beside a column without a width, which Chromium leaves
        /// no width; in the automatic layout percentages of 100 or more beside other columns (not measured). Null when the
        /// table has no percent column or it was refused.
        /// </summary>
        private ValueList<TrackSize>? PercentColumns(CssValue?[] widths, TextPosition[] places, bool isFixed)
        {
            var first = Array.FindIndex(widths, value => value is CssPercentage);
            if (first < 0)
            {
                return null;
            }

            var total = widths.Sum(value => value is CssPercentage percent ? percent.Value : 0);
            var others = widths.Count(value => value is null);
            var reason = !isFixed && total >= 100 && widths.Length > widths.Count(value => value is CssPercentage) ? "percent columns of 100% or more beside other columns are not drawn in the automatic table layout in this version"
                : !isFixed ? null
                : Array.Exists(widths, value => value is CssLength) ? "a percent column beside a column of a length is not drawn in the fixed table layout in this version"
                : others > 0 && total >= 100 ? "percent columns of 100% or more beside a column without a width are not drawn in this version"
                : null;
            if (reason is not null)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'width: {widths[first]}' on a column element is not drawn: {reason}.", widths[first]!.ToString()!, places[first], "width");
                for (var i = 0; i < widths.Length; i++)
                {
                    widths[i] = widths[i] is CssPercentage ? null : widths[i];
                }

                return null;
            }

            if (!isFixed)
            {
                // The automatic layout: percent columns (TrackSize.Percent), column lengths as preferred widths (§17.5.2.2),
                // the others sized by their content.
                return [.. widths.Select(value => value switch
                {
                    CssPercentage percent => new TrackSize.Percent(percent.Value),
                    CssLength length => new TrackSize.Preferred(length.Value),
                    _ => (TrackSize)TrackSize.Auto.Instance,
                })];
            }

            return [.. widths.Select(value => (TrackSize)new TrackSize.Fill(value switch
            {
                CssPercentage percent when others == 0 => percent.Value * 100 / total,
                CssPercentage percent => percent.Value,
                _ => (100 - total) / others,
            }))];
        }

        private static ValueList<TrackSize> Equal(int count) => [.. Enumerable.Repeat((TrackSize)new TrackSize.Fill(1), count)];
    }
}
