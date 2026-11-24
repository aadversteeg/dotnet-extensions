using System;
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
    /// <param name="KeepsItsBreak">For a child that breaks, whether it keeps its break (<see cref="LayoutResult.Placed.KeepsItsBreak"/>).</param>
    /// <param name="KeptAppeal">For a child that keeps its break, that break's appeal for the comparison (<see cref="LayoutResult.Placed.KeptAppeal"/>).</param>
    /// <param name="EndsBelowContent">For a child that breaks, whether its fragment ends right below its content (<see cref="LayoutResult.Placed.EndsBelowContent"/>).</param>
    public sealed record Placement(
        PlacedFragment Fragment, BreakToken? Continuation, BreakAppeal Appeal, EarlyBreak? BestBreak = null, bool KeepsItsBreak = false, BreakAppeal KeptAppeal = BreakAppeal.Perfect,
        bool EndsBelowContent = false);

    /// <summary>Lays out <paramref name="child"/> with the top of its BORDER box at <paramref name="top"/>.</summary>
    /// <remarks>
    /// Horizontally the child's border box spans the container's content width minus its left and right margins
    /// (CSS 2.1 §10.3.3, <c>width: auto</c>), or the width its sizes give (<see cref="Horizontal"/>); horizontal margins
    /// never collapse (§8.3.1).
    /// </remarks>
    /// <param name="environment">The layout environment.</param>
    /// <param name="child">The child box.</param>
    /// <param name="containerWidth">The width of the container's content area.</param>
    /// <param name="top">Where the child's border box starts, from the top of the content area.</param>
    /// <param name="containerHeight">The height of the content area available on this page.</param>
    /// <param name="fragmentainerBlockSize">The height of the page area the child is laid out in, less the repeated header
    /// rows around it (<see cref="ConstraintSpace.FragmentainerBlockSize"/>).</param>
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
        double fragmentainerBlockSize,
        bool isAtPageStart,
        BreakToken? resume,
        bool isPageRoot = false,
        bool followsContent = false,
        EarlyBreak? breakAt = null,
        bool isBelowStartInsets = false)
    {
        var (width, left) = Horizontal(child.Style, containerWidth, environment.Profile.LengthRounding);
        var space = new ConstraintSpace(
            AvailableWidth: width,
            AvailableHeight: containerHeight - top,
            IsAtPageStart: isAtPageStart,
            IsNewFormattingContext: isPageRoot)
        {
            FragmentainerBlockSize = fragmentainerBlockSize,
            FollowsContent = followsContent,
            BreakAt = breakAt,
            IsBelowStartInsets = isBelowStartInsets,
        };

        return environment.Layout(child, space, resume).OnSuccessMap(result => result switch
        {
            LayoutResult.Placed placed => Maybe.From(new Placement(
                new PlacedFragment(new Point(left, top), placed.Fragment), placed.Continuation, placed.Appeal, placed.BestBreak, placed.KeepsItsBreak, placed.KeptAppeal,
                placed.EndsBelowContent)),
            _ => Maybe<Placement>.None,
        });
    }

    /// <summary>
    /// The width of a child's border box in block flow and where it starts, from the left of the container's content
    /// area: CSS 2.1 §10.3.3 and §10.4, in horizontal left-to-right writing.
    /// <list type="bullet">
    /// <item>Width <c>auto</c>: the container's width less the margins.</item>
    /// <item>A length, or a percentage of the container's content width (§10.2), for the box that
    /// <see cref="BoxStyle.BoxSizing"/> names: the content box gets padding and border added, the border box is never
    /// narrower than they are (CSS Box Model 3 §4.3). Then <c>max-width</c> limits it and <c>min-width</c> wins over both
    /// (§10.4: "If the resulting width is greater than 'max-width' … If the resulting width is smaller than 'min-width'").
    /// The same for a width <c>auto</c> that a minimum or maximum changes.</item>
    /// <item>The width left over goes to the auto margins (<see cref="BoxStyle.AutoMargins"/>): all of it to an auto left
    /// margin, half to each of two. A box as wide as the container or wider leaves nothing: its auto margins are 0, and an
    /// over-constrained right margin is ignored, so the box starts at its left margin and may reach past the container's
    /// right edge, as it shows, and the next box starts below it as usual (case <c>sz-width-overflow</c>).</item>
    /// </list>
    /// Under <see cref="LengthRounding.SixtyFourthPixels"/> a percentage and the half of the width left over are floored to
    /// 1/64 px, as Chromium puts them on its layout unit (cases <c>sz-width-percent</c>, <c>sz-width-percent-parent</c>,
    /// <c>sz-width-fraction</c>, <c>sz-min-max</c>, <c>sz-margin-auto</c>).
    /// </summary>
    /// <param name="style">The child's style.</param>
    /// <param name="containerWidth">The width of the container's content area.</param>
    /// <param name="rounding">The profile's length rounding.</param>
    /// <returns>The border box's width and the left edge of the border box.</returns>
    /// <param name="auto">The border box's width at width <c>auto</c> where it is not the container's width less the margins:
    /// an inline-block's shrink-to-fit width (CSS 2.1 §10.3.9), which <c>max-width</c> and <c>min-width</c> then limit as
    /// any width; null in block flow.</param>
    internal static (double Width, double Left) Horizontal(BoxStyle style, double containerWidth, LengthRounding rounding, double? auto = null)
    {
        var margin = style.Margin;
        var available = auto ?? containerWidth - margin.Horizontal.Points;
        if (style.SpecifiedWidth is null && style.MinWidth is null && style.MaxWidth is null)
        {
            return (available, margin.Left.Points);
        }

        var insets = SlicedBox.HorizontalInsets(style);
        double BorderBox(SizeValue size)
        {
            var value = size switch
            {
                SizeValue.Fixed fixedWidth => fixedWidth.Width.Points,
                SizeValue.Percentage percentage => Floored(containerWidth * percentage.Percent / 100, rounding),
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, "a width is a length or a percentage"),
            };
            return style.BoxSizing == BoxSizing.BorderBox ? Math.Max(value, insets) : value + insets;
        }

        var width = style.SpecifiedWidth is { } specified ? BorderBox(specified) : available;
        if (style.MaxWidth is { } max)
        {
            width = Math.Min(width, BorderBox(max));
        }

        if (style.MinWidth is { } min)
        {
            width = Math.Max(width, BorderBox(min));
        }

        var free = available - width;
        var left = margin.Left.Points;
        if (free > 0)
        {
            left += style.AutoMargins switch
            {
                AutoMargins.Left => free,
                AutoMargins.Both => Floored(free / 2, rounding),
                _ => 0,
            };
        }

        return (width, left);
    }

    /// <summary>A length floored to Chromium's layout unit of 1/64 CSS px under <see cref="LengthRounding.SixtyFourthPixels"/>;
    /// as it is under <see cref="LengthRounding.Exact"/>.</summary>
    private static double Floored(double points, LengthRounding rounding)
    {
        const double unit = 0.75 / 64;
        // A length that is a whole number of units up to the error of the product stays that number.
        return rounding == LengthRounding.SixtyFourthPixels ? Math.Floor((points / unit) + 1e-9) * unit : points;
    }
}
