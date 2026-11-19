using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The size of one column of a tracks box (a table or a row of blocks). Four kinds, named after the CSS concepts
/// they follow (the sizing algorithm is described on the tracks layout):
/// <list type="bullet">
/// <item><see cref="Fixed"/>: an exact width, as a column width in the fixed table layout (CSS 2.1 §17.5.2.1);</item>
/// <item><see cref="Auto"/>: as wide as its content needs, as in the automatic table layout (CSS 2.1 §17.5.2.2);</item>
/// <item><see cref="Preferred"/>: a given width that content may widen and a narrow table may shrink, as a column
/// length in the automatic table layout (CSS Tables 3 §3.8.3, a constrained column);</item>
/// <item><see cref="Fill"/>: a share of the width left over, as CSS Grid's <c>fr</c> unit (CSS Grid 1 §7.2.4).</item>
/// </list>
/// A closed set.
/// </summary>
public abstract record TrackSize
{
    private TrackSize()
    {
    }

    /// <summary>A length is a fixed column width, so a column list can be written <c>Columns(2.5.Cm(), Fill(1))</c>.</summary>
    /// <param name="width">The width.</param>
    public static implicit operator TrackSize(Length width) => new Fixed(width);

    /// <summary>A column of an exact width.</summary>
    /// <param name="Width">The width of the column's cells, border box; zero or more.</param>
    public sealed record Fixed(Length Width) : TrackSize
    {
        /// <summary>Formats as the width, for example <c>70.866pt</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => Width.ToString();
    }

    /// <summary>
    /// A column of the automatic table layout with a given width, as Chromium lays out a <c>col</c> length in a table of
    /// <c>table-layout: auto</c>: the width is the column's preferred size (CSS Sizing 3 §3.1, "the preferred size
    /// properties", <c>width</c>), not a fixed one. Its maximum is the larger of the width and its cells' min-content
    /// (a cell's max-content does not widen it); its minimum is its cells' min-content, so a table too narrow for the
    /// widths shrinks it towards its content, after the auto columns are at their minimum. Width left over the
    /// columns' maximums reaches it only in a table without auto columns. Chromium:
    /// <c>third_party/blink/renderer/core/layout/table/table_layout_algorithm_types.cc</c>,
    /// <c>TableTypes::CreateColumn</c> and <c>TableTypes::Column::Encompass</c> (a "constrained" column). Unlike
    /// <see cref="Fixed"/>, content never overflows it.
    /// </summary>
    /// <param name="Width">The preferred width of the column's cells, border box; zero or more.</param>
    public sealed record Preferred(Length Width) : TrackSize
    {
        /// <summary>Formats as <c>preferred(70.866pt)</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"preferred({Width})";
    }

    /// <summary>A column sized to its content.</summary>
    public sealed record Auto : TrackSize
    {
        /// <summary>The one value.</summary>
        public static Auto Instance { get; } = new();

        /// <summary>Returns <c>auto</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "auto";
    }

    /// <summary>A column that takes a share of the leftover width, in proportion to its weight.</summary>
    /// <param name="Weight">The share; a positive number.</param>
    public sealed record Fill(double Weight) : TrackSize
    {
        /// <summary>Formats as <c>2.5fr</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"{InvariantNumber.Format(Weight)}fr";
    }
}
