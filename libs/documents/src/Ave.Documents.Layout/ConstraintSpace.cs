namespace Ave.Documents.Layout;

/// <summary>
/// The space a layout algorithm may use for one box on the current page (Chromium's "constraint space",
/// research §6.1). In horizontal writing mode, the only one stage 0 has, the width is the CSS inline size and
/// the height the block size.
/// </summary>
/// <param name="AvailableWidth">The width of the box's border box: its containing block's width minus its
/// horizontal margins (CSS 2.1 §10.3.3, <c>width: auto</c>).</param>
/// <param name="AvailableHeight">The height left on the current page from the top of the box's border box.
/// May be zero or negative when earlier content used up the page.</param>
/// <param name="IsAtPageStart">True when nothing that could move to a later page comes before this box on
/// the current page. Then a break before the box would make no progress, so a box that does not fit here does
/// not fit on any page.</param>
public sealed record ConstraintSpace(double AvailableWidth, double AvailableHeight, bool IsAtPageStart);
