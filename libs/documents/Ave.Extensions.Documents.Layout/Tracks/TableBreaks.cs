using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Layout.TablePage;

namespace Ave.Extensions.Documents.Layout;

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
    /// <param name="InKeptGroup">True when the breakpoint lies inside a row group kept together (<c>break-inside:
    /// avoid</c>): <paramref name="Appeal"/> is then its appeal inside the group, and <see cref="Overall"/> the group's
    /// keep on top of it.</param>
    public sealed record Breakpoint(BreakKind Kind, int Row, BreakAppeal Appeal, Content Content, TableSegments.Segment? Segment, bool InKeptGroup = false)
    {
        /// <summary>
        /// The appeal against breakpoints outside the group: a break inside a kept group violates its keep. A group's
        /// keep is applied from outside, as Chromium applies <c>break-inside: avoid</c> to a box's own break
        /// (<c>CalculateBreakAppealInside</c>), so inside a group that must break anyway its rows break where breaking is
        /// best by their own rules: between two rows rather than inside a kept row or against orphans (cases
        /// <c>fr-f6-*</c>).
        /// </summary>
        public BreakAppeal Overall => InKeptGroup ? BreakAvoidance.Worst(BreakAppeal.ViolatesAvoid, Appeal) : Appeal;
    }

    /// <summary>The class-A breakpoint before the unit that starts at <paramref name="row"/>.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="content">The page up to the unit, with the line above the unit as its last line.</param>
    /// <param name="row">The unit's first row.</param>
    /// <returns>The breakpoint.</returns>
    public static Breakpoint BeforeUnit(TableGrid grid, Content content, int row) =>
        new(BreakKind.BeforeUnit, row, BeforeAppeal(grid, row), content, null, InKeptGroup(grid, row, between: true));

    /// <summary>
    /// The breakpoints inside a unit that does not fit: between two of its rows whose upper part fits (a spanning
    /// cell continues over it, see <see cref="BetweenRowsAppeal"/>), and inside the first row that does not fit.
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
                yield return new Breakpoint(BreakKind.BetweenRows, row, BetweenRowsAppeal(table.Grid, segment, row), content, segment, InKeptGroup(table.Grid, row, between: true));
            }

            if (!Fitting.Fits(top + segment.Heights[row - segment.FirstRow] + InsideBelow(table, row), height) || row == segment.LastRow)
            {
                // A break inside the row ends the page inside the row's cells, below the whole line above the row: the
                // page must reach the bottom of that line, or its lower half would be cut off at the page end. With
                // clone the cut also needs the half of the line below the row that lies under the cut (see Take).
                if (Fitting.Fits(top + LineInside(table, segment, row) + CutLineBelow(table, row), height))
                {
                    yield return new Breakpoint(BreakKind.InsideRow, row, InsideAppeal(table.Grid, segment, row), content, segment, InKeptGroup(table.Grid, row, between: false));
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

    /// <summary>
    /// The latest breakpoint of the highest appeal that places body content, taken; None when none does. A breakpoint
    /// is taken at the appeal it promised, or kept as a fallback when taking it shows it is worse: a cut inside a row
    /// that would leave a part of the row that shows nothing (<see cref="CutShows"/>) is only a last resort
    /// (<see cref="BreakAppeal.LastResort"/>), and a cut that breaks a cell's content badly (orphans, a kept paragraph)
    /// is as bad as that break. The best fallback is taken when no breakpoint keeps its promise, and the table's parent
    /// then prefers any better breakpoint of its own (which moves the table to the next page); only a table at the top
    /// of a page, where moving gains nothing, is cut there.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="breakpoints">The page's breakpoints.</param>
    /// <param name="height">The page's available height.</param>
    /// <returns>The page's outcome, None, or an error.</returns>
    public static Result<Maybe<Outcome>, Error> TakeBest(TableGeometry table, IEnumerable<Breakpoint> breakpoints, double height)
    {
        var ordered = breakpoints
            .OrderByDescending(breakpoint => breakpoint.Overall)
            .ThenByDescending(breakpoint => breakpoint.Appeal)
            .ThenByDescending(breakpoint => breakpoint.Row)
            .ThenByDescending(breakpoint => breakpoint.Kind);
        Maybe<Outcome> fallback = Maybe<Outcome>.None;
        foreach (var breakpoint in ordered)
        {
            // Taken at the breakpoint's own appeal; the table's parent sees the overall one (Breakpoint.Overall).
            var taken = Take(table, breakpoint, height).OnSuccessMap(outcome => outcome.OnSomeMap(placed =>
                (Promised: placed.Appeal, Outcome: placed with { Appeal = breakpoint.InKeptGroup ? BreakAvoidance.Worst(BreakAppeal.ViolatesAvoid, placed.Appeal) : placed.Appeal })));
            if (taken.IsFailure)
            {
                return Result<Maybe<Outcome>, Error>.Failure(taken.Error);
            }

            if (taken.Value.HasValue && taken.Value.Value.Promised == breakpoint.Appeal)
            {
                return Result<Maybe<Outcome>, Error>.Success(Maybe.From(taken.Value.Value.Outcome));
            }

            // Taken at a lower appeal than the breakpoint promised (a part that shows nothing, a cell's own break): kept in
            // case nothing keeps its promise; of such, the best, and of equal ones the first in page order from the end.
            fallback = taken.Value.HasValue && (!fallback.HasValue || taken.Value.Value.Outcome.Appeal > fallback.Value.Appeal) ? taken.Value.Value.Outcome : fallback;
        }

        return Result<Maybe<Outcome>, Error>.Success(fallback);
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
            return Cut(table, breakpoint.Content, segment, row, segment.RowTop(row), inside: false, breakpoint.Appeal, pageEnd: height);
        }

        // With clone the cut ends the page with the whole line below the row (see TablePage), so it lies that line's
        // lower half above the page end.
        if (IsCloned(table))
        {
            var cut = height - CutLineBelow(table, row);
            return Fitting.Fits(segment.RowTop(row) + LineInside(table, segment, row), cut)
                ? Cut(table, breakpoint.Content, segment, row, cut, inside: true, breakpoint.Appeal, pageEnd: height)
                : Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
        }

        // A break inside the row cuts its cells at the page end. When that cut completes every cell, the row ends on
        // this page after all, and then it needs its share of the line below it, which is what did not fit: so the cut
        // moves up by that share, which leaves the end of the row's content for the next page (still below the line
        // above the row). That is a last resort: every line of the row fits, only the line below it does not, and
        // Chromium then moves the row whole (cases fr-f4-*).
        var below = InsideBelow(table, row);
        var raised = height - below;
        return Cut(table, breakpoint.Content, segment, row, height, inside: true, breakpoint.Appeal, pageEnd: height).OnSuccessBind(atPageEnd =>
            atPageEnd.HasValue || below <= 0 || !Fitting.Fits(segment.RowTop(row) + LineInside(table, segment, row), raised)
                ? Result<Maybe<Outcome>, Error>.Success(atPageEnd)
                : Cut(table, breakpoint.Content, segment, row, raised, inside: true, BreakAppeal.LastResort, pageEnd: height));
    }

    /// <summary>
    /// Takes a break inside a segment: the rows above <paramref name="cutRow"/> whole; the cells that reach past the
    /// cut laid out again in the height down to <paramref name="cutY"/> — the row's top for a break between rows, the
    /// page end (or just above it, see <see cref="Take"/>) for a break inside the row, which then ends the page. A cell
    /// that spans across a break between rows is cut at the page end, not at the row boundary: it fills the page, as
    /// Chromium lays a spanning cell out in the height of all its rows (cases <c>fr-f7-*</c>). None when nothing of the
    /// row would be left (<see cref="LeavesSomething"/>); a last resort when a part of the row would show nothing
    /// (<see cref="CutShows"/>).
    /// </summary>
    private static Result<Maybe<Outcome>, Error> Cut(
        TableGeometry table, Content content, TableSegments.Segment segment, int cutRow, double cutY, bool inside, BreakAppeal appeal, double pageEnd)
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
        var cut = new List<(double Top, BoxFragment Fragment, CellBreak Next)>();
        var cellsAppeal = BreakAppeal.Perfect;
        var keepsItsBreak = false;
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

            // With clone every cut cell keeps clear of the line that ends the page, which runs across the full width
            // (see TablePage): the line below the cut row, or the line above the row a break between rows comes before.
            var top = segment.RowTop(cell.StartRow);
            var clearance = IsCloned(table) ? InsideBelow(table, inside ? cutRow : cutRow - 1) : 0;
            var sliceEnd = inside || IsCloned(table) ? cutY : pageEnd;
            var sliced = TableSegments.Slice(table, segment, cell, sliceEnd - top, clearance, endsAtTheCut: inside && info.LastRow == cutRow);
            if (sliced.IsFailure)
            {
                return Result<Maybe<Outcome>, Error>.Failure(sliced.Error);
            }

            var (fragment, next, cellAppeal, cellKeeps) = sliced.Value;
            cellsAppeal = BreakAvoidance.Worst(cellsAppeal, cellAppeal);
            keepsItsBreak |= cellKeeps;
            cut.Add((top, fragment, next));
            cells = cells.Add(new PlacedCell(cell.Cell, cell.StartRow, fragment));
            continuing.Add(next);
        }

        if (inside && !LeavesSomething(cut))
        {
            return Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
        }

        var shows = inside ? CutShows(table, segment, cutRow, cut) : Result<bool, Error>.Success(true);
        if (shows.IsFailure)
        {
            return Result<Maybe<Outcome>, Error>.Failure(shows.Error);
        }

        // A last resort must still move on: something above the row is on the page, or some cut cell placed something,
        // even if it shows nothing. A cut that changes no cell would start the next page as this one started.
        if (!shows.Value && !content.HasBody && cutRow == segment.FirstRow && !Advances(segment, cut))
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
            : IsCloned(table) ? new LineAt(cutRow, Next(grid, cutRow), PageEndContext(table, cutRow), bottom)
            : null;
        // A cut that leaves a part of the row showing nothing is only a last resort (CutShows). Otherwise the cut is as good
        // as the worst break it makes inside a cut cell's content (a cell's own keep rules, orphans and widows): every
        // cell is a flow of its own that breaks there, and Chromium joins the cells' break appeals into the row's (case
        // c-cell-orphan-moves: a row whose cell would leave one line on the page moves to the next page).
        var taken = shows.Value ? BreakAvoidance.Worst(appeal, cellsAppeal) : BreakAppeal.LastResort;
        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(new Outcome(placed, bottomLine, new TracksBreakToken(cutRow, continuing.ToImmutable(), inside), taken)
        {
            KeepsItsBreak = keepsItsBreak && shows.Value,
        }));
    }

    /// <summary>
    /// THE RULE FOR A CUT INSIDE A ROW, part 1: a row is cut inside itself only when something of it is left for the
    /// next page: some cell the cut lays out again goes on there. Otherwise every cell ends on this page, and the row
    /// would be split into the whole row and an empty part.
    /// </summary>
    /// <param name="cut">Every cell the cut lays out again: its top (from the table's top edge), its fragment on this
    /// page and where it continues.</param>
    /// <returns>Whether anything of the row goes on.</returns>
    private static bool LeavesSomething(IReadOnlyList<(double Top, BoxFragment Fragment, CellBreak Next)> cut) =>
        cut.Any(cell => !cell.Next.IsComplete);

    /// <summary>Whether a cut lays out some cell further than where the page started it.</summary>
    private static bool Advances(TableSegments.Segment segment, IReadOnlyList<(double Top, BoxFragment Fragment, CellBreak Next)> cut) =>
        cut.Any(cell => segment.Cells.Single(active => active.Cell == cell.Next.Cell) is var start
            && (cell.Next.Token != start.Token || cell.Next.IsComplete != start.IsComplete));

    /// <summary>
    /// THE RULE FOR A CUT INSIDE A ROW, part 2: the cut is a breakpoint only when both parts of the row show something.
    /// On this page, some cell draws something in the visible part of the row, which starts under the grid line above it
    /// (that line covers what lies in its lower half); and on the next page, the rest of some cell draws something. What
    /// is drawn is decided from placed results, not from the document tree (<see cref="DrawnContent"/>: a visible glyph,
    /// an image, a rule, a filled or bordered box). So a cell that spans rows and placed its text above the row does not
    /// count, nor does text hidden under a thick line, nor a cell of invisible characters, or an empty block with padding
    /// only; an empty block with a border or a background does, as it draws. A cell's own background is not counted: it
    /// fills every part of the row on every page, whatever the cell holds, so it says nothing about whether a part shows
    /// anything. (In the collapsing border model a cell paints no border of its own; the table draws the grid lines.)
    /// Without the first half a page would end with an empty strip of the row; without the second the next page would
    /// start with one. A cut that fails this rule is a last resort (<see cref="TakeBest"/>): the row moves to the next
    /// page whole when it can, and is cut anyway only where nothing else is possible, so a row that draws nothing over
    /// a page's height is never refused.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="segment">The unit the cut is in, measured.</param>
    /// <param name="cutRow">The row the cut is inside.</param>
    /// <param name="cut">Every cell the cut lays out again, as for <see cref="LeavesSomething"/>.</param>
    /// <returns>Whether both parts show something, or an error from laying out the rest of a cell.</returns>
    private static Result<bool, Error> CutShows(
        TableGeometry table, TableSegments.Segment segment, int cutRow, IReadOnlyList<(double Top, BoxFragment Fragment, CellBreak Next)> cut)
    {
        var visibleTop = segment.RowTop(cutRow) + LineInside(table, segment, cutRow);
        if (!cut.Any(cell => DrawnContent.ChildrenDrawBelow(cell.Fragment.Children, visibleTop - cell.Top)))
        {
            return Result<bool, Error>.Success(false);
        }

        return cut.Where(cell => !cell.Next.IsComplete)
            .TraverseAll(cell => TableSegments.Rest(table, segment, cell.Next))
            .OnSuccessMap(rests => rests.Any(rest => DrawnContent.ChildrenDrawBelow(rest.Children, double.NegativeInfinity)));
    }

    /// <summary>The appeal of a class-A break before a body row: lower where the rows (or, at a group boundary, the
    /// groups) avoid a break between them. Inside a row group kept together the group's keep comes on top
    /// (<see cref="Breakpoint.Overall"/>).</summary>
    private static BreakAppeal BeforeAppeal(TableGrid grid, int row)
    {
        var (current, previous) = (grid.Rows[row], grid.Rows[row - 1]);
        return BreakAvoidance.Worst(
            BreakAvoidance.Between(previous.Box, current.Box),
            current.IsFirstOfGroup ? BreakAvoidance.Between(previous.Group, current.Group) : BreakAppeal.Perfect);
    }

    /// <summary>Whether a breakpoint at <paramref name="row"/> lies inside a row group kept together: a break before the
    /// row inside its group (<paramref name="between"/>; before the group's first row it lies outside), or a cut inside
    /// the row.</summary>
    private static bool InKeptGroup(TableGrid grid, int row, bool between) =>
        grid.Rows[row].Group.Style.BreakInside == BreakInside.Avoid && (!between || !grid.Rows[row].IsFirstOfGroup);

    /// <summary>
    /// THE RULE FOR A BREAK BETWEEN TWO ROWS THAT A CELL SPANNING ROWS CROSSES: a row span does not keep its rows
    /// together. The break is an ordinary break between rows, with the appeal a break before <paramref name="row"/>
    /// has anywhere (<see cref="BeforeAppeal"/>: lower inside a row group kept together, or at a keep-with-next), and
    /// the spanning cell continues on the next page, cut as a break inside it cuts it. The one keep rule a span adds
    /// is the cell's own: the cell's content breaks at this break, and every breakpoint inside it has the cell as an
    /// ancestor, so a cell kept together (<c>break-inside: avoid</c>) lowers the appeal (CSS Fragmentation 3 §4.4,
    /// rules 2 and 4). Without this rule the break before the whole unit would always win, and the rows under a label
    /// would move to the next page together, leaving the page above half empty.
    /// </summary>
    /// <remarks>
    /// CSS Fragmentation 3 §4.1 puts class A breaks "Between sibling boxes of the following types", among them "table
    /// row boxes", and says nothing about a cell that spans them; CSS 2.1 is silent too (§13.3.3 names no table rows, §17
    /// no page breaks). CSS Tables 3 (Working Draft, §6.1) asks to keep a row unbroken only "if the cells spanning the
    /// row do not span any subsequent row"; other rows "are said freely fragmentable". Chromium 159 prints the same
    /// (tools/reference/chromium, cases b4-row-span and b5-row-span-kept-cell: it breaks between the rows under a
    /// label, and moves a label kept together whole); it dropped its own avoidance of such breaks in 2023, because it
    /// was "creating large gaps when printing" (Chromium review 4136462, bug 1396218).
    /// </remarks>
    /// <param name="grid">The grid.</param>
    /// <param name="segment">The unit the break is in, measured.</param>
    /// <param name="row">The row the break comes before.</param>
    /// <returns>The appeal.</returns>
    private static BreakAppeal BetweenRowsAppeal(TableGrid grid, TableSegments.Segment segment, int row) =>
        WithKeptCells(grid, segment, cell => cell.Row < row && cell.LastRow >= row, BeforeAppeal(grid, row));

    /// <summary>The appeal of a break inside a row: lower when the row is kept together, or a cell it cuts is (every
    /// cell of the row, and every cell from a row above that spans into it), as for a break between rows
    /// (<see cref="BetweenRowsAppeal"/>). Inside a row group kept together the group's keep comes on top
    /// (<see cref="Breakpoint.Overall"/>).</summary>
    private static BreakAppeal InsideAppeal(TableGrid grid, TableSegments.Segment segment, int row) =>
        WithKeptCells(grid, segment, cell => cell.Row <= row && cell.LastRow >= row,
            BreakAvoidance.Inside(grid.Rows[row].Box, BreakAppeal.Perfect));

    /// <summary>
    /// <paramref name="appeal"/>, lowered when a cell of the segment that the break cuts is kept together
    /// (<c>break-inside: avoid</c>, <see cref="BreakAvoidance.Inside"/>): the break falls inside that cell; or when the
    /// row such a cell starts in is: a row kept together keeps the rows its spanning cell reaches into, since a break
    /// there breaks inside that row (Chromium: the row's own break token is a break inside it, cases <c>fr-f8-*</c>).
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="segment">The unit the break is in, measured.</param>
    /// <param name="cuts">Which cells the break cuts.</param>
    /// <param name="appeal">The appeal without the cells' keep rules.</param>
    /// <returns>The appeal.</returns>
    private static BreakAppeal WithKeptCells(TableGrid grid, TableSegments.Segment segment, Func<TableGrid.Cell, bool> cuts, BreakAppeal appeal) =>
        segment.Cells
            .Select(cell => grid.Cells[cell.Cell])
            .Where(cuts)
            .Aggregate(appeal, (lowered, cell) => BreakAvoidance.Inside(grid.Rows[cell.Row].Box, BreakAvoidance.Inside(cell.Box, lowered)));
}
