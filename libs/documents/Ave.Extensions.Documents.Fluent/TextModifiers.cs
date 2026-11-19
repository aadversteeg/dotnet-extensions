using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Modifiers for text properties, on every element and on <see cref="Style"/>:
/// <c>Paragraph("…").FontSize(9.Pt()).Color(Grey)</c>. Each returns a new value with ONE property set; nothing
/// is changed in place.
/// </summary>
/// <remarks>
/// ORDER-FREE: every modifier sets its own property, so modifiers for different properties give
/// the same element in any order: <c>a.FontSize(x).Color(y) == a.Color(y).FontSize(x)</c>. Setting the same
/// property twice keeps the last value, exactly as assigning a field twice would. The values are only checked
/// when the document is translated; an invalid one is reported with the file and line of the element.
/// </remarks>
public static class TextModifiers
{
    /// <summary>Sets the font family (CSS <c>font-family</c>, one family).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="family">The family name, for example <c>Lato</c>; must be in the font set when rendering.</param>
    /// <returns>A copy with the family set.</returns>
    public static T FontFamily<T>(this T element, string family) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { FontFamily = family });

    /// <summary>Sets the font size (CSS <c>font-size</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="size">The size, for example <c>11.Pt()</c>; must be positive.</param>
    /// <returns>A copy with the size set.</returns>
    public static T FontSize<T>(this T element, Length size) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { FontSize = size });

    /// <summary>Sets the font weight (CSS <c>font-weight</c>, 1–1000).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="weight">The weight, for example <see cref="Core.FontWeight.Bold"/>.</param>
    /// <returns>A copy with the weight set.</returns>
    public static T FontWeight<T>(this T element, FontWeight weight) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { FontWeight = weight });

    /// <summary>Sets the font weight to bold (700); the same property as <see cref="FontWeight{T}"/>.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <returns>A copy with weight 700.</returns>
    public static T Bold<T>(this T element) where T : IFormatted<T> => element.FontWeight(Core.FontWeight.Bold);

    /// <summary>Sets the font style (CSS <c>font-style</c>): upright or italic. An italic face of the family must be in
    /// the font set when rendering; no slanted face is synthesised.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="style">The style.</param>
    /// <returns>A copy with the style set.</returns>
    public static T FontStyle<T>(this T element, FontStyle style) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { FontStyle = style });

    /// <summary>Sets the font style to italic; the same property as <see cref="FontStyle{T}"/>.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <returns>A copy in italic.</returns>
    public static T Italic<T>(this T element) where T : IFormatted<T> => element.FontStyle(Core.FontStyle.Italic);

    /// <summary>Sets the text colour (CSS <c>color</c>).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="color">The colour.</param>
    /// <returns>A copy with the colour set.</returns>
    public static T Color<T>(this T element, Color color) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { Color = color });

    /// <summary>Sets the line height as a multiple of the font size (CSS <c>line-height: &lt;number&gt;</c>, inherited
    /// as the number: a descendant with another font size gets its own multiple). On a paragraph it is also the height of
    /// its strut, so no line is lower (CSS 2.1 §10.8.1).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="factor">The multiple, for example 1.5; not negative. A plain number is a multiple, also when it is
    /// written as an integer: <c>LineHeight(14)</c> is 14 times the font size; a length of 14 points is
    /// <c>LineHeight(14.Pt())</c>.</param>
    /// <returns>A copy with the line height set.</returns>
    public static T LineHeight<T>(this T element, double factor) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { LineHeight = new LineHeightValue.Number(factor) });

    /// <summary>Sets the line height as a length (CSS <c>line-height: &lt;length&gt;</c>, inherited as the length).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="height">The line height, for example <c>14.Pt()</c>; not negative.</param>
    /// <returns>A copy with the line height set.</returns>
    public static T LineHeight<T>(this T element, Length height) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { LineHeight = new LineHeightValue.Fixed(height) });

    /// <summary>Sets the line height as a percentage of the element's own font size (CSS <c>line-height:
    /// &lt;percentage&gt;</c>), which computes to a length and is inherited as that length.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="percent">The percentage, for example 120; not negative.</param>
    /// <returns>A copy with the line height set.</returns>
    public static T LineHeightPercent<T>(this T element, double percent) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { LineHeight = new LineHeightValue.Percentage(percent) });

    /// <summary>Sets the line height back to <c>normal</c>, the font's own line spacing (the initial value).</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <returns>A copy with the line height set.</returns>
    public static T LineHeightNormal<T>(this T element) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { LineHeight = LineHeightValue.Normal.Instance });

    /// <summary>Sets how white space collapses (CSS <c>white-space-collapse</c>, CSS Text 4, inherited):
    /// <see cref="Core.WhiteSpaceCollapse.Preserve"/> keeps every space and makes a line feed a forced line break (with
    /// wrapping, <c>white-space: pre-wrap</c>), <see cref="Core.WhiteSpaceCollapse.PreserveBreaks"/> collapses spaces but makes a
    /// line feed a forced line break (<c>pre-line</c>), <see cref="Core.WhiteSpaceCollapse.BreakSpaces"/> keeps every space as
    /// <c>Preserve</c> but lets none hang: spaces take their room at a line's end and may wrap (<c>break-spaces</c>). A tab in preserved text is refused when the document is laid out: tab
    /// stops are not built.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="collapse">The value.</param>
    /// <returns>A copy with the value set.</returns>
    public static T WhiteSpaceCollapse<T>(this T element, WhiteSpaceCollapse collapse) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { WhiteSpaceCollapse = collapse });

    /// <summary>Sets whether text wraps at its soft wrap opportunities (CSS <c>text-wrap-mode</c>, CSS Text 4, inherited):
    /// <see cref="Core.TextWrapMode.NoWrap"/> keeps the text on one line up to a forced break (with preserved spaces,
    /// <c>white-space: pre</c>; with collapsing ones, <c>nowrap</c>); a line wider than its box overflows it. A table column with
    /// such text is at least as wide as its line.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="wrap">The value.</param>
    /// <returns>A copy with the value set.</returns>
    public static T TextWrapMode<T>(this T element, TextWrapMode wrap) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with { TextWrapMode = wrap });

    /// <summary>Draws a line under the element's text (CSS <c>text-decoration-line: underline</c>, CSS Text Decoration 3 §2.1):
    /// across all the text inside the element, at the place and in the thickness the element's own font gives, one straight
    /// line also where the text inside changes size; not under an image. Set again, the colour is replaced.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="color">The line's colour (<c>text-decoration-color</c>); the element's text colour when not given.</param>
    /// <returns>A copy with the line set.</returns>
    public static T Underline<T>(this T element, Color? color = null) where T : IFormatted<T> =>
        element.WithDecoration(new TextDecorationFormat(TextDecorationLine.Underline, color));

    /// <summary>Draws a line through the element's text (CSS <c>text-decoration-line: line-through</c>): in the element's font
    /// and painted over the glyphs; it follows the middle of the text inside where its size changes.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="color">The line's colour; the element's text colour when not given.</param>
    /// <returns>A copy with the line set.</returns>
    public static T LineThrough<T>(this T element, Color? color = null) where T : IFormatted<T> =>
        element.WithDecoration(new TextDecorationFormat(TextDecorationLine.LineThrough, color));

    /// <summary>Draws a line over the element's text (CSS <c>text-decoration-line: overline</c>): in the element's font, at the
    /// top of the text inside.</summary>
    /// <typeparam name="T">The element or style type.</typeparam>
    /// <param name="element">The element or style.</param>
    /// <param name="color">The line's colour; the element's text colour when not given.</param>
    /// <returns>A copy with the line set.</returns>
    public static T Overline<T>(this T element, Color? color = null) where T : IFormatted<T> =>
        element.WithDecoration(new TextDecorationFormat(TextDecorationLine.Overline, color));

    private static T WithDecoration<T>(this T element, TextDecorationFormat line) where T : IFormatted<T> =>
        element.WithText(element.Format.Text with
        {
            Decorations = [.. (element.Format.Text.Decorations ?? []).Where(set => set.Line != line.Line), line],
        });

    private static T WithText<T>(this T element, TextFormat text) where T : IFormatted<T> =>
        element.WithFormat(element.Format with { Text = text });
}
