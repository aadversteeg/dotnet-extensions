namespace Ave.Documents.Layout;

/// <summary>
/// The space a layout algorithm may use for one box on the current page (Chromium LayoutNG's "constraint
/// space", see docs/architecture.md, Background). In horizontal writing mode, the only one the engine has, the width is the CSS inline size and
/// the height the block size.
/// </summary>
/// <param name="AvailableWidth">The width of the box's border box: its containing block's width minus its
/// horizontal margins (CSS 2.1 §10.3.3, <c>width: auto</c>).</param>
/// <param name="AvailableHeight">The height left on the current page from the top of the box's border box.
/// May be zero or negative when earlier content used up the page.</param>
/// <param name="IsAtPageStart">True when nothing that could move to a later page comes before this box on
/// the current page. Then a break before the box would make no progress, so a box that does not fit here does
/// not fit on any page.</param>
/// <param name="IsNewFormattingContext">True when the box is the root of a new block formatting context (CSS 2.1
/// §9.4.1), as the document's root box is. Its margins then do not collapse with its children's (§8.3.1: "Margins
/// of the root element's box do not collapse"). Every box inside the flow gets false.</param>
public sealed record ConstraintSpace(double AvailableWidth, double AvailableHeight, bool IsAtPageStart, bool IsNewFormattingContext = false);
