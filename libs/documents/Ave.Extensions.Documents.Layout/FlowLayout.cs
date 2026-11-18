using System;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Block flow: stacks child boxes from top to bottom in a block formatting context ("boxes are laid out one
/// after the other, vertically, beginning at the top of a containing block", CSS 2.1 §9.4.1), collapses their
/// adjoining margins (§8.3.1, <see cref="MarginCollapsing"/>), and breaks across pages where the next child does
/// not fit, preferring breaks that no keep rule is against.
/// </summary>
public static class FlowLayout
{
    /// <summary>The Flow algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <remarks>
    /// <para>Children are placed in order. The vertical distance between two children is ONE collapsed margin:
    /// "Vertical margins between adjacent block-level boxes in a block formatting context collapse" (§9.4.1). The
    /// loop keeps the bottom of the content placed so far (<c>cursor</c>) and the margins waiting below it
    /// (<c>pending</c>); the next child goes at <c>cursor + Collapse(pending, its top margin)</c>.</para>
    /// <para>The content stops on this page, and a <see cref="FlowBreakToken"/> says where to resume, in three
    /// cases (the class-A and nested breakpoints of CSS Fragmentation 3 §4.1):</para>
    /// <list type="number">
    /// <item>a page break is forced before the next child (<see cref="ForcedBreaks"/>);</item>
    /// <item>the next child has no room left on this page: it moves whole to the next page;</item>
    /// <item>a child broke inside itself: the rest of it continues on the next page.</item>
    /// </list>
    /// <para>Keep rules (§3.1 <c>break-before</c>/<c>break-after: avoid</c>, §3.2 <c>break-inside: avoid</c>) give
    /// every breakpoint an appeal (<see cref="BreakAvoidance"/>). Every class-A breakpoint between two children on
    /// this page is remembered as a candidate, and so is the best breakpoint inside every child placed whole (its
    /// early break, <see cref="LayoutResult.Placed.BestBreak"/>), in page order between the breakpoints before and after
    /// that child. A class-A candidate counts — §4.1: "Between sibling boxes", empty boxes included — when a break
    /// there makes progress, which is when the child after it does not start the page
    /// (<see cref="ConstraintSpace.IsAtPageStart"/>): content, empty boxes with their margins, or this box's own top
    /// padding lie above it. When the natural break of cases 2 and 3 has a LOWER appeal than the
    /// best candidate, the page breaks at that candidate instead (the latest one of the highest appeal, the one
    /// that fits the most content, as Chromium chooses; CSS Fragmentation 3 §4.5 leaves the choice to the user agent). The children before a candidate are already laid
    /// out completely, so breaking before a child just keeps a prefix of the fragments; breaking inside one lays that one
    /// child out again, where it was, with the early break in its constraint space (<see cref="ConstraintSpace.BreakAt"/>),
    /// and it breaks exactly there. So a keep rule moves a break into an earlier sibling's content when every break
    /// between the boxes at the page end is worse, as Chromium does with its early breaks. The bound on the work in block
    /// flow: a box given an early break follows it without looking for another, so on a page each box in a flow box is
    /// laid out at most twice for the choice of a break, however deep the early break lies (the second time only on the
    /// way to it). A table cell is laid out more often: each table around it measures it at unlimited height, and its own
    /// table lays it out in a cut and once more for what is left of it, so a box inside k tables is laid out at most
    /// 2k + 1 times on a page (TableNestingCostTests). When no
    /// better candidate exists, the natural break is taken even though it violates a keep rule — so content that is
    /// kept together but taller than a page still breaks, and pagination always ends.</para>
    /// <para>When even the FIRST child of this fragment has no room, there is no breakpoint before it: it is not after
    /// a sibling, and nothing but margins separates it from this box's top edge (a class C breakpoint needs a gap there,
    /// §4.1). The box then breaks before it as a last resort (<see cref="BreakAppeal.LastResort"/>), with the empty
    /// boxes before it on this page; its parent takes any better breakpoint instead, which moves the whole box. A child
    /// that has no room is never at the start of the page (it would have failed instead), so this break always makes
    /// progress.</para>
    /// </remarks>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <param name="box">A box whose children are all boxes.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">A <see cref="FlowBreakToken"/>, or null to start.</param>
    /// <returns>The fragment and continuation, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        if (box.Children.Any(child => child is not Box))
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidContent(box, "flow layout needs box children; text belongs in an inline box."));
        }

        var resume = incoming switch
        {
            null => new FlowBreakToken(0, null),
            FlowBreakToken token => token,
            _ => throw new ArgumentException($"Flow received a {incoming.GetType().Name} it did not produce.", nameof(incoming)),
        };

        var children = box.Children.Cast<Box>().ToImmutableArray();
        return SlicedBox.Layout(box, space, incoming, resume, (width, height, breakAt) =>
            StackChildren(environment, box, space, incoming, resume, children, width, height, breakAt switch
            {
                null => null,
                FlowEarlyBreak flow => flow,
                _ => throw new ArgumentException($"Flow received a {breakAt.GetType().Name} it did not report.", nameof(space)),
            }));
    }

    /// <summary>
    /// The intrinsic widths of a flow box (CSS Sizing 3 §5.1, contributions §5.2): the widest child decides, each child counted with its
    /// horizontal margins; then the box's own border and padding are added.
    /// </summary>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <param name="box">A box whose children are all boxes.</param>
    /// <returns>The widths, or the first error of a child.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        box.Children.OfType<Box>()
            .TraverseAll(child => environment.Measure(child).OnSuccessMap(widths => widths.Plus(child.Style.Margin.Horizontal.Points)))
            .OnSuccessMap(widths => new IntrinsicWidths(
                    widths.Select(width => width.MinContent).DefaultIfEmpty(0).Max(),
                    widths.Select(width => width.MaxContent).DefaultIfEmpty(0).Max())
                .Plus(SlicedBox.HorizontalInsets(box.Style)));

    /// <summary>
    /// A breakpoint on this page: before child <see cref="Index"/> (class A), or inside it at the child's own early break
    /// (<see cref="Inside"/>), which takes laying the child out once more.
    /// </summary>
    /// <param name="Index">The child that would start the next page, or that breaks.</param>
    /// <param name="PlacedCount">How many fragments come before that child.</param>
    /// <param name="Cursor">The bottom of the content before that child.</param>
    /// <param name="Appeal">The breakpoint's appeal.</param>
    /// <param name="Inside">For a break inside the child: how the child was placed and its early break.</param>
    private sealed record Breakpoint(int Index, int PlacedCount, double Cursor, BreakAppeal Appeal, ChildBreak? Inside = null)
    {
        /// <summary>The breakpoint as this box reports it to its parent.</summary>
        public FlowEarlyBreak ToEarlyBreak() => new(Index, Inside?.Break, Appeal);
    }

    /// <summary>A child placed whole on this page, with what laying it out again needs to break inside it.</summary>
    /// <param name="Top">Where its border box starts, from the top of the content area.</param>
    /// <param name="Resume">Where it resumed on this page, or null.</param>
    /// <param name="IsAtPageStart">Whether it starts the page.</param>
    /// <param name="FollowsContent">Whether content comes before it on this page.</param>
    /// <param name="Break">Its early break.</param>
    private sealed record ChildBreak(double Top, BreakToken? Resume, bool IsAtPageStart, bool FollowsContent, EarlyBreak Break);

    /// <summary>The children stacked on this page, and the margin after the last of them that stays inside the box
    /// (0 when the content breaks, or when that margin leaves the box through its bottom edge).</summary>
    /// <param name="Content">The placed content; its height includes <paramref name="TrailingMargin"/>.</param>
    /// <param name="TrailingMargin">The part of the height that is the last child's bottom margin.</param>
    private sealed record Stacked(SlicedBox.Content Content, double TrailingMargin);

    /// <summary>
    /// Stacks the children in a content area of the given size (the loop described on <see cref="Layout"/>), and
    /// makes sure the result fits it. The margin after the last child is the one thing the loop places without a
    /// fit test. When it stays inside the box (§8.3.1: padding, a border or a new formatting context keeps it from
    /// collapsing through the bottom), it adds to the box's height, and what happens when it passes the end of the
    /// content area depends on what follows it inside the box:
    /// <list type="bullet">
    /// <item>Bottom padding or a border: they must end on this page with the box, and there is no breakpoint between
    /// the child and its margin (CSS Fragmentation 3 §4.1: a class C break needs a gap between a child's margin edge and
    /// its container's content edge). So the break must come earlier: the children are laid out once more, breaking at
    /// the best breakpoint among them (an early break), as <see cref="SlicedBox"/> does for the bottom padding. When the
    /// children have no breakpoint, the box does not fit here and moves on — except at the start of an empty page, where
    /// moving on gains nothing: there the margin is truncated where the bottom padding and border still fit above the
    /// page end.</item>
    /// <item>Nothing: no bottom padding and no border. Then the margin is the last thing of the box, so where it passes
    /// the end of the content area it adjoins the break there, and it is truncated as §5.2 truncates the margins
    /// adjoining an unforced break ("any margins adjoining the break are truncated to zero"). It never moves content to
    /// another page. One rule for every box whose last margin stays inside it: the root of the page body, the root of a
    /// page area, and a table cell (a new formatting context), whose cut at a page break inside its row is such a
    /// break.</item>
    /// </list>
    /// None when not even the first content fits.
    /// </summary>
    private static Result<Maybe<SlicedBox.Content>, Error> StackChildren(
        LayoutEnvironment environment,
        Box box,
        ConstraintSpace space,
        BreakToken? incoming,
        FlowBreakToken resume,
        ImmutableArray<Box> children,
        double contentWidth,
        double contentHeight,
        FlowEarlyBreak? breakAt)
    {
        var first = Stack(environment, box, space, incoming, resume, children, contentWidth, contentHeight, breakAt);
        if (first.IsFailure || first.Value.HasNoValue || Fitting.Fits(first.Value.Value.Content.Height, contentHeight))
        {
            return first.OnSuccessMap(stacked => stacked.OnSomeMap(result => result.Content));
        }

        var complete = first.Value.Value;
        if (complete.Content.Continuation is not null)
        {
            // Content that breaks and is still taller than the content area: a child placed at the start of the page
            // overflowed it (the limit of SlicedBox), and something of this box is left for the next page. Cutting the
            // result to the area would drop that rest, and leaving the overflow on this page is not built: refused.
            return Result<Maybe<SlicedBox.Content>, Error>.Failure(Overflowing(box, complete.Content, contentHeight));
        }

        if (!SlicedBox.HasBottomInset(box.Style))
        {
            return Truncated(complete, contentHeight);
        }

        if (complete.Content.BestBreak is FlowEarlyBreak earlier)
        {
            return Stack(environment, box, space, incoming, resume, children, contentWidth, contentHeight, earlier)
                .OnSuccessMap(stacked => stacked.OnSomeMap(result => result.Content));
        }

        // At the start of an empty page the margin is cut off where the box's bottom padding and border still fit.
        return space.IsAtPageStart ? Truncated(complete, contentHeight - SlicedBox.BottomInset(box.Style)) : SlicedBox.NothingFits;
    }

    /// <summary>The refusal for content that breaks below the end of its content area: names the last child that
    /// reaches past the end, with the room it had, or the box itself when no child does.</summary>
    private static Error Overflowing(Box box, SlicedBox.Content content, double contentHeight)
    {
        var child = content.Children.LastOrDefault(placed =>
            placed.Fragment is BoxFragment && !Fitting.Fits(placed.Offset.Y + placed.Fragment.Size.Height, contentHeight));
        return child is { Fragment: BoxFragment fragment }
            ? LayoutErrors.DoesNotFit(fragment.Box, contentHeight - child.Offset.Y)
            : LayoutErrors.DoesNotFit(box, contentHeight);
    }

    /// <summary>The complete content with its trailing margin cut off at the end of the content area (§5.2).</summary>
    private static Result<Maybe<SlicedBox.Content>, Error> Truncated(Stacked complete, double contentHeight) =>
        SlicedBox.Placed(complete.Content.Children, Math.Max(complete.Content.Height - complete.TrailingMargin, contentHeight), null, bestBreak: complete.Content.BestBreak);

    /// <summary>The loop described on <see cref="Layout"/>: stacks the children in a content area of the given
    /// size, or up to the early break when one is given. None when not even the first content fits.</summary>
    private static Result<Maybe<Stacked>, Error> Stack(
        LayoutEnvironment environment,
        Box box,
        ConstraintSpace space,
        BreakToken? incoming,
        FlowBreakToken resume,
        ImmutableArray<Box> children,
        double contentWidth,
        double contentHeight,
        FlowEarlyBreak? breakAt)
    {
        var placed = ImmutableArray.CreateBuilder<PlacedFragment>();
        var cursor = 0.0;
        var pending = 0.0;
        var hasContent = false;
        Breakpoint? best = null;
        var leadingMarginsTakeNoSpace = LeadingMarginsTakeNoSpace(box, space, incoming, resume, children);
        var contentIsAtPageStart = SlicedBox.ContentIsAtPageStart(box.Style, space, incoming);
        for (var index = resume.ChildIndex; index < children.Length; index++)
        {
            var child = children[index];
            if (index > resume.ChildIndex && (hasContent || space.FollowsContent) && ForcedBreaks.Before(child))
            {
                // A forced break between two children is taken when there is content on the page before it — in this
                // box or before the box — even if what this box placed so far is empty (see ForcedBreaks). Margins
                // before the break are truncated (CSS Fragmentation 3 §5.2): `pending` is dropped.
                return Broken(SlicedBox.Placed(placed.ToImmutable(), cursor, new FlowBreakToken(index, null) { IsForced = true }));
            }

            if (breakAt is { Inside: null } before && before.ChildIndex == index)
            {
                // The early break before this child: the page ends here, as it would have at this class-A breakpoint.
                return Broken(SlicedBox.Placed(placed.ToImmutable(), cursor, new FlowBreakToken(index, null), before.Appeal));
            }

            var childResume = index == resume.ChildIndex ? resume.ChildToken : null;
            var isEmpty = MarginCollapsing.IsEmpty(child);
            var top = cursor + Gap(child, pending, isEmpty, leadingMarginsTakeNoSpace || childResume is not null);
            if (isEmpty)
            {
                // A box whose margins collapse through it has no height and needs no room (§8.3.1, last bullet:
                // its top border edge sits where it would be "if the element had a non-zero bottom border").
                // Where its margins alone would pass the page end it is put at the end; it is invisible either way.
                top = Math.Min(top, contentHeight);
            }

            var childIsAtPageStart = contentIsAtPageStart && !(top > 0);
            if (index > resume.ChildIndex && !childIsAtPageStart)
            {
                // Remember this class-A breakpoint; a later tie wins because it fits more content.
                best = Better(best, new Breakpoint(index, placed.Count, cursor, BreakAvoidance.Between(children[index - 1], child)));
            }

            var followsContent = space.FollowsContent || hasContent;
            var inside = breakAt is { Inside: { } target } && breakAt.ChildIndex == index ? target : null;
            var outcome = InFlowChild.Place(
                environment, child, contentWidth, top, contentHeight, childIsAtPageStart, childResume, followsContent: followsContent, breakAt: inside);
            if (outcome.IsFailure)
            {
                return Result<Maybe<Stacked>, Error>.Failure(outcome.Error);
            }

            if (outcome.Value.HasNoValue)
            {
                // The natural break is before this child — the candidate just recorded — unless an earlier one is
                // better. The margins adjoining an unforced break are truncated (§5.2). Before the first child there is
                // no breakpoint, only the last resort (see Layout).
                return Broken(best is null
                    ? SlicedBox.Placed(placed.ToImmutable(), cursor, new FlowBreakToken(index, childResume), BreakAppeal.LastResort)
                    : BreakAt(environment, children, contentWidth, contentHeight, placed, best));
            }

            var placement = outcome.Value.Value;
            if (placement.Continuation is { } continuation)
            {
                // Broken at the early break this box was asked for, or at the child's own best break: unless an earlier
                // breakpoint of this box is better.
                var appeal = inside is not null ? breakAt!.Appeal : BreakAvoidance.Inside(child, placement.Appeal);
                if (inside is null && best is not null && best.Appeal > appeal)
                {
                    return Broken(BreakAt(environment, children, contentWidth, contentHeight, placed, best));
                }

                placed.Add(placement.Fragment);
                return Broken(SlicedBox.Placed(placed.ToImmutable(), top + placement.Fragment.Fragment.Size.Height, new FlowBreakToken(index, continuation), appeal));
            }

            if (inside is not null)
            {
                return Result<Maybe<Stacked>, Error>.Failure(LayoutErrors.EngineDefect(child, "did not break at the early break it reported"));
            }

            if (placement.BestBreak is { } early)
            {
                // A breakpoint inside the child, which came after the one before it and before the one after it.
                best = Better(best, new Breakpoint(
                    index, placed.Count, cursor, BreakAvoidance.Inside(child, early.Appeal), new ChildBreak(top, childResume, childIsAtPageStart, followsContent, early)));
            }

            placed.Add(placement.Fragment);
            if (isEmpty)
            {
                // Its margins join the ones waiting for the next content, unless they are absorbed or truncated.
                pending = leadingMarginsTakeNoSpace ? 0 : MarginCollapsing.Collapse(pending, MarginCollapsing.All(child));
                continue;
            }

            cursor = top + placement.Fragment.Fragment.Size.Height;
            pending = MarginCollapsing.Bottom(child);
            hasContent = true;
            leadingMarginsTakeNoSpace = false;
        }

        // The margins after the last child either leave the box through its bottom edge — they are part of the
        // box's own bottom margin as its parent sees it (MarginCollapsing.Bottom) — or, when padding, a border or a
        // new formatting context separates them from it, they stay inside and add to the box's height.
        var bottomAdjoins = !space.IsNewFormattingContext && MarginCollapsing.BottomAdjoinsChildren(box);
        var trailing = bottomAdjoins ? 0 : pending;
        return SlicedBox.Placed(placed.ToImmutable(), cursor + trailing, null, bestBreak: best?.ToEarlyBreak())
            .OnSuccessMap(content => content.OnSomeMap(complete => new Stacked(complete, trailing)));
    }

    /// <summary>The better of two breakpoints in page order: the higher appeal, and of equal ones the later
    /// (<paramref name="later"/>), which fits more content (CSS Fragmentation 3 §4.5 leaves the choice to the user agent;
    /// Chromium chooses so).</summary>
    private static Breakpoint Better(Breakpoint? earlier, Breakpoint later) =>
        earlier is null || later.Appeal >= earlier.Appeal ? later : earlier;

    /// <summary>Content that ends on this page at a break: no margin trails it (§5.2 truncates the margins there).</summary>
    private static Result<Maybe<Stacked>, Error> Broken(Result<Maybe<SlicedBox.Content>, Error> content) =>
        content.OnSuccessMap(placed => placed.OnSomeMap(result => new Stacked(result, 0)));

    /// <summary>
    /// Ends the page at a remembered breakpoint. Before a child: the fragments before it stay, the rest moves on. Inside a
    /// child: the fragments before the child stay, and the child is laid out once more, where it was, breaking at its
    /// early break; it must break there, so the result always has a continuation. Pure: the second call of the child's
    /// layout sees exactly the arguments of the first, plus the early break.
    /// </summary>
    private static Result<Maybe<SlicedBox.Content>, Error> BreakAt(
        LayoutEnvironment environment, ImmutableArray<Box> children, double contentWidth, double contentHeight, ImmutableArray<PlacedFragment>.Builder placed, Breakpoint breakpoint)
    {
        ValueList<PlacedFragment> before = [.. placed.Take(breakpoint.PlacedCount)];
        if (breakpoint.Inside is not { } inside)
        {
            return SlicedBox.Placed(before, breakpoint.Cursor, new FlowBreakToken(breakpoint.Index, null), breakpoint.Appeal);
        }

        var child = children[breakpoint.Index];
        return InFlowChild.Place(environment, child, contentWidth, inside.Top, contentHeight, inside.IsAtPageStart, inside.Resume, followsContent: inside.FollowsContent, breakAt: inside.Break)
            .OnSuccessBind(outcome => outcome is { HasValue: true, Value.Continuation: { } continuation }
                ? SlicedBox.Placed([.. before, outcome.Value.Fragment], inside.Top + outcome.Value.Fragment.Fragment.Size.Height, new FlowBreakToken(breakpoint.Index, continuation), breakpoint.Appeal)
                : Result<Maybe<SlicedBox.Content>, Error>.Failure(LayoutErrors.EngineDefect(child, "did not break at the early break it reported")));
    }

    /// <summary>
    /// The space above a child: the collapsed margin between the content above (<paramref name="pending"/>) and the
    /// child's top margin (which already includes the margins collapsing through from inside it), or zero when
    /// those margins take no space here.
    /// </summary>
    private static double Gap(Box child, double pending, bool isEmpty, bool marginsTakeNoSpace) =>
        marginsTakeNoSpace ? 0 : MarginCollapsing.Collapse(pending, isEmpty ? MarginCollapsing.All(child) : MarginCollapsing.Top(child));

    /// <summary>
    /// Whether the margins at the start of this fragment's content (up to the first non-empty child) take no space
    /// inside the box. Three cases:
    /// <list type="bullet">
    /// <item>First fragment, top margin adjoining the first child (§8.3.1): those margins collapsed with the box's
    /// own top margin, and the parent already put the box below the collapsed margin
    /// (<see cref="MarginCollapsing.Top"/>). Not for a new formatting context such as the root.</item>
    /// <item>Continuing after an UNFORCED break between two children: "any margins adjoining the break are
    /// truncated to zero" (CSS Fragmentation 3 §5.2).</item>
    /// <item>Continuing after a FORCED break: "When a forced break occurs there, adjoining margins before the break
    /// are truncated, but margins after the break are preserved." (§5.2) — they DO take space, on the page right after
    /// the forced break only: the token says whether the break before the child was forced
    /// (<see cref="FlowBreakToken.IsForced"/>), and when that page ends before the same child again, the break is an
    /// unforced one and the margin is truncated on the next page.</item>
    /// </list>
    /// A child that continues inside itself starts at the top of the page without any margin; that case is
    /// handled where the child is placed.
    /// </summary>
    private static bool LeadingMarginsTakeNoSpace(
        Box box, ConstraintSpace space, BreakToken? incoming, FlowBreakToken resume, ImmutableArray<Box> children) =>
        incoming is null
            ? !space.IsNewFormattingContext && MarginCollapsing.TopAdjoinsChildren(box)
            : resume.ChildToken is null && resume.ChildIndex < children.Length && !resume.IsForced;
}
