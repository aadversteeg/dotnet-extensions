using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Documents.Layout.TablePage;

namespace Ave.Documents.Layout;

/// <summary>
/// Where a page of a tracks box can break, and which break it takes (CSS Fragmentation 3 §4.1, §4.4). A breakpoint is
/// plain data: its kind, the row it comes before or cuts, its appeal and what the page holds up to it. The page takes
/// the latest breakpoint of the highest appeal that places body content, as <see cref="FlowLayout"/> does.
/// </summary>
internal static class TableBreaks
{
    /// <summary>The kinds of breakpoint, in page order for the same row: before a row comes before inside it.</summary>
    public enum BreakKind
    {
        /// <summary>Class A, between two units: no cell is cut; the content before the row stays whole.</summary>
        BeforeUnit,

        /// <summary>Between two rows of one unit: a cell that spans both continues on the next page.</summary>
        BetweenRows,

        /// <summary>Inside a row: every cell of it is a parallel flow that continues on the next page, and the row's
        /// part on this page fills the page (§5.3).</summary>
        InsideRow,
    }

    /// <summary>A breakpoint on this page.</summary>
    /// <param name="Kind">Its kind.</param>
    /// <param name="Row">The row it comes before (<see cref="BreakKind.BeforeUnit"/>, <see cref="BreakKind.BetweenRows"/>)
    /// or cuts (<see cref="BreakKind.InsideRow"/>).</param>
    /// <param name="Appeal">Its appeal.</param>
    /// <param name="Content">What the page holds before the unit the breakpoint is in.</param>
    /// <param name="Segment">The unit, measured; null for <see cref="BreakKind.BeforeUnit"/>.</param>
    public sealed record Breakpoint(BreakKind Kind, int Row, BreakAppeal Appeal, Content Content, TableSegments.Segment? Segment);

    /// <summary>The class-A breakpoint before the unit that starts at <paramref name="row"/>.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="content">The page up to the unit, with the line above the unit as its last line.</param>
    /// <param name="row">The unit's first row.</param>
    /// <returns>The breakpoint.</returns>
    public static Breakpoint BeforeUnit(TableGrid grid, Content content, int row) =>
        new(BreakKind.BeforeUnit, row, BeforeAppeal(grid, row), content, null);

    /// <summary>
    /// The breakpoints inside a unit that does not fit: between two of its rows whose upper part fits (a spanning
    /// cell is cut there, so it violates an avoid), and inside the first row that does not fit.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="content">The page up to the unit.</param>
    /// <param name="segment">The unit, measured.</param>
    /// <param name="height">The page's available height.</param>
    /// <returns>The breakpoints, in page order.</returns>
    public static IEnumerable<Breakpoint> InsideUnit(TableGeometry table, Content content, TableSegments.Segment segment, double height)
    {
        for (var row = segment.FirstRow; row <= segment.LastRow; row++)
        {
            // A break between rows ends the page at the line above `row`: down to its centre (slice) or with the
            // whole line (clone, InsideBelow of the row above). That always fits here: the loop reaches `row` only when
            // the row above fitted with that share of the line (the test below, one round earlier).
            var top = segment.RowTop(row);
            if (row > segment.FirstRow)
            {
                yield return new Breakpoint(BreakKind.BetweenRows, row, BreakAppeal.ViolatesAvoid, content, segment);
            }

            if (!Fitting.Fits(top + segment.Heights[row - segment.FirstRow] + InsideBelow(table, row), height) || row == segment.LastRow)
            {
                // A break inside the row ends the page inside the row's cells, below the whole line above the row: the
                // page must reach the bottom of that line, or its lower half would be cut off at the page end. With
                // clone the cut also needs the half of the line below the row that lies under the cut (see Take).
                if (Fitting.Fits(top + LineInside(table, segment, row) + CutLineBelow(table, row), height))
                {
                    yield return new Breakpoint(BreakKind.InsideRow, row, InsideAppeal(table.Grid, row), content, segment);
                }

                yield break;
            }
        }
    }

    /// <summary>What a cut inside <paramref name="row"/> needs below it: with clone, the lower half of the whole line
    /// below the row that ends the page (<see cref="InsideBelow"/>); with slice nothing, as no line is drawn there.</summary>
    private static double CutLineBelow(TableGeometry table, int row) => IsCloned(table) ? InsideBelow(table, row) : 0;

    /// <summary>
    /// How far the line above <paramref name="row"/> reaches below the row's top: half its width. A row's top is the
    /// centre of the line above it, both for a line between two rows and for the line that starts the page (the
    /// table's top line, or the line that continues after a break, whose centre or upper edge is the row's top). The
    /// line above a segment is never an upper half: upper halves only end pages.
    /// </summary>
    private static double LineInside(TableGeometry table, TableSegments.Segment segment, int row) =>
        row > segment.FirstRow
            ? table.HorizontalWidth(row - 1, row, CollapsedBorders.LineContext.Interior) / 2
            : table.HorizontalWidth(segment.Above.Above, segment.Above.Below, segment.Above.Context) / 2;

    /// <summary>The latest breakpoint of the highest appeal that places body content, taken; None when none does.</summary>
    /// <param name="table">The table.</param>
    /// <param name="breakpoints">The page's breakpoints.</param>
    /// <param name="height">The page's available height.</param>
    /// <returns>The page's outcome, None, or an error.</returns>
    public static Result<Maybe<Outcome>, Error> TakeBest(TableGeometry table, IEnumerable<Breakpoint> breakpoints, double height)
    {
        var ordered = breakpoints
            .OrderByDescending(breakpoint => breakpoint.Appeal)
            .ThenByDescending(breakpoint => breakpoint.Row)
            .ThenByDescending(breakpoint => breakpoint.Kind);
        foreach (var breakpoint in ordered)
        {
            var taken = Take(table, breakpoint, height);
            if (taken.IsFailure || taken.Value.HasValue)
            {
                return taken;
            }
        }

        return Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
    }

    /// <summary>
    /// Takes one breakpoint. Before a unit: the page keeps its content, and the line above the unit becomes its
    /// bottom line. Between rows or inside a row: <see cref="Cut"/> lays the cut cells out again.
    /// </summary>
    private static Result<Maybe<Outcome>, Error> Take(TableGeometry table, Breakpoint breakpoint, double height)
    {
        if (breakpoint.Kind == BreakKind.BeforeUnit || breakpoint.Segment is not { } segment)
        {
            var before = breakpoint.Content with { Lines = breakpoint.Content.Lines.RemoveAt(breakpoint.Content.Lines.Count - 1) };
            return Result<Maybe<Outcome>, Error>.Success(Maybe.From(new Outcome(
                before, BottomLine(table, before.LastRow, breakpoint.Row, before.Bottom), new TracksBreakToken(breakpoint.Row, [], false), breakpoint.Appeal)));
        }

        var row = breakpoint.Row;
        if (breakpoint.Kind == BreakKind.BetweenRows)
        {
            return Cut(table, breakpoint.Content, segment, row, segment.RowTop(row), inside: false, breakpoint.Appeal);
        }

        // With clone the cut ends the page with the whole line below the row (see TablePage), so it lies that line's
        // lower half above the page end.
        if (IsCloned(table))
        {
            var cut = height - CutLineBelow(table, row);
            return Fitting.Fits(segment.RowTop(row) + LineInside(table, segment, row), cut)
                ? Cut(table, breakpoint.Content, segment, row, cut, inside: true, breakpoint.Appeal)
                : Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
        }

        // A break inside the row cuts its cells at the page end. When that cut completes every cell, the row ends on
        // this page after all, and then it needs its share of the line below it, which is what did not fit: so the cut
        // moves up by that share, which leaves the end of the row's content for the next page (still below the line
        // above the row).
        var below = InsideBelow(table, row);
        var raised = height - below;
        return Cut(table, breakpoint.Content, segment, row, height, inside: true, breakpoint.Appeal).OnSuccessBind(atPageEnd =>
            atPageEnd.HasValue || below <= 0 || !Fitting.Fits(segment.RowTop(row) + LineInside(table, segment, row), raised)
                ? Result<Maybe<Outcome>, Error>.Success(atPageEnd)
                : Cut(table, breakpoint.Content, segment, row, raised, inside: true, breakpoint.Appeal));
    }

    /// <summary>
    /// Takes a break inside a segment: the rows above <paramref name="cutRow"/> whole; the cells that reach past the
    /// cut laid out again in the height down to <paramref name="cutY"/> — the row's top for a break between rows, the
    /// page end (or just above it, see <see cref="Take"/>) for a break inside the row, which then ends the page. None
    /// when a break inside the row would place nothing of it, or leave nothing of it for the next page.
    /// </summary>
    private static Result<Maybe<Outcome>, Error> Cut(
        TableGeometry table, Content content, TableSegments.Segment segment, int cutRow, double cutY, bool inside, BreakAppeal appeal)
    {
        var grid = table.Grid;
        var rows = content.Rows;
        for (var row = segment.FirstRow; row < cutRow; row++)
        {
            rows = rows.Add(new PlacedRow(row, segment.RowTop(row), segment.Heights[row - segment.FirstRow], false));
        }

        if (inside)
        {
            rows = rows.Add(new PlacedRow(cutRow, segment.RowTop(cutRow), cutY - segment.RowTop(cutRow), false));
        }

        var cells = content.Cells;
        var continuing = ImmutableArray.CreateBuilder<CellBreak>();
        var placedInRow = false;
        var leftForNextPage = false;
        foreach (var cell in segment.Cells)
        {
            var info = grid.Cells[cell.Cell];
            if (info.LastRow < cutRow)
            {
                cells = cells.Add(TableSegments.Whole(table, segment, cell));
                continue;
            }

            if (cell.StartRow > cutRow || (cell.StartRow == cutRow && !inside))
            {
                continue;
            }

            // With clone a cut cell that ends in the cut row keeps clear of the line drawn below the cut (see TablePage).
            var top = segment.RowTop(cell.StartRow);
            var clearance = inside && IsCloned(table) && info.LastRow == cutRow ? CutLineBelow(table, cutRow) : 0;
            var sliced = TableSegments.Slice(table, segment, cell, cutY - top, clearance);
            if (sliced.IsFailure)
            {
                return Result<Maybe<Outcome>, Error>.Failure(sliced.Error);
            }

            var (fragment, next) = sliced.Value;
            placedInRow |= !cell.IsComplete && (next.IsComplete || next.Token != cell.Token);
            leftForNextPage |= !next.IsComplete;
            cells = cells.Add(new PlacedCell(cell.Cell, cell.StartRow, fragment));
            continuing.Add(next);
        }

        if (inside && !(placedInRow && leftForNextPage))
        {
            return Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
        }

        var lines = content.Lines;
        for (var row = segment.FirstRow + 1; row <= cutRow - (inside ? 0 : 1); row++)
        {
            lines = lines.Add(new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, segment.RowTop(row)));
        }

        var bottom = inside ? cutY : segment.RowTop(cutRow);
        var placed = new Content(rows, cells, lines, bottom, HasBody: rows.Count > content.Rows.Count || content.HasBody);
        var bottomLine = !inside ? BottomLine(table, cutRow - 1, cutRow, bottom)
            : IsCloned(table) ? new LineAt(cutRow, Next(grid, cutRow), EdgeContext(grid, cutRow), bottom)
            : null;
        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(new Outcome(placed, bottomLine, new TracksBreakToken(cutRow, continuing.ToImmutable(), inside), appeal)));
    }

    /// <summary>The appeal of a class-A break before a body row: lower inside a row group kept together, or where
    /// the rows (or, at a group boundary, the groups) avoid a break between them.</summary>
    private static BreakAppeal BeforeAppeal(TableGrid grid, int row)
    {
        var (current, previous) = (grid.Rows[row], grid.Rows[row - 1]);
        var withinKeptGroup = !current.IsFirstOfGroup && current.Group.Style.BreakInside == BreakInside.Avoid;
        var between = BreakAvoidance.Worst(
            BreakAvoidance.Between(previous.Box, current.Box),
            current.IsFirstOfGroup ? BreakAvoidance.Between(previous.Group, current.Group) : BreakAppeal.Perfect);
        return withinKeptGroup ? BreakAppeal.ViolatesAvoid : between;
    }

    /// <summary>The appeal of a break inside a row: lower when the row or its group is kept together.</summary>
    private static BreakAppeal InsideAppeal(TableGrid grid, int row) =>
        BreakAvoidance.Inside(grid.Rows[row].Group, BreakAvoidance.Inside(grid.Rows[row].Box, BreakAppeal.Perfect));
}
