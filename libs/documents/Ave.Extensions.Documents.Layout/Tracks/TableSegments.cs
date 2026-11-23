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
/// The rows and cells of one page of a tracks box: a unit (or the rest of one) measured with unlimited height, its
/// row heights (CSS 2.1 §17.5.3), and its cells placed whole or sliced at a page end. Used by <see cref="TablePage"/>
/// and <see cref="TableBreaks"/>.
/// </summary>
internal static class TableSegments
{
    /// <summary>A cell being laid out on this page.</summary>
    /// <param name="Cell">The cell index.</param>
    /// <param name="StartRow">Its first row on this page.</param>
    /// <param name="Token">Its break token, or null when it starts here.</param>
    /// <param name="IsComplete">True when its content is complete and it only goes on covering rows.</param>
    internal sealed record ActiveCell(int Cell, int StartRow, BreakToken? Token, bool IsComplete);

    /// <summary>A unit, or the rest of one, measured with unlimited height.</summary>
    /// <param name="FirstRow">Its first row on this page.</param>
    /// <param name="LastRow">Its last row.</param>
    /// <param name="Cells">The cells that start in it, or continue into it.</param>
    /// <param name="Heights">Each row's height.</param>
    /// <param name="Natural">Each cell's fragment at its natural height; absent for a complete cell.</param>
    /// <param name="Above">The grid line above its first row; its centre is where the segment starts.</param>
    internal sealed record Segment(
        int FirstRow, int LastRow, ImmutableArray<ActiveCell> Cells, ImmutableArray<double> Heights, ImmutableDictionary<int, BoxFragment> Natural, LineAt Above)
    {
        public double Top => Above.Y;

        /// <summary>The room between two of its rows (<see cref="TableGeometry.RowGap"/>): zero in the collapsing border
        /// model.</summary>
        public double Gap { get; init; }

        public double RowTop(int row) => Top + Heights.Take(row - FirstRow).Sum() + ((row - FirstRow) * Gap);

        /// <summary>The height of rows <paramref name="first"/> to <paramref name="last"/> and the gaps between them, which
        /// a cell over them covers (CSS 2.1 §17.6.1; case <c>ts-span</c>: a cell over two rows of 18.234 pt and the 6 pt
        /// between them is 42.469 pt high).</summary>
        public double RowsHeight(int first, int last) => Heights.Skip(first - FirstRow).Take(last - first + 1).Sum() + ((last - first) * Gap);

        public double Height => Heights.Sum() + (Math.Max(0, Heights.Length - 1) * Gap);

        /// <summary>The cells that go on below the slice of a piece in another cell of the segment, each with the height
        /// it is lowered by (<see cref="CellsBesideSlices.Below"/>, <see cref="BelowSlices"/>); empty otherwise.</summary>
        public ImmutableDictionary<int, double> BelowSlice { get; init; } = ImmutableDictionary<int, double>.Empty;
    }

    /// <summary>
    /// A cut cell: its content laid out in the slice height less its <see cref="ReopenedInset"/> at the top; an empty
    /// slice when it is complete or nothing of it fits. A cell whose content goes on past the cut keeps
    /// <paramref name="clearance"/> free above the cut (clone keeps it clear of the line drawn there,
    /// <see cref="TablePage"/>); a cell that ends in the slice needs no clearance, since its own box already holds the half
    /// of the line below it (<see cref="CellLayoutBox"/>). Which of the two the cell is, its measured fragment says (the
    /// segment's natural height of the cell, <see cref="Segment.Natural"/>): a cell whose natural height fits the slice
    /// ends there and is laid out in the whole slice; any other cell cannot end there and is laid out without the
    /// clearance. So a cut cell is laid out once per cut, never a second time to try the other height: in a cell of a
    /// cell of a cell … a second layout at every level would double the work at every level (TableNestingCostTests). The
    /// one second layout is <see cref="SlicedAboveBottomInset"/>'s, taken only under <see cref="MonolithicOverflow.Slice"/>
    /// for a cell that starts the page and ends there taller than its room; it lets the content break, so the cell around
    /// it no longer ends there and does not take it again. Returns
    /// the fragment, stretched to the slice, where the cell continues, and the appeal of the break inside the cell's
    /// content (its own keep rules, orphans and widows): perfect for a cell that ends in the slice or places nothing of
    /// itself (under <see cref="EmptyRowParts.Allow"/> a last resort for a fresh cell that places nothing, see
    /// <see cref="StartsNothing"/>); for a cell that keeps its break (its content broke at a breakpoint it chose,
    /// <see cref="LayoutResult.Placed.KeepsItsBreak"/>: the row keeps that break too, as Chromium follows such a break,
    /// case <c>fr-f9-cell-orphans3</c>) the kept appeal, perfect unless the break lies inside a box kept together
    /// (<see cref="LayoutResult.Placed.KeptAppeal"/>, cases <c>kb-n5-*</c>, <c>kb-n6-*</c>); a last resort for a cell whose lines all fit the slice and only its bottom padding
    /// or its share of the grid line below it does not, where its last row is the one the cut is in
    /// (<paramref name="endsAtTheCut"/>; Chromium never breaks a cell before its bottom padding: the row moves whole when
    /// it can, cases <c>fr-f4-*</c>). A cell is laid out as starting the page (<see cref="ConstraintSpace.IsAtPageStart"/>)
    /// only when <paramref name="startsPage"/> says nothing lies above it on the page; every other cell may always leave
    /// its content for the next page.
    /// </summary>
    /// <remarks>A fresh cell whose content starts the page below a repeated header row (<paramref name="belowHeader"/>,
    /// see <see cref="TablePage"/>) is laid out without the half of the line above it and lowered by it afterwards, as a
    /// reopened cell is: its content starts the page, so content taller than the page area is sliced from right below
    /// that line, where a whole cell's content starts.</remarks>
    internal static Result<(BoxFragment Fragment, CellBreak Next, BreakAppeal Appeal, bool KeepsItsBreak), Error> Slice(
        TableGeometry table, Segment segment, ActiveCell cell, double sliceHeight, double clearance = 0, bool endsAtTheCut = false,
        bool startsPage = false, bool belowHeader = false, bool belowStartInsets = false)
    {
        var info = table.Grid.Cells[cell.Cell];
        var lineAbove = belowHeader && cell.Token is null;
        var box = CellLayoutBox(table, segment, cell, withoutLineAbove: lineAbove);
        var width = table.CellWidth(info);
        if (cell.IsComplete)
        {
            return Result<(BoxFragment, CellBreak, BreakAppeal, bool), Error>.Success((EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, null, true), BreakAppeal.Perfect, false));
        }

        // A cell that goes on below a slice in another cell (BelowSlices) does not start the page: its content is lowered.
        var belowSlice = segment.BelowSlice.GetValueOrDefault(cell.Cell);
        startsPage &= belowSlice <= 0;
        var inset = ReopenedInset(table, segment, cell) + (lineAbove ? LineAboveInside(table, segment, cell) : 0) + belowSlice;
        Result<LayoutResult, Error> LayOutIn(double height) =>
            table.Environment.Layout(box, new ConstraintSpace(width, Math.Max(0, height - inset), IsAtPageStart: startsPage || belowHeader, IsNewFormattingContext: true)
            {
                FragmentainerBlockSize = table.CellFragmentainerBlockSize,
                KeepsBottomInset = true,
                IsBelowStartInsets = belowStartInsets && !(startsPage || belowHeader),
            }, cell.Token);
        var measured = segment.Natural.TryGetValue(cell.Cell, out var natural);
        var endsInSlice = clearance <= 0 || (measured && Fitting.Fits(natural!.Size.Height, sliceHeight));
        var endsInItsBottomEdge = endsAtTheCut && measured && !Fitting.Fits(natural!.Size.Height, sliceHeight)
            && Fitting.Fits(natural.Size.Height - SlicedBox.BottomInset(box.Style), sliceHeight);
        return LayOutIn(endsInSlice ? sliceHeight : sliceHeight - clearance)
            .OnSuccessBind(whole => SlicedAboveBottomInset(table, box, whole, sliceHeight - inset, startsPage || belowHeader, LayOutIn, sliceHeight))
            .OnSuccessMap(result => result switch
            {
                LayoutResult.Placed placed => (Stretch(Lowered(placed.Fragment, inset), sliceHeight, VerticalAlign.Top),
                    new CellBreak(cell.Cell, placed.Continuation, placed.Continuation is null),
                    placed.Continuation is null ? BreakAppeal.Perfect
                    : endsInItsBottomEdge ? BreakAppeal.LastResort
                    : placed.KeepsItsBreak ? placed.KeptAppeal
                    : placed.Appeal,
                    placed.Continuation is not null && placed.KeepsItsBreak && !endsInItsBottomEdge),
                _ => (EmptyCell(box, width, sliceHeight), new CellBreak(cell.Cell, cell.Token, false),
                    endsInItsBottomEdge || StartsNothing(table, cell) ? BreakAppeal.LastResort : BreakAppeal.Perfect, false),
            });
    }

    /// <summary>
    /// Under <see cref="EmptyRowParts.Allow"/>, whether a cell that starts on this page and has no room for its top border
    /// and padding makes a cut inside its row a last resort: the cell has no breakpoint on this page (a break inside its
    /// top insets is none, CSS Fragmentation 3 §4.1), so the row is not cut where its cells can break, and moves to the
    /// next page whole where a break before it is valid, as Chromium prints it (cases
    /// <c>fr-row-later-page-padding-overflows</c>, <c>fr-row-later-padding-beside-line</c>: also beside a cell whose first
    /// line fits, <c>fr-row-later-padding-under-header</c>, <c>a2-row-end-nested-under-header</c>). A cell whose insets
    /// fit and whose first line does not already breaks at a last resort (<see cref="SlicedBox"/>, case
    /// <c>fr-row-later-padding-fits-line-not</c>). Under <see cref="EmptyRowParts.Avoid"/> such a cut stays as it was.
    /// </summary>
    private static bool StartsNothing(TableGeometry table, ActiveCell cell) =>
        table.Environment.Profile.EmptyRowParts == EmptyRowParts.Allow && cell.Token is null;

    /// <summary>
    /// Under <see cref="MonolithicOverflow.Slice"/>, a cell that starts the page and ends there, but whose content and
    /// bottom padding and border together do not fit the page (a piece that fits the page area alone, not with the insets
    /// around it): the content is laid out once more in the room above the bottom inset, so the piece no longer fits and is
    /// sliced (<see cref="MonolithicSlices"/>), starting the page as it does; its last slice and the bottom inset go to the
    /// next page. The test is "the piece with its insets does not fit a page", not "the piece is taller than the page
    /// area". Taken only when the content then breaks; otherwise the cell is as before (and refused or placed as the
    /// table decides). Under <see cref="MonolithicOverflow.Refuse"/> nothing changes. Chromium does not slice here: it
    /// places the piece whole at the top of the page and lets the bottom padding and border pass the page end
    /// (docs/architecture.md, "Known limits"; reference case <c>a4-cell-pad3-image-177</c>).
    /// </summary>
    private static Result<LayoutResult, Error> SlicedAboveBottomInset(
        TableGeometry table, Box box, LayoutResult whole, double room, bool startsPage, Func<double, Result<LayoutResult, Error>> layOutIn, double sliceHeight)
    {
        if (!startsPage || table.Environment.Profile.MonolithicOverflow != MonolithicOverflow.Slice
            || whole is not LayoutResult.Placed { Continuation: null } placed || Fitting.Fits(placed.Fragment.Size.Height, room))
        {
            return Result<LayoutResult, Error>.Success(whole);
        }

        return layOutIn(sliceHeight - SlicedBox.BottomInset(box.Style))
            .OnSuccessMap(sliced => sliced is LayoutResult.Placed { Continuation: not null } ? sliced : whole);
    }

    /// <summary>
    /// The rest of a cut cell as the next page would lay it out, with unlimited height: what is left of it after the
    /// cut (<see cref="TableBreaks"/>, CutShows, asks whether it draws anything).
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="segment">The segment the cut is in.</param>
    /// <param name="next">Where the cell continues; not complete.</param>
    /// <returns>The rest's fragment, or an error.</returns>
    internal static Result<BoxFragment, Error> Rest(TableGeometry table, Segment segment, CellBreak next)
    {
        var info = table.Grid.Cells[next.Cell];
        var box = CellLayoutBox(table, segment, new ActiveCell(next.Cell, info.Row, next.Token, false));
        return table.Environment.Layout(box, Unlimited(table, table.CellWidth(info)), next.Token)
            .OnSuccessMap(result => result is LayoutResult.Placed placed ? placed.Fragment : EmptyCell(box, table.CellWidth(info), 0));
    }

    /// <summary>The space of a cell's content measured with unlimited height: the cell's width, and the fragmentainer block
    /// size of the page it measures for, so that a table inside decides on its header rows as on the page.</summary>
    private static ConstraintSpace Unlimited(TableGeometry table, double width) =>
        new(width, double.PositiveInfinity, false, true) { FragmentainerBlockSize = table.CellFragmentainerBlockSize };

    /// <summary>
    /// With clone, the rest of every cell that continues from the previous page — a cell cut inside its own row, or a
    /// spanning cell cut between or inside the rows it spans — starts under the whole line that starts the page, which
    /// runs across the full width (see <see cref="TablePage"/>): half that line's width lies in the cell, as for a whole
    /// cell (<see cref="CellLayoutBox"/>), but the cell's own layout gives a continued box no top inset, so the rest is
    /// lowered by it here. Only the first segment of a page has continued cells. 0 for every other cell, and with slice.
    /// </summary>
    internal static double ReopenedInset(TableGeometry table, Segment segment, ActiveCell cell) =>
        TablePage.IsCloned(table) && cell.Token is not null
            ? table.HorizontalWidth(segment.Above.Above, segment.Above.Below, segment.Above.Context) / 2
            : 0;

    /// <summary>
    /// Under <see cref="SliceRestEnd.AtLineCentre"/>, the measured fragment of a cell that continues from an earlier page
    /// and holds nothing but the rest of a piece sliced there (its later slices end where its own half of the line below
    /// it starts, and its content draws nothing beside them): made as tall as that rest, without the half, so its row ends
    /// where the rest ends and the line below is centred there, as Chromium prints it (<see cref="SliceRestEnd.AtLineCentre"/>;
    /// cases <c>a4-header-image-228</c>, <c>a4-header-image-228-line3-below</c>). The cell's own background does not
    /// decide it (<c>a4-header-image-228-background</c>). The half takes no part in the cell's paint: the table draws the grid lines. Any other
    /// fragment as it is, and so is this one beside a cell of its own row that goes on with nothing left: that cell's part
    /// keeps its half of the line below, so the row ends that half below the rest (cases
    /// <c>a4-header-image-228-beside-fit</c>, <c>a4-header-image-228-beside-line</c>,
    /// <c>a4-header-image-228-background-beside</c>: the line at 76.5–77.25 pt below a rest that ends at 76.125 pt;
    /// <c>a4-image-in-cell</c>: at 60–60.75 pt below a rest that ends at 60 pt). A cell that spans into the row from an
    /// earlier one does not keep it (<c>a4-header-empty-rowspan-line-image</c>: the line centred on the rest's end).
    /// </summary>
    private static BoxFragment AtRestEnd(TableGeometry table, ActiveCell cell, BoxFragment fragment, bool besideCompleteCell)
    {
        if (table.Environment.Profile.SliceRestEnd != SliceRestEnd.AtLineCentre || cell.Token is null || besideCompleteCell)
        {
            return fragment;
        }

        var rest = ContinuedSliceBottom(fragment);
        var half = LineBelowInside(table, table.Grid.Cells[cell.Cell]);
        return rest > 0 && half > 0 && Math.Abs(rest + half - fragment.Size.Height) < 1e-6
            && !fragment.Children.Any(child => DrawnContent.DrawsOutsideSlices(child.Fragment))
            ? new BoxFragment(fragment.Box, new Size(fragment.Size.Width, rest), fragment.Children, fragment.Border, fragment.IsRepeated)
            : fragment;
    }

    /// <summary>A fragment moved <paramref name="inset"/> down inside itself, and that much taller.</summary>
    private static BoxFragment Lowered(BoxFragment fragment, double inset) =>
        inset <= 0
            ? fragment
            : new BoxFragment(
                fragment.Box,
                new Size(fragment.Size.Width, fragment.Size.Height + inset),
                [.. fragment.Children.Select(child => child with { Offset = child.Offset + new Point(0, inset) })],
                fragment.Border,
                fragment.IsRepeated);

    /// <summary>Places a measured segment whole: its rows at their heights, every cell as tall as its rows.</summary>
    internal static Content PlaceWhole(TableGeometry table, Content content, Segment segment, bool isRepeated)
    {
        var rows = content.Rows.AddRange(Enumerable.Range(segment.FirstRow, segment.LastRow - segment.FirstRow + 1)
            .Select(row => new PlacedRow(row, segment.RowTop(row), segment.Heights[row - segment.FirstRow], isRepeated)));
        var cells = content.Cells.AddRange(segment.Cells.Select(cell => Whole(table, segment, cell)));
        var lines = content.Lines.AddRange(Enumerable.Range(segment.FirstRow + 1, segment.LastRow - segment.FirstRow)
            .Select(row => new LineAt(row - 1, row, CollapsedBorders.LineContext.Interior, segment.RowTop(row))));
        return content with { Rows = rows, Cells = cells, Lines = lines, Bottom = segment.Top + segment.Height };
    }

    /// <summary>A whole cell: its natural fragment stretched to the rows it covers, content aligned (§17.5.3); the rest of a
    /// cell cut on an earlier page at the top under <see cref="CutRowAlignment.TopOnEveryPage"/>, as every part of a cut
    /// row is on the page where it is cut (<see cref="Slice"/>).</summary>
    internal static PlacedCell Whole(TableGeometry table, Segment segment, ActiveCell cell)
    {
        var info = table.Grid.Cells[cell.Cell];
        var height = segment.RowsHeight(cell.StartRow, info.LastRow);
        var align = cell.Token is not null && table.Environment.Profile.CutRowAlignment == CutRowAlignment.TopOnEveryPage
            ? VerticalAlign.Top
            : info.Box.Style.VerticalAlign;
        var fragment = segment.Natural.TryGetValue(cell.Cell, out var natural)
            ? Stretch(natural, height, align)
            : EmptyCell(CellLayoutBox(table, segment, cell), table.CellWidth(info), height);
        return new PlacedCell(cell.Cell, cell.StartRow, fragment);
    }

    /// <summary>
    /// Measures a segment: every cell laid out with unlimited height, then the row heights (see remarks). A cell that
    /// still breaks with unlimited height holds a forced page break, which a table cell cannot honour.
    /// </summary>
    internal static Result<Segment, Error> Measure(TableGeometry table, int firstRow, int lastRow, ImmutableArray<ActiveCell> cells, LineAt above)
    {
        var shell = new Segment(firstRow, lastRow, cells, [], ImmutableDictionary<int, BoxFragment>.Empty, above) { Gap = table.RowGap };
        return cells.Where(cell => !cell.IsComplete)
            .TraverseAll(cell => table.Environment
                .Layout(CellLayoutBox(table, shell, cell), Unlimited(table, table.CellWidth(table.Grid.Cells[cell.Cell])), cell.Token)
                .OnSuccessBind(result => result is LayoutResult.Placed { Continuation: null } placed
                    ? Result<KeyValuePair<int, BoxFragment>, Error>.Success(KeyValuePair.Create(cell.Cell, AtRestEnd(table, cell, Lowered(placed.Fragment, ReopenedInset(table, shell, cell)), cells.Any(other => other.IsComplete && table.Grid.Cells[other.Cell].Row == table.Grid.Cells[cell.Cell].Row))))
                    : Result<KeyValuePair<int, BoxFragment>, Error>.Failure(
                        LayoutErrors.InvalidContent(table.Grid.Cells[cell.Cell].Box, "a table cell cannot hold a forced page break."))))
            .OnSuccessMap(natural =>
            {
                var measured = natural.ToImmutableDictionary();
                var below = BelowSlices(table, cells, measured);
                var fragments = measured.SetItems(below.Select(cell => KeyValuePair.Create(cell.Key, Lowered(measured[cell.Key], cell.Value))));
                return shell with { Heights = RowHeights(table.Grid, firstRow, lastRow, cells, fragments, table.RowGap), Natural = fragments, BelowSlice = below };
            });
    }

    /// <summary>
    /// Under <see cref="CellsBesideSlices.Below"/>, the cells that go on below the slice of a piece taller than the page
    /// area in another cell, on a page that goes on with that piece: every cell that continues from the previous page with
    /// content left and holds no such slice itself, lowered by the bottom of the lowest slice the continued cells start
    /// with (measured with unlimited height, so it is all that is left of the piece; where that passes the page end, the
    /// cell places nothing on this page and goes on below the next slice on the next). Chromium lays the piece out whole
    /// on the page where it starts and starts what the row has left below it on the later pages (cases
    /// <c>a4-image-beside-lines</c>, <c>a4-slice2-beside-lines</c>, <c>a4-slice-near-end-beside-lines</c>). Empty under
    /// <see cref="CellsBesideSlices.GoOn"/>, and on a page where no continued cell goes on with a slice.
    /// </summary>
    private static ImmutableDictionary<int, double> BelowSlices(TableGeometry table, ImmutableArray<ActiveCell> cells, ImmutableDictionary<int, BoxFragment> natural)
    {
        if (table.Environment.Profile.CellsBesideSlices != CellsBesideSlices.Below)
        {
            return ImmutableDictionary<int, double>.Empty;
        }

        var continued = cells.Where(cell => cell.Token is not null && natural.ContainsKey(cell.Cell))
            .Select(cell => (cell.Cell, Slice: ContinuedSliceBottom(natural[cell.Cell])))
            .ToList();
        var piece = continued.Select(cell => cell.Slice).DefaultIfEmpty(0).Max();
        return piece <= 0
            ? ImmutableDictionary<int, double>.Empty
            : continued.Where(cell => cell.Slice <= 0).ToImmutableDictionary(cell => cell.Cell, _ => piece);
    }

    /// <summary>The bottom of the lowest later slice (<see cref="SliceFragment.IsContinuation"/>) in a fragment, from its
    /// top; 0 when it holds none. Walks the fragment without recursion (fragments nest as deep as the input).</summary>
    private static double ContinuedSliceBottom(Fragment fragment)
    {
        var bottom = 0.0;
        var pending = new Stack<(Fragment Fragment, double Top)>();
        pending.Push((fragment, 0));
        while (pending.Count > 0)
        {
            var (current, top) = pending.Pop();
            switch (current)
            {
                case SliceFragment { IsContinuation: true } slice:
                    bottom = Math.Max(bottom, top + slice.Size.Height);
                    break;
                case BoxFragment box:
                    foreach (var child in box.Children)
                    {
                        pending.Push((child.Fragment, top + child.Offset.Y));
                    }

                    break;
                case LineFragment line:
                    foreach (var child in line.Children)
                    {
                        pending.Push((child.Fragment, top + child.Offset.Y));
                    }

                    break;
            }
        }

        return bottom;
    }

    /// <summary>The row heights of §17.5.3 for one segment: single-row cells first, then each spanning cell grows the
    /// last row it spans by what is missing; a spanning cell covers the <paramref name="gap"/> between its rows.</summary>
    internal static ImmutableArray<double> RowHeights(
        TableGrid grid, int firstRow, int lastRow, ImmutableArray<ActiveCell> cells, ImmutableDictionary<int, BoxFragment> natural, double gap = 0)
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
            var missing = HeightOf(cell) - (heights.Skip(cell.StartRow - firstRow).Take(last - cell.StartRow + 1).Sum() + ((last - cell.StartRow) * gap));
            heights[last - firstRow] += Math.Max(0, missing);
        }

        return [.. heights];
    }

    /// <summary>The cells of a segment: those that continue from the previous page, then those that start in it.</summary>
    internal static ImmutableArray<ActiveCell> ActiveCells(TableGrid grid, int firstRow, int lastRow, ValueList<CellBreak> continuing)
    {
        var resumed = continuing.Select(cell => new ActiveCell(cell.Cell, firstRow, cell.Token, cell.IsComplete)).ToImmutableArray();
        var fresh = grid.CellsStartingInRows(firstRow, lastRow)
            .Where(index => !continuing.Any(resume => resume.Cell == index))
            .Select(index => new ActiveCell(index, grid.Cells[index].Row, null, false));
        return [.. resumed, .. fresh];
    }

    /// <summary>
    /// The box a cell is laid out as on this page (<see cref="TracksLayout.CellBox"/>): half of its own vertical lines
    /// beside it (<see cref="CollapsedBorders.OwnVerticalWidth"/>), half of its own part of the line above it when it
    /// starts here (the line above the segment, or the line between two of its rows), and half of its own part of the
    /// line below its last row. Its own part: the widest of the line's segments over the cell's columns, not the whole
    /// line, measured in Chromium 159 (a 6 pt top border on the second cell of the first row lowers that cell's
    /// content alone, 2.25 pt below its neighbour's under a 1.5 pt line: case <c>te-top</c>; the bottom border of a last
    /// row: <c>te-bottom</c>). The rows still start and end at the line's centre, and the room a line takes at a page
    /// end is still its whole width. A cell that continues from an earlier page has no top inset (slice); its token
    /// already tells its algorithm so. In the separated borders model the cell's own box
    /// (<see cref="TracksLayout.SeparatedCellBox"/>).
    /// </summary>
    internal static Box CellLayoutBox(TableGeometry table, Segment segment, ActiveCell cell, bool withoutLineAbove = false)
    {
        var grid = table.Grid;
        var info = grid.Cells[cell.Cell];
        if (table.IsSeparated)
        {
            return TracksLayout.SeparatedCellBox(info);
        }

        var top = cell.Token is null && !withoutLineAbove ? LineAboveInside(table, segment, cell) : 0;
        var bottom = LineBelowInside(table, info);
        var halves = new Edges(
            top.Pt(),
            (CollapsedBorders.OwnVerticalWidth(table.Box, grid, info, info.Column + info.ColumnSpan) / 2).Pt(),
            bottom.Pt(),
            (CollapsedBorders.OwnVerticalWidth(table.Box, grid, info, info.Column) / 2).Pt());
        return TracksLayout.CellBox(info, halves);
    }

    /// <summary>The half of the cell's own part of the line below its last row that lies inside the cell.</summary>
    private static double LineBelowInside(TableGeometry table, TableGrid.Cell info) =>
        table.HorizontalWidth(info.LastRow, TablePage.Next(table.Grid, info.LastRow), TablePage.EdgeContext(table.Grid, info.LastRow), info) / 2;

    /// <summary>The half of the cell's own part of the line above it that lies inside the cell when it starts on this
    /// page.</summary>
    private static double LineAboveInside(TableGeometry table, Segment segment, ActiveCell cell)
    {
        var startsSegment = cell.StartRow == segment.FirstRow;
        var above = startsSegment ? segment.Above.Above : cell.StartRow - 1;
        var context = startsSegment ? segment.Above.Context : CollapsedBorders.LineContext.Interior;
        return table.HorizontalWidth(above, cell.StartRow, context, table.Grid.Cells[cell.Cell]) / 2;
    }

    internal static BoxFragment EmptyCell(Box box, double width, double height) => new(box, new Size(width, height), []);

    /// <summary>A cell fragment made as tall as its rows; with middle or bottom alignment its content moves down by half
    /// or all of the extra height (§17.5.3). Background and box keep the full height.</summary>
    internal static BoxFragment Stretch(BoxFragment fragment, double height, VerticalAlign align)
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
}
