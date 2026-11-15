using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

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
/// this page fills the page (§5.3). Each breakpoint has an appeal (<see cref="BreakAppeal"/>): a break inside a row or
/// a row group marked <c>break-inside: avoid</c> violates it, so does a break between two rows of a unit (it cuts a
/// spanning cell) and a break at <c>break-before</c>/<c>break-after: avoid</c>. The page breaks at the latest
/// breakpoint of the highest appeal (research §7), as in <see cref="FlowLayout"/>.</para>
/// <para><b>The grid line at a break between rows</b> is split along its centre: its upper half ends this page's
/// fragment and its lower half starts the next one. In the collapsing border model half of a line lies in the cells on
/// each side of it (CSS 2.1 §17.6.2), and a box cut by a break gets no border at the cut (CSS Fragmentation 3 §5.4,
/// <c>box-decoration-break: slice</c>), so each page keeps the half that belongs to its own rows — Chromium draws a
/// broken table the same way. A row therefore fits when it fits down to the centre of the line below it; only the
/// last row of the table also needs the half of the table's bottom border that lies inside the table. A break inside a
/// row is a breakpoint only when it cuts content: a row whose cells all end on this page is never split into a full
/// row and an empty one.</para>
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
    /// continuation and the appeal of the break.</summary>
    public sealed record Outcome(Content Content, LineAt? BottomLine, TracksBreakToken? Continuation, BreakAppeal Appeal);

    /// <summary>A cell being laid out on this page.</summary>
    /// <param name="Cell">The cell index.</param>
    /// <param name="StartRow">Its first row on this page.</param>
    /// <param name="Token">Its break token, or null when it starts here.</param>
    /// <param name="IsComplete">True when its content is complete and it only goes on covering rows.</param>
    private sealed record ActiveCell(int Cell, int StartRow, BreakToken? Token, bool IsComplete);

    /// <summary>A unit, or the rest of one, measured with unlimited height.</summary>
    /// <param name="FirstRow">Its first row on this page.</param>
    /// <param name="LastRow">Its last row.</param>
    /// <param name="Cells">The cells that start in it, or continue into it.</param>
    /// <param name="Heights">Each row's height.</param>
    /// <param name="Natural">Each cell's fragment at its natural height; absent for a complete cell.</param>
    /// <param name="Above">The grid line above its first row; its centre is where the segment starts.</param>
    private sealed record Segment(
        int FirstRow, int LastRow, ImmutableArray<ActiveCell> Cells, ImmutableArray<double> Heights, ImmutableDictionary<int, BoxFragment> Natural, LineAt Above)
    {
        public double Top => Above.Y;

        public double RowTop(int row) => Top + Heights.Take(row - FirstRow).Sum();

        public double RowsHeight(int first, int last) => Heights.Skip(first - FirstRow).Take(last - first + 1).Sum();

        public double Height => Heights.Sum();
    }

    /// <summary>A breakpoint on this page and how to take it.</summary>
    /// <param name="Position">Its order on the page (later = more content).</param>
    /// <param name="Appeal">Its appeal.</param>
    /// <param name="Take">Builds the outcome; None when breaking there would place no body content.</param>
    private sealed record Candidate(int Position, BreakAppeal Appeal, Func<Result<Maybe<Outcome>, Error>> Take);

    /// <summary>Lays out one page of the table and builds its fragment.</summary>
    public static Result<LayoutResult, Error> LayOut(TableGeometry table, ConstraintSpace space, TracksBreakToken? incoming)
    {
        var grid = table.Grid;
        var isFirst = incoming is null;
        var resume = incoming ?? new TracksBreakToken(grid.HeaderRowCount, [], false);
        var hasHeader = grid.HeaderRowCount > 0;
        var outcome = Attempt(table, space, resume, isFirst, hasHeader);

        // A later page that cannot hold the header and some body content goes on without the header (see remarks).
        if (!isFirst && hasHeader && outcome.IsSuccess && outcome.Value.HasNoValue)
        {
            outcome = Attempt(table, space, resume, isFirst, includeHeader: false);
        }

        if (outcome.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(outcome.Error);
        }

        return outcome.Value.HasValue
            ? LayoutResult.Place(TableFragments.Build(table, outcome.Value.Value), outcome.Value.Value.Continuation, outcome.Value.Value.Appeal)
            : LayoutResult.NothingFits(table.Box, space);
    }

    /// <summary>One try at the page, with or without the header rows on top. None when no body content fits.</summary>
    private static Result<Maybe<Outcome>, Error> Attempt(TableGeometry table, ConstraintSpace space, TracksBreakToken resume, bool isFirst, bool includeHeader)
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
        // so its first row starts at the top (see remarks).
        var continuesAfterBreak = !isFirst && !includeHeader && !resume.RowIsSplit;
        var top = continuesAfterBreak ? 0 : table.HorizontalWidth(null, firstRow, topContext) / 2;
        var content = Empty(top) with
        {
            Lines = [new LineAt(null, firstRow, topContext, top, continuesAfterBreak ? LinePart.LowerHalf : LinePart.Whole)],
        };
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

        var body = PlaceBody(table, content, resume, height);

        // The header alone at the start of an empty page: nothing better is possible there (see remarks).
        return body.IsSuccess && body.Value.HasNoValue && includeHeader && isFirst && space.IsAtPageStart
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
        var cells = grid.Cells.Select((cell, index) => (cell, index)).Where(pair => pair.cell.Row < grid.HeaderRowCount)
            .Select(pair => new ActiveCell(pair.index, pair.cell.Row, null, false)).ToImmutableArray();
        return Measure(table, 0, grid.HeaderRowCount - 1, cells, content.Lines[^1]).OnSuccessMap(segment =>
        {
            var below = table.HorizontalWidth(grid.HeaderRowCount - 1, nextBody, belowContext) / 2;
            return Fitting.Fits(segment.Top + segment.Height + below, height)
                ? Maybe.From(PlaceWhole(table, content, segment, isRepeated))
                : Maybe<Content>.None;
        });
    }

    /// <summary>The body rows from the resume point, unit by unit, until one does not fit; then the best break.</summary>
    private static Result<Maybe<Outcome>, Error> PlaceBody(TableGeometry table, Content start, TracksBreakToken resume, double height)
    {
        var grid = table.Grid;
        var candidates = new List<Candidate>();
        var content = start;
        var row = resume.NextRow;
        while (row < grid.Rows.Length)
        {
            var unit = grid.UnitOf(row);
            var active = ActiveCells(grid, row, unit.LastRow, row == resume.NextRow ? resume.Cells : []);
            var measured = Measure(table, row, unit.LastRow, active, content.Lines[^1]);
            if (measured.IsFailure)
            {
                return Result<Maybe<Outcome>, Error>.Failure(measured.Error);
            }

            var segment = measured.Value;
            if (content.HasBody)
            {
                // Breaking before this row: the line above it becomes the bottom line of the page. The candidate keeps
                // its own copies of the row and the content; `row` and `content` move on below.
                var (before, next, appeal) = (content with { Lines = content.Lines.RemoveAt(content.Lines.Count - 1) }, row, BeforeAppeal(grid, row));
                candidates.Add(new Candidate(2 * next, appeal, () => Result<Maybe<Outcome>, Error>.Success(Maybe.From(
                    new Outcome(before, BottomLine(table, before.LastRow, next, before.Bottom), new TracksBreakToken(next, [], false), appeal)))));
            }

            if (!Fitting.Fits(segment.Top + segment.Height + InsideBelow(table, unit.LastRow), height))
            {
                candidates.AddRange(InnerCandidates(table, content, segment, height));
                return Best(candidates);
            }

            content = PlaceWhole(table, content, segment, isRepeated: false) with { HasBody = true };
            row = unit.LastRow + 1;
            if (row < grid.Rows.Length)
            {
                content = content with { Lines = content.Lines.Add(new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, content.Bottom)) };
            }
        }

        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(Complete(table, content)));
    }

    /// <summary>
    /// The breakpoints inside a unit that does not fit: between two of its rows whose upper part fits (a spanning
    /// cell is cut there, so it violates an avoid), and inside the first row that does not fit.
    /// </summary>
    private static IEnumerable<Candidate> InnerCandidates(TableGeometry table, Content content, Segment segment, double height)
    {
        var grid = table.Grid;
        for (var row = segment.FirstRow; row <= segment.LastRow; row++)
        {
            var top = segment.RowTop(row);
            var cutRow = row;
            if (row > segment.FirstRow && Fitting.Fits(top, height))
            {
                yield return new Candidate(2 * row, BreakAppeal.ViolatesAvoid, () => Cut(table, content, segment, cutRow, top, inside: false, BreakAppeal.ViolatesAvoid));
            }

            if (!Fitting.Fits(top + segment.Heights[row - segment.FirstRow] + InsideBelow(table, row), height) || row == segment.LastRow)
            {
                var appeal = InsideAppeal(grid, row);
                yield return new Candidate(2 * row + 1, appeal, () => Cut(table, content, segment, cutRow, height, inside: true, appeal));
                yield break;
            }
        }
    }

    /// <summary>The latest breakpoint of the highest appeal that places body content.</summary>
    private static Result<Maybe<Outcome>, Error> Best(List<Candidate> candidates)
    {
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Appeal).ThenByDescending(candidate => candidate.Position))
        {
            var taken = candidate.Take();
            if (taken.IsFailure || taken.Value.HasValue)
            {
                return taken;
            }
        }

        return Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
    }

    /// <summary>
    /// Takes a break inside a segment: the rows above <paramref name="cutRow"/> whole; the cells that reach past the
    /// cut laid out again in the height down to <paramref name="cutY"/> — the row's top for a break between rows, the
    /// page end for a break inside the row (that row then fills the page). None when a break inside the row would
    /// place nothing of it, or leave nothing of it for the next page.
    /// </summary>
    private static Result<Maybe<Outcome>, Error> Cut(
        TableGeometry table, Content content, Segment segment, int cutRow, double cutY, bool inside, BreakAppeal appeal)
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
                cells = cells.Add(Whole(table, segment, cell));
                continue;
            }

            if (cell.StartRow > cutRow || (cell.StartRow == cutRow && !inside))
            {
                continue;
            }

            var top = segment.RowTop(cell.StartRow);
            var sliced = Slice(table, segment, cell, cutY - top);
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
        var bottomLine = inside ? null : BottomLine(table, cutRow - 1, cutRow, bottom);
        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(new Outcome(placed, bottomLine, new TracksBreakToken(cutRow, continuing.ToImmutable(), inside), appeal)));
    }

    /// <summary>A cut cell: its content laid out in the slice height; an empty slice when it is complete or nothing
    /// of it fits. Returns the fragment, stretched to the slice, and where the cell continues.</summary>
    private static Result<(BoxFragment Fragment, CellBreak Next), Error> Slice(TableGeometry table, Segment segment, ActiveCell cell, double sliceHeight)
    {
        var info = table.Grid.Cells[cell.Cell];
        var box = CellLayoutBox(table, segment, cell);
        var width = table.CellWidth(info);
        if (cell.IsComplete)
        {
            return Result<(BoxFragment, CellBreak), Error>.Success((EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, null, true)));
        }

        var space = new ConstraintSpace(width, sliceHeight, IsAtPageStart: false, IsNewFormattingContext: true);
        return table.Environment.Layout(box, space, cell.Token).OnSuccessMap(result => result switch
        {
            LayoutResult.Placed placed => (Stretch(placed.Fragment, sliceHeight, VerticalAlign.Top),
                new CellBreak(cell.Cell, placed.Continuation, placed.Continuation is null)),
            _ => (EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, cell.Token, false)),
        });
    }

    /// <summary>Places a measured segment whole: its rows at their heights, every cell as tall as its rows.</summary>
    private static Content PlaceWhole(TableGeometry table, Content content, Segment segment, bool isRepeated)
    {
        var rows = content.Rows.AddRange(Enumerable.Range(segment.FirstRow, segment.LastRow - segment.FirstRow + 1)
            .Select(row => new PlacedRow(row, segment.RowTop(row), segment.Heights[row - segment.FirstRow], isRepeated)));
        var cells = content.Cells.AddRange(segment.Cells.Select(cell => Whole(table, segment, cell)));
        var lines = content.Lines.AddRange(Enumerable.Range(segment.FirstRow + 1, segment.LastRow - segment.FirstRow)
            .Select(row => new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, segment.RowTop(row))));
        return content with { Rows = rows, Cells = cells, Lines = lines, Bottom = segment.Top + segment.Height };
    }

    /// <summary>A whole cell: its natural fragment stretched to the rows it covers, content aligned (§17.5.3).</summary>
    private static PlacedCell Whole(TableGeometry table, Segment segment, ActiveCell cell)
    {
        var info = table.Grid.Cells[cell.Cell];
        var height = segment.RowsHeight(cell.StartRow, info.LastRow);
        var fragment = segment.Natural.TryGetValue(cell.Cell, out var natural)
            ? Stretch(natural, height, info.Box.Style.VerticalAlign)
            : EmptyCell(CellLayoutBox(table, segment, cell), table.CellWidth(info), height);
        return new PlacedCell(cell.Cell, cell.StartRow, fragment);
    }

    /// <summary>
    /// Measures a segment: every cell laid out with unlimited height, then the row heights (see remarks). A cell that
    /// still breaks with unlimited height holds a forced page break, which a table cell cannot honour.
    /// </summary>
    private static Result<Segment, Error> Measure(TableGeometry table, int firstRow, int lastRow, ImmutableArray<ActiveCell> cells, LineAt above)
    {
        var shell = new Segment(firstRow, lastRow, cells, [], ImmutableDictionary<int, BoxFragment>.Empty, above);
        return cells.Where(cell => !cell.IsComplete)
            .TraverseAll(cell => table.Environment
                .Layout(CellLayoutBox(table, shell, cell), new ConstraintSpace(table.CellWidth(table.Grid.Cells[cell.Cell]), double.PositiveInfinity, false, true), cell.Token)
                .OnSuccessBind(result => result is LayoutResult.Placed { Continuation: null } placed
                    ? Result<KeyValuePair<int, BoxFragment>, Error>.Success(KeyValuePair.Create(cell.Cell, placed.Fragment))
                    : Result<KeyValuePair<int, BoxFragment>, Error>.Failure(
                        LayoutErrors.InvalidContent(table.Grid.Cells[cell.Cell].Box, "a table cell cannot hold a forced page break."))))
            .OnSuccessMap(natural =>
            {
                var fragments = natural.ToImmutableDictionary();
                return shell with { Heights = RowHeights(table.Grid, firstRow, lastRow, cells, fragments), Natural = fragments };
            });
    }

    /// <summary>The row heights of §17.5.3 for one segment: single-row cells first, then each spanning cell grows the
    /// last row it spans by what is missing.</summary>
    private static ImmutableArray<double> RowHeights(
        TableGrid grid, int firstRow, int lastRow, ImmutableArray<ActiveCell> cells, ImmutableDictionary<int, BoxFragment> natural)
    {
        var heights = new double[lastRow - firstRow + 1];
        double HeightOf(ActiveCell cell) => natural.TryGetValue(cell.Cell, out var fragment) ? fragment.Size.Height : 0;
        foreach (var cell in cells.Where(cell => grid.Cells[cell.Cell].LastRow == cell.StartRow))
        {
            heights[cell.StartRow - firstRow] = Math.Max(heights[cell.StartRow - firstRow], HeightOf(cell));
        }

        foreach (var cell in cells.Where(cell => grid.Cells[cell.Cell].LastRow > cell.StartRow)
                     .OrderBy(cell => grid.Cells[cell.Cell].LastRow).ThenByDescending(cell => cell.StartRow))
        {
            var last = grid.Cells[cell.Cell].LastRow;
            var missing = HeightOf(cell) - heights.Skip(cell.StartRow - firstRow).Take(last - cell.StartRow + 1).Sum();
            heights[last - firstRow] += Math.Max(0, missing);
        }

        return [.. heights];
    }

    /// <summary>The cells of a segment: those that continue from the previous page, then those that start in it.</summary>
    private static ImmutableArray<ActiveCell> ActiveCells(TableGrid grid, int firstRow, int lastRow, ValueList<CellBreak> continuing)
    {
        var resumed = continuing.Select(cell => new ActiveCell(cell.Cell, firstRow, cell.Token, cell.IsComplete)).ToImmutableArray();
        var fresh = grid.Cells.Select((cell, index) => (cell, index))
            .Where(pair => pair.cell.Row >= firstRow && pair.cell.Row <= lastRow && !continuing.Any(resume => resume.Cell == pair.index))
            .Select(pair => new ActiveCell(pair.index, pair.cell.Row, null, false));
        return [.. resumed, .. fresh];
    }

    /// <summary>
    /// The box a cell is laid out as on this page (<see cref="TracksLayout.CellBox"/>): half of the vertical lines
    /// beside it, half of the line above it when it starts here (the line above the segment, or the line between two
    /// of its rows), and half of the line below its last row. A cell that continues from an earlier page has no top
    /// inset (slice); its token already tells its algorithm so.
    /// </summary>
    private static Box CellLayoutBox(TableGeometry table, Segment segment, ActiveCell cell)
    {
        var grid = table.Grid;
        var info = grid.Cells[cell.Cell];
        var startsSegment = cell.StartRow == segment.FirstRow;
        var above = startsSegment ? segment.Above.Above : cell.StartRow - 1;
        var context = startsSegment ? segment.Above.Context : CollapsedBorders.LineContext.Interior;
        var top = cell.Token is null ? table.HorizontalWidth(above, cell.StartRow, context) / 2 : 0;
        var bottom = table.HorizontalWidth(info.LastRow, Next(grid, info.LastRow), EdgeContext(grid, info.LastRow)) / 2;
        var halves = new Edges(top.Pt(), (table.LineWidths[info.Column + info.ColumnSpan] / 2).Pt(), bottom.Pt(), (table.LineWidths[info.Column] / 2).Pt());
        return TracksLayout.CellBox(info, halves);
    }

    private static BoxFragment EmptyCell(Box box, double width, double height) => new(box, new Size(width, height), []);

    /// <summary>A cell fragment made as tall as its rows; with middle or bottom alignment its content moves down by half
    /// or all of the extra height (§17.5.3). Background and box keep the full height.</summary>
    private static BoxFragment Stretch(BoxFragment fragment, double height, VerticalAlign align)
    {
        var extra = Math.Max(0, height - fragment.Size.Height);
        var shift = align switch
        {
            VerticalAlign.Middle => extra / 2,
            VerticalAlign.Bottom => extra,
            _ => 0,
        };
        ValueList<PlacedFragment> children = [.. fragment.Children.Select(child => child with { Offset = child.Offset + new Point(0, shift) })];
        return new BoxFragment(fragment.Box, new Size(fragment.Size.Width, Math.Max(height, fragment.Size.Height)), children, fragment.Border, fragment.IsRepeated);
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

    private static int? Next(TableGrid grid, int row) => row + 1 < grid.Rows.Length ? row + 1 : null;

    /// <summary>The context of the line below a row: the table's bottom border joins it when it is the last row.</summary>
    private static CollapsedBorders.LineContext EdgeContext(TableGrid grid, int row) =>
        row == grid.Rows.Length - 1 ? new CollapsedBorders.LineContext(true, false, new HashSet<int>()) : CollapsedBorders.LineContext.Interior;

    /// <summary>The line that ends a page at a break between rows: its upper half (see remarks).</summary>
    private static LineAt? BottomLine(TableGeometry table, int? above, int? below, double y) =>
        above is null ? null : new LineAt(above, below, CollapsedBorders.LineContext.Interior, y, LinePart.UpperHalf);

    /// <summary>What a row needs below its bottom edge on this page: for the table's last row, the half of the table's
    /// bottom border inside the table; for any other row nothing, because a break below it splits the line there.</summary>
    private static double InsideBelow(TableGeometry table, int row) =>
        row == table.Grid.Rows.Length - 1 ? table.HorizontalWidth(row, null, EdgeContext(table.Grid, row)) / 2 : 0;

    private static Content Empty(double top) => new([], [], [], top, false);

    /// <summary>The table is complete on this page: the line below its last row joins the table's bottom border.</summary>
    private static Outcome Complete(TableGeometry table, Content content) =>
        new(content, content.LastRow is { } last ? new LineAt(last, null, EdgeContext(table.Grid, last), content.Bottom) : null, null, BreakAppeal.Perfect);
}
