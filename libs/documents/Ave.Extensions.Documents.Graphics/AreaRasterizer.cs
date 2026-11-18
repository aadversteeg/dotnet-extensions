using System;
using System.Collections.Generic;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>Receives the coverage of one row of cells: <paramref name="coverage"/>[i] is the share of cell
/// (<paramref name="firstColumn"/> + i, <paramref name="row"/>) the shape covers, 0 to 1.</summary>
/// <param name="row">The row.</param>
/// <param name="firstColumn">The column of the first value.</param>
/// <param name="coverage">The coverage of consecutive cells; only valid during the call.</param>
internal delegate void CoverageRow(int row, int firstColumn, ReadOnlySpan<double> coverage);

/// <summary>
/// The area coverage of polygons on a grid of unit cells (pixels in device space, samples in image space), by
/// signed-area accumulation: the algorithm known from libart and described for stb_truetype's second rasteriser
/// (S. Barrett, "How the stb_truetype Anti-Aliased Software Rasterizer v2 Works", 2015) and for font-rs (R. Levien,
/// "Inside the fastest font renderer in the world", 2016). Written from those descriptions; no code was taken from them.
/// </summary>
/// <remarks>
/// <para><b>The algorithm.</b> The coverage of a cell is the integral, over the cell, of the winding number. Each edge
/// changes the winding number to its right by +1 (going down) or −1 (going up). Cut an edge at the rows and at the
/// columns it crosses; one piece lies in one cell (column c, row r), runs from x0 to x1 over a height h, signed by its
/// direction. Of that cell, the part to the right of the piece is (1 − (m − c))·h, with m = (x0 + x1)/2 its middle (the
/// piece is straight, so this is exact), and every cell to the right gets the whole h. So the piece adds (1 − (m − c))·h
/// to cell c and (m − c)·h to cell c + 1, and a running sum along the row turns these differences into the integral of the
/// winding number over each cell. Exact for straight edges: the only error is the rounding of double arithmetic.</para>
/// <para><b>Fill rules.</b> The integral a is turned into coverage by the non-zero rule as min(1, |a|) and by the even-odd
/// rule by folding |a| into [0, 1] with period 2, each over the part of the cell inside the area the polygons are clipped
/// to (A · g(|a| / A), <see cref="Integrate"/>; A = 1 for a cell wholly inside). That is exact where the winding number
/// takes at most two values in the cell, 0 and one other or two of one sign: in every cell that one run of an outline
/// crosses simply. Where it takes more (contours that overlap or cross in the cell, a contour that crosses itself there,
/// a clip box's side between them) the integral alone cannot tell the parts apart. <see cref="CellFlags"/> flags every
/// such cell, from the pieces of edges in it (a cell it does not flag is exact, by the argument there), and a flagged
/// cell gets its coverage from the exact arrangement of those pieces (<see cref="CellArrangement"/>): the area where the
/// winding number is not 0, or is odd, inside the cell and the area. It keeps the integral's value where the two differ by
/// at most <see cref="KeepWithin"/>, so a cell that was right keeps its bits. So the coverage is exact in every cell, to the
/// rounding of doubles.</para>
/// <para><b>Work.</b> A flagged cell with m pieces costs m·(2m + m(m − 1)/2) + m² units
/// (<see cref="CellArrangement.Charge"/>); the first pass adds them up before a row is reported, and the caller decides
/// whether to go on (<see cref="Canvas.MaxFillWork"/>, <see cref="Canvas.MaxCanvasWork"/>). A row whose flagged cells hold
/// more than <see cref="MaxRowPieces"/> pieces is refused here.</para>
/// <para><b>Determinism.</b> The pixel path uses only addition, subtraction, multiplication, division and
/// <see cref="Math.Sqrt"/>, which IEEE 754 requires to be correctly rounded, plus floor, ceiling and conversions, all on
/// doubles and in a fixed order (no parallel loops, no vector instructions, no <see cref="Math.FusedMultiplyAdd"/>; .NET
/// does not fuse a multiplication and an addition on its own). No trigonometric, exponential or power function, whose
/// last bit may differ per platform, is used to make a pixel. So the same input gives the same pixels on Linux and
/// Windows, x64 and arm64.</para>
/// </remarks>
internal sealed class AreaRasterizer
{
    private readonly List<Edge> _edges = [];
    private readonly List<Flat> _flats = [];
    private readonly List<Edge> _active = [];
    private double[] _cells = [];
    private double[] _coverage = [];
    private readonly CellArrangement _arrangement = new();
    private CellFlags _flags = new(0);
    private bool[] _flagged = [];
    private bool _collecting;
    private Kept[] _kept = new Kept[64];
    private long[] _keys = new long[64];
    private int _pieces;
    private int _firstColumn;
    private int _width;
    private int _firstRow;
    private int _lastRow;
    private int _fromColumn;
    private int _toColumn;

    /// <summary>An edge going down from (X0, Y0) to (X1, Y1), Y0 &lt; Y1; <see cref="Direction"/> is +1 when the polygon goes
    /// down along it and −1 when it goes up. <see cref="Serial"/> and <see cref="Turned"/> number the edges along their
    /// polygon (<see cref="Number"/>).</summary>
    private readonly record struct Edge(double X0, double Y0, double X1, double Y1, int Direction, int Serial, int Turned);

    /// <summary>A piece of an edge kept for a flagged cell, in the cell's own coordinates and the polygon's order.</summary>
    private readonly record struct Kept(double X0, double Y0, double X1, double Y1);

    /// <summary>The most pieces of edges the flagged cells of one row may hold: 2^18. Above it a shape is refused
    /// (<c>Documents.Graphics.TooMuchWork</c>); the pieces of one row are kept at once.</summary>
    public const int MaxRowPieces = 1 << 18;

    /// <summary>A flagged cell keeps the value of the integral where its exact coverage differs from it by at most this,
    /// 10^-9: a cell that was right keeps its bits.</summary>
    public const double KeepWithin = 1e-9;

    /// <summary>A horizontal edge at height Y, from x <see cref="From"/> to x <see cref="To"/> in the polygon's own order.
    /// It adds nothing to the integral (no height), but it is part of the outline, so it counts for the flag.</summary>
    private readonly record struct Flat(double Y, double From, double To, int Serial, int Turned);

    /// <summary>
    /// Computes the coverage of the polygons, row by row from the top, and hands each row to <paramref name="row"/>: every
    /// row and column between the polygons' extremes that is a cell of <paramref name="area"/>, and no other. Exact in
    /// every cell, also where contours overlap or cross: the cells <see cref="CellFlags"/> flags get their area from
    /// <see cref="CellArrangement"/>.
    /// </summary>
    /// <remarks>
    /// <para>The polygons are clipped to the area first (<see cref="Clipping"/>), so no cell outside it has coverage. The
    /// extremes may still name a cell just outside: a polygon squeezed onto the area's right edge by the clip (a shape
    /// outside that touches the edge) spans no column and is given the one cell to the right of that edge, which it covers
    /// by 0. Such a cell is not reported, so a caller may index its own buffer by every cell it receives; the running sum
    /// along a row still starts at the polygons' own first column, so the cells reported keep their exact values.</para>
    /// <para>Two passes. The first flags the cells and adds up their charge (<see cref="CellArrangement.Charge"/>) over the
    /// whole shape, and refuses a row whose flagged cells hold more than <see cref="MaxRowPieces"/> pieces; then
    /// <paramref name="admit"/> sees the charge and may refuse it. Only then is the first row reported, so a refused shape
    /// reports nothing. The second pass computes the rows. A flagged cell whose arrangement and integral do not agree
    /// (<see cref="CellArrangement.TryArea"/>) stops it with <c>Documents.Graphics.InexactCoverage</c> before that row is
    /// reported; the rows above it have been.</para>
    /// </remarks>
    /// <param name="polygons">Closed polygons; their edges together are the shape.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="area">The cells that may be reported: columns ⌊left⌋ to ⌈right⌉ − 1, rows ⌊top⌋ to ⌈bottom⌉ − 1; its
    /// edges are integers of 32 bits after rounding outwards.</param>
    /// <param name="row">Receives each row's coverage.</param>
    /// <param name="admit">Given the charge of the shape, an error to refuse it with, or null; null admits every shape
    /// the row limit lets through.</param>
    /// <returns>The charge, or the error.</returns>
    public Result<long, Error> Rasterize(List<List<Vertex>> polygons, FillRule rule, PixelBox area, CoverageRow row, Func<long, Error?>? admit = null)
    {
        if (!Prepare(polygons, area))
        {
            return Result<long, Error>.Success(0);
        }

        var (from, to) = (_fromColumn - _firstColumn, _toColumn - _firstColumn);
        var charge = 0L;
        var (next, flat) = (0, 0);
        for (var y = _firstRow; y < _lastRow; y++)
        {
            Advance(y, ref next);
            Flag(y, ref flat);
            var pieces = 0L;
            for (var column = from; column < to; column++)
            {
                if (_flags.IsFlagged(column))
                {
                    pieces += _flags.Pieces(column);

                    // Counted up to MaxCharge, without passing it (each charge is at most that).
                    var cost = CellArrangement.Charge(_flags.Pieces(column));
                    charge = cost > CellArrangement.MaxCharge - charge ? CellArrangement.MaxCharge : charge + cost;
                }
            }

            if (pieces > MaxRowPieces)
            {
                return Result<long, Error>.Failure(TooMuchWork(
                    $"Row {y} of the shape has {pieces} pieces of edges in pixels where its contours may overlap; a row may have at most {MaxRowPieces}.",
                    pieces,
                    MaxRowPieces));
            }
        }

        if (admit?.Invoke(charge) is { } refused)
        {
            return Result<long, Error>.Failure(refused);
        }

        _active.Clear();
        (next, flat) = (0, 0);
        for (var y = _firstRow; y < _lastRow; y++)
        {
            Advance(y, ref next);
            Flag(y, ref flat);
            _collecting = false;
            for (var column = from; column < to; column++)
            {
                _flagged[column] = _flags.IsFlagged(column);
                _collecting |= _flagged[column];
            }

            _pieces = 0;
            Array.Clear(_cells, 0, _width + 2);
            foreach (var edge in _active)
            {
                AddPiece(edge, y, _firstColumn);
            }

            Integrate(_width, rule, _firstColumn, Share(y, area.Top, area.Bottom), area);
            if (_collecting)
            {
                CollectFlats(y, flat);
                if (Repair(y, rule) is { } inexact)
                {
                    return Result<long, Error>.Failure(inexact);
                }
            }

            row(y, _fromColumn, _coverage.AsSpan(from, to - from));
        }

        return Result<long, Error>.Success(charge);
    }

    /// <summary>
    /// Computes the coverage of one convex polygon, as <see cref="Rasterize"/> does, without flagging: a convex polygon
    /// winds around every point 0 times or once (one way), so the integral is the coverage in every cell.
    /// </summary>
    /// <param name="polygons">One convex polygon (a list of one).</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="area">The cells that may be reported, as for <see cref="Rasterize"/>.</param>
    /// <param name="row">Receives each row's coverage.</param>
    public void RasterizeConvex(List<List<Vertex>> polygons, FillRule rule, PixelBox area, CoverageRow row)
    {
        if (!Prepare(polygons, area))
        {
            return;
        }

        var next = 0;
        for (var y = _firstRow; y < _lastRow; y++)
        {
            Advance(y, ref next);
            Array.Clear(_cells, 0, _width + 2);
            foreach (var edge in _active)
            {
                AddPiece(edge, y, _firstColumn);
            }

            Integrate(_width, rule, _firstColumn, Share(y, area.Top, area.Bottom), area);
            row(y, _fromColumn, _coverage.AsSpan(_fromColumn - _firstColumn, _toColumn - _fromColumn));
        }
    }

    /// <summary>An error <c>Documents.Graphics.TooMuchWork</c> with the work asked for and the limit.</summary>
    /// <param name="message">What was asked for.</param>
    /// <param name="value">The amount asked for.</param>
    /// <param name="limit">The limit.</param>
    /// <returns>The error.</returns>
    public static Error TooMuchWork(string message, long value, long limit) =>
        new Error(GraphicsErrorCodes.TooMuchWork, message)
            .With(ErrorMetadataKeys.Value, value.ToString(CultureInfo.InvariantCulture))
            .With(GraphicsErrorCodes.Limit, limit.ToString(CultureInfo.InvariantCulture));

    /// <summary>The cells of the polygons inside the area that the flag rule (<see cref="CellFlags"/>) marks as needing
    /// more than the integral: column and row of each, row by row from the top.</summary>
    /// <param name="polygons">Closed polygons, as for <see cref="Rasterize"/>.</param>
    /// <param name="area">The cells that may be reported, as for <see cref="Rasterize"/>.</param>
    /// <returns>The flagged cells.</returns>
    public List<(int Column, int Row)> FlaggedCells(List<List<Vertex>> polygons, PixelBox area)
    {
        var flagged = new List<(int Column, int Row)>();
        if (!Prepare(polygons, area))
        {
            return flagged;
        }

        var (next, flat) = (0, 0);
        for (var y = _firstRow; y < _lastRow; y++)
        {
            Advance(y, ref next);
            Flag(y, ref flat);
            for (var column = _fromColumn - _firstColumn; column < _toColumn - _firstColumn; column++)
            {
                if (_flags.IsFlagged(column))
                {
                    flagged.Add((_firstColumn + column, y));
                }
            }
        }

        return flagged;
    }

    /// <summary>
    /// Takes the polygons' edges, numbered (<see cref="Number"/>) and sorted, and the extent of rows and columns; false
    /// when there is nothing to report.
    /// </summary>
    private bool Prepare(List<List<Vertex>> polygons, PixelBox area)
    {
        _edges.Clear();
        _flats.Clear();
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        var serial = 0;
        foreach (var polygon in polygons)
        {
            var count = 0;
            var previous = polygon[^1];
            foreach (var point in polygon)
            {
                count += point != previous ? 1 : 0;
                previous = point;
            }

            var index = 0;
            previous = polygon[^1];
            foreach (var point in polygon)
            {
                if (point != previous)
                {
                    var (number, turned) = Number(serial, index++, count);
                    if (point.Y != previous.Y)
                    {
                        _edges.Add(previous.Y < point.Y
                            ? new Edge(previous.X, previous.Y, point.X, point.Y, 1, number, turned)
                            : new Edge(point.X, point.Y, previous.X, previous.Y, -1, number, turned));
                    }
                    else
                    {
                        _flats.Add(new Flat(point.Y, previous.X, point.X, number, turned));
                    }
                }

                minX = double.Min(minX, point.X);
                maxX = double.Max(maxX, point.X);
                minY = double.Min(minY, point.Y);
                maxY = double.Max(maxY, point.Y);
                previous = point;
            }

            // A gap of one between polygons: the last edge of one and the first of the next are never consecutive.
            serial += count + 1;
        }

        if (_edges.Count == 0)
        {
            return false;
        }

        // The row's cells are the ones the polygons' extent touches, ⌊min x⌋ to ⌈max x⌉ − 1 (at least one), and of them
        // only those inside the area are reported. A piece adds to its own cell and to the next, and a vertical edge on
        // ⌈max x⌉ itself adds to the cell there: two cells more are kept for those differences, and they are never reported.
        _firstColumn = (int)Math.Floor(minX);
        _width = int.Max(1, (int)Math.Ceiling(maxX) - _firstColumn);
        _firstRow = int.Max((int)Math.Floor(minY), (int)Math.Floor(area.Top));
        _lastRow = int.Min((int)Math.Ceiling(maxY), (int)Math.Ceiling(area.Bottom));
        _fromColumn = int.Max(_firstColumn, (int)Math.Floor(area.Left));
        _toColumn = int.Min(_firstColumn + _width, (int)Math.Ceiling(area.Right));
        if (_toColumn <= _fromColumn)
        {
            return false;
        }

        if (_cells.Length < _width + 2)
        {
            _cells = new double[_width + 2];
            _coverage = new double[_width + 2];
            _flags = new CellFlags(_width + 2);
            _flagged = new bool[_width + 2];
        }

        // A total order of its own (not the sort's), so the sums along a row are added in the same order everywhere.
        _edges.Sort(static (left, right) =>
        {
            var order = left.Y0.CompareTo(right.Y0);
            order = order != 0 ? order : left.X0.CompareTo(right.X0);
            order = order != 0 ? order : left.Y1.CompareTo(right.Y1);
            order = order != 0 ? order : left.X1.CompareTo(right.X1);
            return order != 0 ? order : left.Direction.CompareTo(right.Direction);
        });
        _flats.Sort(static (left, right) => left.Y.CompareTo(right.Y));
        _active.Clear();
        return true;
    }

    /// <summary>
    /// The two numbers of edge <paramref name="index"/> of a polygon with <paramref name="count"/> edges whose numbers start
    /// at <paramref name="first"/>: its place along the polygon, and its place counted from the edge half way round.
    /// </summary>
    /// <remarks>
    /// Edges that follow each other along the polygon have numbers that follow each other, except where the polygon closes
    /// (its last edge and its first). Counted from half way round, those two follow each other, and the break lies
    /// opposite. So a run of edges that follow each other around the polygon, the closing pair included, has consecutive
    /// numbers in at least one of the two countings unless it holds both breaks, which takes more than half the polygon.
    /// </remarks>
    private static (int Serial, int Turned) Number(int first, int index, int count) =>
        (first + index, first + ((index + (count / 2)) % count));

    /// <summary>Brings the active edges to row <paramref name="y"/>: those that start above its bottom and end below its
    /// top.</summary>
    private void Advance(int y, ref int next)
    {
        while (next < _edges.Count && _edges[next].Y0 < y + 1)
        {
            _active.Add(_edges[next++]);
        }

        // Drop the edges that end above this row, in place: no closure, so a row allocates nothing.
        var kept = 0;
        for (var index = 0; index < _active.Count; index++)
        {
            if (_active[index].Y1 > y)
            {
                _active[kept++] = _active[index];
            }
        }

        _active.RemoveRange(kept, _active.Count - kept);
    }

    /// <summary>
    /// The flags of row <paramref name="y"/>: every piece of an edge that lies in a cell, the cell taken closed (its sides
    /// included), with the edges' own pieces as <see cref="AddPiece"/> cuts them and the horizontal edges at the row's height.
    /// </summary>
    private void Flag(int y, ref int flat)
    {
        _flags.Clear(_width + 2);
        foreach (var edge in _active)
        {
            var top = double.Max(edge.Y0, y);
            var bottom = double.Min(edge.Y1, y + 1);
            if (bottom <= top)
            {
                continue;
            }

            var slope = (edge.X1 - edge.X0) / (edge.Y1 - edge.Y0);
            var (xMin, xMax) = (double.Min(edge.X0, edge.X1), double.Max(edge.X0, edge.X1));
            var xTop = double.Clamp(edge.X0 + ((top - edge.Y0) * slope), xMin, xMax) - _firstColumn;
            var xBottom = double.Clamp(edge.X0 + ((bottom - edge.Y0) * slope), xMin, xMax) - _firstColumn;

            // The sides as the polygon runs: down (+1) or up (−1); right, left or neither.
            var dx = (xBottom - xTop) * edge.Direction;
            var signs = (edge.Direction > 0 ? CellFlags.Down : CellFlags.Up) | (dx > 0 ? CellFlags.Right : dx < 0 ? CellFlags.Left : CellFlags.Vertical);
            Mark(double.Min(xTop, xBottom), double.Max(xTop, xBottom), edge.Serial, edge.Turned, signs);
        }

        // Horizontal edges at the row's height, its top and bottom lines included: sorted by height, and one on a line
        // between two rows belongs to both.
        while (flat < _flats.Count && _flats[flat].Y < y)
        {
            flat++;
        }

        for (var index = flat; index < _flats.Count && _flats[index].Y <= y + 1; index++)
        {
            var piece = _flats[index];
            var (from, to) = (piece.From - _firstColumn, piece.To - _firstColumn);
            Mark(double.Min(from, to), double.Max(from, to), piece.Serial, piece.Turned, to > from ? CellFlags.Right : CellFlags.Left);
        }
    }

    /// <summary>Marks the closed cells a piece from x <paramref name="low"/> to <paramref name="high"/> (row-relative
    /// columns) lies in with a positive length: a vertical piece on the line between two cells lies in both.</summary>
    private void Mark(double low, double high, int serial, int turned, int signs)
    {
        var column = (int)Math.Floor(low);
        if (high <= low)
        {
            _flags.Mark(column, serial, turned, signs);
            if (column == low)
            {
                _flags.Mark(column - 1, serial, turned, signs);
            }

            return;
        }

        var last = (int)Math.Ceiling(high) - 1;
        for (var cell = column; cell <= last; cell++)
        {
            _flags.Mark(cell, serial, turned, signs);
        }
    }

    /// <summary>Adds the part of an edge inside row y to the cells, cut at the columns it crosses.</summary>
    private void AddPiece(Edge edge, int y, int firstColumn)
    {
        var top = double.Max(edge.Y0, y);
        var bottom = double.Min(edge.Y1, y + 1);
        if (bottom <= top)
        {
            return;
        }

        // The interpolated x is kept inside the edge's own x range: rounding may otherwise put it a hair beyond an end
        // point, and beyond the polygons' extremes, which size the row.
        var slope = (edge.X1 - edge.X0) / (edge.Y1 - edge.Y0);
        var (xMin, xMax) = (double.Min(edge.X0, edge.X1), double.Max(edge.X0, edge.X1));
        var xTop = double.Clamp(edge.X0 + ((top - edge.Y0) * slope), xMin, xMax) - firstColumn;
        var xBottom = double.Clamp(edge.X0 + ((bottom - edge.Y0) * slope), xMin, xMax) - firstColumn;
        var height = (bottom - top) * edge.Direction;
        var low = double.Min(xTop, xBottom);
        var high = double.Max(xTop, xBottom);
        var column = (int)Math.Floor(low);

        // For a flagged cell: an end of the piece that is the edge's own end point is kept at that point, not where the
        // interpolation (which may miss it by a rounding) puts it; on a cell's side it must be found there exactly.
        var (keptTop, keptBottom) = _collecting
            ? (top == edge.Y0 ? edge.X0 - firstColumn : xTop, bottom == edge.Y1 ? edge.X1 - firstColumn : xBottom)
            : (xTop, xBottom);
        if (high <= column + 1)
        {
            Add(column, (xTop + xBottom) / 2, height);
            if (_collecting && _flagged[column])
            {
                Collect(column, y, edge.Direction, keptTop, top, keptBottom, bottom);
            }

            return;
        }

        // The piece crosses columns: each column gets the share of the height that lies in it, which for a straight
        // piece is its share of the width.
        var last = (int)Math.Ceiling(high) - 1;
        for (var cell = column; cell <= last; cell++)
        {
            var from = double.Max(low, cell);
            var to = double.Min(high, cell + 1);
            Add(cell, (from + to) / 2, height * ((to - from) / (high - low)));
            if (_collecting && _flagged[cell])
            {
                // The part of the piece in this column, from its top end to its bottom end: at the column's sides
                // where it is cut there, else the piece's own ends.
                var (start, end) = xTop < xBottom ? (from, to) : (to, from);
                Collect(
                    cell,
                    y,
                    edge.Direction,
                    start == xTop ? keptTop : start,
                    At(start, xTop, top, xBottom, bottom),
                    end == xBottom ? keptBottom : end,
                    At(end, xTop, top, xBottom, bottom));
            }
        }
    }

    /// <summary>y on the piece from (<paramref name="xTop"/>, <paramref name="top"/>) to (<paramref name="xBottom"/>,
    /// <paramref name="bottom"/>) at x, its own ends exactly at its ends.</summary>
    private static double At(double x, double xTop, double top, double xBottom, double bottom) =>
        x == xTop ? top : x == xBottom ? bottom : double.Clamp(top + ((x - xTop) * ((bottom - top) / (xBottom - xTop))), top, bottom);

    /// <summary>Keeps a piece of a flagged cell, from its top end to its bottom end (row-relative x), in the order the
    /// polygon runs and relative to the cell's corner.</summary>
    private void Collect(int cell, int y, int direction, double xTop, double top, double xBottom, double bottom)
    {
        var (x0, y0, x1, y1) = direction > 0 ? (xTop, top, xBottom, bottom) : (xBottom, bottom, xTop, top);
        Keep(cell, x0 - cell, y0 - y, x1 - cell, y1 - y);
    }

    /// <summary>Keeps the horizontal edges of row <paramref name="y"/> in its flagged cells.</summary>
    private void CollectFlats(int y, int flat)
    {
        for (var index = flat; index < _flats.Count && _flats[index].Y <= y + 1; index++)
        {
            var piece = _flats[index];
            var (from, to) = (piece.From - _firstColumn, piece.To - _firstColumn);
            var (low, high) = (double.Min(from, to), double.Max(from, to));
            for (var cell = (int)Math.Floor(low); cell < high; cell++)
            {
                if ((uint)cell < (uint)_flagged.Length && _flagged[cell])
                {
                    var (left, right) = (double.Max(low, cell), double.Min(high, cell + 1));
                    var (start, end) = to > from ? (left, right) : (right, left);
                    Keep(cell, start - cell, piece.Y - y, end - cell, piece.Y - y);
                }
            }
        }
    }

    private void Keep(int cell, double x0, double y0, double x1, double y1)
    {
        if (_pieces == _kept.Length)
        {
            Array.Resize(ref _kept, _kept.Length * 2);
            Array.Resize(ref _keys, _keys.Length * 2);
        }

        _kept[_pieces] = new Kept(x0, y0, x1, y1);

        // Sorted by cell, then by the order kept: a key of the two.
        _keys[_pieces] = ((long)cell << 32) | (uint)_pieces;
        _pieces++;
    }

    /// <summary>Gives every flagged cell of the row its exact coverage (<see cref="CellArrangement"/>); the old value
    /// stays where the new one differs from it by at most <see cref="KeepWithin"/>. An error when a cell's arrangement and
    /// integral do not agree.</summary>
    private Error? Repair(int y, FillRule rule)
    {
        Array.Sort(_keys, 0, _pieces);
        var index = 0;
        for (var cell = _fromColumn - _firstColumn; cell < _toColumn - _firstColumn; cell++)
        {
            if (!_flagged[cell])
            {
                continue;
            }

            _arrangement.Clear();
            while (index < _pieces && (int)(_keys[index] >> 32) < cell)
            {
                index++;
            }

            for (; index < _pieces && (int)(_keys[index] >> 32) == cell; index++)
            {
                var piece = _kept[(int)(_keys[index] & uint.MaxValue)];
                _arrangement.Add(piece.X0, piece.Y0, piece.X1, piece.Y1);
            }

            if (!_arrangement.TryArea(_cells[cell], rule, out var exact))
            {
                return new Error(GraphicsErrorCodes.InexactCoverage, $"The coverage of pixel ({_firstColumn + cell}, {y}) could not be established: its edges and its integral do not agree.")
                    .With(ErrorMetadataKeys.Value, string.Create(CultureInfo.InvariantCulture, $"{_firstColumn + cell},{y}"));
            }

            if (!(Math.Abs(exact - _coverage[cell]) <= KeepWithin))
            {
                _coverage[cell] = exact;
            }
        }

        return null;
    }

    private void Add(int cell, double middle, double height)
    {
        var right = middle - cell;
        _cells[cell] += height * (1 - right);
        _cells[cell + 1] += height * right;
    }

    /// <summary>
    /// The running sum of the cells is each cell's integral a of the winding number over the part of the cell inside the
    /// area (the polygons are clipped to it); the fill rule makes it coverage over that part. With A the size of that
    /// part (<see cref="Share"/> of the column times that of the row), the mean winding number over it is a / A, so the
    /// coverage is A · g(|a| / A), where g is the fill rule on a mean winding: non-zero g(w) = min(1, w), so min(A, |a|);
    /// even-odd the fold, so A · fold(|a| / A), and 0 where A is 0. A cell wholly inside the area has A = 1 and gets
    /// g(|a|), bit for bit what it got before the area was taken into account. Taking the rule over the whole cell
    /// instead (g(|a|)) treats winding 2 over half a cell as winding 1 over all of it: inside two overlapping contours
    /// that a clip edge cuts, a cell would be fully covered (non-zero, instead of half) or covered where even-odd leaves
    /// it empty. g is 1-Lipschitz, so the rounding of a counts no more in A · g(|a| / A) than in a, however thin A is.
    /// </summary>
    private void Integrate(int width, FillRule rule, int firstColumn, double rowShare, PixelBox area)
    {
        var sum = 0.0;
        for (var cell = 0; cell < width; cell++)
        {
            sum += _cells[cell];

            // The running sum stays in the cell: a flagged cell's arrangement is anchored on it (Repair).
            _cells[cell] = sum;
            var integral = Math.Abs(sum);
            var part = Share(firstColumn + cell, area.Left, area.Right) * rowShare;
            if (rule == FillRule.NonZero)
            {
                _coverage[cell] = double.Min(part, integral);
            }
            else
            {
                // Over a part of a denormal size the mean may overflow; such a cell's coverage is at most that part, and
                // a flagged one gets its exact value from Repair.
                var mean = part > 0 ? integral / part : 0;
                _coverage[cell] = part > 0 && double.IsFinite(mean) ? part * Fold(mean) : 0;
            }
        }
    }

    /// <summary>The share of the unit interval of cell <paramref name="cell"/> (from cell to cell + 1) that lies between
    /// <paramref name="from"/> and <paramref name="to"/>: 1 for a cell wholly inside, 0 for one outside.</summary>
    private static double Share(int cell, double from, double to) =>
        double.Max(0, double.Min(cell + 1, to) - double.Max(cell, from));

    /// <summary>Even-odd: the integral folded into [0, 1] with period 2 (winding 1 covered, 2 not, 3 covered, …).</summary>
    private static double Fold(double area)
    {
        var folded = area - (2 * Math.Floor(area / 2));
        return folded > 1 ? 2 - folded : folded;
    }
}
