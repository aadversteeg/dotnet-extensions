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
    /// <param name="BestBreak">For complete content, the best breakpoint inside it (<see cref="LayoutResult.Placed.BestBreak"/>).</param>
    /// <param name="AtEnd">For complete content, the algorithm's token for "all content placed, only the end of the box
    /// is left": where the box resumes when it breaks before its bottom padding and border (see <see cref="Layout"/>).
    /// Null when the algorithm has none; then the box never breaks there.</param>
    /// <param name="KeepsItsBreak">For content that breaks, true when it broke at a breakpoint it chose before the place
    /// where it ran out of room (<see cref="LayoutResult.Placed.KeepsItsBreak"/>).</param>
    /// <param name="KeptAppeal">For content that keeps its break, that break's appeal for the parent's comparison
    /// (<see cref="LayoutResult.Placed.KeptAppeal"/>).</param>
    public sealed record Content(
        ValueList<PlacedFragment> Children, double Height, BreakToken? Continuation, BreakAppeal Appeal = BreakAppeal.Perfect, EarlyBreak? BestBreak = null,
        BreakToken? AtEnd = null, bool KeepsItsBreak = false, BreakAppeal KeptAppeal = BreakAppeal.Perfect);

    /// <summary>
    /// Lays out an algorithm's content in a content area of the given size, ending at <paramref name="breakAt"/> when
    /// it is given: the content or None when not even its first piece fits, or an error. Pure, so it may be called
    /// twice (see <see cref="Layout"/>).
    /// </summary>
    public delegate Result<Maybe<Content>, Error> ContentLayout(double contentWidth, double contentHeight, EarlyBreak? breakAt);

    /// <summary>The result "this content was placed".</summary>
    /// <param name="children">The child fragments, relative to the content area.</param>
    /// <param name="height">The height the content used.</param>
    /// <param name="continuation">Where to resume on the next page, or null.</param>
    /// <param name="appeal">How good the break is that ends the content.</param>
    /// <param name="bestBreak">For complete content, the best breakpoint inside it.</param>
    /// <param name="atEnd">For complete content, the token that resumes after all of it (<see cref="Content.AtEnd"/>).</param>
    /// <param name="keepsItsBreak">For content that breaks, whether it broke at a breakpoint it chose (<see cref="Content.KeepsItsBreak"/>).</param>
    /// <param name="keptAppeal">For content that keeps its break, that break's appeal for the comparison (<see cref="Content.KeptAppeal"/>).</param>
    /// <returns>The successful content result.</returns>
    public static Result<Maybe<Content>, Error> Placed(
        ValueList<PlacedFragment> children, double height, BreakToken? continuation, BreakAppeal appeal = BreakAppeal.Perfect, EarlyBreak? bestBreak = null,
        BreakToken? atEnd = null, bool keepsItsBreak = false, BreakAppeal keptAppeal = BreakAppeal.Perfect) =>
        Result<Maybe<Content>, Error>.Success(Maybe.From(new Content(
            children, height, continuation, appeal, continuation is null ? bestBreak : null, continuation is null ? atEnd : null, continuation is not null && keepsItsBreak,
            continuation is not null && keepsItsBreak ? keptAppeal : BreakAppeal.Perfect)));

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

    /// <summary>The vertical space the top border and padding take on this fragment: on the first fragment only
    /// (slice).</summary>
    /// <param name="style">The box's style.</param>
    /// <param name="incoming">The box's break token, or null on its first fragment.</param>
    /// <returns>The inset, in points.</returns>
    public static double TopInset(BoxStyle style, BreakToken? incoming) =>
        incoming is null ? style.Border.Widths.Top.Points + style.Padding.Top.Points : 0;

    /// <summary>
    /// Whether the box's content starts at the very top of the page area: the box does
    /// (<see cref="ConstraintSpace.IsAtPageStart"/>) and no top border or padding of it lies above its content on this
    /// fragment.
    /// </summary>
    /// <param name="style">The box's style.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">The box's break token, or null on its first fragment.</param>
    /// <returns>True when a break before the content would make no progress.</returns>
    public static bool ContentIsAtPageStart(BoxStyle style, ConstraintSpace space, BreakToken? incoming) =>
        space.IsAtPageStart && !(TopInset(style, incoming) > 0);

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
    /// <item>THE RULE FOR BOTTOM PADDING AND BORDER THAT DO NOT FIT, as Chromium has it (<c>FinishFragmentation</c> in
    /// <c>third_party/blink/renderer/core/layout/fragmentation_utils.cc</c>): the content is complete, but the bottom
    /// padding and border no longer fit below it. They are no place for a valid break (§4.1: a class C break needs a gap
    /// between the last child and the box's content edge), so:
    /// <list type="number">
    /// <item>When the content has a breakpoint inside it (its <see cref="Content.BestBreak"/>, an early break), the
    /// content is laid out once more, breaking there; a fragment that breaks has no bottom padding or border (slice), so
    /// the content may use the whole height up to that breakpoint. The box keeps that break
    /// (<see cref="LayoutResult.Placed.KeepsItsBreak"/>) and reports it as perfect, lowered only by the
    /// <c>break-inside: avoid</c> of the boxes it lies inside (<see cref="BreakAvoidance.Kept"/>): a break against orphans,
    /// widows or a keep-with-next stays inside the box although a perfect break before the box exists (cases
    /// <c>a3-pad-*</c>, <c>fr-pad-own-break*</c>, <c>fr-pad-against-keep</c>: <c>ia0 p0 | p1 z0</c>); a break inside a box or
    /// paragraph kept together loses to a better break before the box, which moves whole (cases <c>kb-n*</c>:
    /// <c>i | a0 a1 a2 z</c>).</item>
    /// <item>Otherwise the box breaks before its bottom padding and border, as a last resort
    /// (<see cref="BreakAppeal.LastResort"/>): this fragment holds all the content and ends right below it (it does not
    /// fill the page), and the bottom padding and border start the next page, with what follows the box below them
    /// (cases <c>fr-pad-line</c>: <c>c | (padding) | AFTER</c>; <c>fr-border-continued</c>; <c>fr-pad-figure</c>). A
    /// parent with a better breakpoint takes that instead, so a box lower on the page moves whole
    /// (<c>fr-pad-after-content</c>). Only where something of the box besides its bottom inset is on the page (progress),
    /// and only for an algorithm that can resume after all its content (<see cref="Content.AtEnd"/>).</item>
    /// <item>Never in a table cell (<see cref="ConstraintSpace.KeepsBottomInset"/>): Chromium prevents both for every
    /// cell, whose bottom inset holds the row together; the cell keeps the engine's rule for cells (the table decides).</item>
    /// </list>
    /// Because a layout call is pure, a second call is just another call; no state is kept.</item>
    /// <item>When not even the first piece of the content fits, the box breaks before it as a last resort
    /// (<see cref="BreakAppeal.LastResort"/>): its fragment holds only its top border and padding, filling the rest of the
    /// page, and the content starts the next page. A parent with any better breakpoint takes that instead, so a box lower
    /// on the page moves whole; at the top of the page the box's own padding and border are the space a break leaves
    /// behind (Chromium prints both cases so). Only when the content itself starts the page
    /// (<see cref="ContentIsAtPageStart"/>) does nothing fit on any page: <c>Documents.Layout.DoesNotFit</c>. A box that
    /// continues from the page before has no top border or padding here, so such a break would leave nothing of it on
    /// the page and resume where it resumed: no progress. It has no room here instead (under a table's repeated header,
    /// the table then goes on without the header).</item>
    /// <item>LIMIT: if even then nothing fits and the box is at the start of a page (bottom padding and border alone
    /// taller than the page area, or an algorithm without <see cref="Content.AtEnd"/>), it is placed anyway and its
    /// bottom padding and border overflow the page area; there is no earlier page where it would fit better.
    /// CSS Fragmentation 3 §4.4 would let the user agent "break anywhere in order to avoid losing content off the edge
    /// of the fragmentainer"; the engine does not cut padding or borders. When anything of the container follows the box
    /// on a later page, the container refuses the page instead (<c>Documents.Layout.DoesNotFit</c>, see
    /// <see cref="FlowLayout"/>): no result is a success while content is left that no page shows.</item>
    /// </list>
    /// </remarks>
    /// <param name="box">The box.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">The box's break token, or null on its first fragment.</param>
    /// <param name="start">The token that starts the content where this fragment would: the algorithm resumes there on
    /// the next page after a last-resort break before the content.</param>
    /// <param name="layOut">The algorithm's content layout.</param>
    /// <returns>The box fragment and continuation, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(Box box, ConstraintSpace space, BreakToken? incoming, BreakToken start, ContentLayout layOut)
    {
        var topInset = TopInset(box.Style, incoming);
        var bottomInset = BottomInset(box.Style);
        if (!Fitting.Fits(topInset, space.AvailableHeight))
        {
            return LayoutResult.NothingFits(box, space);
        }

        // The content width never becomes negative: wider content overflows, as in CSS.
        var contentWidth = Math.Max(0, space.AvailableWidth - HorizontalInsets(box.Style));
        var contentHeight = space.AvailableHeight - topInset;
        var first = layOut(contentWidth, contentHeight, space.BreakAt);
        if (first.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(first.Error);
        }

        if (first.Value.HasNoValue)
        {
            // A last resort that resumes where this fragment resumed leaves nothing of the box on the page: no progress, so
            // nothing of the box fits here.
            return ContentIsAtPageStart(box.Style, space, incoming) || start.Equals(incoming)
                ? LayoutResult.NothingFits(box, space)
                : LayoutResult.Place(Wrap(box, space, incoming is null, topInset, bottomInset, new Content([], 0, start, BreakAppeal.LastResort)), start, BreakAppeal.LastResort);
        }

        var content = first.Value.Value;
        var endsHere = content.Continuation is null;
        var fillsPage = true;
        if (endsHere && !Fitting.Fits(topInset + content.Height + bottomInset, space.AvailableHeight))
        {
            var progress = topInset + content.Height > 0 && Fitting.Fits(topInset + content.Height, space.AvailableHeight);
            if (content.BestBreak is { } earlier)
            {
                var broken = layOut(contentWidth, contentHeight, earlier);
                if (broken.IsFailure)
                {
                    return Result<LayoutResult, Error>.Failure(broken.Error);
                }

                if (broken.Value.HasNoValue || broken.Value.Value.Continuation is null)
                {
                    return Result<LayoutResult, Error>.Failure(LayoutErrors.EngineDefect(box, "did not break at the early break it reported"));
                }

                content = space.KeepsBottomInset ? broken.Value.Value : broken.Value.Value with { KeepsItsBreak = true, KeptAppeal = BreakAvoidance.Kept(box, earlier) };
            }
            else if (content.AtEnd is { } atEnd && !space.KeepsBottomInset && progress)
            {
                // Before the bottom padding and border: the content stays whole, they start the next page.
                content = content with { Continuation = atEnd, Appeal = BreakAppeal.LastResort, AtEnd = null };
                fillsPage = false;
            }
            else if (!space.IsAtPageStart)
            {
                return LayoutResult.NothingFits(box, space);
            }
        }

        return LayoutResult.Place(Wrap(box, space, incoming is null, topInset, bottomInset, content, fillsPage), content.Continuation, content.Appeal, content.BestBreak, content.KeepsItsBreak, content.KeptAppeal);
    }

    /// <summary>
    /// The box fragment around the content: the content moved inside the border and padding; the height is the top
    /// inset plus the content plus the bottom inset on the last fragment, or the whole remaining page on a fragment
    /// that breaks (§5.3) — except before its bottom padding and border (<paramref name="fillsPage"/> false), where it
    /// ends right below its content, as Chromium prints it. The fragment paints the left and right border always, the
    /// top one on the first fragment and the bottom one on the last (slice, §5.4).
    /// </summary>
    private static BoxFragment Wrap(Box box, ConstraintSpace space, bool isFirst, double topInset, double bottomInset, Content content, bool fillsPage = true)
    {
        var style = box.Style;
        var shift = new Point(style.Border.Widths.Left.Points + style.Padding.Left.Points, topInset);
        ValueList<PlacedFragment> children = [.. content.Children.Select(child => child with { Offset = child.Offset + shift })];
        var isLast = content.Continuation is null;
        var used = topInset + content.Height;
        var height = isLast ? used + bottomInset : fillsPage ? Math.Max(used, space.AvailableHeight) : used;
        var painted = style.Border with
        {
            Top = isFirst ? style.Border.Top : BorderSide.None,
            Bottom = isLast ? style.Border.Bottom : BorderSide.None,
        };
        return new BoxFragment(box, new Size(space.AvailableWidth, height), children, painted);
    }
}
