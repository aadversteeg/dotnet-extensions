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
/// the spanned columns that are not fixed so that together they hold it. CSS 2.1 says "by approximately the same
/// amount" in an algorithm that is not normative; the engine shares the cell's widths over the spanned columns as
/// Chromium does (<see cref="SpreadOverSpan"/>), with the same distribution as the table's width. The distribution of
/// the space between minimum and maximum follows CSS Tables 3 (in proportion to max − min), which Chromium also does.</item>
/// <item><b>Preferred</b>: a column length of the automatic table layout, a "constrained" column (CSS Tables 3
/// §3.8.3): its minimum is its cells' min-content and its maximum the larger of its width and that min-content (a cell's
/// max-content does not widen it). It grows from its minimum towards its maximum before the auto columns do, so in a
/// table too narrow for every width it shrinks below its width once the auto columns are at their minimum; it takes
/// width past the maximums only in a table without auto columns. Chromium: <c>TableTypes::Column::Encompass</c>
/// (<c>table_layout_algorithm_types.cc</c>) and the guesses of <c>DistributeInlineSizeToComputedInlineSizeAuto</c>
/// (<c>table_layout_utils.cc</c>): min, specified (constrained columns at their maximum), max, above max. CSS Tables 3
/// §3.8.2 makes the width a minimum too; the engine follows Chromium (<c>docs/architecture.md</c>, deliberate
/// deviations).</item>
/// <item><b>Fill</b>: CSS Grid's flexible length (<c>fr</c>, CSS Grid 1 §7.2.4: a share "of the leftover space"),
/// with the minimum Grid gives <c>1fr</c> (<c>minmax(auto, 1fr)</c>: never narrower than its min-content) and the
/// "Find the Size of an fr" loop of §11.7.1, which drops a fill column whose minimum is larger than its share and
/// shares the rest again.</item>
/// </list>
/// <para>The order is Grid's (§11.4–§11.7) with the guesses of CSS Tables 3 §3.9.3: fixed and min-content widths
/// first; then preferred columns grow towards their maximum, then auto columns; then fill columns share what is left. Without fill columns the box's <see cref="BoxStyle.Width"/> decides:
/// with <see cref="BoxWidth.Auto"/> a table is only as wide as its columns need (CSS 2.1 §17.5.2.2: with
/// <c>width: auto</c> "use max(MAX, CAPMIN)" when that is less than the containing block), so leftover space is not
/// spread; with <see cref="BoxWidth.Stretch"/> the columns past their maximum share it (<see cref="GrowPastMaximum"/>).</para>
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
        var max = columns.Select(column => column switch
        {
            TrackSize.Fixed fixedWidth => fixedWidth.Width.Points,
            TrackSize.Preferred preferred => preferred.Width.Points,
            _ => 0,
        }).ToArray();

        // Step 1: cells in one column set that column's minimum and maximum; a preferred column's maximum takes only
        // the cells' min-content (Chromium, TableTypes::Column::Encompass: a constrained column encompasses an
        // unconstrained cell's minimum).
        foreach (var cell in all.Where(cell => cell.Span == 1 && columns[cell.Column] is not TrackSize.Fixed))
        {
            min[cell.Column] = Math.Max(min[cell.Column], cell.Widths.MinContent);
            max[cell.Column] = Math.Max(max[cell.Column], columns[cell.Column] is TrackSize.Preferred ? cell.Widths.MinContent : cell.Widths.MaxContent);
        }

        // Step 2: spanning cells, narrowest span first and then by first column (Chromium's order), widen the spanned
        // columns that are not fixed: the cell's minimum first, then its maximum over the widened minimums.
        foreach (var cell in all.Where(cell => cell.Span > 1).OrderBy(cell => cell.Span).ThenBy(cell => cell.Column))
        {
            var spanned = Enumerable.Range(cell.Column, cell.Span).ToImmutableArray();
            var flexible = spanned.Where(index => columns[index] is not TrackSize.Fixed).ToImmutableArray();
            var lows = SpreadOverSpan(columns, min, max, spanned, flexible, cell.Widths.MinContent, growPreferred: true);
            foreach (var index in flexible)
            {
                min[index] = Math.Max(min[index], lows[index]);
            }

            var highs = SpreadOverSpan(columns, min, max, spanned, flexible, cell.Widths.MaxContent, growPreferred: false);
            foreach (var index in flexible)
            {
                max[index] = Math.Max(Math.Max(min[index], max[index]), highs[index]);
            }
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
    /// <param name="width">Whether the columns stay at what they need or fill <paramref name="available"/> when no
    /// column is a fill column.</param>
    /// <returns>The widths, or the width needed.</returns>
    public static (ImmutableArray<double> Widths, double? Needed) Resolve(
        ValueList<TrackSize> columns, ImmutableArray<IntrinsicWidths> ranges, double available, BoxWidth width = BoxWidth.Auto)
    {
        var widths = ranges.Select(range => range.MinContent).ToArray();
        var needed = widths.Sum();
        if (!Fitting.Fits(needed, available))
        {
            return ([], needed);
        }

        GrowTowardsMaximum(columns, ranges, widths, available, column => column is TrackSize.Preferred);
        GrowTowardsMaximum(columns, ranges, widths, available, column => column is TrackSize.Auto);
        if (width == BoxWidth.Stretch)
        {
            GrowPastMaximum(columns, ranges, widths, available);
        }

        ShareAmongFillColumns(columns, ranges, widths, available);
        return ([.. widths], null);
    }

    /// <summary>
    /// Columns of one kind grow from their minimum towards their maximum with the width not yet taken: first the
    /// preferred columns (CSS Tables 3 §3.9.3, the specified sizing-guess: constrained columns at their maximum), then
    /// the auto columns (the max-content sizing-guess). When the space does not reach every maximum, each column gets
    /// the same fraction of its own range (max − min), so a column whose content wraps a lot grows more than one that
    /// is nearly at its maximum (Chromium, <c>DistributeInlineSizeToComputedInlineSizeAuto</c>, cases
    /// <c>kSpecifiedGuess</c> and <c>kMaxGuess</c>).
    /// </summary>
    private static void GrowTowardsMaximum(
        ValueList<TrackSize> columns, ImmutableArray<IntrinsicWidths> ranges, double[] widths, double available, Func<TrackSize, bool> kind)
    {
        var free = available - widths.Sum();
        var autos = Enumerable.Range(0, columns.Count).Where(index => kind(columns[index])).ToImmutableArray();
        var room = autos.Sum(index => ranges[index].MaxContent - ranges[index].MinContent);
        var fraction = room <= 0 ? 0 : Math.Min(1, free / room);
        foreach (var index in autos)
        {
            widths[index] = ranges[index].MinContent + fraction * (ranges[index].MaxContent - ranges[index].MinContent);
        }
    }

    /// <summary>
    /// The width left over the columns' maximum widths, for a table as wide as the width it is given and without fill
    /// columns (CSS Tables 3 §3.9.3, "Distribution algorithm": "starting from the max-content sizing-guess and
    /// distributing the excess width to the columns", by the rules of §3.9.3.2, "Distributing excess width to
    /// columns"). The auto columns receive it, each in proportion to its maximum width; when the table has no auto
    /// column the fixed and preferred columns do, each in proportion to its maximum (a fixed column's width; the
    /// automatic layout has preferred columns only, the fixed layout fixed ones); when those weights are all zero (empty
    /// columns without insets), in equal parts. A column whose weight is zero beside others that are not receives
    /// nothing. Chromium, <c>third_party/blink/renderer/core/layout/table/table_layout_utils.cc</c>: automatic layout in
    /// <c>DistributeInlineSizeToComputedInlineSizeAuto</c>, case <c>kAboveMax</c> (auto columns first, fixed columns only
    /// without them); fixed layout in <c>SynchronizeAssignableTableInlineSizeAndColumnsFixed</c> (fixed columns scaled up
    /// when there is no auto column; a column of width 0 receives nothing). Chromium truncates every share to a layout
    /// unit and gives the remainder to the last receiving column; the engine keeps the exact shares.
    /// </summary>
    private static void GrowPastMaximum(ValueList<TrackSize> columns, ImmutableArray<IntrinsicWidths> ranges, double[] widths, double available)
    {
        if (Enumerable.Range(0, columns.Count).Any(index => columns[index] is TrackSize.Fill))
        {
            return;
        }

        var leftover = available - widths.Sum();
        var autos = Enumerable.Range(0, columns.Count).Where(index => columns[index] is TrackSize.Auto).ToImmutableArray();
        var receivers = autos.IsEmpty ? Enumerable.Range(0, columns.Count).ToImmutableArray() : autos;
        if (leftover <= 0 || receivers.IsEmpty)
        {
            return;
        }

        Share(widths, receivers, [.. ranges.Select(range => range.MaxContent)], leftover);
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

    /// <summary>
    /// What a cell spanning several columns asks of each of them: the width distribution of CSS Tables 3 §3.9.3 over the
    /// spanned columns (§3.8.3, "Computing Column Measures": "updated by taking into account cells spanning
    /// incrementally more columns"; §3.9.3.2 uses its rules for "the excess max-content or min-content width of a cell
    /// spanning more than one column"), as Chromium applies it to a spanning cell
    /// (<c>third_party/blink/renderer/core/layout/table/table_layout_utils.cc</c>, <c>DistributeColspanCellToColumnsAuto</c>,
    /// which calls <c>DistributeInlineSizeToComputedInlineSizeAuto</c> once for the cell's minimum and once for its
    /// maximum). When the spanned minimums hold the target, nothing changes; when they hold it with the preferred columns
    /// at their maximum, the preferred columns grow from their minimum in proportion to max − min; when the spanned
    /// maximums hold it, the preferred columns are at their maximum and the others grow so; otherwise each starts at
    /// its maximum and the rest is shared in proportion to the maximums (in equal parts when they are all zero) over
    /// the auto columns, or, for the cell's minimum only, over the preferred columns when there is no auto column.
    /// Fixed columns never grow (fixed means fixed: content overflows instead).
    /// </summary>
    /// <returns>Per column of the table, the width the cell asks of it (only the flexible columns are filled in).</returns>
    private static double[] SpreadOverSpan(
        ValueList<TrackSize> columns, double[] min, double[] max, ImmutableArray<int> spanned, ImmutableArray<int> flexible, double target, bool growPreferred)
    {
        var sizes = new double[min.Length];
        var lowest = spanned.Sum(index => min[index]);
        if (target <= lowest || flexible.IsEmpty)
        {
            return sizes;
        }

        var preferred = flexible.Where(index => columns[index] is TrackSize.Preferred).ToImmutableArray();
        var autos = flexible.Where(index => columns[index] is not TrackSize.Preferred).ToImmutableArray();
        var specified = lowest + preferred.Sum(index => max[index] - min[index]);
        if (target <= specified)
        {
            Grow(sizes, preferred, min, max, target - lowest);
            foreach (var index in autos)
            {
                sizes[index] = min[index];
            }

            return sizes;
        }

        var highest = spanned.Sum(index => max[index]);
        if (target <= highest)
        {
            foreach (var index in preferred)
            {
                sizes[index] = max[index];
            }

            Grow(sizes, autos, min, max, target - specified);
            return sizes;
        }

        foreach (var index in flexible)
        {
            sizes[index] = max[index];
        }

        // Past the maximums: the auto columns; without them the preferred ones, but only for the cell's minimum (Chromium
        // treats the target as constrained there, and a cell without a width of its own does not constrain its maximum).
        var receivers = !autos.IsEmpty ? autos : growPreferred ? preferred : [];
        if (!receivers.IsEmpty)
        {
            Share(sizes, receivers, max, target - highest);
        }

        return sizes;
    }

    /// <summary>The columns grow from their minimum by <paramref name="amount"/> together, each in proportion to its
    /// range (max − min).</summary>
    private static void Grow(double[] sizes, ImmutableArray<int> growing, double[] min, double[] max, double amount)
    {
        var room = growing.Sum(index => max[index] - min[index]);
        foreach (var index in growing)
        {
            sizes[index] = min[index] + (room <= 0 ? 0 : amount * (max[index] - min[index]) / room);
        }
    }

    /// <summary>Adds <paramref name="amount"/> to the receiving columns, each in proportion to its weight, or in equal
    /// parts when the weights are all zero: the step above the max-content guess of the width distribution of CSS
    /// Tables 3 (§3.9.3.2: "increased in proportion to max-content width", or "by equal amounts" when every max-content
    /// width is zero), for a table (<see cref="GrowPastMaximum"/>) and for a spanning cell (<see cref="SpreadOverSpan"/>).</summary>
    private static void Share(double[] sizes, ImmutableArray<int> receivers, double[] weights, double amount)
    {
        var total = receivers.Sum(index => weights[index]);
        foreach (var index in receivers)
        {
            sizes[index] += total > 0 ? amount * weights[index] / total : amount / receivers.Length;
        }
    }
}
