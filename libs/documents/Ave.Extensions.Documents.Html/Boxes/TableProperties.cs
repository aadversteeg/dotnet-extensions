using System;
using System.Linq;
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

    /// <summary>The table display types drawn (CSS Display 3 §2.4), the caption among them; the rest refused (style
    /// resolution refuses <c>inline-table</c> already).</summary>
    public static string? Display(CssValue value) => HtmlBoxGenerator.PartOf(value) switch
    {
        TablePart.None => "this display type is not drawn in this version",
        _ => null,
    };

    /// <summary>
    /// The width of a table part: the table's <c>auto</c>, a length or a positive percentage; a column's, a column group's
    /// and a cell's a length or a positive percentage (the table reads them for its layout, CSS 2.1 §17.5.2, and refuses
    /// one where its layout does not draw it); no effect on a row or a row group (CSS 2.1 §17.5.2 reads the widths of
    /// columns and cells only).
    /// </summary>
    public static string? Width(CssValue value, ElementFacts facts) => Part(facts) switch
    {
        _ when value is CssKeyword { Name: "auto" } => null,
        TablePart.Table => value is CssLength or CssPercentage { Value: > 0 } ? null : "a table is drawn only at width: auto, a length or a positive percentage in this version",
        TablePart.Column => value is CssLength or CssPercentage { Value: > 0 } ? null : "a column width other than a length or a positive percentage is not drawn in this version",
        TablePart.Cell => value is CssLength or CssPercentage { Value: > 0 } ? null : "a width on a cell other than a length or a positive percentage is not drawn in this version",
        TablePart.ColumnGroup => value is CssLength or CssPercentage { Value: > 0 } ? null : "a column group width other than a length or a positive percentage is not drawn in this version",
        TablePart.Caption => "a width on a table caption is not drawn in this version: a caption is as wide as its table",
        _ => null,
    };

    /// <summary>
    /// The height of a table part: on a table, a row and a cell a length, which is a least height (cases <c>tg-row</c>,
    /// <c>tg-cell</c>, <c>tg-table</c>; the table draws it, <see cref="HtmlBoxGenerator"/>, tables); no effect on a column
    /// or a column group, to which <c>height</c> does not apply. Refused: a percentage (case <c>tg-percent</c> prints it as
    /// <c>auto</c> in a page, but not measured in a cell with a height), and a height on a row group or a caption.
    /// </summary>
    public static string? Height(CssValue value, ElementFacts facts) => Part(facts) switch
    {
        _ when value is CssKeyword { Name: "auto" } => null,
        TablePart.Column or TablePart.ColumnGroup => null,
        TablePart.Table or TablePart.Row or TablePart.Cell => value switch
        {
            CssLength { Value.Points: >= 0 } => null,
            CssPercentage => "a percentage height on a table, a row or a cell is not drawn in this version: give it a length",
            _ => "a height other than a length on a table, a row or a cell is not drawn in this version",
        },
        TablePart.Caption => "a height on a table caption is not drawn in this version",
        _ => "a height on a row group is not drawn in this version: give the height to its rows",
    };

    /// <summary>Margins: internal table elements have none (CSS 2.1 §17.5: "Internal table elements do not have
    /// margins"); a table has no horizontal margin in this version (at width 100% it would pass its container, and auto
    /// margins centre a narrower table), but <c>auto</c> on a table of width 100%, where it is 0 (CSS 2.1 §10.3.3). A
    /// caption's margins are a block's, inside the table's outer box (case <c>tc-box</c>).</summary>
    public static string? Margin(string side, CssValue value, ElementFacts facts) => Part(facts) switch
    {
        TablePart.Caption => value switch
        {
            CssKeyword { Name: "auto" } => null,
            CssLength { Value.Points: >= 0 } => null,
            CssLength => "negative margins are not drawn in this version",
            _ => "percentage and calculated margins are not drawn in this version",
        },
        TablePart.Table when side is "left" or "right" => value is CssLength { Value.Points: 0 }
            || (value is CssKeyword { Name: "auto" } && facts.Style[PropertyRegistry.Find("width")!] is CssPercentage { Value: 100 })
            ? null
            : "a horizontal margin on a table is not drawn in this version",
        TablePart.Table => value switch
        {
            CssKeyword { Name: "auto" } => null,
            CssLength { Value.Points: >= 0 } => null,
            CssLength => "negative margins are not drawn in this version",
            _ => "percentage and calculated margins are not drawn in this version",
        },
        _ => null,
    };

    /// <summary>True when the element's <c>border-collapse</c> is <c>separate</c>: on a table, its border model; on a
    /// table part, its table's, as the property is inherited (CSS 2.1 §17.6) and never set on a part in the cases.</summary>
    public static bool IsSeparated(ElementFacts facts) => facts.Style[PropertyRegistry.Find("border-collapse")!] is not CssKeyword { Name: "collapse" };

    /// <summary>A background: a cell's and a caption's are drawn (case <c>tc-box</c>); a row group's, a row's, a column's and a
    /// column group's, which the cells take (<c>HtmlBoxGenerator</c>, Backgrounds; cases <c>tk-*</c>); and a table's, which
    /// paints it under its whole border box as a block box does: with separated borders spacing included (CSS 2.1 §17.6.1;
    /// case <c>ts-paint</c>), with collapsed borders out to the outer edge of its outer lines, under the cells' and rows'
    /// backgrounds and the lines, never under a caption (cases <c>tk-table-collapsed</c>, <c>tk-table-caption</c>).</summary>
    public static string? Background(ElementFacts facts) => Part(facts) switch
    {
        _ => null,
    };

    /// <summary>A border width: the columns' borders are not in the model's collapsing border model (CSS 2.1 §17.6.2.1
    /// lets them take part in the conflicts); in the separated borders model rows, columns and their groups have no
    /// borders (CSS 2.1 §17.6.1: "Rows, columns, row groups, and column groups cannot have borders"), which is not
    /// measured, so a border on a row or a row group of such a table is refused.</summary>
    public static string? BorderWidth(CssValue value, ElementFacts facts) => value is CssLength { Value.Points: > 0 }
        ? Part(facts) switch
        {
            TablePart.Column or TablePart.ColumnGroup => "a border on a column or a column group is not drawn in this version",
            TablePart.Row or TablePart.RowGroup when IsSeparated(facts) => "a border on a row or a row group of a table with separated borders is not drawn in this version: put it on the cells",
            _ => null,
        }
        : null;

    /// <summary>The alignment of a cell's content (CSS 2.1 §17.5.3): top, middle and bottom, and baseline, which is drawn as
    /// top (<c>HtmlBoxGenerator</c>, VerticalAlignOf); no effect on the other parts.</summary>
    public static string? VerticalAlign(CssValue value, ElementFacts facts) =>
        Part(facts) != TablePart.Cell || value is CssKeyword { Name: "top" or "middle" or "bottom" or "baseline" } ? null : "only the top, middle, bottom and baseline alignment of a cell's content is drawn";

    /// <summary>
    /// The table properties. <c>border-collapse</c> on a table: <c>collapse</c> and <c>separate</c>, the initial value and
    /// HTML's (HTML Standard §15.3.8). <c>border-spacing</c> and <c>empty-cells</c> apply only in the separated borders
    /// model (CSS 2.1 §17.6.1, §17.6.1.1), so they have no effect with collapsed borders; with separated borders a
    /// spacing is drawn in whole CSS pixels, the spacings the cases measure (<c>ts-*</c>: 0, 2, 4, 8 and 12 px), and another
    /// is refused, as Chromium's rounding of it is not measured; <c>empty-cells</c> takes only <c>show</c> (the registry),
    /// which draws every cell. <c>caption-side</c> takes <c>top</c> and <c>bottom</c> (the registry), read on the caption;
    /// <c>table-layout</c> is read for the columns.
    /// </summary>
    public static string? TableProperty(string name, CssValue value, ElementFacts facts) =>
        name == "border-spacing" && Part(facts) == TablePart.Table && IsSeparated(facts)
            && value is CssList { Items: var lengths } && lengths.Any(length => length is not CssLength { Value.Points: var points } || !IsWholePixels(points))
            ? "a border spacing other than a whole number of CSS pixels (a multiple of 0.75pt) is not drawn in this version"
            : null;

    /// <summary>True for a whole number of CSS pixels of 0.75 pt (CSS Values 4 §6.2), within a millionth of one.</summary>
    private static bool IsWholePixels(double points) => Math.Abs((points / 0.75) - Math.Round(points / 0.75)) < 1e-6;
}
