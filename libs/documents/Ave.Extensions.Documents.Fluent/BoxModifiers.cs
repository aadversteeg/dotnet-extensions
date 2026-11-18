using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Modifiers for box properties, on every element with a box (blocks, paragraphs, tables, rows, cells, images) and
/// on <see cref="Style"/>:
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

    /// <summary>
    /// Sets how the lines are aligned (CSS <c>text-align</c>): left, right, centre or justified. Inherited, like a
    /// text property, by the paragraphs inside a block; only for blocks, paragraphs and styles, because a span has
    /// no lines of its own.
    /// </summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="align">The alignment.</param>
    /// <returns>A copy with the alignment set.</returns>
    public static T TextAlign<T>(this T element, TextAlign align) where T : IBoxFormatted<T> =>
        element.WithFormat(element.Format with { Text = element.Format.Text with { TextAlign = align } });

    /// <summary>Sets the fewest lines of a paragraph a page may hold before a break inside it (CSS <c>orphans</c>,
    /// CSS Fragmentation 3 §3.3; inherited, like <c>TextAlign</c>). The initial value is 2, as in CSS. A page break that
    /// leaves fewer is taken only when no better one exists; a paragraph with fewer lines than orphans and widows
    /// together then moves to the next page whole.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The block, paragraph or style.</param>
    /// <param name="lines">The number of lines, at least 1.</param>
    /// <returns>A copy with orphans set.</returns>
    public static T Orphans<T>(this T element, int lines) where T : IBoxFormatted<T> =>
        element.WithFormat(element.Format with { Text = element.Format.Text with { Orphans = lines } });

    /// <summary>Sets the fewest lines of a paragraph a page may hold after a break inside it (CSS <c>widows</c>, CSS
    /// Fragmentation 3 §3.3; inherited). The initial value is 2, as in CSS.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The block, paragraph or style.</param>
    /// <param name="lines">The number of lines, at least 1.</param>
    /// <returns>A copy with widows set.</returns>
    public static T Widows<T>(this T element, int lines) where T : IBoxFormatted<T> =>
        element.WithFormat(element.Format with { Text = element.Format.Text with { Widows = lines } });

    /// <summary>Sets a solid border on all four sides (CSS <c>border</c>). A side set with <see cref="BorderTop{T}"/>
    /// and the like wins over it.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="width">The line width; zero or more (zero is no border).</param>
    /// <param name="color">The line colour.</param>
    /// <returns>A copy with the border set.</returns>
    public static T Border<T>(this T element, Length width, Color color) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { Border = new BorderSide(width, color) });

    /// <summary>Sets the top border (CSS <c>border-top</c>, solid).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="width">The line width; zero or more.</param>
    /// <param name="color">The line colour.</param>
    /// <returns>A copy with the border set.</returns>
    public static T BorderTop<T>(this T element, Length width, Color color) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { BorderTop = new BorderSide(width, color) });

    /// <summary>Sets the right border (CSS <c>border-right</c>, solid).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="width">The line width; zero or more.</param>
    /// <param name="color">The line colour.</param>
    /// <returns>A copy with the border set.</returns>
    public static T BorderRight<T>(this T element, Length width, Color color) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { BorderRight = new BorderSide(width, color) });

    /// <summary>Sets the bottom border (CSS <c>border-bottom</c>, solid).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="width">The line width; zero or more.</param>
    /// <param name="color">The line colour.</param>
    /// <returns>A copy with the border set.</returns>
    public static T BorderBottom<T>(this T element, Length width, Color color) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { BorderBottom = new BorderSide(width, color) });

    /// <summary>Sets the left border (CSS <c>border-left</c>, solid).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="width">The line width; zero or more.</param>
    /// <param name="color">The line colour.</param>
    /// <returns>A copy with the border set.</returns>
    public static T BorderLeft<T>(this T element, Length width, Color color) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { BorderLeft = new BorderSide(width, color) });

    /// <summary>
    /// Keeps the element on one page when it fits on one (CSS <c>break-inside: avoid</c>): it moves to the next page
    /// whole rather than break. An element taller than a page still breaks, so nothing is lost.
    /// </summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <returns>A copy that is kept together.</returns>
    public static T KeepTogether<T>(this T element) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { KeepTogether = true });

    /// <summary>Avoids a page break between this element and the next one (CSS <c>break-after: avoid</c>), for
    /// example to keep a heading with the paragraph or table under it.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <returns>A copy that is kept with the next element.</returns>
    public static T KeepWithNext<T>(this T element) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { KeepWithNext = true });

    /// <summary>Avoids a page break between this element and the one before it (CSS <c>break-before: avoid</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <returns>A copy that is kept with the previous element.</returns>
    public static T KeepWithPrevious<T>(this T element) where T : IBoxFormatted<T> =>
        element.WithBox(element.Format.Box with { KeepWithPrevious = true });

    private static T WithBox<T>(this T element, BoxFormat box) where T : IBoxFormatted<T> =>
        element.WithFormat(element.Format with { Box = box });
}
