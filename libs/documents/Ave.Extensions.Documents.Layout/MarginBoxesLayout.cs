using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The MarginBoxes algorithm: the page-margin boxes of one page edge side by side (CSS Paged Media 3 §5, the top and
/// bottom rows), the content of a header or footer area with a fixed height (<see cref="ModelCheck"/> allows it nowhere
/// else). Each child is one generated box at its place (<see cref="BoxStyle.MarginBoxPlace"/>); its width comes from the
/// intrinsic widths of all of them (§5.3.2, <see cref="Widths"/>), its height is the row's height less its own margins
/// (§5.3), and its content sits in that height as <see cref="BoxStyle.VerticalAlign"/> says. The row never breaks.
/// </summary>
/// <remarks>
/// Content wider or taller than its box is refused (<see cref="LayoutErrorCodes.MarginBoxOverflow"/>), also when not
/// even the boxes' min-content widths fit the row. Chromium 159 does not refuse it: it lets the content overflow its box
/// unclipped, over the neighbouring box, the body or past the sheet (a documented limit, docs/architecture.md).
/// </remarks>
public static class MarginBoxesLayout
{
    /// <summary>The MarginBoxes algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <param name="environment">The fonts, algorithms and current page (the boxes may show page counters).</param>
    /// <param name="box">A box whose children are boxes, at most one at each place of the row.</param>
    /// <param name="space">The space of the area: its width, and its fixed height below the row's top margin.</param>
    /// <param name="incoming">Null: a row never breaks, so it never returns a token.</param>
    /// <returns>The fragment, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        if (incoming is not null)
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidBreakToken(box, incoming, "a row of page-margin boxes never breaks"));
        }

        var style = box.Style;
        var rowHeight = space.AvailableHeight - style.Margin.Bottom.Points;
        if (!double.IsFinite(rowHeight))
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidContent(box, "a row of page-margin boxes is laid out only as the content of a header or footer area with a fixed height."));
        }

        var left = style.Padding.Left.Points + style.Border.Widths.Left.Points;
        var top = style.Padding.Top.Points + style.Border.Widths.Top.Points;
        var width = space.AvailableWidth - SlicedBox.HorizontalInsets(style);
        var height = rowHeight - top - style.Padding.Bottom.Points - style.Border.Widths.Bottom.Points;
        if (!Fitting.Fits(0, width) || !Fitting.Fits(0, height))
        {
            return Result<LayoutResult, Error>.Failure(!Fitting.Fits(0, width)
                ? LayoutErrors.MarginBoxOverflow(box, "width", SlicedBox.HorizontalInsets(style), space.AvailableWidth)
                : LayoutErrors.MarginBoxOverflow(box, "height", rowHeight - height, rowHeight));
        }

        var start = Child(box, MarginBoxPlace.Start);
        var center = Child(box, MarginBoxPlace.Center);
        var end = Child(box, MarginBoxPlace.End);
        if (start.IsFailure || center.IsFailure || end.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(start.IsFailure ? start.Error : center.IsFailure ? center.Error : end.Error);
        }

        return Outer(environment, start.Value).OnSuccessBind(startWidths =>
            Outer(environment, center.Value).OnSuccessBind(centerWidths =>
                Outer(environment, end.Value).OnSuccessBind(endWidths =>
                {
                    var (startWidth, centerWidth, endWidth) = Widths(startWidths, centerWidths, endWidths, width);
                    var placed = new List<PlacedFragment>(3);
                    foreach (var (child, widths, x, outer) in new[]
                             {
                                 (start.Value, startWidths, 0.0, startWidth),
                                 (center.Value, centerWidths, (width - centerWidth) / 2, centerWidth),
                                 (end.Value, endWidths, width - endWidth, endWidth),
                             })
                    {
                        if (child is null || widths is null)
                        {
                            continue;
                        }

                        var fragment = LayOutBox(environment, child, widths, outer, height);
                        if (fragment.IsFailure)
                        {
                            return Result<LayoutResult, Error>.Failure(fragment.Error);
                        }

                        placed.Add(new PlacedFragment(new Point(left + x + child.Style.Margin.Left.Points, top + child.Style.Margin.Top.Points), fragment.Value));
                    }

                    return LayoutResult.Place(new BoxFragment(box, new Size(space.AvailableWidth, rowHeight), [.. placed], style.Border), null);
                })));
    }

    /// <summary>A row is never sized from its content: it is only the content of a page area, which is as wide as the
    /// page area. Measuring one is <c>Documents.Layout.InvalidContent</c>.</summary>
    /// <param name="environment">Not used.</param>
    /// <param name="box">The row.</param>
    /// <returns>The error.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        Result<IntrinsicWidths, Error>.Failure(LayoutErrors.InvalidContent(box, "a row of page-margin boxes is the content of a header or footer area and has no intrinsic widths."));

    /// <summary>
    /// The outer widths (margins included) of the start, centre and end boxes in a row whose content box is
    /// <paramref name="available"/> wide: CSS Paged Media 3 §5.3.2, every box with <c>width: auto</c>, from the boxes'
    /// outer min-content and max-content widths; null for a box that is not generated. A box alone in the row takes the
    /// whole row. Without a centre box the start and end boxes share the row (<see cref="Distribute"/>). With one, the
    /// centre box shares the row with an imaginary box AC whose widths are twice the larger of the two side boxes'
    /// (an absent side box counts as zero wide), is centred, and each side box gets half of what is left, whether the other
    /// one is generated or not.
    /// </summary>
    /// <remarks>
    /// Measured in Chromium 159 on a 400 × 300 pt page with <c>margin: 60pt 50pt</c>, Lato 10 pt, at the edges of its
    /// painted rectangles (whole pixels), the row laid out from 49.992 to 351.0 pt: "Aaaa" beside three words of
    /// sixteen letters gets 21.75 pt; "Text" beside an empty box with 10 pt of padding on each side 144.0 pt; a centre box
    /// "TC" (20 pt bold) after "TL" with no end box 107.25 pt, with "TR" 99.75 pt; a two-line centre box between "L" and
    /// "Right side text that is longer" 147.75 pt; two empty boxes without padding half the row each; an empty centre box
    /// beside an empty start box a third of it. On A4 with <c>margin: 4cm 1cm 2cm</c>, 8 pt, the bottom row of three
    /// boxes meets at 198.0 and 398.25 pt. One measured edge is not reproduced: "Eeeeeee Eeeeeee" beside "Dd" meet at
    /// 306.75 pt in Chromium, where this rule puts the edge at 307.13 pt, 0.005 pt past the middle of the pixel, which
    /// paints at 307.5 pt; Chromium computes in 1/64 px and the engine exactly.
    /// </remarks>
    /// <param name="start">The start box's outer widths, or null.</param>
    /// <param name="center">The centre box's outer widths, or null.</param>
    /// <param name="end">The end box's outer widths, or null.</param>
    /// <param name="available">The row's content width.</param>
    /// <returns>The outer widths; 0 for a box that is not generated.</returns>
    internal static (double Start, double Center, double End) Widths(IntrinsicWidths? start, IntrinsicWidths? center, IntrinsicWidths? end, double available)
    {
        var generated = (start is null ? 0 : 1) + (center is null ? 0 : 1) + (end is null ? 0 : 1);
        if (generated == 1)
        {
            return (start is null ? 0 : available, center is null ? 0 : available, end is null ? 0 : available);
        }

        var none = IntrinsicWidths.Zero;
        if (center is null)
        {
            var (first, second) = Distribute(start ?? none, 1, end ?? none, 1, available);
            return (first, 0, second);
        }

        // The imaginary box AC stands for both side boxes: twice the wider of them, and, where every width is zero, two
        // shares of the row against the centre box's one.
        var sides = new IntrinsicWidths(
            2 * Math.Max((start ?? none).MinContent, (end ?? none).MinContent),
            2 * Math.Max((start ?? none).MaxContent, (end ?? none).MaxContent));
        var (centerWidth, _) = Distribute(center, 1, sides, 2, available);
        var side = (available - centerWidth) / 2;
        return (start is null ? 0 : side, centerWidth, end is null ? 0 : side);
    }

    /// <summary>
    /// The outer widths of two boxes that share <paramref name="available"/> (CSS Paged Media 3 §5.3.2, "distribute"):
    /// when both max-content widths fit, the space left over them is shared in proportion to the max-content widths;
    /// else when both min-content widths fit, the space left over those is shared in proportion to the difference between
    /// max-content and min-content; else each box gets its min-content width less a share of the shortfall in
    /// proportion to its min-content width (narrower than its content: <see cref="LayOutBox"/> refuses that). Where both
    /// max-content widths are zero the space is shared in proportion to <paramref name="firstShares"/> and
    /// <paramref name="secondShares"/>, how many boxes each stands for: the proportion of the section is undefined
    /// there, and Chromium 159 gives two empty side boxes half the row each and an empty centre box beside one empty
    /// side box a third.
    /// </summary>
    private static (double First, double Second) Distribute(IntrinsicWidths first, int firstShares, IntrinsicWidths second, int secondShares, double available)
    {
        var maxContent = first.MaxContent + second.MaxContent;
        if (Fitting.Fits(maxContent, available))
        {
            var flex = available - maxContent;
            var share = maxContent > 0 ? first.MaxContent / maxContent : (double)firstShares / (firstShares + secondShares);
            return (first.MaxContent + (flex * share), second.MaxContent + (flex * (1 - share)));
        }

        var minContent = first.MinContent + second.MinContent;
        if (Fitting.Fits(minContent, available))
        {
            // The max-content widths do not fit and the min-content widths do, so the differences add up to more than 0.
            var flex = available - minContent;
            var share = (first.MaxContent - first.MinContent) / (maxContent - minContent);
            return (first.MinContent + (flex * share), second.MinContent + (flex * (1 - share)));
        }

        // The min-content widths do not fit, so they add up to more than the available width, which is not negative.
        var shortfall = available - minContent;
        var part = first.MinContent / minContent;
        return (first.MinContent + (shortfall * part), second.MinContent + (shortfall * (1 - part)));
    }

    /// <summary>The child at a place, or null; an error when a child is not a box or two share the place (a model
    /// <see cref="ModelCheck"/> refuses before layout; this keeps the algorithm safe when it is called directly).</summary>
    private static Result<Box?, Error> Child(Box row, MarginBoxPlace place)
    {
        Box? found = null;
        foreach (var child in row.Children)
        {
            if (child is not Box { Style.MarginBoxPlace: not MarginBoxPlace.None } box)
            {
                return Result<Box?, Error>.Failure(LayoutErrors.InvalidContent(row, "every child of a row of page-margin boxes is a box with a place in the row."));
            }

            if (box.Style.MarginBoxPlace != place)
            {
                continue;
            }

            if (found is not null)
            {
                return Result<Box?, Error>.Failure(LayoutErrors.InvalidContent(row, $"a row of page-margin boxes has at most one box at each place; it has two at {place}."));
            }

            found = box;
        }

        return Result<Box?, Error>.Success(found);
    }

    /// <summary>A box's outer intrinsic widths: its border box's (<see cref="LayoutEnvironment.Measure"/>) and its
    /// horizontal margins; null for no box.</summary>
    private static Result<IntrinsicWidths?, Error> Outer(LayoutEnvironment environment, Box? box) =>
        box is null
            ? Result<IntrinsicWidths?, Error>.Success(null)
            : environment.Measure(box).OnSuccessMap(widths => (IntrinsicWidths?)widths.Plus(box.Style.Margin.Horizontal.Points));

    /// <summary>
    /// One page-margin box at its outer width: its content laid out with unlimited height at its border box's width,
    /// then the box made as tall as the row less its own vertical margins, its content moved down by
    /// <see cref="BoxStyle.VerticalAlign"/> (the same stretch a table cell gets, <see cref="TableSegments.Stretch"/>).
    /// Measured in Chromium 159: one 10 pt line of Lato in a 60 pt box with <c>vertical-align: middle</c> has its
    /// baseline at 33.75 pt, two lines at 267.75 and 279.75 pt in the box from 240 to 300 pt. Content wider than the
    /// box (its outer min-content width is more than the box's outer width) or taller than it is refused.
    /// </summary>
    private static Result<BoxFragment, Error> LayOutBox(LayoutEnvironment environment, Box box, IntrinsicWidths outer, double outerWidth, double rowHeight)
    {
        var margin = box.Style.Margin;
        var width = outerWidth - margin.Horizontal.Points;
        var height = rowHeight - margin.Vertical.Points;
        if (!Fitting.Fits(outer.MinContent, outerWidth))
        {
            return Result<BoxFragment, Error>.Failure(LayoutErrors.MarginBoxOverflow(box, "width", outer.MinContent - margin.Horizontal.Points, width));
        }

        if (!Fitting.Fits(0, height))
        {
            return Result<BoxFragment, Error>.Failure(LayoutErrors.MarginBoxOverflow(box, "height", margin.Vertical.Points, rowHeight));
        }

        return environment.Layout(box, new ConstraintSpace(width, double.PositiveInfinity, false, true), null).OnSuccessBind(result =>
            result is LayoutResult.Placed { Continuation: null } placed
                ? Fitting.Fits(placed.Fragment.Size.Height, height)
                    ? Result<BoxFragment, Error>.Success(TableSegments.Stretch(placed.Fragment, height, box.Style.VerticalAlign))
                    : Result<BoxFragment, Error>.Failure(LayoutErrors.MarginBoxOverflow(box, "height", placed.Fragment.Size.Height, height))
                : Result<BoxFragment, Error>.Failure(LayoutErrors.InvalidContent(box, "a page-margin box cannot hold a forced page break.")));
    }
}
