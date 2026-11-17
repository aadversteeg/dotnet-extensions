using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Elements side by side, each in its own column: a one-row table without lines, laid out by the same tracks
/// algorithm as <see cref="TableElement"/>. Made by <see cref="Elements.Row"/>. Without <see cref="Columns"/> every
/// item gets an equal share of the width.
/// </summary>
/// <param name="Items">The elements, left to right.</param>
/// <param name="ColumnSizes">One size per item, or empty for equal shares.</param>
/// <param name="VerticalAlignment">Where items shorter than the row sit; null for the top.</param>
/// <param name="Format">What the modifiers set on the row.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record RowElement(
    ValueList<Element> Items, ValueList<TrackSize> ColumnSizes, VerticalAlign? VerticalAlignment, Style Format, Style Style, SourceLocation Source)
    : Element(Source), IBoxFormatted<RowElement>
{
    /// <summary>Sets the size of each item's column; there must be as many sizes as items.</summary>
    /// <param name="sizes">The sizes: fixed lengths, <see cref="Elements.Auto"/> or <see cref="Elements.Fill"/>.</param>
    /// <returns>A copy with the column sizes.</returns>
    public RowElement Columns(params TrackSize[] sizes) => this with { ColumnSizes = [.. sizes] };

    /// <summary>Places every item at the top, middle or bottom of the row's height.</summary>
    /// <param name="align">The alignment.</param>
    /// <returns>A copy with the alignment.</returns>
    public RowElement VerticalAlign(VerticalAlign align) => this with { VerticalAlignment = align };

    /// <summary>Applies a shared style.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public RowElement With(Style style) => this with { Style = style };

    /// <summary>The column sizes the row uses: the ones set, or one equal fill share per item.</summary>
    internal ValueList<TrackSize> EffectiveColumns =>
        ColumnSizes.Count > 0 ? ColumnSizes : [.. Items.Select(_ => (TrackSize)new TrackSize.Fill(1))];

    /// <inheritdoc/>
    RowElement IFormatted<RowElement>.WithFormat(Style format) => this with { Format = format };
}
