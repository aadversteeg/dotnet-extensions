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
/// caption, a footer group, a header group after a row group; a table, group or row group without rows.</para>
/// <para>Column widths (§17.5.2): in the fixed table layout (<c>table-layout: fixed</c>) the widths of the column
/// elements, then of the cells of the first row, as fixed columns, and the width left shared equally over the columns
/// without one (fill columns of weight 1); columns in percent of the table's width as fill columns of that weight, the
/// columns without a width sharing the percent left. In the automatic table layout only what does not depend on the
/// content: fixed columns from column elements and one column without a width, which takes the rest. The table's width
/// is <c>100%</c>, the model's only table width.</para>
/// <para>The parts follow CSS 2.1 §17.5: internal table elements have no margins; rows and row groups no padding; the
/// table none in the collapsing border model (§17.6.2: "a table does not have padding"). A row span ends with its row
/// group (HTML Standard §4.9.12.1). Chromium paints half of a collapsed line on each side of a page break
/// (<see cref="BoxDecorationBreak.Slice"/>).</para>
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
            (TablePart.Table, "table") or (TablePart.HeaderGroup, "thead") or (TablePart.RowGroup, "tbody") or (TablePart.Row, "tr")
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
        /// <summary>Per table: per column, the width its column element gives, or null for none (auto).</summary>
        private readonly Dictionary<StyledElement, List<CssValue?>> _columns = new(ReferenceEqualityComparer.Instance);

        /// <summary>Per cell box: its computed width, where not auto.</summary>
        private readonly Dictionary<NodeId, CssValue> _cellWidths = [];

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
            if (parent is not (TablePart.Table or TablePart.HeaderGroup or TablePart.RowGroup or TablePart.Row))
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
                TablePart.Table => part is TablePart.HeaderGroup or TablePart.RowGroup or TablePart.Column or TablePart.ColumnGroup,
                TablePart.Row => part == TablePart.Cell,
                _ => part == TablePart.Row,
            };
            if (!belongs)
            {
                var what = element is null ? "text" : $"<{element.Element.LocalName}> ({(part == TablePart.None ? "not a table part" : DisplayName(part))})";
                var message = part switch
                {
                    TablePart.Caption => $"The table caption <{element!.Element.LocalName}> is not drawn in this version.",
                    TablePart.FooterGroup => $"The footer row group <{element!.Element.LocalName}> is not drawn in this version: the document model's table has no footer rows.",
                    _ => $"The {what} directly inside the <{host}> ({DisplayName(parent)}) is not drawn in this version: CSS would wrap it in an anonymous table box (CSS 2.1 §17.2.1).",
                };
                var value = part switch
                {
                    TablePart.Caption or TablePart.FooterGroup => element!.Element.LocalName,
                    _ => $"{element?.Element.LocalName ?? "text"} in {host}",
                };
                RefuseTable(message, value, position);
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

            // CSS 2.1 §17.2: the header group is drawn before every other row group; the model's is its table's first child.
            if (part == TablePart.HeaderGroup && block.Nodes.Count > 0)
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
        /// span (HTML Standard §4.9.3, §4.9.4), each with the width that applies to it.</summary>
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
                columns.AddRange(Enumerable.Repeat(Width(element), Span(element.Element.Attribute("span"), TableSlots.MaxColumnSpan, zero: 1)));
                return;
            }

            foreach (var column in children)
            {
                Check(column, isBlock: true);
                columns.AddRange(Enumerable.Repeat(Width(column), Span(column.Element.Attribute("span"), TableSlots.MaxColumnSpan, zero: 1)));
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

        /// <summary>The box of a finished block frame, made the table part its element is.</summary>
        private Box TablePartBox(BlockFrame block, Box box) => PartOf(block.Element) switch
        {
            TablePart.Table => TableBox(block.Element, box),
            TablePart.HeaderGroup or TablePart.RowGroup => GroupBox(block.Element, box),
            TablePart.Row => box with
            {
                Role = SemanticRole.TableRow,
                Layout = LayoutAlgorithmId.Tracks,
                Style = box.Style with { TableDisplay = TableDisplay.Row, Margin = Edges.Zero, Padding = Edges.Zero, Text = null },
            },
            TablePart.Cell => CellBox(block.Element, box),
            _ => box,
        };

        private Box CellBox(StyledElement element, Box box)
        {
            var html = element.Element;
            var rows = Span(html.Attribute("rowspan"), TableSlots.MaxRowSpan, zero: 0);
            if (rows == 0)
            {
                RefuseTable($"'rowspan=0' on <{html.LocalName}> (the cell spans the rest of its row group) is not drawn in this version.", "rowspan=0", html.Position);
                rows = 1;
            }

            if (Width(element) is { } width)
            {
                _cellWidths[box.Id] = width;
            }

            var align = Value(element, "vertical-align") switch
            {
                CssKeyword { Name: "middle" } => VerticalAlign.Middle,
                CssKeyword { Name: "bottom" } => VerticalAlign.Bottom,
                _ => VerticalAlign.Top,
            };
            return box with
            {
                Role = html.LocalName == "th" ? SemanticRole.TableHeaderCell : SemanticRole.TableCell,
                Style = box.Style with
                {
                    Margin = Edges.Zero,
                    Span = new CellSpan(Span(html.Attribute("colspan"), TableSlots.MaxColumnSpan, zero: 1), rows),
                    VerticalAlign = align,
                },
            };
        }

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

        private Box TableBox(StyledElement element, Box box)
        {
            var html = element.Element;
            // A row group without rows was refused (GroupBox); under Warn it is left out, and a table without rows is an
            // empty block.
            var groups = box.Children.OfType<Box>().Where(group => group.Children.Count > 0).ToList();
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

            return box with
            {
                Role = SemanticRole.Table,
                Layout = LayoutAlgorithmId.Tracks,
                Style = box.Style with { Columns = Columns(element, grid.ColumnCount, groups), DecorationBreak = BoxDecorationBreak.Slice, Padding = Edges.Zero, Text = null },
                Children = [.. groups.Select(group => (Node)(group.Style.TableDisplay == TableDisplay.HeaderGroup && hasBody ? group with { Role = SemanticRole.TableHead } : group))],
            };
        }

        private static IEnumerable<Box> Parts(Box box) => box.Children.OfType<Box>();

        /// <summary>The slots of the table's cells (HTML Standard §4.9.12.1).</summary>
        private static TableSlots.Grid Grid(List<Box> groups) =>
            TableSlots.Fill([.. groups.Select(group => (IReadOnlyList<IReadOnlyList<CellSpan>>)[.. Parts(group).Select(row => (IReadOnlyList<CellSpan>)[.. Parts(row).Select(cell => cell.Style.Span)])])]);

        /// <summary>The column sizes (CSS 2.1 §17.5.2); after a refusal (drawn under <see cref="UnsupportedPolicy.Warn"/>)
        /// equal shares of the width.</summary>
        private ValueList<TrackSize> Columns(StyledElement table, int count, List<Box> groups)
        {
            var html = table.Element;
            var given = _columns.TryGetValue(table, out var columns) ? columns : [];
            if (given.Count > count)
            {
                RefuseTable($"The table <{html.LocalName}> has {given.Count} column elements for {count} columns of cells, which is not drawn in this version.", $"{given.Count} column elements for {count} columns", html.Position);
                return Equal(count);
            }

            var isFixed = Value(table, "table-layout") is CssKeyword { Name: "fixed" };
            var cells = groups.SelectMany(group => group.Children.OfType<Box>()).SelectMany(row => row.Children.OfType<Box>()).ToList();
            var widths = Enumerable.Range(0, count).Select(column => column < given.Count ? given[column] : null).ToArray();
            if (isFixed)
            {
                // §17.5.2.1: "A cell in the first row with a value other than 'auto' for the 'width' property determines
                // the width for that column."
                var first = groups.SelectMany(group => group.Children.OfType<Box>()).FirstOrDefault();
                var column = 0;
                foreach (var cell in first?.Children.OfType<Box>() ?? [])
                {
                    if (_cellWidths.TryGetValue(cell.Id, out var width) && widths[column] is null)
                    {
                        if (cell.Style.Span.Columns > 1)
                        {
                            RefuseTable($"A width on a cell of the first row that spans {cell.Style.Span.Columns} columns of the fixed table <{html.LocalName}> is not drawn in this version.", "width on a first-row cell that spans columns", html.Position);
                            return Equal(count);
                        }

                        widths[column] = width;
                    }

                    column += cell.Style.Span.Columns;
                }
            }
            else if (cells.Any(cell => _cellWidths.ContainsKey(cell.Id)))
            {
                RefuseTable($"Cell widths in the automatic table layout of <{html.LocalName}> are not drawn in this version: they depend on the content (CSS 2.1 §17.5.2.2).", "cell widths in the automatic table layout", html.Position);
                return Equal(count);
            }

            // The sum of the percentages as written: 100 within the rounding of their binary form (14.29% + 17.14% +
            // 57.14% + 11.43% is 100.00000000000001).
            var percents = widths.OfType<CssPercentage>().Sum(width => width.Value);
            const double tolerance = 1e-9;
            var open = widths.Count(width => width is null);
            if (widths.Any(width => width is CssPercentage))
            {
                if (!isFixed || widths.Any(width => width is CssLength) || percents > 100 + tolerance || (open == 0 && Math.Abs(percents - 100) > tolerance) || (open > 0 && percents >= 100 - tolerance))
                {
                    RefuseTable($"Column widths in percent of <{html.LocalName}> are drawn only in the fixed table layout, without columns of a length, adding up to 100% with the columns without a width.", !isFixed ? "percent columns in the automatic table layout" : widths.Any(width => width is CssLength) ? "percent and length columns together" : "percent columns that do not add up to 100%", html.Position);
                    return Equal(count);
                }

                return [.. widths.Select(width => (TrackSize)new TrackSize.Fill(width is CssPercentage percentage ? percentage.Value : (100 - percents) / open))];
            }

            if (open == 0 || (!isFixed && open > 1))
            {
                RefuseTable(open == 0
                    ? $"The table <{html.LocalName}> whose every column has a width is not drawn in this version: the width left would be shared out over the columns."
                    : $"The automatic table layout of <{html.LocalName}> with {open} columns without a width is not drawn in this version: their widths depend on the content (CSS 2.1 §17.5.2.2).",
                    open == 0 ? "every column of a given width" : $"automatic table layout, {open} columns without a width", html.Position);
                return Equal(count);
            }

            return [.. widths.Select(width => width is CssLength length ? (TrackSize)new TrackSize.Fixed(length.Value) : new TrackSize.Fill(1))];
        }

        private static ValueList<TrackSize> Equal(int count) => [.. Enumerable.Repeat((TrackSize)new TrackSize.Fill(1), count)];
    }
}
