namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The values of <see cref="BoxStyle.CaptionSide"/>, named after <c>caption-side</c> (CSS 2.1 §17.4.1): where a table
/// draws its caption.
/// </summary>
public enum CaptionSide
{
    /// <summary>Above the table's grid, on the page where the table starts (the initial value).</summary>
    Top,

    /// <summary>Below the table's grid, on the page where the table ends.</summary>
    Bottom,
}
