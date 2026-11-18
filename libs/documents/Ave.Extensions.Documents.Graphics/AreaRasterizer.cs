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
/// The exact area coverage of polygons on a grid of unit cells (pixels in device space, samples in image space), by
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
/// <para><b>Fill rules.</b> The integral is turned into coverage by the non-zero rule as min(1, |a|) and by the even-odd
/// rule by folding |a| into [0, 1] with period 2. Both are exact where the winding number in a cell is 0 or ±1; in a cell
/// where parts with winding 2 and 0 meet (overlapping contours of a glyph) the folded integral differs from the area of
/// the covered part. FreeType, stb_truetype and font-rs make the same approximation.</para>
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
    /// Computes the coverage of the polygons, row by row from the top, and hands each row to <paramref name="row"/>. The
    /// polygons must lie inside the grid area the caller draws into (clip them first); every row and column between the
    /// polygons' extremes is reported.
    /// </summary>
    /// <param name="polygons">Closed polygons; their edges together are the shape.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="row">Receives each row's coverage.</param>
    public void Rasterize(List<List<Vertex>> polygons, FillRule rule, CoverageRow row)
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

        // The reported columns are exactly the cells the polygons' extent touches, ⌊min x⌋ to ⌈max x⌉ − 1 (at least one).
        // A piece adds to its own cell and to the next, and a vertical edge on ⌈max x⌉ itself adds to the cell there: two
        // cells more are kept for those differences, and they are never reported.
        var firstColumn = (int)Math.Floor(minX);
        var width = int.Max(1, (int)Math.Ceiling(maxX) - firstColumn);
        var firstRow = (int)Math.Floor(minY);
        var lastRow = (int)Math.Ceiling(maxY);
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

            _active.RemoveAll(edge => edge.Y1 <= y);
            Array.Clear(_cells, 0, width + 2);
            foreach (var edge in _active)
            {
                AddPiece(edge, y, firstColumn);
            }

            Integrate(width, rule);
            row(y, firstColumn, _coverage.AsSpan(0, width));
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

    /// <summary>The running sum of the cells is each cell's integral of the winding number; the fill rule makes it coverage.</summary>
    private void Integrate(int width, FillRule rule)
    {
        var sum = 0.0;
        for (var cell = 0; cell < width; cell++)
        {
            sum += _cells[cell];
            var area = Math.Abs(sum);
            _coverage[cell] = rule == FillRule.NonZero
                ? double.Min(1, area)
                : Fold(area);
        }
    }

    /// <summary>Even-odd: the integral folded into [0, 1] with period 2 (winding 1 covered, 2 not, 3 covered, …).</summary>
    private static double Fold(double area)
    {
        var folded = area - (2 * Math.Floor(area / 2));
        return folded > 1 ? 2 - folded : folded;
    }
}
