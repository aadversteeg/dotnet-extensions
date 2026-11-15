using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// Modifiers for box properties, on blocks, paragraphs and headings and on <see cref="Style"/>:
/// <c>Heading(1, "…").MarginBottom(12.Pt())</c>. Each returns a new value with ONE property set.
/// </summary>
/// <remarks>
/// ORDER-FREE, like <see cref="TextModifiers"/>: modifiers for different properties commute. The all-sides
/// modifiers <see cref="Margin{T}"/> and <see cref="Padding{T}"/> are properties of their own, not a short way
/// of writing four sides; when an element has both, the single side wins over the all-sides value by a fixed
/// rule, in whatever order the two were called. So <c>p.Padding(4.Mm()).PaddingTop(0.Pt())</c> and
/// <c>p.PaddingTop(0.Pt()).Padding(4.Mm())</c> are the same element: no top padding, 4 mm on the other sides.
/// </remarks>
public static class BoxModifiers
{
    /// <summary>Sets the margin on all four sides (CSS <c>margin</c> with one value). A side set with
    /// <see cref="MarginTop{T}"/> and the like wins over it.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="all">The margin; not negative in this version.</param>
    /// <returns>A copy with the margin set.</returns>
    public static T Margin<T>(this T element, Length all) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { Margin = all });

    /// <summary>Sets the top margin (CSS <c>margin-top</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="top">The margin; not negative in this version.</param>
    /// <returns>A copy with the margin set.</returns>
    public static T MarginTop<T>(this T element, Length top) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { MarginTop = top });

    /// <summary>Sets the right margin (CSS <c>margin-right</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="right">The margin; not negative in this version.</param>
    /// <returns>A copy with the margin set.</returns>
    public static T MarginRight<T>(this T element, Length right) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { MarginRight = right });

    /// <summary>Sets the bottom margin (CSS <c>margin-bottom</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="bottom">The margin; not negative in this version.</param>
    /// <returns>A copy with the margin set.</returns>
    public static T MarginBottom<T>(this T element, Length bottom) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { MarginBottom = bottom });

    /// <summary>Sets the left margin (CSS <c>margin-left</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="left">The margin; not negative in this version.</param>
    /// <returns>A copy with the margin set.</returns>
    public static T MarginLeft<T>(this T element, Length left) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { MarginLeft = left });

    /// <summary>Sets the padding on all four sides (CSS <c>padding</c> with one value). A side set with
    /// <see cref="PaddingTop{T}"/> and the like wins over it.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="all">The padding; never negative.</param>
    /// <returns>A copy with the padding set.</returns>
    public static T Padding<T>(this T element, Length all) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { Padding = all });

    /// <summary>Sets the top padding (CSS <c>padding-top</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="top">The padding; never negative.</param>
    /// <returns>A copy with the padding set.</returns>
    public static T PaddingTop<T>(this T element, Length top) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { PaddingTop = top });

    /// <summary>Sets the right padding (CSS <c>padding-right</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="right">The padding; never negative.</param>
    /// <returns>A copy with the padding set.</returns>
    public static T PaddingRight<T>(this T element, Length right) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { PaddingRight = right });

    /// <summary>Sets the bottom padding (CSS <c>padding-bottom</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="bottom">The padding; never negative.</param>
    /// <returns>A copy with the padding set.</returns>
    public static T PaddingBottom<T>(this T element, Length bottom) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { PaddingBottom = bottom });

    /// <summary>Sets the left padding (CSS <c>padding-left</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="left">The padding; never negative.</param>
    /// <returns>A copy with the padding set.</returns>
    public static T PaddingLeft<T>(this T element, Length left) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { PaddingLeft = left });

    /// <summary>Sets the background colour (CSS <c>background-color</c>), painted under the padding and content.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="color">The colour.</param>
    /// <returns>A copy with the background set.</returns>
    public static T Background<T>(this T element, Color color) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { Background = color });

    private static T WithBox<T>(this T element, BoxFormat box) where T : IBoxFormatted<T> =>
        element.WithFormat(element.Format with { Box = box });
}
