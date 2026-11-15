using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// Text formatting as the author specifies it. A null field means "inherit from the parent" — font family,
/// size, weight, style and colour are inherited properties in CSS (CSS Cascade 4 §7.2).
/// </summary>
/// <param name="FontFamily">The font family, for example <c>Lato</c>.</param>
/// <param name="FontSize">The font size.</param>
/// <param name="FontWeight">The weight.</param>
/// <param name="Color">The text colour.</param>
/// <param name="TextAlign">How lines are aligned (CSS <c>text-align</c>, inherited). Set only on elements with a box
/// and on styles: a span has no lines of its own.</param>
/// <param name="FontStyle">Upright or italic (CSS <c>font-style</c>).</param>
public sealed record TextFormat(
    string? FontFamily = null,
    Length? FontSize = null,
    FontWeight? FontWeight = null,
    Color? Color = null,
    TextAlign? TextAlign = null,
    FontStyle? FontStyle = null)
{
    /// <summary>No formatting: everything inherits.</summary>
    public static TextFormat Inherit { get; } = new();
}
