using Ave.Documents.Core;

namespace Ave.Documents.DocumentModel;

/// <summary>
/// The page box (CSS Paged Media 3 §3): the size of the sheet and the margins around the page area. The page
/// area, where the root box is laid out, is the size minus the margins. Stage 0 has one page style for the
/// whole document; named pages and first/left/right selectors come with page templates.
/// </summary>
/// <param name="Width">The page width (the first value of the CSS <c>size</c> property).</param>
/// <param name="Height">The page height.</param>
/// <param name="Margins">The page margins.</param>
public sealed record PageStyle(Length Width, Length Height, Edges Margins)
{
    /// <summary>The width of the page area: width minus left and right margin.</summary>
    public Length AreaWidth => Width - Margins.Horizontal;

    /// <summary>The height of the page area: height minus top and bottom margin.</summary>
    public Length AreaHeight => Height - Margins.Vertical;
}
