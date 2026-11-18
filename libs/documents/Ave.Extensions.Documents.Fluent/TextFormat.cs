using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Text formatting as the author specifies it. A null field means "inherit from the parent" — font family,
/// size, weight, style and colour are inherited properties in CSS (CSS Cascade 4 §7.2), and so are the paragraph
/// properties kept here: text alignment, orphans and widows.
/// </summary>
/// <param name="FontFamily">The font family, for example <c>Lato</c>.</param>
/// <param name="FontSize">The font size.</param>
/// <param name="FontWeight">The weight.</param>
/// <param name="Color">The text colour.</param>
/// <param name="TextAlign">How lines are aligned (CSS <c>text-align</c>, inherited). Set only on elements with a box
/// and on styles: a span has no lines of its own.</param>
/// <param name="FontStyle">Upright or italic (CSS <c>font-style</c>).</param>
/// <param name="Orphans">The fewest lines of a paragraph before a page break inside it (CSS <c>orphans</c>, inherited).
/// Set only on elements with a box and on styles, as <paramref name="TextAlign"/>.</param>
/// <param name="Widows">The fewest lines of a paragraph after a page break inside it (CSS <c>widows</c>, inherited).
/// Set only on elements with a box and on styles.</param>
/// <param name="LineHeight">The line height (CSS <c>line-height</c>, inherited): on a paragraph also the height of its
/// strut, the least height of its lines.</param>
public sealed record TextFormat(
    string? FontFamily = null,
    Length? FontSize = null,
    FontWeight? FontWeight = null,
    Color? Color = null,
    TextAlign? TextAlign = null,
    FontStyle? FontStyle = null,
    int? Orphans = null,
    int? Widows = null,
    LineHeightValue? LineHeight = null)
{
    /// <summary>No formatting: everything inherits.</summary>
    public static TextFormat Inherit { get; } = new();
}
