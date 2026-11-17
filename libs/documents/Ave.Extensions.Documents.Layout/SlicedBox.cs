using System;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// What every block-level algorithm does the same way around its content: border and padding, and how the box is
/// cut at page breaks. Follows <c>box-decoration-break: slice</c>, the initial value (CSS Fragmentation 3 §5.4): the
/// box is laid out as if unbroken and then cut, so the top border and padding appear only on the first fragment and
/// the bottom border and padding only on the last.
/// </summary>
internal static class SlicedBox
{
    /// <summary>The content an algorithm placed inside the padding on this page.</summary>
    /// <param name="Children">The child fragments, positioned relative to the content area.</param>
    /// <param name="Height">The height the content used.</param>
    /// <param name="Continuation">Where the content resumes on the next page, or null when it is complete.</param>
    /// <param name="Appeal">How good the break is that ends the content on this page.</param>
    public sealed record Content(ValueList<PlacedFragment> Children, double Height, BreakToken? Continuation, BreakAppeal Appeal = BreakAppeal.Perfect);

    /// <summary>
    /// Lays out an algorithm's content in a content area of the given size: the content or None when not even
    /// its first piece fits, or an error. Pure, so it may be called twice (see <see cref="Layout"/>).
    /// </summary>
    public delegate Result<Maybe<Content>, Error> ContentLayout(double contentWidth, double contentHeight);

    /// <summary>The result "this content was placed".</summary>
    /// <param name="children">The child fragments, relative to the content area.</param>
    /// <param name="height">The height the content used.</param>
    /// <param name="continuation">Where to resume on the next page, or null.</param>
    /// <param name="appeal">How good the break is that ends the content.</param>
    /// <returns>The successful content result.</returns>
    public static Result<Maybe<Content>, Error> Placed(
        ValueList<PlacedFragment> children, double height, BreakToken? continuation, BreakAppeal appeal = BreakAppeal.Perfect) =>
        Result<Maybe<Content>, Error>.Success(Maybe.From(new Content(children, height, continuation, appeal)));

    /// <summary>The result for a box of which not even the first piece of content fits.</summary>
    public static Result<Maybe<Content>, Error> NothingFits => Result<Maybe<Content>, Error>.Success(Maybe<Content>.None);

    /// <summary>The horizontal space border and padding take: left plus right.</summary>
    /// <param name="style">The box's style.</param>
    /// <returns>The inset, in points.</returns>
    public static double HorizontalInsets(BoxStyle style) => style.Padding.Horizontal.Points + style.Border.Widths.Horizontal.Points;

    /// <summary>The vertical space the bottom padding and border take on the box's last fragment.</summary>
    /// <param name="style">The box's style.</param>
    /// <returns>The inset, in points.</returns>
    public static double BottomInset(BoxStyle style) => style.Padding.Bottom.Points + style.Border.Widths.Bottom.Points;

    /// <summary>Whether anything of the box itself (bottom padding or border) comes after its content.</summary>
    /// <param name="style">The box's style.</param>
    /// <returns>True when the bottom inset is not zero.</returns>
    public static bool HasBottomInset(BoxStyle style) => BottomInset(style) > 0;

    /// <summary>
    /// Lays out a box: its content inside the border and padding, sliced at the page end.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The content may use all the height below the top border and padding: with <c>slice</c> the bottom ones
    /// are not part of a fragment that breaks.</item>
    /// <item>A fragment that breaks fills the rest of the page: "When a box breaks, its content box extends to
    /// fill any remaining fragmentainer extent" (CSS Fragmentation 3 §5.3), so its background reaches the page
    /// end.</item>
    /// <item>When the content is complete but the bottom padding and border no longer fit below it, the box cannot
    /// end on this page: they are not a place where a break is possible (§4.1), so the break must come earlier.
    /// The content is laid out again with that space kept free, which moves the break to the last breakpoint
    /// before it — what Chromium prints for this case (checked against its print output). Because a layout call is
    /// pure, this second call is just another call; no state is kept.</item>
    /// <item>Content that does not leave that room either — empty content, which fits any height — means nothing of
    /// the box fits: the box moves to the next page whole, since its bottom padding and border are no breakpoint.</item>
    /// <item>LIMIT: if even then nothing fits and the box is at the start of a page, it is placed anyway and its
    /// bottom padding and border overflow the page area; there is no earlier page where it would fit better.
    /// CSS Fragmentation 3 §4.4 would let the user agent "break anywhere in order to avoid losing content off the edge
    /// of the fragmentainer"; the engine does not cut padding or borders.</item>
    /// </list>
    /// </remarks>
    /// <param name="box">The box.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">The box's break token, or null on its first fragment.</param>
    /// <param name="layOut">The algorithm's content layout.</param>
    /// <returns>The box fragment and continuation, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(Box box, ConstraintSpace space, BreakToken? incoming, ContentLayout layOut)
    {
        var (padding, border) = (box.Style.Padding, box.Style.Border.Widths);
        var topInset = incoming is null ? border.Top.Points + padding.Top.Points : 0;
        var bottomInset = BottomInset(box.Style);
        if (!Fitting.Fits(topInset, space.AvailableHeight))
        {
            return LayoutResult.NothingFits(box, space);
        }

        // The content width never becomes negative: wider content overflows, as in CSS.
        var contentWidth = Math.Max(0, space.AvailableWidth - HorizontalInsets(box.Style));
        var contentHeight = space.AvailableHeight - topInset;
        var first = layOut(contentWidth, contentHeight);
        if (first.IsFailure || first.Value.HasNoValue)
        {
            return first.IsFailure ? Result<LayoutResult, Error>.Failure(first.Error) : LayoutResult.NothingFits(box, space);
        }

        var content = first.Value.Value;
        var endsHere = content.Continuation is null;
        if (endsHere && !Fitting.Fits(topInset + content.Height + bottomInset, space.AvailableHeight))
        {
            var reserved = layOut(contentWidth, contentHeight - bottomInset);
            if (reserved.IsFailure)
            {
                return Result<LayoutResult, Error>.Failure(reserved.Error);
            }

            // The content placed with the room kept free must leave that room: empty content "fits" any height, even
            // the negative one left when not even the bottom padding and border fit, and would end below the page.
            if (reserved.Value.HasValue && Fitting.Fits(topInset + reserved.Value.Value.Height + bottomInset, space.AvailableHeight))
            {
                content = reserved.Value.Value;
            }
            else if (!space.IsAtPageStart)
            {
                return LayoutResult.NothingFits(box, space);
            }
        }

        return LayoutResult.Place(Wrap(box, space, incoming is null, topInset, bottomInset, content), content.Continuation, content.Appeal);
    }

    /// <summary>
    /// The box fragment around the content: the content moved inside the border and padding; the height is the top
    /// inset plus the content plus the bottom inset on the last fragment, or the whole remaining page on a fragment
    /// that breaks (§5.3). The fragment paints the left and right border always, the top one on the first fragment
    /// and the bottom one on the last (slice, §5.4).
    /// </summary>
    private static BoxFragment Wrap(Box box, ConstraintSpace space, bool isFirst, double topInset, double bottomInset, Content content)
    {
        var style = box.Style;
        var shift = new Point(style.Border.Widths.Left.Points + style.Padding.Left.Points, topInset);
        ValueList<PlacedFragment> children = [.. content.Children.Select(child => child with { Offset = child.Offset + shift })];
        var isLast = content.Continuation is null;
        var used = topInset + content.Height;
        var height = isLast ? used + bottomInset : Math.Max(used, space.AvailableHeight);
        var painted = style.Border with
        {
            Top = isFirst ? style.Border.Top : BorderSide.None,
            Bottom = isLast ? style.Border.Bottom : BorderSide.None,
        };
        return new BoxFragment(box, new Size(space.AvailableWidth, height), children, painted);
    }
}
