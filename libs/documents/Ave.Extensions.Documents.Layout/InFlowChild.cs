using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Places one child box in the block flow of its container: applies the child's horizontal margins and asks the
/// child's own algorithm for its fragment. The vertical position (where margins collapse) is decided by the
/// caller: <see cref="FlowLayout"/> for children, <see cref="Paginator"/> for the root box and the page areas.
/// </summary>
internal static class InFlowChild
{
    /// <summary>A child placed on the current page.</summary>
    /// <param name="Fragment">The child's fragment, positioned in the container's content area.</param>
    /// <param name="Continuation">Where the child resumes on the next page, or null when it is complete.</param>
    /// <param name="Appeal">How good the break inside the child is (see <see cref="LayoutResult.Placed"/>).</param>
    /// <param name="BestBreak">For a complete child, the best breakpoint inside it (see <see cref="LayoutResult.Placed.BestBreak"/>).</param>
    public sealed record Placement(PlacedFragment Fragment, BreakToken? Continuation, BreakAppeal Appeal, EarlyBreak? BestBreak = null);

    /// <summary>Lays out <paramref name="child"/> with the top of its BORDER box at <paramref name="top"/>.</summary>
    /// <remarks>
    /// Horizontally the child's border box spans the container's content width minus its left and right margins
    /// (CSS 2.1 §10.3.3, <c>width: auto</c>); horizontal margins never collapse (§8.3.1).
    /// </remarks>
    /// <param name="environment">The layout environment.</param>
    /// <param name="child">The child box.</param>
    /// <param name="containerWidth">The width of the container's content area.</param>
    /// <param name="top">Where the child's border box starts, from the top of the content area.</param>
    /// <param name="containerHeight">The height of the content area available on this page.</param>
    /// <param name="isAtPageStart">Whether the child's border box starts at the very top of the page area
    /// (<see cref="ConstraintSpace.IsAtPageStart"/>).</param>
    /// <param name="resume">Where to resume inside the child, or null to start it.</param>
    /// <param name="isPageRoot">True for the root box and the page areas: the child is the root of a new formatting
    /// context (see <see cref="ConstraintSpace.IsNewFormattingContext"/>).</param>
    /// <param name="followsContent">Whether content comes before the child on this page
    /// (<see cref="ConstraintSpace.FollowsContent"/>).</param>
    /// <param name="breakAt">The early break the child must end at, or null (<see cref="ConstraintSpace.BreakAt"/>).</param>
    /// <param name="isBelowStartInsets">Whether only the top insets of boxes that start the page lie above the child
    /// (<see cref="ConstraintSpace.IsBelowStartInsets"/>).</param>
    /// <returns>The placement; None when the child has no room on this page; or an error.</returns>
    public static Result<Maybe<Placement>, Error> Place(
        LayoutEnvironment environment,
        Box child,
        double containerWidth,
        double top,
        double containerHeight,
        bool isAtPageStart,
        BreakToken? resume,
        bool isPageRoot = false,
        bool followsContent = false,
        EarlyBreak? breakAt = null,
        bool isBelowStartInsets = false)
    {
        var margin = child.Style.Margin;
        var space = new ConstraintSpace(
            AvailableWidth: containerWidth - margin.Horizontal.Points,
            AvailableHeight: containerHeight - top,
            IsAtPageStart: isAtPageStart,
            IsNewFormattingContext: isPageRoot)
        {
            FollowsContent = followsContent,
            BreakAt = breakAt,
            IsBelowStartInsets = isBelowStartInsets,
        };

        return environment.Layout(child, space, resume).OnSuccessMap(result => result switch
        {
            LayoutResult.Placed placed => Maybe.From(new Placement(
                new PlacedFragment(new Point(margin.Left.Points, top), placed.Fragment), placed.Continuation, placed.Appeal, placed.BestBreak)),
            _ => Maybe<Placement>.None,
        });
    }
}
