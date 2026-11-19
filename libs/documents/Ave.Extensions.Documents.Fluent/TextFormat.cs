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

    /// <summary>The fewest lines of a paragraph before a page break inside it (CSS <c>orphans</c>, inherited). Set only
    /// on elements with a box and on styles, as <see cref="TextAlign"/>. A property of the body, not of the positional
    /// record, so the record keeps the shape of 0.1.0-preview.2.</summary>
    public int? Orphans { get; init; }

    /// <summary>The fewest lines of a paragraph after a page break inside it (CSS <c>widows</c>, inherited). Set only on
    /// elements with a box and on styles.</summary>
    public int? Widows { get; init; }

    /// <summary>The line height (CSS <c>line-height</c>, inherited): on a paragraph also the height of its strut, the least
    /// height of its lines.</summary>
    public LineHeightValue? LineHeight { get; init; }

    /// <summary>How white space collapses (CSS <c>white-space-collapse</c>, CSS Text 4, inherited).</summary>
    public WhiteSpaceCollapse? WhiteSpaceCollapse { get; init; }

    /// <summary>Whether text wraps at its soft wrap opportunities (CSS <c>text-wrap-mode</c>, CSS Text 4, inherited).</summary>
    public TextWrapMode? TextWrapMode { get; init; }

    /// <summary>Whether underlines and overlines leave gaps around the glyphs' ink (CSS <c>text-decoration-skip-ink</c>, CSS Text
    /// Decoration 4 §2.10.4, inherited): the value of the text decides, not that of the element that draws the line.</summary>
    public TextDecorationSkipInk? DecorationSkipInk { get; init; }

    /// <summary>The lines the element draws across its text (CSS <c>text-decoration-line</c> and <c>text-decoration-color</c>,
    /// CSS Text Decoration 3 §2): not inherited, but passed on to all the text inside the element, in the element's own font.
    /// A line set on the document's text format is passed on to every element, the page header and footer included. Null when
    /// the element sets none.</summary>
    public ValueList<TextDecorationFormat>? Decorations { get; init; }
}

/// <summary>One line an element draws across its text (CSS Text Decoration 3 §2.1).</summary>
/// <param name="Line">The line.</param>
/// <param name="Color">Its colour (<c>text-decoration-color</c>), or null for the element's text colour (the initial
/// value, <c>currentcolor</c>).</param>
public sealed record TextDecorationFormat(TextDecorationLine Line, Color? Color = null);
