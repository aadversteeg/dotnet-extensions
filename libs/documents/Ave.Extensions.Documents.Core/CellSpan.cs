namespace Ave.Extensions.Documents.Core;

/// <summary>
/// How many columns and rows a table cell covers. Shared vocabulary: layout sizes the cell from it (document model
/// <c>BoxStyle.Span</c>), and tagged PDF writes it as the cell's ColSpan and RowSpan attributes (ISO 32000-1
/// §14.8.5.7, Table 349), so assistive technology knows which header covers which data cells.
/// </summary>
/// <param name="Columns">Columns, at least 1.</param>
/// <param name="Rows">Rows, at least 1; a row span ends at the end of its row group.</param>
public readonly record struct CellSpan(int Columns, int Rows)
{
    /// <summary>One column, one row.</summary>
    public static CellSpan Single => new(1, 1);

    /// <summary>Formats as <c>2x1</c> (columns x rows).</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Columns}x{Rows}";
}
