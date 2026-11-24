namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The part of a table a box is: the layout-internal display types of CSS (CSS Display 3 §2.4, <c>table-*</c>
/// keywords). They are not layout algorithms: the tracks algorithm lays out these boxes itself, as part of the box
/// whose columns they fill (<see cref="LayoutAlgorithmId.Tracks"/>).
/// </summary>
public enum TableDisplay
{
    /// <summary>Not a part of a table (the initial value).</summary>
    None,

    /// <summary>The group of header rows, repeated on every page the table continues on when it is at most a quarter of
    /// the page area, as Chromium (<c>table-header-group</c>, CSS 2.1 §17.2).</summary>
    HeaderGroup,

    /// <summary>A group of body rows (<c>table-row-group</c>).</summary>
    RowGroup,

    /// <summary>A row inside a row group (<c>table-row</c>); its children are the cells.</summary>
    Row,

    /// <summary>The table's caption (<c>table-caption</c>, CSS 2.1 §17.4): at most one, the table's first child, laid out
    /// by the table as a block as wide as the table, above its grid or below it (<see cref="BoxStyle.CaptionSide"/>).</summary>
    Caption,
}
