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

    /// <summary>The table display types drawn (CSS Display 3 §2.4); a caption and the rest refused (style resolution
    /// refuses <c>inline-table</c> already).</summary>
    public static string? Display(CssValue value) => HtmlBoxGenerator.PartOf(value) switch
    {
        TablePart.None => "this display type is not drawn in this version",
        TablePart.Caption => "a table caption is not drawn in this version",
        _ => null,
    };

    /// <summary>
    /// The width of a table part: the table's <c>auto</c>, <c>100%</c> or a length; a column's a length or a positive
    /// percentage (the table reads it for its layout, CSS 2.1 §17.5.2, and refuses a percentage where its layout does not
    /// draw it); a cell's a length (the table refuses it where its layout does not draw it); a column group's is not drawn; no effect on a row or a row group (CSS 2.1 §17.5.2 reads the widths
    /// of columns and cells only).
    /// </summary>
    public static string? Width(CssValue value, ElementFacts facts) => Part(facts) switch
    {
        _ when value is CssKeyword { Name: "auto" } => null,
        TablePart.Table => value is CssLength or CssPercentage { Value: 100 } ? null : "a table is drawn only at width: auto, 100% or a length in this version",
        TablePart.Column => value is CssLength or CssPercentage { Value: > 0 } ? null : "a column width other than a length or a positive percentage is not drawn in this version",
        TablePart.Cell => value is CssLength ? null : "a width on a cell other than a length is not drawn in this version",
        TablePart.ColumnGroup => "a width on a column group is not drawn in this version",
        _ => null,
    };

    /// <summary>Margins: internal table elements have none (CSS 2.1 §17.5: "Internal table elements do not have
    /// margins"); a table has no horizontal margin in this version (at width 100% it would pass its container, and auto
    /// margins centre a narrower table), but <c>auto</c> on a table of width 100%, where it is 0 (CSS 2.1 §10.3.3).</summary>
    public static string? Margin(string side, CssValue value, ElementFacts facts) => Part(facts) switch
    {
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

    /// <summary>A background: a cell's is drawn, and a table's with separated borders, which paints it under its whole
    /// border box as a block box does (CSS 2.1 §17.6.1; case <c>ts-paint</c>); a group's, a row's and a column's are not in
    /// this version, nor a table's with collapsed borders.</summary>
    public static string? Background(ElementFacts facts) => Part(facts) switch
    {
        TablePart.Cell or TablePart.None => null,
        TablePart.Table when IsSeparated(facts) => null,
        TablePart.Table => "a background on a table with collapsed borders is not drawn in this version: put it on the cells, or draw the table with border-collapse: separate",
        _ => "a background on a row group, a row or a column is not drawn in this version: put it on the cells",
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

    /// <summary>The alignment of a cell's content (CSS 2.1 §17.5.3): top, middle and bottom; no effect on the other parts.</summary>
    public static string? VerticalAlign(CssValue value, ElementFacts facts) =>
        Part(facts) != TablePart.Cell || value is CssKeyword { Name: "top" or "middle" or "bottom" } ? null : "only the top, middle and bottom alignment of a cell's content is drawn";

    /// <summary>
    /// The table properties. <c>border-collapse</c> on a table: <c>collapse</c> and <c>separate</c>, the initial value and
    /// HTML's (HTML Standard §15.3.8). <c>border-spacing</c> and <c>empty-cells</c> apply only in the separated borders
    /// model (CSS 2.1 §17.6.1, §17.6.1.1), so they have no effect with collapsed borders; with separated borders a
    /// spacing is drawn in whole CSS pixels, the spacings the cases measure (<c>ts-*</c>: 0, 2, 4, 8 and 12 px), and another
    /// is refused, as Chromium's rounding of it is not measured; <c>empty-cells</c> takes only <c>show</c> (the registry),
    /// which draws every cell. <c>caption-side</c> none without a caption (a caption is refused); <c>table-layout</c> is read
    /// for the columns.
    /// </summary>
    public static string? TableProperty(string name, CssValue value, ElementFacts facts) =>
        name == "border-spacing" && Part(facts) == TablePart.Table && IsSeparated(facts)
            && value is CssList { Items: var lengths } && lengths.Any(length => length is not CssLength { Value.Points: var points } || !IsWholePixels(points))
            ? "a border spacing other than a whole number of CSS pixels (a multiple of 0.75pt) is not drawn in this version"
            : null;

    /// <summary>True for a whole number of CSS pixels of 0.75 pt (CSS Values 4 §6.2), within a millionth of one.</summary>
    private static bool IsWholePixels(double points) => Math.Abs((points / 0.75) - Math.Round(points / 0.75)) < 1e-6;
}
