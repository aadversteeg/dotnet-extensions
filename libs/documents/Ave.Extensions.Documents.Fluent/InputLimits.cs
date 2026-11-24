using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>The limits the code front-end sets on a document, beyond the rules of CSS.</summary>
public static class InputLimits
{
    /// <summary>
    /// How deep elements may nest: a block in a block in a table cell … at most 64 levels, the document's own list
    /// counting as level 1. Each level costs the layout one call frame, so without a limit a deep enough document ends
    /// the process with a stack overflow; 64 is far beyond any report or letter. At the limit, measured on .NET 10 x64
    /// (2026-11-18): 64 nested blocks take a quarter of a second and need 304 KB of stack; 63 nested one-cell tables that
    /// break across a page take 3 to 5 seconds, allocate 1.5 to 1.8 GB (each table measures every table inside it, so the
    /// work grows with the square of the depth) and need 672 KB of stack. Deeper nesting is
    /// <c>Documents.Input.Invalid.TooDeep</c>. The same value as <see cref="ModelLimits.MaxNestingDepth"/>, which layout
    /// holds every model to.
    /// </summary>
    public const int MaxNestingDepth = ModelLimits.MaxNestingDepth;

    /// <summary>The most items one list may have: 10 000 (<see cref="ModelLimits.MaxListItems"/>, which layout holds every
    /// model to). More is <c>Documents.Input.Invalid.List</c>.</summary>
    public const int MaxListItems = ModelLimits.MaxListItems;

    /// <summary>
    /// The longest length the front-end accepts, in every profile: 14 400 pt (200 inches), the largest page side
    /// ISO 32000-1 names (Annex C, the limit on page size). It holds for a page side, a page margin, a margin, a padding,
    /// a border width, a font size, a fixed column width, an image's width and height and a header or footer height.
    /// Nothing on a page can usefully be longer than the largest page, so a longer value is an author's error (a
    /// forgotten unit, a number in the wrong field) and is refused with the property's code rather than laid out off
    /// the page. The limit also keeps every coordinate the layout computes from these lengths far inside the number
    /// range a PDF can hold. The same value as <see cref="ModelLimits.MaxLength"/>, which layout holds every model to.
    /// </summary>
    public static Length MaxLength => ModelLimits.MaxLength;
}
