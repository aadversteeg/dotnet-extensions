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
/// later fragment, when they are at most a quarter of the page area the table's first fragment is laid out in, as
/// Chromium prints (<see cref="RepeatsHeader"/>, decided once); a taller header is shown on the table's first page only. They never break. A break right after the header rows, with no body row on the page, is not a
/// breakpoint, so a header is never left alone at a page end — except at the start of an empty page, where nothing
/// would be gained by moving on. There, below a header that repeats and under <see cref="EmptyRowParts.Allow"/>, the
/// page ends with the first body row cut below the header instead, where only such a cut places it (a last resort, see
/// above): a cell whose first content, an image or a line, does not fit below the header is drawn empty down to the page
/// end, its side lines and no line below it, the other cells draw what they hold there (text, a background), and the
/// content goes on in its part on the next page, as Chromium prints it (<see cref="CutsBelowHeader"/>). The row has then
/// moved on once (<see cref="TracksBreakToken.CutBelowHeader"/>): on the next page its cells go on below the repeated
/// header, where content taller than the room is sliced, beside the rest of the other cells. The cells of the cut row
/// lie below the header as below start insets (<see cref="TableGeometry.CutsBelowHeaderRows"/>), so a table nested in
/// such a cell starts on that page and cuts its own first row as at the top of a page, as Chromium prints it (case
/// <c>a4-header-nested-image-228</c>: the nested table's top line on page 1, its image from page 2). On a later page whose height cannot hold the header and some
/// body content, the header is left out, so the table always makes progress.</para>
/// <para><b>Content taller than the page area under a repeated header row</b> (<see cref="MonolithicOverflow.Slice"/>
/// only): it is sliced right below the header, on a page after one that showed no body row other than slices of a
/// piece that was itself sliced (<see cref="TracksBreakToken.BodyShown"/>); after a page that did show a body row, the
/// page holds only the header (under <see cref="EmptyRowParts.Allow"/> the header and the row cut empty below it, as
/// above) and the row moves on once. The header stays above every later slice, and each slice is
/// as tall as the room below it, so the slices show the piece once. Chromium starts the first slice on the same page
/// and at the same place in every measured case (<c>tools/reference/chromium</c>, cases <c>a4-header-*</c>), the page
/// before it included; on the later pages it moves the piece up a whole page area and either leaves the
/// header out or draws it over the piece, which the engine does not copy (docs/architecture.md). The first row's cells
/// then start the page through the line above them (<see cref="TableSegments.Slice"/>), so the piece starts where a
/// whole cell's content starts. Whether the piece is taller than the page area is found by laying the page out without
/// the header in that way: only if it is sliced even then. A piece not sliced yet that is taller than the room below the
/// header but not than the page area goes on without the header, as any body content does; the last slice of a sliced
/// piece stays below the header however tall it is. All of this under <see cref="RepeatedHeaderRows.AboveEverySlice"/>.
/// Under <see cref="RepeatedHeaderRows.WhereContentIsLaidOut"/>, as Chromium prints it, such a piece is sliced below the
/// header also when it is no taller than the page area, and a later page where all that is left of the table is the rest
/// of pieces laid out before (<see cref="OnlyRestLeft"/>) is laid out without the header, so the rest starts at the top
/// of the page area; where a row is still to come or a cell of the piece's row beside it goes on, also an empty one
/// (<see cref="OnlySlicesGoOn"/>), the header stays above the slice (Chromium draws it over the slice there, which the
/// engine does not copy).</para>
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

        /// <summary>Nothing of the line, though the page keeps the room of its lower half: the line at the start of the
        /// next page where a cell that spans rows is cut by the break between them, or below part of a cell that passed
        /// the page end (see <see cref="ContinuationLine"/>).</summary>
        Unpainted,
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
    public sealed record Outcome(Content Content, LineAt? BottomLine, TracksBreakToken? Continuation, BreakAppeal Appeal, TracksEarlyBreak? BestBreak = null)
    {
        /// <summary>True when the page ends in a cut whose cell keeps its break (<see cref="LayoutResult.Placed.KeepsItsBreak"/>):
        /// the table keeps it too, at the cut's appeal, in which the cell's kept appeal stands
        /// (<see cref="LayoutResult.Placed.KeptAppeal"/>).</summary>
        public bool KeepsItsBreak { get; init; }

        /// <summary>True when the page ends with its first body row cut right below the header rows
        /// (<see cref="CutsBelowHeader"/>): the row has moved on once, so on the next page its content goes on below the
        /// repeated header, and content taller than the room there is sliced there.</summary>
        public bool CutBelowHeader { get; init; }
    }

    /// <summary>Lays out one page of the table and builds its fragment.</summary>
    public static Result<LayoutResult, Error> LayOut(TableGeometry geometry, ConstraintSpace space, TracksBreakToken? incoming)
    {
        var grid = geometry.Grid;
        if (incoming is { Passed: > 0 } passed)
        {
            return passed.NextRow == grid.Rows.Length ? PlacePassed(geometry, space, passed) : BelowPassed(geometry, space, passed);
        }

        var isFirst = incoming is null;
        var decided = RepeatsHeader(geometry, space, incoming);
        if (decided.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(decided.Error);
        }

        var (repeatsHeader, repeatedHeight) = decided.Value;
        var table = geometry with { CellFragmentainerBlockSize = Math.Max(0, space.FragmentainerBlockSize - Reserved(geometry, repeatsHeader, repeatedHeight, isFirst)) };
        var resume = incoming ?? new TracksBreakToken(grid.HeaderRowCount, [], false) { RepeatsHeader = repeatsHeader };
        var hasHeader = isFirst ? grid.HeaderRowCount > 0 : repeatsHeader;
        var breakAt = space.BreakAt switch
        {
            null => null,
            TracksEarlyBreak row => row,
            _ => throw new ArgumentException($"Tracks received a {space.BreakAt.GetType().Name} it did not report.", nameof(space)),
        };
        // Under RepeatedHeaderRows.WhereContentIsLaidOut a later page that only shows the rest of content laid out on an
        // earlier page draws no header rows; the rest starts at the top of the page area (see remarks).
        var leavesOutHeader = !isFirst && hasHeader && LaysOutBelowHeader(table)
            ? OnlyRestLeft(geometry, space, resume)
            : Result<bool, Error>.Success(false);
        if (leavesOutHeader.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(leavesOutHeader.Error);
        }

        if (leavesOutHeader.Value)
        {
            table = geometry with { CellFragmentainerBlockSize = space.FragmentainerBlockSize };
            hasHeader = false;
        }

        // After a page that ended with the first body row cut right below the header, that row's cells go on below the
        // repeated header, where content taller than the room is sliced (see remarks).
        var slicesAfterCut = !isFirst && hasHeader && resume.CutBelowHeader && space.IsAtPageStart
            && table.Environment.Profile.MonolithicOverflow == MonolithicOverflow.Slice;
        var outcome = Attempt(slicesAfterCut ? table with { SlicesBelowHeader = true } : table, space, resume, isFirst, hasHeader, breakAt);

        // A later page that cannot hold the header and some body content that shows goes on without the header (see
        // remarks): under the header the body has no room, or only a last-resort cut. Content taller than the page area,
        // which would be sliced anyway, is sliced below the header instead, after a page with only the header (see
        // remarks). A page that goes on with the next slice of such content keeps the header even when its cut is a last
        // resort: the later slices of a line draw nothing (its glyphs lie in one slice), so no part of the row shows.
        if (!isFirst && hasHeader && outcome.IsSuccess && OnlyLastResort(outcome.Value) && !ContinuesSlice(outcome.Value))
        {
            // Taller than the page area: laid out on the page without the header, with the first row's cells starting the
            // page (through the line above them), it would still be sliced.
            var sliced = table with { SlicesBelowHeader = true };
            var slices = space.IsAtPageStart && table.Environment.Profile.MonolithicOverflow == MonolithicOverflow.Slice;
            var taller = slices
                ? Attempt(sliced, space, resume, isFirst, includeHeader: LaysOutBelowHeader(table), breakAt)
                : Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
            var belowHeader = taller.IsFailure || (taller.Value.HasValue && StartsSlice(taller.Value.Value))
                ? taller.OnSuccessBind(_ => resume.BodyShown
                    ? Attempt(table, space, resume, isFirst, includeHeader: true, breakAt, headerAlone: true)
                    : Attempt(sliced, space, resume, isFirst, includeHeader: true, breakAt))
                : Result<Maybe<Outcome>, Error>.Success(Maybe<Outcome>.None);
            if (belowHeader.IsFailure || (belowHeader.Value.HasValue && (resume.BodyShown || !OnlyLastResort(belowHeader.Value))))
            {
                outcome = belowHeader;
            }
            else
            {
                var without = Attempt(table, space, resume, isFirst, includeHeader: false, breakAt);
                outcome = without.IsFailure || (without.Value.HasValue && !OnlyLastResort(without.Value)) || outcome.Value.HasNoValue ? without : outcome;
            }
        }

        if (outcome.IsFailure)
        {
            return Result<LayoutResult, Error>.Failure(outcome.Error);
        }

        // A table with separated borders breaks only where a measured case says how (see SeparatedBreakIsDrawn).
        if (table.IsSeparated && outcome.Value.HasValue && !SeparatedBreakIsDrawn(table, incoming, outcome.Value.Value))
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.TableBreak(table.Box, space.AvailableHeight));
        }

        // A last-resort cut (a part of a row that shows nothing, TableBreaks.CutShows) is taken only where the table
        // cannot move: at the top of a page, or below nothing but the top insets of boxes that start the page or the margins
        // kept there after a forced break, where a break before the table would be a last resort as well
        // (ConstraintSpace.IsBelowStartInsets). Lower on the page the
        // table reports no room, and its parent breaks before it instead, which leaves no strip of the row behind.
        if (outcome.Value.HasValue && outcome.Value.Value.Appeal == BreakAppeal.LastResort && !space.IsAtPageStart && !space.IsBelowStartInsets)
        {
            return LayoutResult.NothingFits(table.Box, space);
        }

        if (outcome.Value.HasNoValue)
        {
            return LayoutResult.NothingFits(table.Box, space);
        }

        var placed = outcome.Value.Value;
        var fragment = TableFragments.Build(table, placed, incoming, space.AvailableHeight);
        if (table.IsSeparated && !SeparatedFits(fragment, space))
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.TableBreak(table.Box, space.AvailableHeight));
        }

        var passes = PassesPageArea(table, fragment, space);
        var carries = passes && table.Environment.Profile.TableEndOverflow == TableEndOverflow.ContinuesOnNextPage
            && (placed.Continuation is null || (!placed.Continuation.RowIsSplit && !repeatsHeader));
        var continuation = carries
            ? Passed(placed.Continuation ?? new TracksBreakToken(grid.Rows.Length, [], false), table, placed.Content, fragment, space.AvailableHeight)
            : placed.Continuation;
        return LayoutResult.Place(
            passes ? WithinPageArea(fragment, space) : fragment,
            continuation is null ? null : continuation with { BodyShown = ShowsBody(table, placed.Content), CutBelowHeader = placed.CutBelowHeader, RepeatsHeader = repeatsHeader },
            placed.Appeal,
            placed.BestBreak,
            placed.KeepsItsBreak,
            placed.Appeal);
    }

    /// <summary>
    /// Whether a page's fragment of a table with separated borders stays in the room it has on its page. Where it would
    /// pass the page end (a row that starts its page area and does not fit with the spacing, padding and border below it,
    /// <see cref="PassesOnlyByItsLineBelow"/>), it is refused (<see cref="LayoutErrorCodes.TableBreak"/>, at the table's
    /// source line): what Chromium prints there is not measured for separated borders.
    /// </summary>
    private static bool SeparatedFits(BoxFragment fragment, ConstraintSpace space) => Fitting.Fits(fragment.Size.Height, space.AvailableHeight);

    /// <summary>
    /// Whether a page of a table with separated borders breaks where a measured case says how Chromium prints it. Drawn:
    /// a break between two rows that no cell spans (cases <c>ts-break-rows</c>, <c>ts-break-header</c>, <c>ts-break-end</c>)
    /// and a break inside a row whose cells are all aligned at the top (<c>ts-break-cut</c>, <c>ts-break-move</c>), the
    /// table and the cut cells sliced (<see cref="TableFragments"/>). Refused (<see cref="LayoutErrorCodes.TableBreak"/>):
    /// a cell over several rows across the break (case <c>ts-break-span</c>: Chromium starts the next row one spacing below
    /// the page's top while the spanning cell's rest ends higher, a part of a cell the rows do not set; no such mechanism
    /// here); content taller than the page sliced in a cell, or a row cut empty below a repeated header (case
    /// <c>ts-break-image</c>: Chromium moves the image's row to the next page with its cells empty, slices the image on the
    /// pages after it and draws the table's last fragment twice); a cut row with a cell aligned at the middle or bottom
    /// (<see cref="CutRowAlignment"/> is not measured for separated borders); a table of
    /// <see cref="BoxDecorationBreak.Clone"/> (not measured for separated borders).
    /// </summary>
    private static bool SeparatedBreakIsDrawn(TableGeometry table, TracksBreakToken? incoming, Outcome outcome)
    {
        var grid = table.Grid;
        var breaks = outcome.Continuation is { } next ? [incoming, next] : new[] { incoming };
        foreach (var token in breaks)
        {
            if (token is null)
            {
                continue;
            }

            if (IsCloned(table) || token.Cells.Any(cell => grid.Cells[cell.Cell].RowSpan > 1))
            {
                return false;
            }

            if (token.RowIsSplit && token.Cells.Any(cell => grid.Cells[cell.Cell].Box.Style.VerticalAlign != VerticalAlign.Top))
            {
                return false;
            }
        }

        return !outcome.CutBelowHeader && !StartsSlice(outcome) && !ContinuesSlice(Maybe.From(outcome));
    }

    /// <summary>
    /// The token after a page whose table passes its page area below a row placed whole, under
    /// <see cref="TableEndOverflow.ContinuesOnNextPage"/>: what passed takes its height again at the top of the next page,
    /// painted where part of a cell's border box passed (cases <c>a2-row-end</c>, <c>-after</c>, <c>-6pt</c>,
    /// <c>-6pt-after</c>), else empty (only the table's outer half of its bottom line passed: cases
    /// <c>a2-row-end-first-row-half-*</c>). Where only part of that outer half passed, the next page takes the whole half,
    /// as Chromium moves the table's own bottom border that does not fit to the next page (cases
    /// <c>a2-row-end-thick-less-after</c>: 3 pt of a 6 pt line, 0.75 of them past the page end; <c>a4-cell-image-179-5</c>:
    /// 0.375 pt, 0.25 of them past). In the collapsing border model a cell's border box ends at the centre of the
    /// line below it (CSS 2.1 §17.6.2), so a cell passes where that centre lies below the page end. Where the table goes
    /// on after the row, at a break between rows, the next page lays its rows out below what passed (<see cref="BelowPassed"/>):
    /// the part of the line's upper half that passed the page end is then on the next page above the lower half (case
    /// <c>a2-row-end-span-after</c>: "after" 1.5 pt below the page's top, the whole line between the rows, the empty row and
    /// the table's bottom line). Not after a cut inside a row, and not in a table that repeats its header rows: there the
    /// table takes no more than the page area, and what passed does not continue.
    /// </summary>
    /// <param name="token">Where the table goes on: past its last row, or at a break before a row.</param>
    /// <param name="table">The table.</param>
    /// <param name="content">What the page holds.</param>
    /// <param name="fragment">The page's fragment, before it is cut to the page area.</param>
    /// <param name="end">The page end, from the table's top edge.</param>
    /// <returns>The token with what passed.</returns>
    private static TracksBreakToken Passed(TracksBreakToken token, TableGeometry table, Content content, BoxFragment fragment, double end)
    {
        var topOf = content.Rows.ToDictionary(row => row.Row, row => row.Top);
        var paints = content.Cells.Any(cell => !Fitting.Fits(topOf[cell.TopRow] + cell.Fragment.Size.Height, end));
        var outerHalfOnly = !paints && token.NextRow == table.Grid.Rows.Length;
        return token with
        {
            Passed = outerHalfOnly ? fragment.Size.Height - content.Bottom : fragment.Size.Height - end,
            PaintsPassed = paints,
            PassedLines = paints ? TableFragments.Below(fragment, end) : [],
        };
    }

    /// <summary>
    /// The next page of a table that passed the page end at a break between rows (<see cref="Passed"/>): the page's rows
    /// are laid out in the room below what passed, and the page's fragment starts with it, painted with the grid lines that
    /// passed when the token paints them. Where what passed does not fit, the table reports no room; at the top of a page
    /// it takes the page area and the rest passes again (<see cref="OverPageArea"/>).
    /// </summary>
    private static Result<LayoutResult, Error> BelowPassed(TableGeometry table, ConstraintSpace space, TracksBreakToken passed)
    {
        var rest = passed with { Passed = 0, PaintsPassed = false, PassedLines = [], FollowsPassed = passed.PaintsPassed };
        if (!Fitting.Fits(passed.Passed, space.AvailableHeight))
        {
            return !space.IsAtPageStart ? LayoutResult.NothingFits(table.Box, space)
                : TakesPageArea(space) ? OverPageArea(table, space, passed)
                : LayOut(table, space, rest);
        }

        var lines = passed.PaintsPassed ? passed.PassedLines : [];
        return LayOut(table, space with { AvailableHeight = space.AvailableHeight - passed.Passed }, rest)
            .OnSuccessMap(result => result is LayoutResult.Placed placed ? placed with { Fragment = Below(placed.Fragment, passed.Passed, lines) } : result);
    }

    /// <summary>A page's fragment moved down by <paramref name="by"/>, with <paramref name="lines"/> above it.</summary>
    private static BoxFragment Below(BoxFragment fragment, double by, ValueList<PlacedFragment> lines) =>
        new(
            fragment.Box,
            new Size(fragment.Size.Width, fragment.Size.Height + by),
            [.. lines, .. fragment.Children.Select(child => child with { Offset = new Point(child.Offset.X, child.Offset.Y + by) })],
            fragment.Border,
            fragment.IsRepeated);

    /// <summary>
    /// The next page's part of a table that passed the page end below its last row (<see cref="Passed"/>): as tall as what
    /// passed, with the grid lines that passed when the token paints them, and no row. Where it is taller than the page
    /// area at the top of a page, the page takes the page area and the rest passes again (<see cref="OverPageArea"/>).
    /// Lower on a page, where it does not fit, the table reports no room.
    /// </summary>
    private static Result<LayoutResult, Error> PlacePassed(TableGeometry table, ConstraintSpace space, TracksBreakToken passed)
    {
        var fits = Fitting.Fits(passed.Passed, space.AvailableHeight);
        if (!fits && !space.IsAtPageStart)
        {
            return LayoutResult.NothingFits(table.Box, space);
        }

        if (!fits && TakesPageArea(space))
        {
            return OverPageArea(table, space, passed);
        }

        var lines = passed.PaintsPassed ? passed.PassedLines : [];
        return LayoutResult.Place(new BoxFragment(table.Box, new Size(table.Width, fits ? passed.Passed : space.AvailableHeight), lines), null);
    }

    /// <summary>Whether a page at whose top what passed does not fit takes its page area of it and lets the rest pass
    /// again (<see cref="OverPageArea"/>): only where that area has a height, so every such page takes some of it.</summary>
    private static bool TakesPageArea(ConstraintSpace space) => space.AvailableHeight > Fitting.TolerancePoints;

    /// <summary>
    /// A page at whose top what passed is taller than the page area (bottom padding taller than a page, see
    /// <see cref="PassesByItsBottomPadding"/>): the page holds the page area of it, with the grid lines that passed when
    /// the token paints them, and the rest passes the page end again, with the lines below it, to the next page; there
    /// the rows after it, or what follows the table, start below it. Chromium goes on painting the cell's sides over as
    /// many pages as the padding takes and starts the next row below it (cases
    /// <c>fr-continued-pad-past-taller-than-page</c> and <c>-next-row</c>: 220 pt of padding, 190.75 pt past the page end,
    /// a page area of 150 pt: one whole page and 40.75 pt on the next). Without a row after the table Chromium starts what
    /// follows 0.75 pt below that page's top, over the padding; the engine starts it below the padding, as where what
    /// passed fits one page (a documented limit, listed for the first case). The work is bounded: every page takes the page
    /// area, more than zero (<see cref="TakesPageArea"/>), of a finite height, so the rest ends after at most that height
    /// divided by the page area pages.
    /// </summary>
    private static Result<LayoutResult, Error> OverPageArea(TableGeometry table, ConstraintSpace space, TracksBreakToken passed)
    {
        var lines = passed.PaintsPassed ? passed.PassedLines : [];
        var page = space.AvailableHeight;
        var rest = passed with
        {
            Passed = passed.Passed - page,
            PassedLines = TableFragments.Below(new BoxFragment(table.Box, new Size(table.Width, passed.Passed), lines), page),
        };
        return LayoutResult.Place(new BoxFragment(table.Box, new Size(table.Width, page), lines), rest);
    }

    /// <summary>Whether a page's body cells draw anything besides slices of content taller than the page
    /// (<see cref="TracksBreakToken.BodyShown"/>). A cell's own background is not counted: it fills every part of the
    /// cell on every page, whatever the part holds, as for a cut inside a row (<see cref="TableBreaks"/>, CutShows); so
    /// a page with only the rest of an image in a cell with a background draws no header rows, as Chromium prints it
    /// (case <c>a4-header-image-228-background</c>).</summary>
    private static bool ShowsBody(TableGeometry table, Content content) =>
        content.Cells.Any(cell => table.Grid.Cells[cell.Cell].Row >= table.Grid.HeaderRowCount
            && cell.Fragment.Children.Any(child => DrawnContent.DrawsOutsideSlices(child.Fragment)));

    /// <summary>Whether an outcome starts slicing content taller than the page area: some cell holds a first slice.</summary>
    private static bool StartsSlice(Outcome outcome) => outcome.Content.Cells.Any(cell => HoldsSlice(cell.Fragment, continuation: false));

    /// <summary>Whether an outcome goes on slicing content taller than the page area: some cell holds a later slice.</summary>
    private static bool ContinuesSlice(Maybe<Outcome> outcome) =>
        outcome.HasValue && outcome.Value.Content.Cells.Any(cell => HoldsSlice(cell.Fragment, continuation: true));

    private static bool HoldsSlice(Fragment fragment, bool continuation) => fragment switch
    {
        SliceFragment slice => slice.IsContinuation == continuation,
        BoxFragment box => box.Children.Any(child => HoldsSlice(child.Fragment, continuation)),
        LineFragment line => line.Children.Any(child => HoldsSlice(child.Fragment, continuation)),
        _ => false,
    };

    /// <summary>
    /// Whether the table repeats its header rows at the top of its later fragments, and their height when it does (zero
    /// when it does not). Decided once, for the table's first fragment, and carried to the later pages in the break token
    /// (<see cref="TracksBreakToken.RepeatsHeader"/>), as Chromium decides (<c>third_party/blink/renderer/core/layout/table/table_layout_algorithm.cc</c>,
    /// <c>TableLayoutAlgorithm::GenerateFragment</c>: "If this isn't the first fragment for the table box, and the section
    /// didn't repeat in the previous fragment, it doesn't make sense to start repeating now"; read in the source of
    /// Chromium 150, the measured browser is Chromium 159). Chromium repeats a header group when two conditions hold: its
    /// block size is at most a quarter of the fragmentainer's (<see cref="ConstraintSpace.FragmentainerBlockSize"/>, which
    /// does not depend on where the table starts and is the same in a measurement with unlimited height), and its
    /// <c>break-inside</c> has an applicable <c>avoid</c> value. The engine checks the first condition only: its header rows
    /// have no <c>break-inside</c> of their own in this respect, and they are repeated as the header groups of the measured
    /// cases, which carry no <c>break-inside</c> in their documents and which Chromium repeats. The check is the
    /// measurement (<c>tools/reference/chromium</c>, cases <c>a6-header-quarter-*</c>: on a page area of 180 pt a header of
    /// 45.0 pt is repeated and one of 45.75 pt is not; on 360 pt, 90.0 and 90.75). The header's height is that of its rows,
    /// from the centre of the line above them to the centre of the line below. Under an outer table's repeated header the
    /// fragmentainer is less by the room that header takes, as Chromium reserves it for the rows below it
    /// (<see cref="Reserved"/>: the header on the outer table's first page, the header and the outer half of the outer
    /// table's top grid line on its later pages; cases <c>a6-nested-under-header-h2400</c> and <c>-h2700</c>: on a page
    /// area of 180 pt below an outer header of one line, a nested header of 39.75 pt is repeated and one of 42.75 pt is
    /// not; cases <c>a6-nested-first-*</c> and <c>a6-nested-late-*</c> for the step on the first and on a later page).
    /// </summary>
    private static Result<(bool Repeats, double Height), Error> RepeatsHeader(TableGeometry table, ConstraintSpace space, TracksBreakToken? incoming)
    {
        var grid = table.Grid;
        if (grid.HeaderRowCount == 0 || incoming is { RepeatsHeader: false })
        {
            return Result<(bool, double), Error>.Success((false, 0));
        }

        var cells = grid.CellsStartingInRows(0, grid.HeaderRowCount - 1)
            .Select(index => new TableSegments.ActiveCell(index, grid.Cells[index].Row, null, false)).ToImmutableArray();
        var top = new LineAt(null, 0, new CollapsedBorders.LineContext(true, false, new HashSet<int>()), 0);
        return TableSegments.Measure(table, 0, grid.HeaderRowCount - 1, cells, top)
            .OnSuccessMap(header => incoming?.RepeatsHeader ?? Fitting.Fits(header.Height, space.FragmentainerBlockSize / 4)
                ? (true, header.Height)
                : (false, 0.0));
    }

    /// <summary>
    /// The room the repeated header rows take from the page area of the table's cells
    /// (<see cref="TableGeometry.CellFragmentainerBlockSize"/>). On the table's first page that is the header's height. On a
    /// later page the repeated header draws the table's whole top grid line, so the room is the header's height plus the
    /// outer half of that line, as Chromium reserves it (<c>TableLayoutAlgorithm::GenerateFragment</c>: "when repeated it
    /// will draw the whole border itself. We need to reserve additional space at the block-start for this additional
    /// border space", <c>child_block_offset += border_padding.block_start</c> where the block-start border is not included
    /// in the fragment, then <c>repeated_header_block_size = child_block_offset - *offset_before_repeated_header +
    /// border_spacing.block_size</c>; read in the source of Chromium 150). The measurement: below an outer header of one
    /// line on a page area of 180 pt, a nested table that starts on the outer table's second page repeats a header of
    /// 40.96875 pt and not one of 41.0 pt (cases <c>a6-nested-late-*</c>), where on the first page it repeats one of
    /// 41.0625 pt (<c>a6-nested-first-*</c>); with grid lines of 6 pt the second-page limit is 39.0 pt
    /// (<c>a6-nested-late6-*</c>), and each outer level between takes its own share (<c>a6-nested-late-d3-*</c>). An outer
    /// table with <c>box-decoration-break: clone</c> does not repeat a nested header of 41.0 pt either
    /// (<c>a6-nested-late-clone-h2525</c>), so the half is reserved there too.
    /// </summary>
    private static double Reserved(TableGeometry table, bool repeatsHeader, double headerHeight, bool isFirst) =>
        repeatsHeader && !isFirst
            ? headerHeight + (table.HorizontalWidth(null, 0, new CollapsedBorders.LineContext(true, false, new HashSet<int>())) / 2)
            : headerHeight;

    /// <summary>One try at the page, with or without the header rows on top. None when no body content fits.</summary>
    private static Result<Maybe<Outcome>, Error> Attempt(
        TableGeometry table, ConstraintSpace space, TracksBreakToken resume, bool isFirst, bool includeHeader, TracksEarlyBreak? breakAt, bool headerAlone = false)
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
        // same context whether a repeated header comes before it or not. With separated borders the first row of the
        // table's first page lies below the table's border, padding and one spacing (TopInset), and the first row of a
        // later page, or its repeated header, at the fragment's top: the table is sliced there (cases ts-break-rows,
        // ts-break-header, ts-break-cut).
        var reopensCutRow = !isFirst && resume.RowIsSplit && IsCloned(table);
        if (!isFirst && IsCloned(table))
        {
            resumeContext = CollapsedBorders.LineContext.ClonedBreak;
        }

        var continuesAfterBreak = !isFirst && !includeHeader && (!resume.RowIsSplit || reopensCutRow);
        var firstLine = continuesAfterBreak
            ? ContinuationLine(table, firstRow.Value, resumeContext, resume.FollowsPassed)
            : new LineAt(null, firstRow, topContext, table.IsSeparated ? (isFirst ? table.TopInset : 0) : table.HorizontalWidth(null, firstRow, topContext) / 2);
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

            content = content with { Lines = content.Lines.Add(new LineAt(grid.HeaderRowCount - 1, nextBody, resumeContext, content.Bottom + table.RowGap)) };
        }

        var body = PlaceBody(table, content, resume, height, breakAt);

        // The header alone at the start of an empty page: nothing better is possible there (see remarks); a body that only
        // a last-resort cut would place is not better. So is the page with only the header before content taller than the
        // page area, which is sliced below it on the next page (see remarks). Below a header that repeats, under
        // EmptyRowParts.Allow, the page ends with that cut instead, as Chromium's does (see remarks): at the same appeal,
        // since the table breaks on the same page either way.
        if (body.IsFailure || !OnlyLastResort(body.Value) || !includeHeader || !(headerAlone || (isFirst && space.IsAtPageStart)))
        {
            return body;
        }

        // The cut row's cells lie below the header as below start insets, so content nested in them takes its own
        // last-resort cut on this page (see remarks).
        if (CutsBelowHeader(table, resume) && body.Value.HasValue)
        {
            var deeper = PlaceBody(table with { CutsBelowHeaderRows = true }, content, resume, height, breakAt);
            return deeper.OnSuccessMap(cut => Maybe.From((cut.HasValue ? cut.Value : body.Value.Value) with { Appeal = BreakAppeal.ViolatesAvoid, CutBelowHeader = true }));
        }

        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(new Outcome(
            content with { Lines = content.Lines.RemoveAt(content.Lines.Count - 1) },
            BottomLine(table, grid.HeaderRowCount - 1, nextBody, content.Bottom),
            resume,
            BreakAppeal.ViolatesAvoid)));
    }

    /// <summary>
    /// Whether a page that would end right below the header rows ends with its first body row cut below them instead,
    /// its cells empty down to the page end and their content on the next page (see remarks): only below header rows that
    /// repeat (<see cref="TracksBreakToken.RepeatsHeader"/>) and under <see cref="EmptyRowParts.Allow"/>, as Chromium
    /// prints it (<c>tools/reference/chromium</c>, cases <c>a4-header-image-228</c>, <c>a4-header2-image-228</c>,
    /// <c>a4-header-tall-line</c>, <c>a4-header-row-image-row</c>: the cells' side lines from the header to the page end
    /// and no line below them; case <c>a4-header-unrepeated-image-228</c>: a header that does not repeat ends its page
    /// alone). Also where another cell of the row draws something there: text that fits the page or goes on over two or
    /// three pages, an empty cell with a background, the image's own cell with a background, the row as the second body
    /// row (<c>a4-header-image-228-beside-fit</c>, <c>-beside-two-pages</c>, <c>a4-header-image-beside-lines</c>,
    /// <c>a4-header-image-228-beside-line</c>, <c>-background-beside</c>, <c>-background</c>,
    /// <c>a4-header-row-image-228-beside-line</c>: the image on the next page, below the header, beside the rest of the
    /// text).
    /// </summary>
    private static bool CutsBelowHeader(TableGeometry table, TracksBreakToken resume) =>
        resume.RepeatsHeader == true && table.Environment.Profile.EmptyRowParts == EmptyRowParts.Allow;

    /// <summary>
    /// Whether the repeated header rows are drawn where the table lays content out and left out where it only shows the
    /// rest of content laid out before (<see cref="RepeatedHeaderRows.WhereContentIsLaidOut"/>, see remarks).
    /// </summary>
    private static bool LaysOutBelowHeader(TableGeometry table) =>
        table.Environment.Profile.RepeatedHeaderRows == RepeatedHeaderRows.WhereContentIsLaidOut
        && table.Environment.Profile.MonolithicOverflow == MonolithicOverflow.Slice;

    /// <summary>
    /// Whether all that is left of the table at <paramref name="resume"/> is the rest of pieces laid out on an earlier
    /// page: laid out without the header rows and without a page end (so it ends there), the rest of the table holds a
    /// later slice, and its body cells draw nothing besides slices (no row still to come draws anything, no cell beside the
    /// piece goes on). Chromium draws no header rows on such a page (cases <c>a4-header-image-228</c>,
    /// <c>a4-header-row-image-228</c>, <c>a4-header-tall-line</c>; and on the pages of <c>a4-header-image-360-image</c>
    /// and <c>a4-header-image-beside-lines</c> where a row or text is still to come, it does).
    /// </summary>
    private static Result<bool, Error> OnlyRestLeft(TableGeometry geometry, ConstraintSpace space, TracksBreakToken resume)
    {
        if (resume.Cells.Count == 0)
        {
            return Result<bool, Error>.Success(false);
        }

        var bare = geometry with { CellFragmentainerBlockSize = space.FragmentainerBlockSize };
        return Attempt(bare, space with { AvailableHeight = double.PositiveInfinity }, resume, isFirst: false, includeHeader: false, breakAt: null)
            .OnSuccessMap(rest => ContinuesSlice(rest) && !ShowsBody(bare, rest.Value.Content) && OnlySlicesGoOn(bare, rest.Value.Content));
    }

    /// <summary>
    /// Whether every body cell of the page's first row holds a later slice: no cell of that row beside the piece goes on.
    /// A cell is as tall as its row (CSS 2.1 §17.5.3), so the cells beside a piece whose rest ends the row go on down to
    /// that rest's end, empty, and Chromium repeats the header rows above them (cases
    /// <c>a4-header-image-228-beside-fit</c>, <c>a4-header-image-228-beside-line</c>,
    /// <c>a4-header-image-228-background-beside</c>, <c>a4-header-row-image-228-beside-line</c>: the last page shows the
    /// header; <c>a4-header-image-228</c>, one column: it does not). A cell that spans into the row from an earlier one
    /// does not count (<c>a4-header-empty-rowspan-line-image</c>: no header on the page of the rest).
    /// </summary>
    private static bool OnlySlicesGoOn(TableGeometry table, Content content) =>
        content.Cells.All(cell => table.Grid.Cells[cell.Cell].Row < table.Grid.HeaderRowCount
            || table.Grid.Cells[cell.Cell].Row < cell.TopRow
            || HoldsSlice(cell.Fragment, continuation: true));

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
                    return TableBreaks.TakeBest(table, [before with { Appeal = target.Appeal, InKeptGroup = false }], height);
                }

                breakpoints.Add(before);
            }

            if (!Fitting.Fits(segment.Top + segment.Height + InsideBelow(table, unit.LastRow), height)
                && !PassesOnlyByItsLineBelow(table, content, segment, segment.Top + segment.Height, unit.LastRow, height))
            {
                breakpoints.AddRange(TableBreaks.InsideUnit(table, content, segment, height));
                return TableBreaks.TakeBest(table, breakpoints, height);
            }

            content = TableSegments.PlaceWhole(table, content, segment, isRepeated: false) with { HasBody = true };
            row = unit.LastRow + 1;
            if (row < grid.Rows.Length)
            {
                content = content with { Lines = content.Lines.Add(new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, content.Bottom + table.RowGap)) };
            }
        }

        var best = breakpoints
            .Where(breakpoint => breakpoint.Kind == TableBreaks.BreakKind.BeforeUnit)
            .OrderByDescending(breakpoint => breakpoint.Overall)
            .ThenByDescending(breakpoint => breakpoint.Row)
            .Select(breakpoint => new TracksEarlyBreak(breakpoint.Row, breakpoint.Overall) { InKeptGroup = breakpoint.InKeptGroup })
            .FirstOrDefault();
        return Result<Maybe<Outcome>, Error>.Success(Maybe.From(Complete(table, content) with { BestBreak = best }));
    }

    /// <summary>
    /// Under <see cref="EmptyRowParts.Allow"/>, as Chromium prints it: in a table that continues from the page before, a
    /// unit that starts its page area (nothing above it on the page, not even a header row: a row that continues from the
    /// page before, or the first on the page), or the first row of such a unit, whose cells' content fits and whose grid
    /// line below does not, is placed whole; the line passes the page area, by at most its width. Moving the row would gain
    /// nothing, and cutting it would put words on other pages than Chromium does (cases a2-row-end*). Where that row ends
    /// the table, what passes continues on the next page under <see cref="TableEndOverflow.ContinuesOnNextPage"/>
    /// (<see cref="Passed"/>), and ends on this page under <see cref="TableEndOverflow.EndsOnPage"/>. In a table that itself
    /// starts at the top of the page, the first row is placed so only when the row up to
    /// the middle of its line below fits, so that only the outer half of the line passes: Chromium places that row whole
    /// (cases a2-row-end-first-row-half-*), also where the table starts below only the top insets of boxes that start the
    /// page (<see cref="ConstraintSpace.IsBelowStartInsets"/>, case a2-row-end-first-row-half-in-cell: a table at the top of
    /// a table cell). Where only the content fits, Chromium leaves that page empty or cuts the row
    /// between its lines (cases a2-row-end-first-row, a2-row-end-first-row-cut), and the engine refuses the table
    /// (docs/architecture.md, Known limits). In a table that continues from the page before, under
    /// <see cref="TableEndOverflow.ContinuesOnNextPage"/>, such a unit of one row is also placed whole where its cells'
    /// content fits and their bottom padding does not (<see cref="PassesByItsBottomPadding"/>).
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="content">The page up to the unit.</param>
    /// <param name="segment">The unit, measured.</param>
    /// <param name="bottom">The bottom of the unit, or of its first row (<see cref="TableBreaks.InsideUnit"/>), with the cells' share of the line below.</param>
    /// <param name="row">The unit's last row, or its first row.</param>
    /// <param name="height">The page's available height.</param>
    /// <returns>True when it is placed whole and its line below passes the page area.</returns>
    internal static bool PassesOnlyByItsLineBelow(TableGeometry table, Content content, TableSegments.Segment segment, double bottom, int row, double height) =>
        table.Environment.Profile.EmptyRowParts == EmptyRowParts.Allow
        && (table.IsAtPageStart || table.IsBelowStartInsets)
        && content.Rows.IsEmpty
        && (Fitting.Fits(table.ContinuesFromPageBefore ? bottom - LineBelowShare(table, row) : bottom, height)
            || PassesByItsBottomPadding(table, segment, row, height));

    /// <summary>
    /// Under <see cref="TableEndOverflow.ContinuesOnNextPage"/>, as Chromium prints it: in a table that continues from the
    /// page before, a row that starts its page area and is a unit of its own (a row that continues from the page before,
    /// or one that moved to this page whole), each of whose cells that pass the page end has content on the page that fits
    /// and a bottom padding and border that do not, beside cells that end above the page end, is placed whole: the padding and the line below pass the page end, by more than a line
    /// where the padding is taller, and continue on the next page (<see cref="Passed"/>). Chromium keeps the lines on the
    /// page and does not move the last of them so that the padding fits (cases <c>fr-continued-pad-past-33</c>,
    /// <c>-45</c>, <c>-60</c>: 3, 15 and 30 pt of padding past the page end; <c>fr-continued-pad-past-33-next-row</c>,
    /// with a row after it; <c>fr-row-later-page-padding-overflows</c>, a row that moved to the page whole;
    /// <c>a4-continued-cell-pad-bottom-30</c>, a nested table as the content). Where a cell's content does not fit, the
    /// row is cut as before, and its last lines go to the next page with the padding. The cells that end above the page
    /// end give the next page nothing, where Chromium gives it the largest of their bottom paddings again (a documented
    /// limit; cases <c>fr-row-later-page-padding-overflows-two-cells</c>, <c>-two-cells-pad-6</c>, <c>-three-cells</c>:
    /// the line below 13.125, 7.125 and 19.125 pt below the next page's top as Chromium prints it, beside 12, 6, and 6
    /// and 18 pt of padding). Also as before: a cell whose rest is only padding, without content (not measured).
    /// Under <see cref="TableEndOverflow.EndsOnPage"/> nothing changes.
    /// </summary>
    private static bool PassesByItsBottomPadding(TableGeometry table, TableSegments.Segment segment, int row, double height)
    {
        if (table.Environment.Profile.TableEndOverflow != TableEndOverflow.ContinuesOnNextPage || !table.ContinuesFromPageBefore
            || segment.FirstRow != row || segment.LastRow != row)
        {
            return false;
        }

        var cells = segment.Cells.Where(cell => segment.Natural.ContainsKey(cell.Cell))
            .Select(cell => (Top: segment.RowTop(cell.StartRow), Bottom: segment.RowTop(cell.StartRow) + segment.Natural[cell.Cell].Size.Height,
                Inset: SlicedBox.BottomInset(TableSegments.CellLayoutBox(table, segment, cell).Style)))
            .ToList();
        return cells.Any(cell => !Fitting.Fits(cell.Bottom, height)) && cells.All(cell => Fitting.Fits(cell.Bottom, height)
            || (cell.Bottom - cell.Inset > cell.Top + Fitting.TolerancePoints && Fitting.Fits(cell.Bottom - cell.Inset, height)));
    }

    /// <summary>Whether the table's fragment passes the page area where a unit placed whole lets its line below pass the
    /// page end (<see cref="PassesOnlyByItsLineBelow"/>): at the top of the page, or below only the top insets of boxes that
    /// start it.</summary>
    private static bool PassesPageArea(TableGeometry table, BoxFragment fragment, ConstraintSpace space) =>
        table.Environment.Profile.EmptyRowParts == EmptyRowParts.Allow && (space.IsAtPageStart || space.IsBelowStartInsets) && !Fitting.Fits(fragment.Size.Height, space.AvailableHeight);

    /// <summary>
    /// The table's fragment as tall as the page area where it passes the page area (<see cref="PassesPageArea"/>): the
    /// line and the cells' share of it are drawn past the page area, but the table takes no more than the page area in
    /// its container, so what follows the table starts on the next page; there, under
    /// <see cref="TableEndOverflow.ContinuesOnNextPage"/>, below what passed when the table ends on this page
    /// (<see cref="Passed"/>).
    /// </summary>
    private static BoxFragment WithinPageArea(BoxFragment fragment, ConstraintSpace space) =>
        new(fragment.Box, new Size(fragment.Size.Width, space.AvailableHeight), fragment.Children, fragment.Border, fragment.IsRepeated);

    /// <summary>The half of the grid line below a row that lies inside the row's cells.</summary>
    private static double LineBelowShare(TableGeometry table, int row) =>
        table.HorizontalWidth(row, Next(table.Grid, row), EdgeContext(table.Grid, row)) / 2;

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
    /// first row of this page: its lower half at the table's top, or the whole line, centred half its width down. When
    /// the page starts at the table's first row there is no row before (with clone after a cut inside that row, or a
    /// token for row 0 of a table without header rows): the line is the table's top line. Where a cell that spans rows
    /// continues over the break (with slice), the page draws nothing of the lower half, in any column, and keeps its
    /// room: Chromium prints no line at the top of that page, while the first row's content starts below the half
    /// (case <c>b4-row-span-6pt</c>: page 2 has no rectangle at 12 pt, and row r8's text starts at 15 pt = 12 + 3, half of the
    /// 6 pt line, as in <c>a2-row-break-line-6pt</c>; case <c>b4-row-span</c> with a 0.75 pt line likewise). Below part of a
    /// cell that passed the page end (<paramref name="followsPassed"/>, <see cref="TracksBreakToken.FollowsPassed"/>) the
    /// page draws nothing of the lower half either and keeps its room: the upper half that passed is the line Chromium
    /// prints there (cases <c>fr-continued-pad-past-33-next-row-line-6pt</c>: the 3 pt upper half at 15 to 18 pt and the
    /// next row's text from 21 pt; <c>-line-2-25pt</c>: 15 to 16.5 pt at whole pixels; <c>-33-next-row</c> and
    /// <c>-taller-than-page-next-row</c> with a 0.75 pt line, the half drawn one pixel thick).</summary>
    private static LineAt ContinuationLine(TableGeometry table, int firstRow, CollapsedBorders.LineContext context, bool followsPassed = false)
    {
        if (firstRow == 0)
        {
            var top = new CollapsedBorders.LineContext(true, false, new HashSet<int>());
            return new LineAt(null, firstRow, top, table.HorizontalWidth(null, firstRow, top) / 2);
        }

        return IsCloned(table)
            ? new LineAt(firstRow - 1, firstRow, context, table.HorizontalWidth(firstRow - 1, firstRow, context) / 2)
            : new LineAt(firstRow - 1, firstRow, context, 0, context.CutCells.Count > 0 || followsPassed ? LinePart.Unpainted : LinePart.LowerHalf);
    }

    /// <summary>What a row needs below its bottom edge on this page, if the page ends there: for the table's last row,
    /// the half of the table's bottom border inside the table (in the separated borders model the spacing, padding and
    /// border below it, <see cref="TableGeometry.BottomInset"/>); for any other row, with clone the half of the line below
    /// it that lies under its centre (the line that closes the table at the break, see remarks), and with slice nothing,
    /// because the line is split at its centre.</summary>
    internal static double InsideBelow(TableGeometry table, int row) =>
        row == table.Grid.Rows.Length - 1 && table.IsSeparated ? table.BottomInset
        : row == table.Grid.Rows.Length - 1 || IsCloned(table) ? table.HorizontalWidth(row, Next(table.Grid, row), PageEndContext(table, row)) / 2
        : 0;

    internal static Content Empty(double top) => new([], [], [], top, false);

    /// <summary>The table is complete on this page: the line below its last row joins the table's bottom border.</summary>
    internal static Outcome Complete(TableGeometry table, Content content) =>
        new(content, content.LastRow is { } last ? new LineAt(last, null, EdgeContext(table.Grid, last), content.Bottom) : null, null, BreakAppeal.Perfect);
}
