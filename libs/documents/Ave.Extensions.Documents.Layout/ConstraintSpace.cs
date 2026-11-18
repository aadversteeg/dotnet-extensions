namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The space a layout algorithm may use for one box on the current page (Chromium LayoutNG's "constraint
/// space", see docs/architecture.md, Background). In horizontal writing mode, the only one the engine has, the width is the CSS inline size and
/// the height the block size.
/// </summary>
/// <param name="AvailableWidth">The width of the box's border box: its containing block's width minus its
/// horizontal margins (CSS 2.1 §10.3.3, <c>width: auto</c>).</param>
/// <param name="AvailableHeight">The height left on the current page from the top of the box's border box.
/// May be zero or negative when earlier content used up the page.</param>
/// <param name="IsAtPageStart">True when the box's border box starts at the very top of the page area: nothing lies
/// above it on this page, no content, no margin and no padding or border of a box around it. Then a break before the
/// box would make no progress, because the next page would start the same way, so a box that does not fit here does
/// not fit on any page. Below any space at all a break before the box makes progress: the space above it stays on this
/// page (padding, a border, empty boxes) or is truncated at the break (margins, CSS Fragmentation 3 §5.2), so the box
/// moves to the next page, as Chromium prints it.</param>
/// <param name="IsNewFormattingContext">True when the box is the root of a new block formatting context (CSS 2.1
/// §9.4.1), as the document's root box is. Its margins then do not collapse with its children's (§8.3.1: "Margins
/// of the root element's box do not collapse"). Every box inside the flow gets false.</param>
public sealed record ConstraintSpace(
    double AvailableWidth, double AvailableHeight, bool IsAtPageStart, bool IsNewFormattingContext = false)
{
    /// <summary>
    /// True when content comes before the box on this page: a box that is not empty (<see cref="MarginCollapsing.IsEmpty"/>),
    /// in the box's container or before it. Only margins and empty boxes may come before a box for which this is false.
    /// A forced page break is taken only after such content (<see cref="ForcedBreaks"/>): with nothing but margins
    /// before it, it would only make an empty page.
    /// </summary>
    public bool FollowsContent { get; init; }

    /// <summary>
    /// Null, or the early break at which the box must end its fragment on this page: one the box reported in an earlier
    /// layout of the same page (<see cref="LayoutResult.Placed.BestBreak"/>), when its parent found that breaking there
    /// is better than where the parent ran out of room. The box lays its content out as before up to that breakpoint and
    /// breaks there, with the breakpoint's appeal; it looks for no other break.
    /// </summary>
    public EarlyBreak? BreakAt { get; internal init; }

    /// <summary>
    /// True for a table cell: its bottom padding and its share of the grid line below it are never left for the next page
    /// on their own, and its content does not break earlier because of them (<see cref="SlicedBox"/>); Chromium prevents
    /// the break before the block-end decorations of every table cell ("that might disturb the row stretching machinery",
    /// <c>block_layout_algorithm.cc</c>, <c>FinalizeForFragmentation</c>). The table decides where its rows break.
    /// </summary>
    public bool KeepsBottomInset { get; internal init; }
}
