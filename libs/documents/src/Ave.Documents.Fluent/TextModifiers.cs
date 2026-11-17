using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

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

    private static T WithText<T>(this T element, TextFormat text) where T : IFormatted<T> =>
        element.WithFormat(element.Format with { Text = text });
}
