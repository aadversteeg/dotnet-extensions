using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The computed box properties of a <see cref="Box"/>. Every property starts at its CSS initial value
/// (<see cref="Initial"/>); a front-end sets what the author specified with <c>with { … }</c>.
/// </summary>
public sealed record BoxStyle
{
    /// <summary>The CSS initial values of every property.</summary>
    public static BoxStyle Initial { get; } = new();

    /// <summary>Space outside the border edge (CSS Box Model 3 §3). Not negative in this version.</summary>
    public Edges Margin { get; init; }

    /// <summary>Space between the border edge and the content (CSS Box Model 3 §4). Never negative.</summary>
    public Edges Padding { get; init; }

    /// <summary>The border, between margin and padding (CSS Backgrounds 3 §3). Solid lines only.</summary>
    public Border Border { get; init; }

    /// <summary>The background colour, or null for transparent (CSS Backgrounds 3 §2.2, initial value
    /// <c>transparent</c>). Painted under the border box (initial <c>background-clip: border-box</c>).</summary>
    public Color? Background { get; init; }

    /// <summary>Whether a page break before the box is forced or to be avoided (CSS Fragmentation 3 §3.1).</summary>
    public BreakBetween BreakBefore { get; init; }

    /// <summary>Whether a page break after the box is to be avoided (CSS Fragmentation 3 §3.1). A forced break after a
    /// box is expressed as <see cref="BreakBefore"/> on the next one.</summary>
    public BreakBetween BreakAfter { get; init; }

    /// <summary>Whether a page break inside the box is to be avoided (CSS Fragmentation 3 §3.2).</summary>
    public BreakInside BreakInside { get; init; }

    /// <summary>How the box's lines are aligned (CSS Text 3 §6.1, <c>text-align</c>); an inherited property, so the
    /// front-end computes it for every box. Used by inline layout.</summary>
    public TextAlign TextAlign { get; init; }

    /// <summary>
    /// The box's own computed font and line height, for a box with lines (a paragraph): every line box starts "with a
    /// zero-width inline box with the element's font and line height properties. We call that imaginary box a 'strut'"
    /// (CSS 2.1 §10.8.1), so no line is lower than the box's own line height, whatever its runs are. Null when the
    /// producer of the model gives none (a box without lines; a model built by hand in a test): then the lines have no
    /// strut. The code front-end sets it on every paragraph.
    /// </summary>
    public TextStyle? Text { get; init; }

    /// <summary>The fewest lines of the box's paragraph a page may hold before a break inside it (CSS Fragmentation 3
    /// §3.3, <c>orphans</c>: "the minimum number of line boxes in a block container that must be left in a fragment
    /// before a fragmentation break"); initial value 2, an inherited property, so the front-end computes it for every
    /// box; at least 1. Used by inline layout.</summary>
    public int Orphans { get; init; } = 2;

    /// <summary>The fewest lines of the box's paragraph a page may hold after a break inside it (CSS Fragmentation 3
    /// §3.3, <c>widows</c>: "the minimum number of line boxes of a block container that must be left in a fragment after
    /// a break"); initial value 2, inherited, at least 1. Used by inline layout.</summary>
    public int Widows { get; init; } = 2;

    /// <summary>Where a table cell's content sits in the cell's height (CSS 2.1 §17.5.3, <c>vertical-align</c> on
    /// table cells). Used by the tracks algorithm for the boxes in a row; ignored elsewhere.</summary>
    public VerticalAlign VerticalAlign { get; init; }

    /// <summary>The column sizes of a box laid out by <see cref="LayoutAlgorithmId.Tracks"/> (like CSS Grid's
    /// <c>grid-template-columns</c>); empty for every other box.</summary>
    public ValueList<TrackSize> Columns { get; init; } = [];

    /// <summary>How wide a box laid out by <see cref="LayoutAlgorithmId.Tracks"/> is when none of its columns is a
    /// <see cref="TrackSize.Fill"/> column (CSS 2.1 §17.5.2.2, CSS Sizing 3 §3.2): <see cref="BoxWidth.Auto"/>, the CSS
    /// initial value, unless set. With a fill column the box takes the width it is given (<see cref="BoxWidth.Stretch"/>
    /// and <see cref="BoxWidth.Auto"/> mean the same) or its <see cref="BoxWidth.Fixed"/> width. On a table cell, a
    /// <see cref="BoxWidth.Fixed"/> width its column takes (CSS 2.1 §17.5.2). Ignored for every other box.</summary>
    public BoxWidth Width { get; init; } = BoxWidth.Auto;

    /// <summary>The table part this box is (CSS Display 3 §2.4), or <see cref="TableDisplay.None"/>: a row group or a row
    /// of a box laid out by <see cref="LayoutAlgorithmId.Tracks"/>.</summary>
    public TableDisplay TableDisplay { get; init; }

    /// <summary>How many columns and rows a table cell covers (HTML <c>colspan</c> / <c>rowspan</c>, HTML §4.9.11; CSS 2.1 §17.5);
    /// <see cref="CellSpan.Single"/> for every other box.</summary>
    public CellSpan Span { get; init; } = CellSpan.Single;

    /// <summary>How a table's grid lines are drawn where the table breaks, between two rows or inside one, named after CSS
    /// <c>box-decoration-break</c> (CSS Fragmentation 3 §5.4); <see cref="BoxDecorationBreak.Slice"/>, the CSS initial
    /// value, unless set. Used by the tracks algorithm for a table; every other box is sliced.</summary>
    public BoxDecorationBreak DecorationBreak { get; init; }

    /// <summary>How the borders of a box laid out by <see cref="LayoutAlgorithmId.Tracks"/> are drawn, named after CSS
    /// <c>border-collapse</c> (CSS 2.1 §17.6); <see cref="TableBorderModel.Collapse"/> unless set (where CSS's initial
    /// value is <c>separate</c>). Ignored for every other box.</summary>
    public TableBorderModel BorderModel { get; init; }

    /// <summary>The room between the cells of a box laid out by <see cref="LayoutAlgorithmId.Tracks"/> in the separated
    /// borders model (CSS 2.1 §17.6.1, <c>border-spacing</c>); <see cref="BorderSpacing.Zero"/> unless set. Never
    /// negative. Ignored in the collapsing border model and for every other box.</summary>
    public BorderSpacing BorderSpacing { get; init; }

    /// <summary>Which page-margin box of its edge this box is (CSS Paged Media 3 §5: the start, centre and end boxes
    /// of the row, <c>@top-left</c>, <c>@top-center</c> and <c>@top-right</c> on the top edge), for a child of a box laid
    /// out by <see cref="LayoutAlgorithmId.MarginBoxes"/>; <see cref="MarginBoxPlace.None"/> for every other box.</summary>
    public MarginBoxPlace MarginBoxPlace { get; init; }
}

/// <summary>The values of <see cref="BoxStyle.MarginBoxPlace"/>: the three places of a row of page-margin boxes (CSS
/// Paged Media 3 §5), in horizontal left-to-right writing, the only one the engine has.</summary>
public enum MarginBoxPlace
{
    /// <summary>Not a page-margin box (the initial value).</summary>
    None,

    /// <summary>The box at the start of the row (<c>@top-left</c>, <c>@bottom-left</c>).</summary>
    Start,

    /// <summary>The box centred on the row (<c>@top-center</c>, <c>@bottom-center</c>).</summary>
    Center,

    /// <summary>The box at the end of the row (<c>@top-right</c>, <c>@bottom-right</c>).</summary>
    End,
}

/// <summary>
/// The values of <see cref="BoxStyle.DecorationBreak"/>, named after <c>box-decoration-break</c> (CSS Fragmentation 3
/// §5.4), for the grid lines of a table at a break between two rows or inside one.
/// </summary>
public enum BoxDecorationBreak
{
    /// <summary>The line is cut along its centre: its upper half ends the page and its lower half starts the next, as
    /// Chromium prints collapsed table borders. A row fits when it fits down to the centre of the line below it. A row
    /// cut inside itself gets no line at the cut.</summary>
    Slice,

    /// <summary>Each fragment of the table is closed by a whole line: the page ends with the whole line and the next
    /// page starts with it again, as word processors print tables; a row cut inside itself ends the page with the whole
    /// line below it and goes on under the whole line above it. For collapsed table borders this is the engine's
    /// rule, modelled on <c>clone</c> (which gives each fragment its own complete border); no specification defines it,
    /// and Chromium does not implement it for collapsed borders (it paints half the line on each side of a break).
    /// A row fits when it fits with the whole line below it.</summary>
    Clone,
}

/// <summary>
/// The values of <see cref="BoxStyle.Width"/>, named after the CSS <c>width</c> values they follow: for a table, how wide it
/// is when its columns do not fill the width it is given; for a table cell, the width its column takes from it. A closed
/// set.
/// </summary>
public abstract record BoxWidth
{
    private BoxWidth()
    {
    }

    /// <summary>As wide as its columns need, never wider than the width it is given (<c>width: auto</c>; CSS 2.1
    /// §17.5.2.2: "use max(MAX, CAPMIN)" when that is less than the containing block). Width left over the columns'
    /// maximum widths stays empty. The initial value; on a cell, no width.</summary>
    public static BoxWidth Auto { get; } = new Keyword("auto");

    /// <summary>As wide as the width it is given (<c>width: stretch</c>, CSS Sizing 3 §3.2: the margin box matches the
    /// containing block; for a table without horizontal margins this is <c>width: 100%</c>). The width left over the
    /// columns' maximum widths is shared over the auto columns in proportion to their maximum widths, or over the fixed
    /// columns in proportion to their widths when the table has no auto column (CSS Tables 3 §3.9.3.2, "Distributing
    /// excess width to columns": "increased in proportion to max-content width", as Chromium implements it). Only for a
    /// table.</summary>
    public static BoxWidth Stretch { get; } = new Keyword("stretch");

    /// <summary>
    /// A given width (CSS 2.1 §17.5.2, <c>width: &lt;length&gt;</c>). On a table, its border box (HTML's user agent sheet
    /// gives a table <c>box-sizing: border-box</c>): the columns share it as under <see cref="Stretch"/>, and a table
    /// whose columns need more is as wide as they need (cases <c>twd-table-wide</c>, <c>twd-table-narrow</c>,
    /// <c>twd-sum</c>). On a table cell, its content box: the cell's padding and border are added, and its column takes
    /// it as a preferred width in the automatic table layout and as an exact width in the fixed one (the tracks layout says
    /// which cell counts; cases <c>twd-cell-*</c>).
    /// </summary>
    /// <param name="Width">The width; zero or more.</param>
    public sealed record Fixed(Length Width) : BoxWidth
    {
        /// <summary>Formats as the width, for example <c>150pt</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => Width.ToString();
    }

    private sealed record Keyword(string Name) : BoxWidth
    {
        public override string ToString() => Name;
    }
}

/// <summary>The values of <c>break-before</c> and <c>break-after</c> (CSS Fragmentation 3 §3.1) that this version
/// implements.</summary>
public enum BreakBetween
{
    /// <summary>Neither force nor forbid a break (the initial value).</summary>
    Auto,

    /// <summary>Avoid a break here (<c>avoid</c>): used only when no better break exists (§4.4 rules, by appeal).</summary>
    Avoid,

    /// <summary>Always force a page break here (<c>page</c>). Only for <c>break-before</c> in this model.</summary>
    Page,
}

/// <summary>The values of <c>break-inside</c> (CSS Fragmentation 3 §3.2) that this version implements.</summary>
public enum BreakInside
{
    /// <summary>Breaks inside the box are allowed (the initial value).</summary>
    Auto,

    /// <summary>Avoid breaking inside the box: keep it together on one page when it fits on one.</summary>
    Avoid,
}

/// <summary>The values of <c>vertical-align</c> for table cells (CSS 2.1 §17.5.3) that this version implements.</summary>
public enum VerticalAlign
{
    /// <summary>The content starts at the top of the cell (<c>top</c>). The default in this model; CSS's initial value
    /// <c>baseline</c> is not implemented.</summary>
    Top,

    /// <summary>The content is centred in the cell's height (<c>middle</c>).</summary>
    Middle,

    /// <summary>The content ends at the bottom of the cell (<c>bottom</c>).</summary>
    Bottom,
}
