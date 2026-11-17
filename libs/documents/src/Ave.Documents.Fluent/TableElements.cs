using Ave.Documents.Core;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Fluent;

/// <summary>
/// A table: columns and rows of cells, with grid lines in the collapsing border model (one line between two cells).
/// Made by <see cref="Elements.Table"/>. Header rows (<see cref="Elements.HeaderRow"/>) come first and repeat on every
/// page the table continues on.
/// </summary>
/// <param name="Columns">The column sizes: fixed lengths, <see cref="Elements.Auto"/> or <see cref="Elements.Fill"/>.</param>
/// <param name="Rows">Header rows first, then body rows and row groups.</param>
/// <param name="Format">What the modifiers set on the table.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="CellDefaults">Padding and border for every cell that does not set its own (<see cref="CellPadding(Length)"/>,
/// <see cref="GridLines"/>).</param>
/// <param name="Source">Where the element was created.</param>
public sealed record TableElement(
    ValueList<TrackSize> Columns, ValueList<TablePart> Rows, Style Format, Style Style, BoxFormat CellDefaults, SourceLocation Source)
    : Element(Source), IBoxFormatted<TableElement>
{
    /// <summary>Applies a shared style. The table's own modifier values still win over it.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public TableElement With(Style style) => this with { Style = style };

    /// <summary>Draws a grid: a solid line of this width and colour around every cell. In the collapsing border model
    /// two adjacent cells share one line. A cell's own border modifiers win over it.</summary>
    /// <param name="width">The line width.</param>
    /// <param name="color">The line colour.</param>
    /// <returns>A copy with the cell border default set.</returns>
    public TableElement GridLines(Length width, Color color) => this with { CellDefaults = CellDefaults with { Border = new BorderSide(width, color) } };

    /// <summary>The padding of every cell that does not set its own.</summary>
    /// <param name="all">The padding on all four sides.</param>
    /// <returns>A copy with the cell padding default set.</returns>
    public TableElement CellPadding(Length all) => this with { CellDefaults = CellDefaults with { Padding = all } };

    /// <summary>The padding of every cell that does not set its own, as vertical and horizontal values.</summary>
    /// <param name="vertical">Top and bottom.</param>
    /// <param name="horizontal">Left and right.</param>
    /// <returns>A copy with the cell padding default set.</returns>
    public TableElement CellPadding(Length vertical, Length horizontal) =>
        this with { CellDefaults = CellDefaults with { PaddingTop = vertical, PaddingBottom = vertical, PaddingLeft = horizontal, PaddingRight = horizontal } };

    /// <summary>How the grid lines are drawn where the table continues on the next page:
    /// <see cref="BoxDecorationBreak.Clone"/> (the default for a table) closes the table on both pages with whole lines —
    /// at a break between rows the line there ends the page and starts the next, at a cut inside a row the line below it
    /// ends the page and the line above it starts the next; <see cref="BoxDecorationBreak.Slice"/> splits the line at a
    /// break between rows, half on each page, and draws none at a cut inside a row.</summary>
    public BoxDecorationBreak LinesAtBreaks { get; init; } = BoxDecorationBreak.Clone;

    /// <summary>At a page break between two rows, the page ends with the whole grid line and the next page starts with
    /// it again; a row cut inside itself ends the page with the whole line below it and goes on under the whole line
    /// above it: the table is closed on both pages, as word processors print a table. This is the default.</summary>
    /// <returns>A copy that repeats the line.</returns>
    public TableElement RepeatLinesAtBreaks() => this with { LinesAtBreaks = BoxDecorationBreak.Clone };

    /// <summary>At a page break between two rows, the grid line is split along its centre: the upper half ends the page,
    /// the lower half starts the next; a row cut inside itself gets no line at the cut; as browsers print a table (CSS
    /// <c>box-decoration-break: slice</c>).</summary>
    /// <returns>A copy that splits the line.</returns>
    public TableElement SplitLinesAtBreaks() => this with { LinesAtBreaks = BoxDecorationBreak.Slice };

    /// <inheritdoc/>
    TableElement IFormatted<TableElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>A part of a table's row list: a row or a group of rows. A closed set.</summary>
public abstract record TablePart
{
    // Only the cases in this assembly exist, so the translator handles every one.
    private protected TablePart(SourceLocation source) => Source = source;

    /// <summary>Where the part was created.</summary>
    public SourceLocation Source { get; }
}

/// <summary>
/// A row of cells. Made by <see cref="Elements.TableRow"/> or <see cref="Elements.HeaderRow"/>. Text modifiers set the
/// text of all its cells; of the box modifiers only background, border and the keep rules apply to a row.
/// </summary>
/// <param name="Cells">The cells, left to right; a cell spanning rows from above takes its slot.</param>
/// <param name="IsHeader">True for a header row.</param>
/// <param name="Format">What the modifiers set on the row.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the row was created.</param>
public sealed record TableRowElement(ValueList<CellElement> Cells, bool IsHeader, Style Format, Style Style, SourceLocation Source)
    : TablePart(Source), IBoxFormatted<TableRowElement>
{
    /// <summary>Applies a shared style.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public TableRowElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    TableRowElement IFormatted<TableRowElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>
/// A group of body rows that belong together, for example an entry and its description. Made by
/// <see cref="Elements.RowGroup"/>; with <c>.KeepTogether()</c> the group moves to the next page whole rather than break.
/// A cell's row span ends at the end of its group.
/// </summary>
/// <param name="Rows">The rows.</param>
/// <param name="Format">What the modifiers set on the group.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the group was created.</param>
public sealed record RowGroupElement(ValueList<TableRowElement> Rows, Style Format, Style Style, SourceLocation Source)
    : TablePart(Source), IBoxFormatted<RowGroupElement>
{
    /// <summary>Applies a shared style.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public RowGroupElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    RowGroupElement IFormatted<RowGroupElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>
/// A table cell: any content, stacked vertically. Made by <see cref="Elements.Cell(string, string, int)"/> or
/// <see cref="Elements.Cell(ValueList{Element}, string, int)"/>.
/// </summary>
/// <param name="Content">The content.</param>
/// <param name="Span">How many columns and rows the cell covers.</param>
/// <param name="VerticalAlignment">Top, middle or bottom; null to inherit the row's (top by default).</param>
/// <param name="Format">What the modifiers set on the cell.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the cell was created.</param>
public sealed record CellElement(ValueList<Element> Content, CellSpan Span, VerticalAlign? VerticalAlignment, Style Format, Style Style, SourceLocation Source)
    : IBoxFormatted<CellElement>
{
    /// <summary>Lets the cell cover this many columns, from its own to the right.</summary>
    /// <param name="columns">At least 1.</param>
    /// <returns>A copy with the column span.</returns>
    public CellElement ColumnSpan(int columns) => this with { Span = Span with { Columns = columns } };

    /// <summary>Lets the cell cover this many rows, from its own down; the rows below leave its slot free. A row span
    /// ends at the end of the cell's row group.</summary>
    /// <param name="rows">At least 1.</param>
    /// <returns>A copy with the row span.</returns>
    public CellElement RowSpan(int rows) => this with { Span = Span with { Rows = rows } };

    /// <summary>Places the content at the top, middle or bottom of the cell's height (CSS <c>vertical-align</c> on a
    /// table cell).</summary>
    /// <param name="align">The alignment.</param>
    /// <returns>A copy with the alignment.</returns>
    public CellElement VerticalAlign(VerticalAlign align) => this with { VerticalAlignment = align };

    /// <summary>Applies a shared style.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public CellElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    CellElement IFormatted<CellElement>.WithFormat(Style format) => this with { Format = format };
}
