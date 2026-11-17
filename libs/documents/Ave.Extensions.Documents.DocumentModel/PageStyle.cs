using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The page box (CSS Paged Media 3 §3): the size of the sheet, the margins around the page area, and what is
/// repeated on every page (the page template). The page area is the size minus the margins; the header and footer
/// areas are taken from its top and bottom, and the document's root box is laid out in what is left.
/// </summary>
/// <param name="Width">The page width (the first value of the CSS <c>size</c> property).</param>
/// <param name="Height">The page height.</param>
/// <param name="Margins">The page margins.</param>
public sealed record PageStyle(Length Width, Length Height, Edges Margins)
{
    /// <summary>The template of every page, unless <see cref="FirstPage"/> overrides it for page 1.</summary>
    public PageTemplate Template { get; init; } = PageTemplate.Empty;

    /// <summary>The template of the first page (CSS Paged Media 3 §4.2.2, the <c>:first</c> page pseudo-class), or null
    /// when page 1 uses <see cref="Template"/>.</summary>
    public PageTemplate? FirstPage { get; init; }

    /// <summary>The width of the page area: width minus left and right margin.</summary>
    public Length AreaWidth => Width - Margins.Horizontal;

    /// <summary>The height of the page area: height minus top and bottom margin.</summary>
    public Length AreaHeight => Height - Margins.Vertical;

    /// <summary>The template of a page: <see cref="FirstPage"/> for page 1 when set, else <see cref="Template"/>.</summary>
    /// <param name="pageNumber">The page number, from 1.</param>
    /// <returns>The template.</returns>
    public PageTemplate TemplateFor(int pageNumber) => pageNumber == 1 && FirstPage is { } first ? first : Template;
}

/// <summary>
/// What a page shows besides the document's content: a header area at the top of the page area and a footer area
/// at its bottom. Modelled on the page-margin boxes of CSS Paged Media 3 §5 (content laid out in its own box on every
/// page, with the same layout algorithms as the body), with one deliberate difference, as in RDL and Typst: the areas
/// are taken out of the page area, not placed in the page margins, so their height can follow their content.
/// </summary>
/// <param name="Header">The header area, or null for none.</param>
/// <param name="Footer">The footer area, or null for none.</param>
public sealed record PageTemplate(PageArea? Header, PageArea? Footer)
{
    /// <summary>No header and no footer.</summary>
    public static PageTemplate Empty { get; } = new(null, null);
}

/// <summary>
/// The content of a header or footer area and its height.
/// </summary>
/// <param name="Content">The area's content: a box laid out as the root of its own block formatting context, as wide
/// as the page area. It may contain page counters.</param>
/// <param name="Height">A fixed height for the area, or null to take the height of the content (including its
/// margins). Content taller than a fixed height is an error, never clipped.</param>
public sealed record PageArea(Box Content, Length? Height);
