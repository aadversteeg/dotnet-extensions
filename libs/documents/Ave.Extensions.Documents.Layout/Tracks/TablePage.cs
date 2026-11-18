using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// One page of a tracks box: which rows go on it, how tall they are, and where it breaks.
/// </summary>
/// <remarks>
/// <para><b>Row heights</b> (CSS 2.1 §17.5.3): a row is as tall as its tallest cell; a cell that spans rows gets the
/// rows it spans, and when they are together shorter than the cell, the last of them grows by the difference ("CSS 2.1
/// does not specify how cells that span more than one row affect row height calculations except that the sum of the
/// row heights involved must be great enough to encompass the cell"; growing the last row is the simplest rule that
/// never moves a row that is already placed). Every cell
/// is then as tall as its rows, and its content is placed at the top, middle or bottom (§17.5.3).</para>
/// <para><b>Units</b>: the rows are taken unit by unit (<see cref="TableGrid.Unit"/>, rows that row spans join), each
/// first measured with unlimited height. A unit that fits goes on the page whole.</para>
/// <para><b>Breaking</b> (CSS Fragmentation 3 §4.1): the breakpoints are between units (class A, no cell is cut),
/// between two rows inside a unit (a cell that spans both continues on the next page), and inside a row: every cell of
/// the row is a parallel flow that continues on the next page from where the page end cut it, and the row's part on
/// this page fills the page (§5.3). Each breakpoint has an appeal (<see cref="BreakAppeal"/>): a break inside a row, a
/// row group or a cell marked <c>break-inside: avoid</c> violates it, so does a break at <c>break-before</c>/<c>break-after:
/// avoid</c>. A row span does not keep its rows together: a break between two rows of a unit has the appeal it would
/// have without the span, lowered only when it cuts a spanning cell marked <c>break-inside: avoid</c> (stated once, in
/// <see cref="TableBreaks"/>, BetweenRowsAppeal). The page breaks at the latest
/// breakpoint of the highest appeal, as Chromium chooses and as in <see cref="FlowLayout"/> (CSS Fragmentation 3 §4.5
/// leaves the choice among the allowed breakpoints to the user agent).</para>
/// <para><b>The grid line at a break between rows</b> is resolved once, from the rows on both sides, and drawn as the
/// table's <see cref="BoxStyle.DecorationBreak"/> says:</para>
/// <list type="bullet">
/// <item><see cref="BoxDecorationBreak.Slice"/>: split along its centre, its upper half ends this page's fragment and its
/// lower half starts the next one. In the collapsing border model half of a line lies in the cells on each side of it
/// (CSS 2.1 §17.6.2), and a box cut by a break gets no border at the cut (CSS Fragmentation 3 §5.4,
/// <c>box-decoration-break: slice</c>), so each page keeps the half that belongs to its own rows — Chromium draws a
/// broken table the same way. A row fits when it fits down to the centre of the line below it.</item>
/// <item><see cref="BoxDecorationBreak.Clone"/>: the whole line ends this page and the whole line starts the next
/// (above its first body row; with repeated header rows the header comes first, under the table's top line). This is
/// the engine's own rule for collapsed borders, modelled on <c>clone</c> and on how word processors print tables. A
/// row fits when it fits with the whole line below it, and the next page spends the whole line before its first
/// row.</item>
/// </list>
/// <para>Only the last row of the table needs the half of the table's bottom border that lies inside the table. A break
/// inside a row is a breakpoint only when it cuts content. The rule, decided from the placed result: a row is cut
/// inside itself only when something of it is left for the next page, and both parts show something: on this page,
/// some cell draws something below the grid line above the row, and on the next the rest of some cell draws something
/// (stated once, in <see cref="TableBreaks"/>, LeavesSomething and CutShows). So a row whose cells all end on this page
/// is never split into a full row and an empty one, and a row of which nothing would show on one of the pages — beside
/// a cell that spans rows and placed its text higher up, beside cells of invisible characters, under a thick line — is
/// not split into an empty strip and the rest: it moves to the next page whole. Only where it cannot move (at the top of
/// a page) such a cut is taken as a last resort, so no row is refused.</para>
/// <para><b>The grid lines at a break inside a row</b> follow the same decoration break. <see cref="BoxDecorationBreak.Slice"/>
/// draws nothing at the cut: the row's cells are boxes cut by a break, which get no border there (CSS Fragmentation 3
/// §5.4). <see cref="BoxDecorationBreak.Clone"/> closes the table on both pages, as word processors do: the page ends
/// with the whole line below the cut row (the table's bottom border when it is the last row), centred so that its lower
/// edge is the page end, and the next page starts with the whole line above the row, as at the top of a table.</para>
/// <para><b>With clone the table is closed across its full width on both pages</b>, at a break between rows and inside a
/// row alike: the line that ends the page and the line that starts the next also run over the column of a cell that
/// spans rows and continues over the break. Such a cell is closed there by its own borders, as clone gives every
/// fragment of a box its whole border (CSS Fragmentation 3 §5.4): the line in its column is resolved as a line between
/// the part of the cell above it and the part below, so the cell's bottom and top border compete as two cells' borders
/// do, and like every line at a break it is resolved once, the same on both pages
/// (<see cref="CollapsedBorders.LineContext.ClonedBreak"/>). Where the page ends or starts at the cell's own bottom
/// or top edge instead (a cut inside its last or first row), that line is resolved as anywhere else, with what lies
/// beyond it: the cell's bottom border against the row below, its top border against the row above, or the table's
/// border at its edge. Every cut cell keeps clear of the line that ends
/// the page by half the line's width, and every cell that continues starts its rest half the line's width below the line
/// that starts the next page, as whole cells do.</para>
/// <para><b>Header rows</b> (§17.2, <c>table-header-group</c>) start the table and are repeated at the top of every
/// later fragment. They never break. A break right after the header rows, with no body row on the page, is not a
/// breakpoint, so a header is never left alone at a page end — except at the start of an empty page, where nothing
/// would be gained by moving on. On a later page whose height cannot hold the header and some body content, the header
/// is left out, so the table always makes progress.</para>
/// </remarks>
internal static class TablePage
{
    /// <summary>A row on the page.</summary>
    /// <param name="Row">The row index.</param>
    /// <param name="Top">Its top, from the table's top edge.</param>
    /// <param name="Height">Its height on this page.</param>
    /// <param name="IsRepeated">True for a header row repeated on a later page.</param>
    public sealed record PlacedRow(int Row, double Top, double Height, bool IsRepeated);

    /// <summary>A cell on the page.</summary>
    /// <param name="Cell">The cell index.</param>
    /// <param name="TopRow">Its first row on this page: its fragment starts at that row's top. For a cell that
    /// continues from an earlier page this is not the row it belongs to (<see cref="TableGrid.Cell.Row"/>).</param>
    /// <param name="Fragment">Its fragment, as tall as the rows it covers on this page.</param>
    public sealed record PlacedCell(int Cell, int TopRow, BoxFragment Fragment);

    /// <summary>A horizontal grid line on the page.</summary>
    /// <param name="Above">The row above it, or null at the top of the page's grid.</param>
    /// <param name="Below">The row below it, or null at the bottom.</param>
    /// <param name="Context">Table edge and cut cells, for resolving the line.</param>
    /// <param name="Y">Its centre, from the table's top edge.</param>
    /// <param name="Part">Which part of the line this page draws: all of it, or one half at a break.</param>
    public sealed record LineAt(int? Above, int? Below, CollapsedBorders.LineContext Context, double Y, LinePart Part = LinePart.Whole);

    /// <summary>Which part of a grid line a page draws (see remarks: a line at a break between rows is split).</summary>
    public enum LinePart
    {
        /// <summary>The whole line, centred on its position.</summary>
        Whole,

        /// <summary>The half above the centre: the line at the end of a page.</summary>
        UpperHalf,

        /// <summary>The half below the centre: the line at the start of the next page.</summary>
        LowerHalf,
    }

    /// <summary>What is on the page so far: an immutable snapshot, so that a breakpoint can keep it.</summary>
    /// <param name="Rows">The rows.</param>
    /// <param name="Cells">The cells.</param>
    /// <param name="Lines">The horizontal grid lines above and between the rows (not yet the bottom one).</param>
    /// <param name="Bottom">The bottom of the last row.</param>
    /// <param name="HasBody">True when a body row (not a header row) is on the page.</param>
    public sealed record Content(ImmutableList<PlacedRow> Rows, ImmutableList<PlacedCell> Cells, ImmutableList<LineAt> Lines, double Bottom, bool HasBody)
    {
        /// <summary>The last row on the page, or null.</summary>
        public int? LastRow => Rows.IsEmpty ? null : Rows[^1].Row;
    }

    /// <summary>The page's result: its content, the bottom line (null when the page end cuts the last row), the
    /// continuation, the appeal of the break, and for a complete page of the table the best breakpoint between its body
    /// units (its early break), or null.</summary>
    public sealed record Outcome(Content Content, LineAt? BottomLine, TracksBreakToken? Continuation, BreakAppeal Appeal, TracksEarlyBreak? BestBreak = null);

    /// <summary>Lays out one page of the table and builds its fragment.</summary>
    public static Result<LayoutResult, Error> LayOut(TableGeometry table, ConstraintSpace space, TracksBreakToken? incoming)
    {
        var grid = table.Grid;
        var isFirst = incoming is null;
        var resume = incoming ?? new TracksBreakToken(grid.HeaderRowCount, [], false);
        var hasHeader = grid.HeaderRowCount > 0;
        var breakAt = space.BreakAt switch
        {
            null => null,
            TracksEarlyBreak row => row,
            _ => throw new ArgumentException($"Tracks received a {space.BreakAt.GetType().Name} it did not report.", nameof(space)),
        };
        var outcome = Attempt(table, space, resume, isFirst, hasHeader, breakAt);

        // A later page that cannot hold the header and some body content that shows goes on without the header (see
        // remarks): under the header the body has no room, or only a last-resort cut.
        if (!isFirst && hasHeader && outcome.IsSuccess && OnlyLastResort(outcome.Value))
        {
            var without = Attempt(table, space, resume, isFirst, includeHeader: false, breakAt);
            outcome = without.IsFailure || (without.Value.HasValue && !OnlyLastResort(without.Value)) || outcome.Value.HasNoValue ? without : outcome;
        }

        if (outcome.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(outcome.Error);
        }

        return outcome.Value.HasValue
            ? LayoutResult.Place(TableFragments.Build(table, outcome.Value.Value), outcome.Value.Value.Continuation, outcome.Value.Value.Appeal, outcome.Value.Value.BestBreak)
            : LayoutResult.NothingFits(table.Box, space);
    }

    /// <summary>One try at the page, with or without the header rows on top. None when no body content fits.</summary>
    private static Result<Maybe<Outcome>, Error> Attempt(TableGeometry table, ConstraintSpace space, TracksBreakToken resume, bool isFirst, bool includeHeader, TracksEarlyBreak? breakAt)
    {
        var grid = table.Grid;
        var height = space.AvailableHeight;
        var resumeCut = new HashSet<int>(resume.Cells.Select(cell => cell.Cell));
        var nextBody = resume.NextRow < grid.Rows.Length ? resume.NextRow : (int?)null;

        // The line at the top of the page's grid, and the line between the header and the first body row: the cells
        // that continue from the previous page have no border there (slice).
        var firstRow = includeHeader ? 0 : nextBody;
        var topContext = new CollapsedBorders.LineContext(isFirst || includeHeader, !includeHeader && resume.RowIsSplit, includeHeader ? new HashSet<int>() : resumeCut);
        var resumeContext = new CollapsedBorders.LineContext(false, resume.RowIsSplit, resumeCut);
        if (firstRow is null)
        {
            // Nothing left but (repeated) header rows that would stand alone: an empty table, or one whose body ended.
            return Result<Maybe<Outcome>, Error>.Success(Maybe.From(Complete(table, Empty(0))));
        }

        // The top line lies inside the table's border box, so its centre — the top of the first row — is half its width
        // down. A page that continues after a break between rows starts with the lower half of the line at the break,
        // so its first row starts at the top (see remarks). That line is the one between the last row of the previous
        // page and this page's first row, resolved with both rows exactly as the upper half was: the two halves are
        // halves of one line, and a row group's own top border takes part only above the group's first row. With clone a
        // row cut inside itself starts again under the whole line above it, which is resolved as on the page it started
        // on, with the row's own cells and their top borders (see remarks). With clone the line also closes a spanning
        // cell that continues over the break (see remarks). One rule for the line above the page's first body row: the
        // same context whether a repeated header comes before it or not.
        var reopensCutRow = !isFirst && resume.RowIsSplit && IsCloned(table);
        if (!isFirst && IsCloned(table))
        {
            resumeContext = CollapsedBorders.LineContext.ClonedBreak;
        }

        var continuesAfterBreak = !isFirst && !includeHeader && (!resume.RowIsSplit || reopensCutRow);
        var firstLine = continuesAfterBreak
            ? ContinuationLine(table, firstRow.Value, resumeContext)
            : new LineAt(null, firstRow, topContext, table.HorizontalWidth(null, firstRow, topContext) / 2);
        var content = Empty(firstLine.Y) with { Lines = [firstLine] };
        if (includeHeader)
        {
            var header = PlaceHeader(table, content, nextBody, resumeContext, height, isRepeated: !isFirst);
            if (header.IsFailure || header.Value.HasNoValue)
            {
                return header.IsFailure ? Result<Maybe<Outcome>, Error>.Failure(header.Error) : Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
            }

            content = header.Value.Value;
            if (nextBody is null)
            {
                return Result<Maybe<Outcome>, Error>.Success(Maybe.From(Complete(table, content)));
            }

            content = content with { Lines = content.Lines.Add(new LineAt(grid.HeaderRowCount - 1, nextBody, resumeContext, content.Bottom)) };
        }

        var body = PlaceBody(table, content, resume, height, breakAt);

        // The header alone at the start of an empty page: nothing better is possible there (see remarks); a body that only
        // a last-resort cut would place is not better.
        return body.IsSuccess && OnlyLastResort(body.Value) && includeHeader && isFirst && space.IsAtPageStart
            ? Result<Maybe<Outcome>, Error>.Success(Maybe.From(new Outcome(
                content with { Lines = content.Lines.RemoveAt(content.Lines.Count - 1) },
                BottomLine(table, grid.HeaderRowCount - 1, nextBody, content.Bottom),
                resume,
                BreakAppeal.ViolatesAvoid)))
            : body;
    }

    /// <summary>The header rows, whole; None when they do not fit with the line below them.</summary>
    private static Result<Maybe<Content>, Error> PlaceHeader(
        TableGeometry table, Content content, int? nextBody, CollapsedBorders.LineContext belowContext, double height, bool isRepeated)
    {
        var grid = table.Grid;
        var cells = grid.CellsStartingInRows(0, grid.HeaderRowCount - 1)
            .Select(index => new TableSegments.ActiveCell(index, grid.Cells[index].Row, null, false)).ToImmutableArray();
        return TableSegments.Measure(table, 0, grid.HeaderRowCount - 1, cells, content.Lines[^1]).OnSuccessMap(segment =>
        {
            var below = table.HorizontalWidth(grid.HeaderRowCount - 1, nextBody, belowContext) / 2;
            return Fitting.Fits(segment.Top + segment.Height + below, height)
                ? Maybe.From(TableSegments.PlaceWhole(table, content, segment, isRepeated))
                : Maybe<Content>.None;
        });
    }

    /// <summary>The body rows from the resume point, unit by unit, until one does not fit; then the best break
    /// (<see cref="TableBreaks"/>). With an early break, the page ends before its row, as it would have at that
    /// breakpoint. A complete page reports its best breakpoint before a unit as its early break.</summary>
    private static Result<Maybe<Outcome>, Error> PlaceBody(TableGeometry table, Content start, TracksBreakToken resume, double height, TracksEarlyBreak? breakAt)
    {
        var grid = table.Grid;
        var breakpoints = new List<TableBreaks.Breakpoint>();
        var content = start;
        var row = resume.NextRow;
        while (row < grid.Rows.Length)
        {
            var unit = grid.UnitOf(row);
            var active = TableSegments.ActiveCells(grid, row, unit.LastRow, row == resume.NextRow ? resume.Cells : []);
            var measured = TableSegments.Measure(table, row, unit.LastRow, active, content.Lines[^1]);
            if (measured.IsFailure)
            {
                return Result<Maybe<Outcome>, Error>.Failure(measured.Error);
            }

            var segment = measured.Value;
            if (content.HasBody)
            {
                var before = TableBreaks.BeforeUnit(grid, content, row);
                if (breakAt is { } target && target.Row == row)
                {
                    return TableBreaks.TakeBest(table, [before with { Appeal = target.Appeal }], height);
                }

                breakpoints.Add(before);
            }

            if (!Fitting.Fits(segment.Top + segment.Height + InsideBelow(table, unit.LastRow), height))
            {
                breakpoints.AddRange(TableBreaks.InsideUnit(table, content, segment, height));
                return TableBreaks.TakeBest(table, breakpoints, height);
            }

            content = TableSegments.PlaceWhole(table, content, segment, isRepeated: false) with { HasBody = true };
            row = unit.LastRow + 1;
            if (row < grid.Rows.Length)
            {
                content = content with { Lines = content.Lines.Add(new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, content.Bottom)) };
            }
        }

        var best = breakpoints
            .Where(breakpoint => breakpoint.Kind == TableBreaks.BreakKind.BeforeUnit)
            .OrderByDescending(breakpoint => breakpoint.Appeal)
            .ThenByDescending(breakpoint => breakpoint.Row)
            .Select(breakpoint => new TracksEarlyBreak(breakpoint.Row, breakpoint.Appeal))
            .FirstOrDefault();
        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(Complete(table, content) with { BestBreak = best }));
    }

    internal static int? Next(TableGrid grid, int row) => row + 1 < grid.Rows.Length ? row + 1 : null;

    /// <summary>True when an attempt placed nothing, or only by a last-resort cut (<see cref="TableBreaks.TakeBest"/>).</summary>
    private static bool OnlyLastResort(Maybe<Outcome> outcome) => outcome.HasNoValue || outcome.Value.Appeal == BreakAppeal.LastResort;

    /// <summary>The context of the line below a row: the table's bottom border joins it when it is the last row.</summary>
    internal static CollapsedBorders.LineContext EdgeContext(TableGrid grid, int row) =>
        row == grid.Rows.Length - 1 ? new CollapsedBorders.LineContext(true, false, new HashSet<int>()) : CollapsedBorders.LineContext.Interior;

    /// <summary>The context of the line below a row when the page ends there or inside the row: the table's bottom
    /// border for the last row; with clone a line that closes every cell it cuts (see remarks).</summary>
    internal static CollapsedBorders.LineContext PageEndContext(TableGeometry table, int row) =>
        row == table.Grid.Rows.Length - 1 ? EdgeContext(table.Grid, row)
        : IsCloned(table) ? CollapsedBorders.LineContext.ClonedBreak
        : CollapsedBorders.LineContext.Interior;

    /// <summary>True when the table repeats whole grid lines at a break (see remarks).</summary>
    internal static bool IsCloned(TableGeometry table) => table.Box.Style.DecorationBreak == BoxDecorationBreak.Clone;

    /// <summary>The line that ends a page at a break between rows: its upper half, or the whole line (see remarks).</summary>
    internal static LineAt? BottomLine(TableGeometry table, int? above, int? below, double y) =>
        above is null ? null
        : IsCloned(table) ? new LineAt(above, below, CollapsedBorders.LineContext.ClonedBreak, y)
        : new LineAt(above, below, CollapsedBorders.LineContext.Interior, y, LinePart.UpperHalf);

    /// <summary>The line that starts a page after a break between rows, resolved from the row before the break and the
    /// first row of this page: its lower half at the table's top, or the whole line, centred half its width down. With
    /// clone after a cut inside the table's first row there is no row before: the line is the table's top line.</summary>
    private static LineAt ContinuationLine(TableGeometry table, int firstRow, CollapsedBorders.LineContext context)
    {
        if (!IsCloned(table))
        {
            return new LineAt(firstRow - 1, firstRow, context, 0, LinePart.LowerHalf);
        }

        var (above, lineContext) = firstRow == 0
            ? ((int?)null, new CollapsedBorders.LineContext(true, false, new HashSet<int>()))
            : (firstRow - 1, context);
        return new LineAt(above, firstRow, lineContext, table.HorizontalWidth(above, firstRow, lineContext) / 2);
    }

    /// <summary>What a row needs below its bottom edge on this page, if the page ends there: for the table's last row,
    /// the half of the table's bottom border inside the table; for any other row, with clone the half of the line below
    /// it that lies under its centre (the line that closes the table at the break, see remarks), and with slice nothing,
    /// because the line is split at its centre.</summary>
    internal static double InsideBelow(TableGeometry table, int row) =>
        row == table.Grid.Rows.Length - 1 || IsCloned(table) ? table.HorizontalWidth(row, Next(table.Grid, row), PageEndContext(table, row)) / 2
        : 0;

    internal static Content Empty(double top) => new([], [], [], top, false);

    /// <summary>The table is complete on this page: the line below its last row joins the table's bottom border.</summary>
    internal static Outcome Complete(TableGeometry table, Content content) =>
        new(content, content.LastRow is { } last ? new LineAt(last, null, EdgeContext(table.Grid, last), content.Bottom) : null, null, BreakAppeal.Perfect);
}
