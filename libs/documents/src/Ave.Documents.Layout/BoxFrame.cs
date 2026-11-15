using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Model;

namespace Ave.Documents.Layout;

/// <summary>
/// What every block-level algorithm does the same way around its content: padding, and how padding is cut at
/// page breaks. CSS Fragmentation 3 §5.4 <c>box-decoration-break: slice</c> (the initial value): the box is laid
/// out as if unbroken and then cut, so the top padding appears only on the first fragment and the bottom padding
/// only on the last.
/// </summary>
/// <param name="Width">The border-box width: all of the available width (CSS 2.1 §10.3.3).</param>
/// <param name="ContentWidth">The content width: border-box width minus horizontal padding, never negative
/// (content that is wider then overflows, as in CSS).</param>
/// <param name="TopInset">The top padding on this fragment: the full top padding on the first fragment, zero on
/// continuations.</param>
/// <param name="BottomInset">The bottom padding, kept free on every fragment.</param>
/// <param name="ContentHeight">The height available for content on this page.</param>
internal sealed record BoxFrame(double Width, double ContentWidth, double TopInset, double BottomInset, double ContentHeight)
{
    /// <summary>Computes the frame of a box for one layout call.</summary>
    /// <remarks>
    /// The bottom padding is reserved on every page, not only on the last, because whether this fragment is the
    /// last is only known after its content is laid out. A fragment that ends at a page break therefore leaves up
    /// to one bottom padding of space unused. Simpler, and never draws content into the padding.
    /// </remarks>
    public static BoxFrame For(Box box, ConstraintSpace space, BreakToken? incoming)
    {
        var padding = box.Style.Padding;
        var topInset = incoming is null ? padding.Top.Points : 0;
        var bottomInset = padding.Bottom.Points;
        return new BoxFrame(
            Width: space.AvailableWidth,
            ContentWidth: Math.Max(0, space.AvailableWidth - padding.Horizontal.Points),
            TopInset: topInset,
            BottomInset: bottomInset,
            ContentHeight: space.AvailableHeight - topInset - bottomInset);
    }

    /// <summary>True when at least the padding of this fragment fits in the space.</summary>
    public bool PaddingFits(ConstraintSpace space) => Fitting.Fits(TopInset + BottomInset, space.AvailableHeight);

    /// <summary>
    /// Builds the box fragment around content laid out in content coordinates: the content is moved inside the
    /// padding, and the height is the top inset plus the content plus — on the last fragment only — the bottom
    /// padding.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="content">The content fragments, positioned relative to the content area.</param>
    /// <param name="contentHeight">The height the content used.</param>
    /// <param name="isLastFragment">True when the box ends on this page.</param>
    public BoxFragment Wrap(Box box, IEnumerable<PlacedFragment> content, double contentHeight, bool isLastFragment)
    {
        var shift = new Point(box.Style.Padding.Left.Points, TopInset);
        var children = content.Select(child => child with { Offset = child.Offset + shift }).ToImmutableArray();
        var height = TopInset + contentHeight + (isLastFragment ? BottomInset : 0);
        return new BoxFragment(box, new Size(Width, height), children);
    }
}
