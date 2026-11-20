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
    /// The height of the page area the box is laid out in (Chromium's "fragmentainer block size", beside its available
    /// block size), less the room the repeated header rows of the tables around the box take on this page: Chromium
    /// reserves it in the fragmentainer for the content of the rows below them
    /// (<c>ConstraintSpaceBuilder::ReserveSpaceInFragmentainer</c>); on a table's later pages that room includes the outer
    /// half of the table's top grid line, which the repeated header draws (<see cref="TablePage"/>, Reserved).
    /// Unlike <see cref="AvailableHeight"/> it does not depend on where the box starts on the page, and a layout with
    /// unlimited height (the measurement of a table cell) carries the value of the page it measures for. A table decides
    /// from it whether its header rows repeat (<see cref="TablePage"/>). Zero when not given: then no header row repeats.
    /// </summary>
    public double FragmentainerBlockSize { get; init; }

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

    /// <summary>
    /// True when <see cref="IsAtPageStart"/> is false only because of the top padding and borders of boxes around this
    /// one that start the page (a table cell's half of the grid line above it is such an inset): nothing else lies above
    /// the box on this page. A break before the box would then leave only those insets on the page, which is a last
    /// resort too, so a last-resort break inside the box is taken instead: it is as good, and it lies deeper, where
    /// Chromium breaks when no breakpoint is valid (case <c>a4-image-nested-table</c>: the content of a table in a table
    /// cell goes on on the next page, inside both tables). Below such insets it is set only under
    /// <c>MonolithicOverflow.Slice</c>, where it lets the slice of such content start at the top of the next page; under the
    /// default profile a break before the box keeps its own strip of insets and leaves no empty part of a table row
    /// (<c>EmptyRowParts.Avoid</c>). Under every profile it is set too for the first child of a box that starts the page
    /// when nothing but the margins kept at the page start lie above it (after a forced break, CSS Fragmentation 3 §5.2),
    /// also through boxes around it without top padding or border: there is no breakpoint before it, and a break before
    /// it would leave only those margins on the page. A table there keeps its first row's top line and padding on the page
    /// and goes on on the next, as Chromium prints it (cases <c>fr-forced-table</c>, <c>fr-forced-table-in-box</c>).
    /// </summary>
    public bool IsBelowStartInsets { get; init; }
}
