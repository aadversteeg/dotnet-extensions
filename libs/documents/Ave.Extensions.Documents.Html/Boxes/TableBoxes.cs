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
/// table of width <c>100%</c>; at width <c>auto</c> Chromium lays the table out automatically) the lengths of the column
/// elements as fixed columns, and the width left shared equally over the columns without one (fill columns of weight
/// 1). In the automatic table layout a column element's length is the column's preferred width and a column without
/// one is sized by its content. The table's width is <c>auto</c> (as wide as its columns need) or <c>100%</c> (its
/// container's width). Percent columns are drawn in the fixed layout (<c>PercentColumns</c>); widths on cells and
/// column groups are refused.</para>
/// <para>The parts follow CSS 2.1 §17.5: internal table elements have no margins; rows and row groups no padding; the
/// table none in the collapsing border model (§17.6.2: "a table does not have padding"). A row span ends with its row
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
                or (TablePart.Row, "tr")
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
                TablePart.Table => part is TablePart.HeaderGroup or TablePart.RowGroup or TablePart.FooterGroup or TablePart.Column or TablePart.ColumnGroup,
                TablePart.Row => part == TablePart.Cell,
                _ => part == TablePart.Row,
            };
            if (!belongs)
            {
                var what = element is null ? "text" : $"<{element.Element.LocalName}> ({(part == TablePart.None ? "not a table part" : DisplayName(part))})";
                var message = part switch
                {
                    TablePart.Caption => $"The table caption <{element!.Element.LocalName}> is not drawn in this version.",
                    _ => $"The {what} directly inside the <{host}> ({DisplayName(parent)}) is not drawn in this version: CSS would wrap it in an anonymous table box (CSS 2.1 §17.2.1).",
                };
                var value = part switch
                {
                    TablePart.Caption => element!.Element.LocalName,
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
        /// span (HTML Standard §4.9.3, §4.9.4), each with the width that applies to it. Each element is kept as one entry
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

        /// <summary>The box of a finished block frame, made the table part its element is.</summary>
        private Box TablePartBox(BlockFrame block, Box box) => PartOf(block.Element) switch
        {
            TablePart.Table => TableBox(block.Element, box),
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

        private Box CellBox(StyledElement element, Box box)
        {
            var html = element.Element;
            var rows = Span(html.Attribute("rowspan"), TableSlots.MaxRowSpan, zero: 0);
            if (rows == 0)
            {
                RefuseTable($"'rowspan=0' on <{html.LocalName}> (the cell spans the rest of its row group) is not drawn in this version.", "rowspan=0", html.Position);
                rows = 1;
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
            var groups = DrawOrder([.. box.Children.OfType<Box>().Where(group => group.Children.Count > 0)]);
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
            return box with
            {
                Role = SemanticRole.Table,
                Layout = LayoutAlgorithmId.Tracks,
                Style = box.Style with { Columns = sizes.Columns, Width = sizes.Width, DecorationBreak = BoxDecorationBreak.Slice, Padding = Edges.Zero, Text = null },
                Children = [.. groups.Select(group => (Node)(group.Style.TableDisplay == TableDisplay.HeaderGroup && hasBody ? group with { Role = SemanticRole.TableHead } : group))],
            };
        }

        private static IEnumerable<Box> Parts(Box box) => box.Children.OfType<Box>();

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
            var stretch = Value(table, "width") is CssPercentage { Value: 100 };
            var width = stretch ? BoxWidth.Stretch : BoxWidth.Auto;
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
        /// The columns of a table with percent columns (CSS 2.1 §17.5.2.1, where a percentage is of the table's width), as
        /// Chromium's fixed layout sizes them (<c>SynchronizeAssignableTableInlineSizeAndColumnsFixed</c>,
        /// table_layout_utils.cc): a percentage is of the width the columns share, the table's width less its outer half
        /// borders in the collapsing model (case <c>tp-outer-border</c>). Without a column of another kind the percent
        /// columns grow or shrink in proportion to fill that width (sums below and above 100, cases <c>tp-sum-below</c>,
        /// <c>tp-sum-above</c>); beside columns without a width each keeps its percentage and those columns share the rest
        /// equally (<c>tp-beside-auto</c>). Both are fill columns whose weights are the shares of the width, so layout
        /// divides the width exactly where Chromium cuts each share to 1/64 px and gives the remainder to the last column.
        /// A span repeats the percentage over its columns (<c>tp-col-span</c>). Refused, at the column element, and then
        /// read as a column without a width: a percent column outside the fixed layout of a table of width <c>100%</c>
        /// (Chromium's automatic layout has other rules); beside a column of a length, which Chromium widens to what the
        /// percentages leave (<c>tp-beside-length</c>); and percentages of 100 or more beside a column without a width,
        /// which Chromium leaves no width. Null when the table has no percent column or it was refused.
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
            var reason = !isFixed ? "a percent column is drawn only in the fixed table layout (table-layout: fixed) of a table of width 100% in this version"
                : Array.Exists(widths, value => value is CssLength) ? "a percent column beside a column of a length is not drawn in this version"
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
