using System;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// Block flow: stacks child boxes from top to bottom in a block formatting context ("boxes are laid out one
/// after the other, vertically, beginning at the top of a containing block", CSS 2.1 §9.4.1), breaking across
/// pages where the next child does not fit.
/// </summary>
public static class FlowLayout
{
    /// <summary>The Flow algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <remarks>
    /// Children are placed in order. The content stops on this page, and a <see cref="FlowBreakToken"/> says
    /// where to resume, in three cases (the class-A and nested breakpoints of CSS Fragmentation 3 §4.1):
    /// <list type="number">
    /// <item>a page break is forced before the next child (<see cref="ForcedBreaks"/>);</item>
    /// <item>the next child has no room left on this page: it moves whole to the next page;</item>
    /// <item>a child broke inside itself: the rest of it continues on the next page.</item>
    /// </list>
    /// When even the FIRST child of this fragment has no room, nothing of this box fits, and the box reports
    /// that to its own parent instead of leaving an empty fragment behind (<see cref="LayoutResult.NothingFits"/>).
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

        var frame = BoxFrame.For(box, space, incoming);
        if (!frame.PaddingFits(space))
        {
            return LayoutResult.NothingFits(box, space);
        }

        var children = box.Children.Cast<Box>().ToImmutableArray();
        var placed = ImmutableArray.CreateBuilder<PlacedFragment>();
        var bottom = 0.0;
        for (var index = resume.ChildIndex; index < children.Length; index++)
        {
            var child = children[index];
            var isFirstOnPage = placed.Count == 0;
            if (!isFirstOnPage && ForcedBreaks.Before(child))
            {
                return LayoutResult.Place(frame.Wrap(box, placed, bottom, isLastFragment: false), new FlowBreakToken(index, null));
            }

            var childResume = index == resume.ChildIndex ? resume.ChildToken : null;
            var outcome = InFlowChild.Place(
                environment, child, frame.ContentWidth, bottom, frame.ContentHeight, space.IsAtPageStart && isFirstOnPage, childResume);
            if (outcome.IsFailure)
            {
                return Result<LayoutResult, Error>.Failure(outcome.Error);
            }

            if (outcome.Value.HasNoValue)
            {
                return isFirstOnPage
                    ? LayoutResult.NothingFits(box, space)
                    : LayoutResult.Place(frame.Wrap(box, placed, bottom, isLastFragment: false), new FlowBreakToken(index, null));
            }

            var placement = outcome.Value.Value;
            placed.Add(placement.Fragment);
            bottom = placement.Bottom;
            if (placement.Continuation is { } continuation)
            {
                return LayoutResult.Place(frame.Wrap(box, placed, bottom, isLastFragment: false), new FlowBreakToken(index, continuation));
            }
        }

        return LayoutResult.Place(frame.Wrap(box, placed, bottom, isLastFragment: true), null);
    }
}
