using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The size of one column of a tracks box (a table or a row of blocks). Three kinds, named after the CSS concepts
/// they follow (the sizing algorithm is described on the tracks layout):
/// <list type="bullet">
/// <item><see cref="Fixed"/>: an exact width, as a column width in the fixed table layout (CSS 2.1 §17.5.2.1);</item>
/// <item><see cref="Auto"/>: as wide as its content needs, as in the automatic table layout (CSS 2.1 §17.5.2.2);</item>
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
