using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// Places one child box in the block flow of its container: applies the child's margins and asks the child's
/// own algorithm for its fragment. Used by <see cref="FlowLayout"/> for each child and by
/// <see cref="Paginator"/> for the root box, whose container is the page area.
/// </summary>
internal static class InFlowChild
{
    /// <summary>A child placed on the current page.</summary>
    /// <param name="Fragment">The child's fragment, positioned in the container's content area.</param>
    /// <param name="Bottom">Where the next sibling's margin box starts: below this child's bottom margin.</param>
    /// <param name="Continuation">Where the child resumes on the next page, or null when it is complete.</param>
    public sealed record Placement(PlacedFragment Fragment, double Bottom, BreakToken? Continuation);

    /// <summary>
    /// Lays out <paramref name="child"/> with the top of its margin box at <paramref name="top"/>.
    /// </summary>
    /// <remarks>
    /// Margins follow CSS 2.1 §8.3 except that stage 0 does NOT collapse adjoining margins (§8.3.1): the
    /// space between two siblings is the first one's bottom margin plus the second one's top margin. At a page
    /// break margins are sliced like padding: a child that continues from the previous page has no top margin,
    /// and its bottom margin only counts on its last fragment.
    /// </remarks>
    /// <param name="environment">The layout environment.</param>
    /// <param name="child">The child box.</param>
    /// <param name="containerWidth">The width of the container's content area.</param>
    /// <param name="top">Where the child's margin box starts, from the top of the content area.</param>
    /// <param name="containerHeight">The height of the content area available on this page.</param>
    /// <param name="isAtPageStart">Whether nothing that could move to another page precedes the child.</param>
    /// <param name="resume">Where to resume inside the child, or null to start it.</param>
    /// <returns>The placement; None when the child has no room on this page; or an error.</returns>
    public static Result<Maybe<Placement>, Error> Place(
        LayoutEnvironment environment,
        Box child,
        double containerWidth,
        double top,
        double containerHeight,
        bool isAtPageStart,
        BreakToken? resume)
    {
        var margin = child.Style.Margin;
        var marginTop = resume is null ? margin.Top.Points : 0;
        var space = new ConstraintSpace(
            AvailableWidth: containerWidth - margin.Horizontal.Points,
            AvailableHeight: containerHeight - top - marginTop,
            IsAtPageStart: isAtPageStart);

        return environment.Layout(child, space, resume).OnSuccessMap(result => result switch
        {
            LayoutResult.Placed placed => Maybe.From(new Placement(
                new PlacedFragment(new Point(margin.Left.Points, top + marginTop), placed.Fragment),
                Bottom: top + marginTop + placed.Fragment.Size.Height + (placed.Continuation is null ? margin.Bottom.Points : 0),
                placed.Continuation)),
            _ => Maybe<Placement>.None,
        });
    }
}
