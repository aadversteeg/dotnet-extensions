using System;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// What every block-level algorithm does the same way around its content: padding, and how the box is cut at
/// page breaks. Follows <c>box-decoration-break: slice</c>, the initial value (CSS Fragmentation 3 §5.4): the box
/// is laid out as if unbroken and then cut, so the top padding appears only on the first fragment and the bottom
/// padding only on the last.
/// </summary>
internal static class SlicedBox
{
    /// <summary>The content an algorithm placed inside the padding on this page.</summary>
    /// <param name="Children">The child fragments, positioned relative to the content area.</param>
    /// <param name="Height">The height the content used.</param>
    /// <param name="Continuation">Where the content resumes on the next page, or null when it is complete.</param>
    public sealed record Content(ValueList<PlacedFragment> Children, double Height, BreakToken? Continuation);

    /// <summary>
    /// Lays out an algorithm's content in a content area of the given size: the content or None when not even
    /// its first piece fits, or an error. Pure, so it may be called twice (see <see cref="Layout"/>).
    /// </summary>
    public delegate Result<Maybe<Content>, Error> ContentLayout(double contentWidth, double contentHeight);

    /// <summary>The result "this content was placed".</summary>
    /// <param name="children">The child fragments, relative to the content area.</param>
    /// <param name="height">The height the content used.</param>
    /// <param name="continuation">Where to resume on the next page, or null.</param>
    /// <returns>The successful content result.</returns>
    public static Result<Maybe<Content>, Error> Placed(ValueList<PlacedFragment> children, double height, BreakToken? continuation) =>
        Result<Maybe<Content>, Error>.Success(Maybe.From(new Content(children, height, continuation)));

    /// <summary>The result "not even the first piece of content fits".</summary>
    public static Result<Maybe<Content>, Error> NothingFits => Result<Maybe<Content>, Error>.Success(Maybe<Content>.None);

    /// <summary>
    /// Lays out a box: its content inside the padding, sliced at the page end.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The content may use all the height below the top padding: with <c>slice</c> the bottom padding is not
    /// part of a fragment that breaks.</item>
    /// <item>A fragment that breaks fills the rest of the page: "When a box breaks, its content box extends to
    /// fill any remaining fragmentainer extent" (CSS Fragmentation 3 §5.3), so its background reaches the page
    /// end.</item>
    /// <item>When the content is complete but the bottom padding no longer fits below it, the box cannot end on
    /// this page: the padding is not a place where a break is possible (§4.1), so the break must come earlier.
    /// The content is laid out again with the bottom padding kept free, which moves the break to the last
    /// breakpoint before it — what Chromium prints for this case (checked against its print output). Because a
    /// layout call is pure, this second call is just another call; no state is kept.</item>
    /// <item>LIMIT: if even then nothing fits and the box is at the start of a page, it is placed anyway and its
    /// bottom padding overflows the page area; there is no earlier page where it would fit better (§4.4 allows
    /// a UA to overflow when no break point is left).</item>
    /// </list>
    /// </remarks>
    /// <param name="box">The box.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">The box's break token, or null on its first fragment.</param>
    /// <param name="layOut">The algorithm's content layout.</param>
    /// <returns>The box fragment and continuation, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(Box box, ConstraintSpace space, BreakToken? incoming, ContentLayout layOut)
    {
        var padding = box.Style.Padding;
        var topInset = incoming is null ? padding.Top.Points : 0;
        var bottomInset = padding.Bottom.Points;
        if (!Fitting.Fits(topInset, space.AvailableHeight))
        {
            return LayoutResult.NothingFits(box, space);
        }

        // The content width never becomes negative: wider content overflows, as in CSS.
        var contentWidth = Math.Max(0, space.AvailableWidth - padding.Horizontal.Points);
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

            if (reserved.Value.HasValue)
            {
                content = reserved.Value.Value;
            }
            else if (!space.IsAtPageStart)
            {
                return LayoutResult.NothingFits(box, space);
            }
        }

        return LayoutResult.Place(Wrap(box, space, topInset, bottomInset, content), content.Continuation);
    }

    /// <summary>
    /// The box fragment around the content: the content moved inside the padding; the height is the top inset
    /// plus the content plus the bottom padding on the last fragment, or the whole remaining page on a fragment
    /// that breaks (§5.3).
    /// </summary>
    private static BoxFragment Wrap(Box box, ConstraintSpace space, double topInset, double bottomInset, Content content)
    {
        var shift = new Point(box.Style.Padding.Left.Points, topInset);
        ValueList<PlacedFragment> children = [.. content.Children.Select(child => child with { Offset = child.Offset + shift })];
        var used = topInset + content.Height;
        var height = content.Continuation is null ? used + bottomInset : Math.Max(used, space.AvailableHeight);
        return new BoxFragment(box, new Size(space.AvailableWidth, height), children);
    }
}
