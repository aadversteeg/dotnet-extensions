using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// The rows of <see cref="BoxProperties"/> where a table part draws other values than a block box
/// (<see cref="HtmlBoxGenerator"/>, tables): what each part draws of its display, width, margins, borders, background,
/// vertical alignment and the table properties; a null result draws the value, any other is why it is refused.
/// </summary>
internal static class TableProperties
{
    /// <summary>The table part of the element.</summary>
    public static TablePart Part(ElementFacts facts) => HtmlBoxGenerator.PartOf(facts.Style[PropertyRegistry.Display]);

    /// <summary>True for a table part (a table, a group, a row, a cell, a column or a column group).</summary>
    public static bool IsPart(ElementFacts facts) => Part(facts) != TablePart.None;

    /// <summary>The table display types drawn (CSS Display 3 §2.4); a caption and the rest refused (style resolution
    /// refuses <c>inline-table</c> already).</summary>
    public static string? Display(CssValue value) => HtmlBoxGenerator.PartOf(value) switch
    {
        TablePart.None => "this display type is not drawn in this version",
        TablePart.Caption => "a table caption is not drawn in this version",
        _ => null,
    };

    /// <summary>
    /// The width of a table part: the table's is 100%, the model's only table width; a column's a length or a
    /// percentage, a cell's a length (the table then checks what its layout makes of them, CSS 2.1 §17.5.2); a column
    /// group's auto; no effect on a row or a row group (CSS 2.1 §17.5.2 reads the widths of columns and cells only).
    /// </summary>
    public static string? Width(CssValue value, ElementFacts facts) => Part(facts) switch
    {
        _ when value is CssKeyword { Name: "auto" } && Part(facts) != TablePart.Table => null,
        TablePart.Table => value is CssPercentage { Value: 100 } ? null : "a table is drawn only at width: 100% in this version",
        TablePart.Column => value is CssLength or CssPercentage ? null : "a calculated column width is not drawn in this version",
        TablePart.Cell => value is CssLength ? null : "a cell width other than a length is not drawn in this version",
        TablePart.ColumnGroup => "a width on a column group is not drawn in this version",
        _ => null,
    };

    /// <summary>Margins: internal table elements have none (CSS 2.1 §17.5: "Internal table elements do not have
    /// margins"); a table of width 100% has no horizontal margin in this version (it would pass its container).</summary>
    public static string? Margin(string side, CssValue value, ElementFacts facts) => Part(facts) switch
    {
        TablePart.Table when side is "left" or "right" => value is CssKeyword { Name: "auto" } || value is CssLength { Value.Points: 0 } ? null : "a horizontal margin on a table is not drawn in this version",
        TablePart.Table => value switch
        {
            CssKeyword { Name: "auto" } => null,
            CssLength { Value.Points: >= 0 } => null,
            CssLength => "negative margins are not drawn in this version",
            _ => "percentage and calculated margins are not drawn in this version",
        },
        _ => null,
    };

    /// <summary>A background: a cell's is drawn; the table's, a group's, a row's and a column's are not in this version.</summary>
    public static string? Background(ElementFacts facts) =>
        Part(facts) is TablePart.Cell or TablePart.None ? null : "a background on a table, a row group, a row or a column is not drawn in this version";

    /// <summary>A border width: the columns' borders are not in the model's collapsing border model (CSS 2.1 §17.6.2.1
    /// lets them take part in the conflicts).</summary>
    public static string? BorderWidth(CssValue value, ElementFacts facts) =>
        Part(facts) is TablePart.Column or TablePart.ColumnGroup && value is CssLength { Value.Points: > 0 } ? "a border on a column or a column group is not drawn in this version" : null;

    /// <summary>The alignment of a cell's content (CSS 2.1 §17.5.3): top, middle and bottom; no effect on the other parts.</summary>
    public static string? VerticalAlign(CssValue value, ElementFacts facts) =>
        Part(facts) != TablePart.Cell || value is CssKeyword { Name: "top" or "middle" or "bottom" } ? null : "only the top, middle and bottom alignment of a cell's content is drawn";

    /// <summary>
    /// The table properties. <c>border-collapse</c> on a table: only <c>collapse</c>; <c>separate</c> is the initial
    /// value, and HTML's (HTML Standard §15.3.8). <c>border-spacing</c> and <c>empty-cells</c> apply only in the
    /// separated borders model (CSS 2.1 §17.6.1, §17.6.1.1), so they have no effect with collapsed borders;
    /// <c>caption-side</c> none without a caption (a caption is refused); <c>table-layout</c> is read for the columns.
    /// </summary>
    public static string? TableProperty(string name, CssValue value, ElementFacts facts) =>
        name == "border-collapse" && Part(facts) == TablePart.Table && value is not CssKeyword { Name: "collapse" }
            ? "separated borders (border-collapse: separate, the initial value and HTML's) are not drawn in this version: a table is drawn with border-collapse: collapse"
            : null;
}
