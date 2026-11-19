using System;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// The exact coverage of one cell that <see cref="CellFlags"/> flagged, from the pieces of the edges inside it: the area
/// where the winding number is not 0 (non-zero) or is odd (even-odd).
/// </summary>
/// <remarks>
/// <para><b>Coordinates.</b> The cell is the unit square from (0, 0) to (1, 1), its pieces given relative to its corner in
/// the order the polygon runs, so a piece cut at the cell's left side starts or ends at x = 0 exactly.</para>
/// <para><b>Bands.</b> The cell is cut into horizontal bands at the height of every end of a piece and of every point
/// where two pieces cross. Inside a band no two pieces cross and each spans the whole band or none of it, so the pieces
/// sorted by x at the band's middle part the band into trapezoids, each of which has one winding number; its area is the
/// band's height times its width at the middle. The sort is a total order (x at the middle, then at the top and the
/// bottom of the band, then the place of the piece), so its result does not depend on how the sort works.</para>
/// <para><b>The winding number.</b> Counted from the cell's left side, the winding number of a point is the sum of the
/// directions of the pieces left of it, plus the winding number of the left side at that height. That one changes where a
/// piece leaves or enters the cell through the left side; a piece that ends there is continued up the left side to the
/// cell's top corner (and one that starts there comes down from it), which closes every run of pieces that left the cell
/// through that side. So counted this way the winding number w′ differs from the true one by one whole number W0 over the
/// whole cell. The cell's integral a (the rasteriser's running sum) is the integral of the true winding number, W0 + J with
/// J = Σ w′·area, so W0 = round(a − J). If a − J is not within <see cref="Tolerance"/> of a whole number, the pieces and
/// the integral do not agree and no area is given.</para>
/// <para><b>Bounded work.</b> With m pieces: m(m − 1)/2 pairs tested for a crossing, at most 2m + 2 + m(m − 1)/2 heights,
/// and per band at most m pieces sorted; <see cref="Charge"/> covers that count, made before the cell is computed, and no
/// loop runs over anything but these pieces. Memory: arrays of the size of the pieces and of the heights, kept and reused.</para>
/// </remarks>
internal sealed class CellArrangement
{
    /// <summary>The farthest a − J may lie from a whole number: 2^-10.</summary>
    /// <remarks>
    /// <para>What a − J can round by, from the limits: the three largest terms, each rounding taken at its largest and all
    /// of one sign (2^-53 is the rounding of one operation, relative to its result). Smaller ones, such as the rounding of
    /// the height of a cut at a column and of a piece's middle and products, come on top. (1) The integral a is the
    /// running sum along the row up to the cell. Each piece of an edge in the row adds two terms of at most its height,
    /// at most 1, to its cells, and the running sum adds each cell that received any (an addition of 0 is exact); each
    /// addition rounds by at most 2^-53 of the sum it makes, which is at most the pieces in that cell and in the one to
    /// its left or the winding number there, at most the edges that cross the row. With P pieces in the row up to the cell that is at most about 2^-53 · 4P²: 4.4 × 10^-4 at
    /// P = 10^6, and 2^-10 itself near P = 1.5 × 10^6. No limit bounds P: <see cref="AreaRasterizer.MaxRowPieces"/>
    /// counts the pieces of flagged cells only, and a path has no limit on its edges. (2) J is a sum over the cell's own
    /// pieces, at most 643 within <see cref="Canvas.MaxFillWork"/>: at most 2m + 1 + m(m − 1)/2 bands of at most m + 1
    /// parts, about 1.34 × 10^8 areas, each added into its winding number's sum and weighted by that number, at most m
    /// plus the side steps, at most 3m: below 10^-4. (3) An end of a piece that is the edge's own end point is kept at
    /// that point, and the integral interpolates it; the two differ by the interpolation's rounding, a few times 2^-53 of
    /// the larger of the edge's x and its width, at most about 2^-21 with coordinates up to
    /// <see cref="Canvas.MaxPixelCoordinate"/> (a coverage box far out) and 2^-25 on a canvas (at most
    /// <see cref="Canvas.MaxPixels"/> on a side). Such an end moves the area by half that at most: for 643 pieces
    /// 1.5 × 10^-4, and 10^-5 on a canvas.</para>
    /// <para>So the limits give no bound well below 2^-10, and no smaller tolerance can be shown never to refuse a right
    /// pixel: 2^-10 stays. Nor do they show that 2^-10 never does: in a row of more than about 1.3 × 10^6 pieces before a
    /// flagged cell, with the winding number about as large, the worst case of the three terms together reaches it (by
    /// (1) alone near 1.5 × 10^6), and the fill would be refused with <c>Documents.Graphics.InexactCoverage</c>, never
    /// drawn wrong. Measured, the roundings are many orders smaller and of both signs: over every glyph of seven fonts and
    /// the three of the repository's Roboto subset at 12, 16 and 32 pixels per em, the engine's samples at 96 and 300 dpi
    /// and 210,200 random shapes, a − J was at most 1.2 × 10^-13 from a whole number. A pixel is right because
    /// its pieces are made right, not because of this guard (<see cref="AreaRasterizer"/>).</para>
    /// </remarks>
    public const double Tolerance = 1.0 / 1024;

    private readonly BandOrder _bandOrder = new();
    private double[] _xTop = new double[8];
    private double[] _yTop = new double[8];
    private double[] _xBottom = new double[8];
    private double[] _yBottom = new double[8];
    private int[] _direction = new int[8];
    private int _count;
    private double[] _sideY = new double[8];
    private int[] _sideStep = new int[8];
    private int _sides;
    private double[] _heights = new double[16];
    private double[] _slope = new double[8];
    private double[] _key = new double[8];
    private int[] _seen = new int[8];
    private int[] _order = new int[8];
    private int[] _next = new int[8];
    private double[] _levels = new double[16];

    /// <summary>The work units of a cell with <paramref name="pieces"/> pieces: m·(2m + m(m − 1)/2) + m², at least the
    /// m(m − 1)/2 pairs tested for a crossing and the at most 2m + 1 + m(m − 1)/2 bands of at most m pieces each; large
    /// counts are capped at <see cref="MaxCharge"/>.</summary>
    /// <param name="pieces">m.</param>
    /// <returns>The units.</returns>
    public static long Charge(long pieces) =>
        pieces > 1 << 20 ? MaxCharge : (pieces * ((2 * pieces) + (pieces * (pieces - 1) / 2))) + (pieces * pieces);

    /// <summary>The largest charge counted: 2^62, so sums of a few charges stay in a long.</summary>
    public const long MaxCharge = 1L << 62;

    /// <summary>Forgets the pieces of the last cell.</summary>
    public void Clear() => (_count, _sides) = (0, 0);

    /// <summary>Adds a piece of an edge, from (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>,
    /// <paramref name="y1"/>) in the order the polygon runs, in the cell's own coordinates.</summary>
    /// <param name="x0">x of the start.</param>
    /// <param name="y0">y of the start.</param>
    /// <param name="x1">x of the end.</param>
    /// <param name="y1">y of the end.</param>
    public void Add(double x0, double y0, double x1, double y1)
    {
        // Where the polygon leaves or enters through the left side, the run is continued up the side to the top corner.
        if (x0 == 0)
        {
            AddSide(y0, 1);
        }

        if (x1 == 0)
        {
            AddSide(y1, -1);
        }

        if (y0 == y1)
        {
            // A horizontal piece has no height; it matters only by where it meets the left side.
            return;
        }

        if (_count == _xTop.Length)
        {
            Grow(ref _xTop);
            Grow(ref _yTop);
            Grow(ref _xBottom);
            Grow(ref _yBottom);
            Grow(ref _slope);
            Array.Resize(ref _direction, _direction.Length * 2);
        }

        (_xTop[_count], _yTop[_count], _xBottom[_count], _yBottom[_count], _direction[_count]) =
            y0 < y1 ? (x0, y0, x1, y1, 1) : (x1, y1, x0, y0, -1);
        _slope[_count] = (_xBottom[_count] - _xTop[_count]) / (_yBottom[_count] - _yTop[_count]);
        _count++;
    }

    /// <summary>
    /// The coverage of the cell under the fill rule, from its pieces and its integral; false when the integral and the
    /// pieces do not agree to <see cref="Tolerance"/> (see the class remarks).
    /// </summary>
    /// <param name="integral">The integral of the winding number over the cell.</param>
    /// <param name="rule">The fill rule.</param>
    /// <param name="area">The coverage, 0 to 1 up to rounding.</param>
    /// <returns>True when the area was established.</returns>
    public bool TryArea(double integral, FillRule rule, out double area)
    {
        area = 0;
        var heights = Heights();

        // The winding numbers w′ lie between −(pieces + side steps) and +(pieces + side steps).
        var offset = _count + _sides;
        if (_levels.Length < (2 * offset) + 1)
        {
            _levels = new double[(2 * offset) + 1];
        }

        Array.Clear(_levels, 0, (2 * offset) + 1);
        if (_order.Length < _count)
        {
            (_key, _seen, _order, _next) = (new double[_xTop.Length], new int[_xTop.Length], new int[_xTop.Length], new int[_xTop.Length]);
        }

        Array.Fill(_seen, -1, 0, _count);
        var ordered = 0;
        for (var band = 0; band + 1 < heights; band++)
        {
            var (top, bottom) = (_heights[band], _heights[band + 1]);
            var height = bottom - top;
            var middle = top + (height / 2);

            // The pieces spanning the band: those of the band above in its order, then those that start here.
            var active = 0;
            for (var index = 0; index < ordered; index++)
            {
                var piece = _order[index];
                if (_yBottom[piece] >= bottom)
                {
                    _next[active++] = piece;
                    _seen[piece] = band;
                }
            }

            for (var piece = 0; piece < _count; piece++)
            {
                if (_seen[piece] != band && _yTop[piece] <= top && _yBottom[piece] >= bottom)
                {
                    _next[active++] = piece;
                    _seen[piece] = band;
                }
            }

            for (var index = 0; index < active; index++)
            {
                _key[_next[index]] = X(_next[index], middle);
            }

            Sort(active, top, bottom);
            (_order, _next, ordered) = (_next, _order, active);

            var winding = 0;
            for (var side = 0; side < _sides; side++)
            {
                winding += _sideY[side] >= bottom ? _sideStep[side] : 0;
            }

            var previous = 0.0;
            for (var index = 0; index < active; index++)
            {
                var piece = _order[index];
                var x = double.Clamp(_key[piece], previous, 1);
                _levels[winding + offset] += height * (x - previous);
                previous = x;
                winding += _direction[piece];
            }

            _levels[winding + offset] += height * (1 - previous);
        }

        var weighted = 0.0;
        for (var level = 0; level <= 2 * offset; level++)
        {
            weighted += (level - offset) * _levels[level];
        }

        var shift = integral - weighted;
        var whole = Math.Round(shift);
        if (!(Math.Abs(shift - whole) <= Tolerance && Math.Abs(whole) < 1L << 52))
        {
            return false;
        }

        var anchor = (long)whole;
        for (var level = 0; level <= 2 * offset; level++)
        {
            var winding = anchor + level - offset;
            area += (rule == FillRule.NonZero ? winding != 0 : long.IsOddInteger(winding)) ? _levels[level] : 0;
        }

        return true;
    }

    /// <summary>Sorts the cell's heights into <see cref="_heights"/>, each once: 0, 1, the ends of the pieces, the side
    /// steps and the crossings strictly inside two pieces' common heights. Returns how many.</summary>
    private int Heights()
    {
        var most = 2 + (2 * _count) + _sides + (_count * (_count - 1) / 2);
        if (_heights.Length < most)
        {
            _heights = new double[most];
        }

        var count = 0;
        _heights[count++] = 0;
        _heights[count++] = 1;
        for (var piece = 0; piece < _count; piece++)
        {
            _heights[count++] = _yTop[piece];
            _heights[count++] = _yBottom[piece];
        }

        for (var side = 0; side < _sides; side++)
        {
            _heights[count++] = _sideY[side];
        }

        for (var first = 0; first < _count; first++)
        {
            for (var second = first + 1; second < _count; second++)
            {
                var top = double.Max(_yTop[first], _yTop[second]);
                var bottom = double.Min(_yBottom[first], _yBottom[second]);
                if (!(top < bottom))
                {
                    continue;
                }

                var above = X(first, top) - X(second, top);
                var below = X(first, bottom) - X(second, bottom);
                if ((above < 0 && below > 0) || (above > 0 && below < 0))
                {
                    var y = top + ((bottom - top) * (above / (above - below)));
                    if (y > top && y < bottom)
                    {
                        _heights[count++] = y;
                    }
                }
            }
        }

        // Only heights inside the cell, sorted, each once.
        Array.Sort(_heights, 0, count);
        var kept = 0;
        for (var index = 0; index < count; index++)
        {
            var y = double.Clamp(_heights[index], 0, 1);
            if (kept == 0 || y > _heights[kept - 1])
            {
                _heights[kept++] = y;
            }
        }

        return kept;
    }

    /// <summary>
    /// Sorts the first <paramref name="count"/> pieces of <see cref="_next"/>, which hold the band above's order (pieces
    /// that start in this band after them), by <see cref="BandOrder"/>: in place, by insertion, which costs one step per
    /// pair that changed places (a crossing); past 8 steps per piece it sorts them all anew.
    /// </summary>
    private void Sort(int count, double top, double bottom)
    {
        _bandOrder.Set(this, top, bottom);
        var steps = 0L;
        var limit = (8L * count) + 64;
        for (var index = 1; index < count; index++)
        {
            var piece = _next[index];
            var place = index - 1;
            while (place >= 0 && _bandOrder.Compare(_next[place], piece) > 0)
            {
                _next[place + 1] = _next[place];
                place--;
                if (++steps > limit)
                {
                    _next[place + 1] = piece;
                    Array.Sort(_next, 0, count, _bandOrder);
                    return;
                }
            }

            _next[place + 1] = piece;
        }
    }

    /// <summary>x of a piece at height y inside its range; its own ends exactly at its ends.</summary>
    private double X(int piece, double y)
    {
        var (x0, y0, x1, y1) = (_xTop[piece], _yTop[piece], _xBottom[piece], _yBottom[piece]);
        if (y <= y0)
        {
            return x0;
        }

        if (y >= y1)
        {
            return x1;
        }

        return double.Clamp(x0 + ((y - y0) * _slope[piece]), double.Min(x0, x1), double.Max(x0, x1));
    }

    private void AddSide(double y, int step)
    {
        if (_sides == _sideY.Length)
        {
            Grow(ref _sideY);
            Array.Resize(ref _sideStep, _sideStep.Length * 2);
        }

        (_sideY[_sides], _sideStep[_sides]) = (y, step);
        _sides++;
    }

    private static void Grow(ref double[] values) => Array.Resize(ref values, values.Length * 2);

    /// <summary>The order of the pieces in one band: x at the middle (computed once per band), then x at the top and at
    /// the bottom, then the piece's place.</summary>
    private sealed class BandOrder : IComparer<int>
    {
        private CellArrangement? _cell;
        private double _top;
        private double _bottom;

        public void Set(CellArrangement cell, double top, double bottom) => (_cell, _top, _bottom) = (cell, top, bottom);

        public int Compare(int left, int right)
        {
            var cell = _cell!;
            var order = cell._key[left].CompareTo(cell._key[right]);
            order = order != 0 ? order : cell.X(left, _top).CompareTo(cell.X(right, _top));
            order = order != 0 ? order : cell.X(left, _bottom).CompareTo(cell.X(right, _bottom));
            return order != 0 ? order : left.CompareTo(right);
        }
    }
}
