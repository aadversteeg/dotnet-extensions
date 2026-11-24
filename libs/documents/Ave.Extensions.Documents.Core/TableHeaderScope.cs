namespace Ave.Extensions.Documents.Core;

/// <summary>
/// What a header cell of a table heads: the cells below it in its column, the cells beside it in its row, or both. Tagged
/// PDF writes it as the <c>/Scope</c> of the cell's table attributes (ISO 32000-1 §14.8.5.7, Table 349), which a reader
/// uses to say each data cell's headers; PDF/UA asks it of every <c>TH</c> (veraPDF rule 7.5-1). PDF has no scope for a
/// group of rows or columns.
/// </summary>
public enum TableHeaderScope
{
    /// <summary>The cell heads its column (<c>/Scope /Column</c>); the default, as in a header row.</summary>
    Column,

    /// <summary>The cell heads its row (<c>/Scope /Row</c>).</summary>
    Row,

    /// <summary>The cell heads both its column and its row (<c>/Scope /Both</c>).</summary>
    Both,
}
