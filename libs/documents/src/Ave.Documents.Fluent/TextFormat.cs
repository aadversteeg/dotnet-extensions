using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// Text formatting as the author specifies it. A null field means "inherit from the parent" — font family,
/// size, weight and colour are inherited properties in CSS (CSS Cascade 4 §7.1).
/// </summary>
/// <param name="FontFamily">The font family, for example <c>Lato</c>.</param>
/// <param name="FontSize">The font size.</param>
/// <param name="FontWeight">The weight.</param>
/// <param name="Color">The text colour.</param>
public sealed record TextFormat(string? FontFamily = null, Length? FontSize = null, FontWeight? FontWeight = null, Color? Color = null)
{
    /// <summary>No formatting: everything inherits.</summary>
    public static TextFormat Inherit { get; } = new();
}
