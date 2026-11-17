namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// A set of formatting values as the author specifies them: what the modifiers (<see cref="TextModifiers"/>,
/// <see cref="BoxModifiers"/>) set on one element, or a reusable style applied with <c>.With(style)</c>. Every
/// field that is null is "not set here". A style is built with the same modifiers as an element:
/// <code>static readonly Style Note = Style.Empty.Padding(4.Mm()).Background(PaleBlue).FontSize(9.Pt());</code>
/// </summary>
/// <param name="Box">The box properties: margins, padding, background. Not inherited.</param>
/// <param name="Text">The text properties: font family, size, weight, colour. Inherited by the children.</param>
public sealed record Style(BoxFormat Box, TextFormat Text) : IBoxFormatted<Style>
{
    /// <summary>Nothing set.</summary>
    public static Style Empty { get; } = new(BoxFormat.None, TextFormat.Inherit);

    /// <summary>A style is its own format: a modifier on a style returns the changed style.</summary>
    Style IFormatted<Style>.Format => this;

    /// <inheritdoc/>
    Style IFormatted<Style>.WithFormat(Style format) => format;
}
