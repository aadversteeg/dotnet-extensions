using System;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// Block flow: stacks child boxes from top to bottom in a block formatting context ("boxes are laid out one
/// after the other, vertically, beginning at the top of a containing block", CSS 2.1 §9.4.1), collapses their
/// adjoining margins (§8.3.1, <see cref="MarginCollapsing"/>), and breaks across pages where the next child does
/// not fit.
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
    /// <para>When even the FIRST content of this fragment has no room, nothing of this box fits, and the box reports
    /// that to its own parent instead of leaving an empty fragment behind (<see cref="LayoutResult.NothingFits"/>).</para>
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
        return SlicedBox.Layout(box, space, incoming, (width, height) =>
            StackChildren(environment, box, space, incoming, resume, children, width, height));
    }

    /// <summary>The loop described on <see cref="Layout"/>: stacks the children in a content area of the given
    /// size. None when not even the first content fits.</summary>
    private static Result<Maybe<SlicedBox.Content>, Error> StackChildren(
        LayoutEnvironment environment,
        Box box,
        ConstraintSpace space,
        BreakToken? incoming,
        FlowBreakToken resume,
        ImmutableArray<Box> children,
        double contentWidth,
        double contentHeight)
    {
        var placed = ImmutableArray.CreateBuilder<PlacedFragment>();
        var cursor = 0.0;
        var pending = 0.0;
        var hasContent = false;
        var leadingMarginsTakeNoSpace = LeadingMarginsTakeNoSpace(box, space, incoming, resume, children);
        for (var index = resume.ChildIndex; index < children.Length; index++)
        {
            var child = children[index];
            if (hasContent && ForcedBreaks.Before(child))
            {
                // Margins before the break are truncated (CSS Fragmentation 3 §5.2): `pending` is dropped.
                return SlicedBox.Placed(placed.ToImmutable(), cursor, new FlowBreakToken(index, null));
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

            var outcome = InFlowChild.Place(
                environment, child, contentWidth, top, contentHeight, space.IsAtPageStart && !hasContent, childResume);
            if (outcome.IsFailure)
            {
                return Result<Maybe<SlicedBox.Content>, Error>.Failure(outcome.Error);
            }

            if (outcome.Value.HasNoValue)
            {
                // Break before this child; the margins adjoining an unforced break are truncated (§5.2).
                return hasContent ? SlicedBox.Placed(placed.ToImmutable(), cursor, new FlowBreakToken(index, null)) : SlicedBox.NothingFits;
            }

            var placement = outcome.Value.Value;
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
            if (placement.Continuation is { } continuation)
            {
                return SlicedBox.Placed(placed.ToImmutable(), cursor, new FlowBreakToken(index, continuation));
            }
        }

        // The margins after the last child either leave the box through its bottom edge — they are part of the
        // box's own bottom margin as its parent sees it (MarginCollapsing.Bottom) — or, when padding or a new
        // formatting context separates them from it, they stay inside and add to the box's height.
        var bottomAdjoins = !space.IsNewFormattingContext && MarginCollapsing.BottomAdjoinsChildren(box);
        return SlicedBox.Placed(placed.ToImmutable(), bottomAdjoins ? cursor : cursor + pending, null);
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
    /// <item>Continuing after a FORCED break: "margins after the break are preserved" — they DO take space.</item>
    /// </list>
    /// A child that continues inside itself starts at the top of the page without any margin; that case is
    /// handled where the child is placed.
    /// </summary>
    private static bool LeadingMarginsTakeNoSpace(
        Box box, ConstraintSpace space, BreakToken? incoming, FlowBreakToken resume, ImmutableArray<Box> children) =>
        incoming is null
            ? !space.IsNewFormattingContext && MarginCollapsing.TopAdjoinsChildren(box)
            : resume.ChildToken is null && resume.ChildIndex < children.Length && !ForcedBreaks.Before(children[resume.ChildIndex]);
}
