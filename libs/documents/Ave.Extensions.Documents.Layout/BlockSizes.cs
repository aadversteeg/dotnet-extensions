using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The heights of a box's border box that its height, minimum and maximum height allow (CSS 2.1 §10.6.3 and §10.7, CSS
/// Box Model 3 §4.3): a height is limited by the maximum and then by the minimum, so the minimum wins (case
/// <c>sz-min-max-height</c>: height 30pt with min-height 50pt is 50pt, height 50pt with max-height 30pt is 30pt,
/// min-height 50pt over max-height 30pt is 50pt); without a height the content's height is limited the same way. A
/// content-box size has the vertical padding and border added; a border-box size holds them and is never less than they
/// are (cases <c>sz-height-box-sizing</c>, <c>sz-height-box-sizing-break</c>). Over pages the box's fragments share the
/// height: each page shows what the pages before did not (<see cref="Rest"/>; cases <c>sz-height-break</c>,
/// <c>sz-height-rest-short</c>, <c>sz-min-height-break</c>).
/// </summary>
/// <param name="Low">The least height of the whole border box.</param>
/// <param name="High">The most height of the whole border box (infinite without a height or a maximum); never less than
/// <paramref name="Low"/>.</param>
internal sealed record BlockSizes(double Low, double High)
{
    /// <summary>The heights the box's style allows, or null for a box whose height, minimum and maximum are all
    /// <c>auto</c> or <c>none</c>: its height is its content's.</summary>
    /// <param name="style">The box's style.</param>
    /// <returns>The heights, in points.</returns>
    public static BlockSizes? Of(BoxStyle style)
    {
        if (style.SpecifiedHeight is null && style.MinHeight is null && style.MaxHeight is null)
        {
            return null;
        }

        var insets = style.Padding.Top.Points + style.Padding.Bottom.Points + style.Border.Widths.Top.Points + style.Border.Widths.Bottom.Points;
        double BorderBox(Length length) => style.BoxSizing == BoxSizing.BorderBox ? Math.Max(length.Points, insets) : length.Points + insets;
        var min = style.MinHeight is { } minimum ? BorderBox(minimum) : 0;
        var max = style.MaxHeight is { } maximum ? Math.Max(min, BorderBox(maximum)) : double.PositiveInfinity;
        if (style.SpecifiedHeight is { } height)
        {
            var used = Math.Max(min, Math.Min(BorderBox(height), max));
            return new BlockSizes(used, used);
        }

        return new BlockSizes(min, max);
    }

    /// <summary>The least and the most height of the box's fragment on this page, from its top here: what is left of
    /// <see cref="Low"/> and <see cref="High"/> after the height the pages before showed (Chromium's consumed block size).</summary>
    /// <param name="consumed">The height the pages before showed.</param>
    /// <returns>The two heights; the least may be 0 or less, when the pages before showed it all.</returns>
    public (double Low, double High) Rest(double consumed) => (Low - consumed, High - consumed);
}
