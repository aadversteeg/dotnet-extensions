using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The list-item algorithm (<see cref="LayoutAlgorithmId.ListItem"/>): a list item with an outside marker (CSS Lists 3 §3,
/// <c>list-style-position: outside</c>). Its body is laid out as block flow, as the item's only content; its label sits
/// outside the item's content box, on the baseline of the body's first line, and takes no room in the flow.
/// </summary>
/// <remarks>
/// THE RULE FOR THE LABEL, as measured in Chromium 159's print (cases <c>li-symbol-sizes</c>, <c>li-ol-12</c>,
/// <c>li-counter-styles</c>, <c>li-font</c>, <c>li-first-line</c>, <c>li-page-break</c>), for every front-end:
/// <list type="bullet">
/// <item>The first line: the first line box found going down the body's first children, past empty boxes; a nested list
/// item's own label is no line of it. Where an image, a table or any box that is not block flow or inline comes first, or
/// the body has no line at all, the label gets a line of its own at the top of the item's content box, with the strut of the
/// item's font (an empty item grows to that line's height; a block image first: the item's top plus the ascent).</item>
/// <item>A bullet (<see cref="MarkerSymbol"/>): with A the ascent of the item's font in CSS pixels, as the document's font
/// metric rounding gives it (whole pixels, half away from zero, under <see cref="FontMetricRounding.WholePixels"/>),
/// o = ⌊2A/3⌋ and w = ⌊(o + 1)/2⌋; the bullet is w × w pixels, its left edge o + 7 pixels left of the item's content edge,
/// its top ⌊3(A − o)/2⌋ − A pixels from the baseline. Lato at 10, 14 and 21.33 px gives w 3, 5, 7 at −13, −16, −21 px;
/// Theano Didot at 14 and 21.33 px w 4 and 5 (its typographic ascent). That place is relative to the item; under
/// <see cref="MarkerSnapping.WholePixels"/> the bullet's box is then put on whole pixels of the page, once the page is known
/// (<see cref="InlineBoxPixels"/>; cases <c>li-after-table</c>, <c>li-after-block</c>, <c>li-after-edge</c>,
/// <c>li-after-page</c>).</item>
/// <item>A text label: laid out on one line at its max-content width, right-aligned so that its advance (the space after
/// the number included) ends on the item's content edge; under <see cref="LengthRounding.SixtyFourthPixels"/> that width
/// is on the 1/64 px step, as each text item's (Lato 14 px "1. " starts 11.262 pt left of the edge, "10. " 17.355 pt).</item>
/// <item>The label is placed with the fragment that holds the first line and never with a continuation: an item that
/// breaks over a page shows its label once, on the first page.</item>
/// <item>The label does not move the body and is not clipped by the item; a label past the sheet's left edge is refused
/// (<see cref="ListMarkers.OutsideSheet"/>), and one wholly outside a clipped body's area is not drawn (the painter).</item>
/// </list>
/// LIMIT: an item whose first fragment holds no line but breaks (a last-resort break before its content) gets its label at
/// the top of that first fragment.
/// </remarks>
public static class ListItemLayout
{
    /// <summary>The list-item algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">A list item: an optional label (<see cref="SemanticRole.ListLabel"/>) and its body
    /// (<see cref="SemanticRole.ListBody"/>), a flow box.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">The <see cref="FlowBreakToken"/> of the item's body, or null to start.</param>
    /// <returns>The fragment and continuation, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        if (Parts(box) is not { } parts)
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidContent(box, "a list item holds an optional label and then its body, a flow box."));
        }

        var (label, body) = parts;
        var flow = box with { Children = [body] };
        return FlowLayout.Layout(environment, flow, space, incoming).OnSuccessBind(result => result switch
        {
            LayoutResult.Placed placed when incoming is null && label is not null =>
                WithLabel(environment, box, label, placed.Fragment, placed.Continuation is not null)
                    .OnSuccessMap(fragment => (LayoutResult)(placed with { Fragment = fragment })),
            LayoutResult.Placed placed => Result<LayoutResult, Error>.Success(placed with { Fragment = placed.Fragment with { Box = box } }),
            _ => Result<LayoutResult, Error>.Success(result),
        });
    }

    /// <summary>The intrinsic widths of a list item: those of its body in the item's insets; the label lies outside and adds
    /// nothing.</summary>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <param name="box">A list item.</param>
    /// <returns>The widths, or an error.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        Parts(box) is { } parts
            ? FlowLayout.Measure(environment, box with { Children = [parts.Body] })
            : Result<IntrinsicWidths, Error>.Failure(LayoutErrors.InvalidContent(box, "a list item holds an optional label and then its body, a flow box."));

    /// <summary>The label (or null) and the body of a list item, or null when it has other children.</summary>
    private static (Box? Label, Box Body)? Parts(Box item) => item.Children switch
    {
        [Box { Role: SemanticRole.ListBodyRole } body] => (null, body),
        [Box { Role: SemanticRole.ListLabelRole } label, Box { Role: SemanticRole.ListBodyRole } body] => (label, body),
        _ => null,
    };

    /// <summary>The item's fragment with its label placed by THE RULE FOR THE LABEL, as its first child (so the label comes
    /// before the body in the structure, ISO 32000-1 §14.8.4.3.3).</summary>
    private static Result<BoxFragment, Error> WithLabel(LayoutEnvironment environment, Box item, Box label, BoxFragment fragment, bool breaks)
    {
        var style = item.Style;
        var edge = style.Border.Left.Width.Points + style.Padding.Left.Points;
        var contentTop = style.Border.Top.Width.Points + style.Padding.Top.Points;
        var baseline = FirstBaseline(fragment);
        var laidOut = label.Children is [MarkerSymbol symbol]
            ? Bullet(environment, label, symbol, edge, baseline, contentTop)
            : TextLabel(environment, label, edge, baseline, contentTop);
        return laidOut.OnSuccessMap(placed =>
        {
            // An item without a line of its own gets the label's line: it is at least that line high.
            var lineBottom = placed.LineBottom + style.Padding.Bottom.Points + style.Border.Bottom.Width.Points;
            var height = baseline is null && !breaks ? Math.Max(fragment.Size.Height, lineBottom) : fragment.Size.Height;
            return new BoxFragment(item, new Size(fragment.Size.Width, height), [placed.Label, .. fragment.Children], fragment.Border, fragment.IsRepeated);
        });
    }

    /// <summary>A label placed in its item, and the bottom of the line it would have alone at the item's top.</summary>
    private sealed record PlacedLabel(PlacedFragment Label, double LineBottom);

    /// <summary>A bullet: its size and place from the ascent of the item's font (THE RULE FOR THE LABEL).</summary>
    private static Result<PlacedLabel, Error> Bullet(LayoutEnvironment environment, Box label, MarkerSymbol symbol, double edge, double? baseline, double contentTop)
    {
        var text = symbol.Style;
        return environment.Fonts.Find(text.FontFamily, text.FontWeight, text.FontStyle).MapError(error => error.WithSource(symbol.Source)).OnSuccessMap(font =>
        {
            var rounding = environment.Profile.FontMetricRounding;
            var strut = LineBoxes.InlineExtent.Of(font, text.FontSize.Points, text.LineHeight, rounding);
            var ascent = font.Metrics.ToPoints(font.Metrics.Line.Ascender, text.FontSize.Points) / CssPixels.Points;
            var a = rounding == FontMetricRounding.WholePixels ? Math.Round(ascent, MidpointRounding.AwayFromZero) : ascent;
            var o = Math.Floor(2 * a / 3 + 1e-9);
            var w = Math.Floor((o + 1) / 2 + 1e-9);
            var line = baseline ?? contentTop + strut.Above;
            var top = line + (Math.Floor(3 * (a - o) / 2 + 1e-9) - a) * CssPixels.Points;
            var size = new Size(w * CssPixels.Points, w * CssPixels.Points);
            var fragment = new BoxFragment(label, size, [new PlacedFragment(new Point(0, 0), new MarkerFragment(symbol, size))]);
            return new PlacedLabel(new PlacedFragment(new Point(edge - (o + 7) * CssPixels.Points, top), fragment), contentTop + strut.Above + strut.Below);
        });
    }

    /// <summary>A text label: one line at its max-content width, its advance ending on the content edge (THE RULE FOR THE
    /// LABEL).</summary>
    private static Result<PlacedLabel, Error> TextLabel(LayoutEnvironment environment, Box label, double edge, double? baseline, double contentTop) =>
        InlineLayout.Measure(environment, label).OnSuccessBind(widths =>
            InlineLayout.Layout(environment, label, new ConstraintSpace(widths.MaxContent, double.PositiveInfinity, IsAtPageStart: true), null).OnSuccessBind(result =>
            {
                if (result is not LayoutResult.Placed { Fragment: var fragment })
                {
                    return Result<PlacedLabel, Error>.Failure(LayoutErrors.InvalidContent(label, "a list label is laid out on one line."));
                }

                var own = fragment.Children is [{ Fragment: LineFragment line } placed, ..] ? placed.Offset.Y + line.Baseline : 0;
                var top = baseline is { } first ? first - own : contentTop;
                return Result<PlacedLabel, Error>.Success(
                    new PlacedLabel(new PlacedFragment(new Point(edge - widths.MaxContent, top), fragment), contentTop + fragment.Size.Height));
            }));

    /// <summary>The baseline of the first line in an item's fragment, relative to the item's top, or null when the body
    /// has none or something that is not a line comes first (THE RULE FOR THE LABEL). One walk with an explicit stack, down
    /// the first children in document order.</summary>
    private static double? FirstBaseline(BoxFragment item)
    {
        var pending = new Stack<(Fragment Fragment, double Top)>();
        for (var index = item.Children.Count - 1; index >= 0; index--)
        {
            pending.Push((item.Children[index].Fragment, item.Children[index].Offset.Y));
        }

        while (pending.Count > 0)
        {
            var (fragment, top) = pending.Pop();
            switch (fragment)
            {
                case LineFragment line:
                    return top + line.Baseline;
                case BoxFragment { Box.Role: SemanticRole.ListLabelRole }:
                    break;
                case BoxFragment box when box.Box.Layout == LayoutAlgorithmId.Flow || box.Box.Layout == LayoutAlgorithmId.Inline || box.Box.Layout == LayoutAlgorithmId.ListItem:
                    for (var index = box.Children.Count - 1; index >= 0; index--)
                    {
                        pending.Push((box.Children[index].Fragment, top + box.Children[index].Offset.Y));
                    }

                    break;
                default:
                    return null;
            }
        }

        return null;
    }
}
