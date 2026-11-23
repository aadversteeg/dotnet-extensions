using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The values of <see cref="BoxStyle.BorderModel"/>, named after <c>border-collapse</c> (CSS 2.1 §17.6): how the
/// borders of a table and its cells are drawn.
/// </summary>
public enum TableBorderModel
{
    /// <summary>The collapsing border model (CSS 2.1 §17.6.2): adjacent cells share one grid line, resolved from the
    /// borders of the cells, rows, row groups and the table that meet there, and the table paints the lines. The table
    /// has no padding. The model's default.</summary>
    Collapse,

    /// <summary>The separated borders model (CSS 2.1 §17.6.1, the CSS initial value of <c>border-collapse</c>): every
    /// cell paints its own border, the table paints its own border, padding and background as a block box does, and
    /// <see cref="BoxStyle.BorderSpacing"/> lies between the cells and between the outer cells and the table's padding
    /// edge.</summary>
    Separate,
}

/// <summary>
/// The room between the cells of a table in the separated borders model, named after <c>border-spacing</c> (CSS 2.1
/// §17.6.1): "the distance that separates adjoining cell borders", also between the outer cells and the table's padding
/// edge. Zero, the default, unless set; ignored in the collapsing border model.
/// </summary>
/// <param name="Horizontal">The room between two columns, and left and right of the outer columns.</param>
/// <param name="Vertical">The room between two rows, and above the first and below the last row.</param>
public readonly record struct BorderSpacing(Length Horizontal, Length Vertical)
{
    /// <summary>No room between the cells.</summary>
    public static BorderSpacing Zero => default;

    /// <inheritdoc/>
    public override string ToString() => $"{Horizontal} {Vertical}";
}
