using System;
using System.Collections.Generic;

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
/// to (A · g(|a| / A), <see cref="Integrate"/>; A = 1 for a cell wholly inside). Both are exact where, inside the part of
/// a cell inside the area, the winding number takes only the values 0 and 1, or only 0 and −1: inside every outline that
/// does not overlap itself, and wherever outlines wound the same way do not overlap. Elsewhere they are not exact.
/// Where two contours wound the same way overlap inside a cell, the part both cover counts twice before the clamp, so
/// the cell is up to a quarter too dark: measured on Roboto's 'Ɨ' (two crossing bars) at 2,000 random sizes and offsets,
/// at most 63.3 of 255 steps, and only in pixels that an edge of each contour crosses inside the area. Where parts wound
/// in opposite directions meet inside a cell they cancel, up to the whole cell:
/// two half-cell rectangles of opposite direction side by side give 0 where the non-zero rule asks for 1.</para>
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
    private readonly List<Edge> _active = [];
    private double[] _cells = [];
    private double[] _coverage = [];

    /// <summary>An edge going down from (X0, Y0) to (X1, Y1), Y0 &lt; Y1; <see cref="Direction"/> is +1 when the polygon goes
    /// down along it and −1 when it goes up.</summary>
    private readonly record struct Edge(double X0, double Y0, double X1, double Y1, int Direction);

    /// <summary>
    /// Computes the coverage of the polygons, row by row from the top, and hands each row to <paramref name="row"/>: every
    /// row and column between the polygons' extremes that is a cell of <paramref name="area"/>, and no other.
    /// </summary>
    /// <remarks>
    /// The polygons are clipped to the area first (<see cref="Clipping"/>), so no cell outside it has coverage. The
    /// extremes may still name a cell just outside: a polygon squeezed onto the area's right edge by the clip (a shape
    /// outside that touches the edge) spans no column and is given the one cell to the right of that edge, which it covers
    /// by 0. Such a cell is not reported, so a caller may index its own buffer by every cell it receives; the running sum
    /// along a row still starts at the polygons' own first column, so the cells reported keep their exact values.
    /// </remarks>
    /// <param name="polygons">Closed polygons; their edges together are the shape.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="area">The cells that may be reported: columns ⌊left⌋ to ⌈right⌉ − 1, rows ⌊top⌋ to ⌈bottom⌉ − 1; its
    /// edges are integers of 32 bits after rounding outwards.</param>
    /// <param name="row">Receives each row's coverage.</param>
    public void Rasterize(List<List<Vertex>> polygons, FillRule rule, PixelBox area, CoverageRow row)
    {
        _edges.Clear();
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var polygon in polygons)
        {
            var previous = polygon[^1];
            foreach (var point in polygon)
            {
                if (point.Y != previous.Y)
                {
                    _edges.Add(previous.Y < point.Y
                        ? new Edge(previous.X, previous.Y, point.X, point.Y, 1)
                        : new Edge(point.X, point.Y, previous.X, previous.Y, -1));
                }

                minX = double.Min(minX, point.X);
                maxX = double.Max(maxX, point.X);
                minY = double.Min(minY, point.Y);
                maxY = double.Max(maxY, point.Y);
                previous = point;
            }
        }

        if (_edges.Count == 0)
        {
            return;
        }

        // The row's cells are the ones the polygons' extent touches, ⌊min x⌋ to ⌈max x⌉ − 1 (at least one), and of them
        // only those inside the area are reported. A piece adds to its own cell and to the next, and a vertical edge on
        // ⌈max x⌉ itself adds to the cell there: two cells more are kept for those differences, and they are never reported.
        var firstColumn = (int)Math.Floor(minX);
        var width = int.Max(1, (int)Math.Ceiling(maxX) - firstColumn);
        var firstRow = int.Max((int)Math.Floor(minY), (int)Math.Floor(area.Top));
        var lastRow = int.Min((int)Math.Ceiling(maxY), (int)Math.Ceiling(area.Bottom));
        var fromColumn = int.Max(firstColumn, (int)Math.Floor(area.Left));
        var toColumn = int.Min(firstColumn + width, (int)Math.Ceiling(area.Right));
        if (toColumn <= fromColumn)
        {
            return;
        }

        if (_cells.Length < width + 2)
        {
            _cells = new double[width + 2];
            _coverage = new double[width + 2];
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
        _active.Clear();
        var next = 0;
        for (var y = firstRow; y < lastRow; y++)
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
            Array.Clear(_cells, 0, width + 2);
            foreach (var edge in _active)
            {
                AddPiece(edge, y, firstColumn);
            }

            Integrate(width, rule, firstColumn, Share(y, area.Top, area.Bottom), area);
            row(y, fromColumn, _coverage.AsSpan(fromColumn - firstColumn, toColumn - fromColumn));
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
        if (high <= column + 1)
        {
            Add(column, (xTop + xBottom) / 2, height);
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
        }
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
            var integral = Math.Abs(sum);
            var part = Share(firstColumn + cell, area.Left, area.Right) * rowShare;
            _coverage[cell] = rule == FillRule.NonZero
                ? double.Min(part, integral)
                : part > 0 ? part * Fold(integral / part) : 0;
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
