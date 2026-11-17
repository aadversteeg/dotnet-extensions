using System.Collections.Generic;
using System.Linq;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Layout;

/// <summary>
/// The collapsing border model of tables (CSS 2.1 §17.6.2), chosen over the separated model: between two cells there
/// is ONE line, centred on the grid line, as in a spreadsheet, a Typst table stroke or a Word table — what "grid lines"
/// mean in a business document. The separated model with zero spacing (§17.6.1) would need nothing new in layout
/// (every cell paints its own border box with <see cref="SlicedBox"/>), but two adjacent 1pt cell borders would make a
/// 2pt line, and authors would have to give cells only a right and bottom border to avoid that.
/// </summary>
/// <remarks>
/// <para>Each line segment is resolved from every border that meets there — the cells on both sides, the rows, the
/// row groups at a group boundary, the table at its edge — by the conflict rules of §17.6.2.1: <c>none</c> has the
/// lowest priority (a zero width here); the wider border wins; with equal widths (all borders are solid) "a style set
/// on a cell wins over one on a row, which wins over a row group, column, column group and, lastly, table"; between
/// two of the same kind the one further left (or further up) wins. <c>hidden</c>, other styles than solid, and
/// column borders are not in the model.</para>
/// <para>Fragmentation: a page's grid is the rows on that page. Lines between rows that are neighbours on the page
/// collapse as usual; the table's top border joins the first row only on the table's first fragment and on repeated
/// header rows, its bottom border the last row only on its last fragment; a cell cut by the page end has no border at
/// the cut (slice, CSS Fragmentation 3 §5.4).</para>
/// </remarks>
internal static class CollapsedBorders
{
    /// <summary>Where a border comes from, in the priority order of §17.6.2.1 rule 4 (higher wins).</summary>
    private enum Origin
    {
        Table,
        RowGroup,
        Row,
        Cell,
    }

    /// <summary>
    /// The vertical line at column boundary <paramref name="boundary"/> (0 = left edge, column count = right edge) in
    /// row <paramref name="row"/>. None where a cell spans across the boundary.
    /// </summary>
    /// <param name="table">The tracks box.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="row">The row.</param>
    /// <param name="boundary">The column boundary.</param>
    /// <returns>The resolved border.</returns>
    public static BorderSide Vertical(Box table, TableGrid grid, int row, int boundary)
    {
        var left = grid.CellAt(row, boundary - 1);
        var right = grid.CellAt(row, boundary);
        if (left is not null && left == right)
        {
            return BorderSide.None;
        }

        var rowInfo = grid.Rows[row];
        var isLeftEdge = boundary == 0;
        var isRightEdge = boundary == grid.ColumnCount;
        IEnumerable<(BorderSide, Origin)> candidates =
        [
            .. isLeftEdge ? [(table.Style.Border.Left, Origin.Table), (rowInfo.Group.Style.Border.Left, Origin.RowGroup), (rowInfo.Box.Style.Border.Left, Origin.Row)] : Nothing,
            .. left is { } leftCell ? [(grid.Cells[leftCell].Box.Style.Border.Right, Origin.Cell)] : Nothing,
            .. right is { } rightCell ? [(grid.Cells[rightCell].Box.Style.Border.Left, Origin.Cell)] : Nothing,
            .. isRightEdge ? [(rowInfo.Box.Style.Border.Right, Origin.Row), (rowInfo.Group.Style.Border.Right, Origin.RowGroup), (table.Style.Border.Right, Origin.Table)] : Nothing,
        ];
        return Winner(candidates);
    }

    /// <summary>
    /// The horizontal line in column <paramref name="column"/> between row <paramref name="above"/> and row
    /// <paramref name="below"/> as they stand on the page; null for the table's top or bottom edge on this page. None
    /// where one cell spans both rows — except at a break with clone (<see cref="LineContext.ClosesCutCells"/>), where
    /// that cell is closed by its own bottom and top border, as a cell that ends above the line and one that starts
    /// below it.
    /// </summary>
    /// <param name="table">The tracks box.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="above">The row above the line, or null at the top of the page's grid.</param>
    /// <param name="below">The row below the line, or null at the bottom of the page's grid.</param>
    /// <param name="column">The column.</param>
    /// <param name="edge">Whether the table's own border takes part at a top or bottom edge, and which cells are cut
    /// at this line.</param>
    /// <returns>The resolved border.</returns>
    public static BorderSide Horizontal(Box table, TableGrid grid, int? above, int? below, int column, LineContext edge)
    {
        var upper = above is { } a ? grid.CellAt(a, column) : null;
        var lower = below is { } b ? grid.CellAt(b, column) : null;
        if (upper is not null && upper == lower && !edge.ClosesCutCells)
        {
            return BorderSide.None;
        }

        // A cell takes part with its bottom border when it ends above the line and with its top border when it starts
        // below it; at a break with clone also when the line cuts it, since clone closes every fragment.
        var upperRow = above is { } top ? grid.Rows[top] : null;
        var lowerRow = below is { } bottom ? grid.Rows[bottom] : null;
        var groupBoundary = upperRow is null || lowerRow is null || upperRow.Group != lowerRow.Group;
        var upperCell = upper is { } u && (grid.Cells[u].LastRow == above || edge.ClosesCutCells) && !edge.IsCut(u) ? grid.Cells[u] : null;
        var lowerCell = lower is { } l && (grid.Cells[l].Row == below || edge.ClosesCutCells) && !edge.IsCut(l) ? grid.Cells[l] : null;
        IEnumerable<(BorderSide, Origin)> candidates =
        [
            .. upperRow is null && edge.IncludesTable ? [(table.Style.Border.Top, Origin.Table)] : Nothing,
            .. upperRow is not null && groupBoundary ? [(upperRow.Group.Style.Border.Bottom, Origin.RowGroup)] : Nothing,
            .. upperRow is not null ? [(upperRow.Box.Style.Border.Bottom, Origin.Row)] : Nothing,
            .. upperCell is not null ? [(upperCell.Box.Style.Border.Bottom, Origin.Cell)] : Nothing,
            .. lowerCell is not null ? [(lowerCell.Box.Style.Border.Top, Origin.Cell)] : Nothing,
            .. lowerRow is not null && !edge.RowBelowIsCut ? [(lowerRow.Box.Style.Border.Top, Origin.Row)] : Nothing,
            .. lowerRow is not null && groupBoundary ? [(lowerRow.Group.Style.Border.Top, Origin.RowGroup)] : Nothing,
            .. lowerRow is null && edge.IncludesTable ? [(table.Style.Border.Bottom, Origin.Table)] : Nothing,
        ];
        return Winner(candidates);
    }

    /// <summary>
    /// The conflict rules of §17.6.2.1 over candidates given in left-to-right / top-to-bottom order: the widest wins;
    /// among equal widths the higher origin; among equal origins the first (further left or up). A zero width is
    /// <c>none</c>, which only wins when every candidate is <c>none</c>.
    /// </summary>
    private static BorderSide Winner(IEnumerable<(BorderSide Side, Origin Origin)> candidates) =>
        candidates
            .Where(candidate => candidate.Side.IsVisible)
            .Select((candidate, order) => (candidate.Side, candidate.Origin, Order: order))
            .OrderByDescending(candidate => candidate.Side.Width.Points)
            .ThenByDescending(candidate => candidate.Origin)
            .ThenBy(candidate => candidate.Order)
            .Select(candidate => candidate.Side)
            .FirstOrDefault();

    private static IEnumerable<(BorderSide, Origin)> Nothing => [];

    /// <summary>
    /// What a horizontal line needs to know about the page: whether the table's border joins an edge, which cells and
    /// row continue from the previous page (with slice their border at the cut is sliced away; only lines at the top of a
    /// continued part need this), and whether the line is a break with clone, which closes every cell it cuts with that
    /// cell's own border (see <see cref="TablePage"/>).
    /// </summary>
    /// <param name="IncludesTable">True when the table's top (or bottom) border takes part at this edge.</param>
    /// <param name="RowBelowIsCut">True when the row below the line continues from the previous page.</param>
    /// <param name="CutCells">The cells cut at this line whose border is sliced away.</param>
    /// <param name="ClosesCutCells">True for the line that ends or starts a page at a break with clone: a cell the line
    /// cuts (one that spans across it, or continues below it) takes part with its own bottom border above the line and
    /// its own top border below it, so the table is closed across its full width.</param>
    public sealed record LineContext(bool IncludesTable, bool RowBelowIsCut, IReadOnlySet<int> CutCells, bool ClosesCutCells = false)
    {
        /// <summary>A line between two rows that are both whole on this page.</summary>
        public static LineContext Interior { get; } = new(false, false, new HashSet<int>());

        /// <summary>The line at a break between two rows, or inside a row, with clone: it ends one page or starts the
        /// next, and closes every cell it cuts.</summary>
        public static LineContext ClonedBreak { get; } = new(false, false, new HashSet<int>(), ClosesCutCells: true);

        /// <summary>True when the cell is cut at this line.</summary>
        /// <param name="cell">The cell index.</param>
        /// <returns>Whether its border here is sliced away.</returns>
        public bool IsCut(int cell) => CutCells.Contains(cell);
    }
}
