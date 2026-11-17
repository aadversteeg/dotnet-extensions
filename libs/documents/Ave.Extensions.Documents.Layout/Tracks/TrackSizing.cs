using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Column widths of a tracks box from its column definitions and its cells' intrinsic widths (track sizing: item
/// contributions in, track sizes out). Pure functions of numbers; no layout.
/// </summary>
/// <remarks>
/// <para>Which rule each column kind follows:</para>
/// <list type="bullet">
/// <item><b>Fixed</b>: the width is used as it is, as a column width in the FIXED table layout (CSS 2.1
/// §17.5.2.1): content never changes it; content wider than the column overflows the cell.</item>
/// <item><b>Auto</b>: the AUTOMATIC table layout (CSS 2.1 §17.5.2.2): each column has a minimum (the widest
/// min-content of its cells, "MCW") and a maximum (the widest max-content); a cell spanning several columns widens
/// them so that together they hold it, "by approximately the same amount" — here exactly the same amount, over the
/// spanned columns that are not fixed. The algorithm is not normative in CSS 2.1; the distribution of the space
/// between minimum and maximum follows CSS Tables 3 (in proportion to max − min), which Chromium also does.</item>
/// <item><b>Fill</b>: CSS Grid's flexible length (<c>fr</c>, CSS Grid 1 §7.2.4: a share "of the leftover space"),
/// with the minimum Grid gives <c>1fr</c> (<c>minmax(auto, 1fr)</c>: never narrower than its min-content) and the
/// "Find the Size of an fr" loop of §11.7.1, which drops a fill column whose minimum is larger than its share and
/// shares the rest again.</item>
/// </list>
/// <para>The order is Grid's (§11.4–§11.7): fixed and min-content widths first; then auto columns grow towards their
/// maximum; then fill columns share what is left. Without fill columns a table is only as wide as its columns need
/// (CSS 2.1 §17.5.2.2: with <c>width: auto</c> "use max(MAX, CAPMIN)" when that is less than the containing block),
/// so leftover space is not spread over auto columns.</para>
/// </remarks>
internal static class TrackSizing
{
    /// <summary>What one cell asks of the columns it spans: its min-content and max-content widths, insets included.</summary>
    /// <param name="Column">The first column.</param>
    /// <param name="Span">How many columns.</param>
    /// <param name="Widths">The cell's intrinsic widths.</param>
    public sealed record Contribution(int Column, int Span, IntrinsicWidths Widths);

    /// <summary>The minimum and maximum width of every column (step 1 and 2 of CSS 2.1 §17.5.2.2).</summary>
    /// <param name="columns">The column definitions.</param>
    /// <param name="cells">The cells' contributions.</param>
    /// <returns>Per column: its min and max width.</returns>
    public static ImmutableArray<IntrinsicWidths> ColumnRanges(ValueList<TrackSize> columns, IEnumerable<Contribution> cells)
    {
        var all = cells.ToImmutableArray();
        var min = columns.Select(column => column is TrackSize.Fixed fixedWidth ? fixedWidth.Width.Points : 0).ToArray();
        var max = min.ToArray();

        // Step 1: cells in one column set that column's minimum and maximum.
        foreach (var cell in all.Where(cell => cell.Span == 1 && columns[cell.Column] is not TrackSize.Fixed))
        {
            min[cell.Column] = Math.Max(min[cell.Column], cell.Widths.MinContent);
            max[cell.Column] = Math.Max(max[cell.Column], cell.Widths.MaxContent);
        }

        // Step 2: spanning cells, narrowest span first, widen the spanned columns that are not fixed by equal amounts.
        foreach (var cell in all.Where(cell => cell.Span > 1).OrderBy(cell => cell.Span))
        {
            var spanned = Enumerable.Range(cell.Column, cell.Span).ToImmutableArray();
            var flexible = spanned.Where(index => columns[index] is not TrackSize.Fixed).ToImmutableArray();
            Widen(min, spanned, flexible, cell.Widths.MinContent);
            Widen(max, spanned, flexible, cell.Widths.MaxContent);
        }

        return [.. min.Zip(max, (low, high) => new IntrinsicWidths(low, Math.Max(low, high)))];
    }

    /// <summary>
    /// Resolves the column widths for the available width. Fails with the width needed when the fixed widths and
    /// the minimum widths of the other columns do not fit: overflowing the page is never silent.
    /// </summary>
    /// <param name="columns">The column definitions.</param>
    /// <param name="ranges">The columns' ranges from <see cref="ColumnRanges"/>.</param>
    /// <param name="available">The width the columns may take together.</param>
    /// <returns>The widths, or the width needed.</returns>
    public static (ImmutableArray<double> Widths, double? Needed) Resolve(ValueList<TrackSize> columns, ImmutableArray<IntrinsicWidths> ranges, double available)
    {
        var widths = ranges.Select(range => range.MinContent).ToArray();
        var needed = widths.Sum();
        if (!Fitting.Fits(needed, available))
        {
            return ([], needed);
        }

        GrowAutoColumns(columns, ranges, widths, available - needed);
        ShareAmongFillColumns(columns, ranges, widths, available);
        return ([.. widths], null);
    }

    /// <summary>
    /// Auto columns grow from their minimum towards their maximum. When the space does not reach every maximum, each
    /// column gets the same fraction of its own range (max − min), so a column whose content wraps a lot grows more
    /// than one that is nearly at its maximum (CSS Tables 3, distributing width between min and max).
    /// </summary>
    private static void GrowAutoColumns(ValueList<TrackSize> columns, ImmutableArray<IntrinsicWidths> ranges, double[] widths, double free)
    {
        var autos = Enumerable.Range(0, columns.Count).Where(index => columns[index] is TrackSize.Auto).ToImmutableArray();
        var room = autos.Sum(index => ranges[index].MaxContent - ranges[index].MinContent);
        var fraction = room <= 0 ? 0 : Math.Min(1, free / room);
        foreach (var index in autos)
        {
            widths[index] = ranges[index].MinContent + fraction * (ranges[index].MaxContent - ranges[index].MinContent);
        }
    }

    /// <summary>
    /// CSS Grid 1 §11.7.1, "Find the Size of an fr": the leftover space (available minus every other column) divided
    /// by the sum of the weights — at least 1, as the section says for weights that add up to less — is the size of
    /// one fr. A fill column whose minimum is larger than its weight × fr keeps its minimum, and the loop runs again
    /// without it ("restart this algorithm treating all such tracks as inflexible").
    /// </summary>
    private static void ShareAmongFillColumns(ValueList<TrackSize> columns, ImmutableArray<IntrinsicWidths> ranges, double[] widths, double available)
    {
        var flexible = Enumerable.Range(0, columns.Count).Where(index => columns[index] is TrackSize.Fill).ToHashSet();
        while (flexible.Count > 0)
        {
            var leftover = available - Enumerable.Range(0, columns.Count).Where(index => !flexible.Contains(index)).Sum(index => widths[index]);
            var weights = flexible.Sum(index => ((TrackSize.Fill)columns[index]).Weight);
            var fr = Math.Max(0, leftover) / Math.Max(1, weights);
            var inflexible = flexible.Where(index => ranges[index].MinContent > ((TrackSize.Fill)columns[index]).Weight * fr).ToImmutableArray();
            if (inflexible.IsEmpty)
            {
                foreach (var index in flexible)
                {
                    widths[index] = ((TrackSize.Fill)columns[index]).Weight * fr;
                }

                return;
            }

            foreach (var index in inflexible)
            {
                widths[index] = ranges[index].MinContent;
                flexible.Remove(index);
            }
        }
    }

    /// <summary>Widens the flexible columns of a span by equal amounts until the span is as wide as the cell. When every
    /// spanned column is fixed, the content overflows instead (fixed means fixed).</summary>
    private static void Widen(double[] sizes, ImmutableArray<int> spanned, ImmutableArray<int> flexible, double target)
    {
        var missing = target - spanned.Sum(index => sizes[index]);
        if (missing <= 0 || flexible.IsEmpty)
        {
            return;
        }

        foreach (var index in flexible)
        {
            sizes[index] += missing / flexible.Length;
        }
    }
}
