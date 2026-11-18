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
