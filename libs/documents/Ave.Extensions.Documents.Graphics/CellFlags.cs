using System;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>A piece of an edge in one cell: from (<see cref="X0"/>, <see cref="Y0"/>) to (<see cref="X1"/>,
/// <see cref="Y1"/>) in the order its polygon runs, relative to the cell's corner; the edge's two numbers along its polygon
/// and the piece's directions (<see cref="CellFlags"/>).</summary>
internal readonly record struct CellPiece(double X0, double Y0, double X1, double Y1, int Serial, int Turned, int Signs);

/// <summary>
/// Per cell of one row: whether the integral of the winding number (<see cref="AreaRasterizer"/>) gives the cell's
/// coverage exactly under both fill rules, or the cell is flagged; and of a flagged cell, from its pieces, whether its
/// integral is exact all the same because they are two sides apart (<see cref="IsTwoSided"/>), or it needs its exact
/// arrangement.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> A cell is taken closed, its sides included, and every piece of an edge that lies in it with a
/// positive length counts: horizontal pieces, pieces on its sides and pieces along a clip box's side as well. A cell is
/// clean when it has no piece, or when its pieces are one run of edges that follow each other along one polygon (the
/// polygon's last and first edge included) and there are at most two of them, or all that are not horizontal go down,
/// or all go up, or all go right, or all go left (none vertical). Every other cell is flagged.</para>
/// <para><b>Why a clean cell is exact.</b> Where the winding number takes only two values inside a cell, one of them 0
/// or both of one sign, the integral a gives the area: non-zero min(1, |a|), even-odd the fold of |a|. With no piece the
/// winding number is constant. A run of edges that is monotone in y (or strictly in x) is a simple line, and so are two
/// edges that share a point; it has no end inside the cell (a polygon is closed, so an end of the run lies on the cell's
/// boundary), so it parts the cell into regions whose winding numbers differ by exactly one: two values k and k + 1. If
/// one is 0, the integral is the area of the other; if both have one sign, the whole cell is covered under non-zero, and
/// the fold gives the share of the odd value under even-odd. That the pieces are one run is decided from three numbers per
/// cell, since an edge has at most one piece in a cell: the run's numbers are consecutive exactly when the largest minus
/// the smallest plus one is the count of pieces. Each edge has two numbers (<see cref="AreaRasterizer"/>'s numbering
/// along the polygon and counted from half way round), so a run through the polygon's closing pair is found in the
/// second numbering; a run that would need both breaks covers more than half the polygon, and is flagged.</para>
/// <para><b>Two sides apart.</b> Most flagged cells of text hold the two sides of a thin stroke, or the two sides of a
/// gap between strokes: one side going down and one going up (a vertical stroke), or one going right and one going left
/// (a horizontal one). A flagged cell is two-sided, and its integral is exact, when (X) it has no horizontal piece, its
/// pieces that go down are one run, its pieces that go up are one run, there is at least one of each, and a straight
/// line L that meets the cell's top side and its bottom side (its x at y = 0 and at y = 1 both from 0 to 1) has every end
/// of the downward pieces strictly on one side and every end of the upward ones strictly on the other; or (Y) the same
/// with x and y exchanged: no vertical piece, the pieces that go right one run and those that go left one run, and a line
/// that meets the left and right sides between them. Tried as L: the vertical line between the two kinds' extents in x,
/// then the lines along each piece's own slope (the sides of a slanted stroke are nearly parallel). Why, for (X): a piece
/// is straight, so with its ends it lies wholly on its side of L, and no piece meets L; every edge that crosses the open
/// cell has a piece there, so the winding number is one value w on L inside the cell. L meets every horizontal line of
/// the open cell inside it: its x at a height between 0 and 1 is a weighted mean of its x at 0 and at 1, so it is 0 or 1
/// only when both are, and then L is a side of the cell with a kind strictly beyond it, which cannot be. Now take a
/// point p of the open cell, on no piece, and the horizontal segment from p to L: it stays in the open cell and on p's
/// side of L, so it meets no piece of the other kind and no horizontal piece (there is none), only pieces of p's side.
/// Those are edges that follow each other along a polygon and all go down (or all up): a line strictly monotone in y,
/// which a horizontal segment meets at most once, crossing it. Crossing an edge that goes down from its left to its right
/// raises the winding number by one, crossing one that goes up lowers it; so with the downward side left of L, a point
/// left of L has w or w − 1, and a point right of it (across the upward side) also w or w − 1; with the upward side
/// left, both w or w + 1. In every case the winding number takes at most two values in the cell, next to each other, so
/// one is 0 or both have one sign, and the integral is exact as above, under both rules. (Y) is the same argument turned
/// a quarter: a line between the kinds that meets the left and right sides, vertical segments, a line strictly monotone
/// in x, and crossing an edge that goes right from above to below changes the winding number one way, one that goes left
/// the other way.</para>
/// <para>What the argument needs, and has: the absolute value of the winding number is never used, only that it takes
/// two neighbouring values, so it holds for any winding outside (also inside other contours) and for the two sides from
/// one polygon or from two. It is the same in a cell a clip box cuts: the polygons are clipped first, so a piece along the
/// clip box's side is an ordinary edge of a side (it counts as a piece of its kind), and the part of the cell outside the
/// box has winding 0, which is then one of the two values; the integral over the cell is the area of the other one inside
/// the box, and the rule is taken over the box's part of the cell (<see cref="AreaRasterizer"/>'s A · g(|a| / A)), exact
/// for winding values 0 and ±1. The pieces looked at are every piece in the closed cell, the ones its flags counted;
/// those on a side of the cell touch no point of the open cell, which is all the argument looks at, so counting them
/// only makes a run or the gap harder to have. The gap is strict: two sides that touch or cross, or a side of no width (two
/// coinciding edges), are not two-sided. The pieces and their directions are the ones the flags and the integral are made
/// from (<see cref="AreaRasterizer"/>, an end of an edge at its own point), so the cell is exact to the rounding of
/// doubles, as every clean cell is: a gap the size of a rounding moves an area by that much.</para>
/// <para><b>What the rule may get wrong, and which way.</b> A flagged cell whose integral was exact after all is only
/// work: its arrangement gives the same area. A cell the rule would wrongly call clean or two-sided is the one thing it
/// must not do, so every choice above counts more pieces, or breaks a run sooner, rather than fewer.</para>
/// <para><b>Cost.</b> Per piece, a few numbers of its cell are updated; nothing is cleared between rows (a cell whose
/// row stamp is old counts as empty). Whether a flagged cell is two-sided is decided from its own pieces, which are kept
/// for flagged cells only: a few steps per piece.</para>
/// </remarks>
internal sealed class CellFlags
{
    /// <summary>A piece the polygon runs down along.</summary>
    public const int Down = 1;

    /// <summary>A piece the polygon runs up along.</summary>
    public const int Up = 2;

    /// <summary>A piece the polygon runs right along.</summary>
    public const int Right = 4;

    /// <summary>A piece the polygon runs left along.</summary>
    public const int Left = 8;

    /// <summary>A vertical piece.</summary>
    public const int Vertical = 16;

    /// <summary>A horizontal piece.</summary>
    public const int Flat = 32;

    /// <summary>A vertical piece on the cell's right side: kept for the cell's flags and its two sides, not for its
    /// arrangement (it is the next cell's piece there).</summary>
    public const int Side = 64;

    private readonly Cell[] _cells;
    private int _stamp = 1;

    /// <summary>Room for <paramref name="cells"/> cells.</summary>
    /// <param name="cells">The cells of a row.</param>
    public CellFlags(int cells) => _cells = new Cell[cells];

    /// <summary>The number of cells there is room for.</summary>
    public int Length => _cells.Length;

    /// <summary>Forgets every cell's pieces, for the next row.</summary>
    public void Clear()
    {
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_cells);
            _stamp = 0;
        }

        _stamp++;
    }

    /// <summary>Counts a piece in a cell; a cell outside the room is not one of the row's and is left out.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="serial">The edge's number along its polygon.</param>
    /// <param name="turned">The edge's number counted from half way round.</param>
    /// <param name="signs">The piece's directions: <see cref="Down"/> or <see cref="Up"/> (<see cref="Flat"/> for a
    /// horizontal one), and <see cref="Right"/>, <see cref="Left"/> or <see cref="Vertical"/>.</param>
    public void Mark(int cell, int serial, int turned, int signs)
    {
        if ((uint)cell >= (uint)_cells.Length)
        {
            return;
        }

        ref var state = ref _cells[cell];
        if (state.Stamp != _stamp)
        {
            state = new Cell(_stamp, 1, serial, serial, turned, turned, signs);
            return;
        }

        state = new Cell(
            _stamp,
            state.Count + 1,
            int.Min(state.Low, serial),
            int.Max(state.High, serial),
            int.Min(state.TurnedLow, turned),
            int.Max(state.TurnedHigh, turned),
            state.Signs | signs);
    }

    /// <summary>The number of pieces counted in a cell.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The count.</returns>
    public int Pieces(int cell) => _cells[cell].Stamp == _stamp ? _cells[cell].Count : 0;

    /// <summary>True when the cell's integral may not give its coverage (see the class remarks).</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>Whether it is flagged.</returns>
    public bool IsFlagged(int cell)
    {
        ref readonly var state = ref _cells[cell];
        if (state.Stamp != _stamp)
        {
            return false;
        }

        // Widened: the numbers may lie anywhere in the range of an int.
        var count = state.Count;
        var oneRun = (long)state.High - state.Low + 1 == count || (long)state.TurnedHigh - state.TurnedLow + 1 == count;
        if (!oneRun)
        {
            return true;
        }

        var vertically = state.Signs & (Down | Up);
        var horizontally = state.Signs & (Right | Left | Vertical);
        return !(count <= 2 || vertically == Down || vertically == Up || horizontally == Right || horizontally == Left);
    }

    /// <summary>True when a flagged cell's pieces are two sides apart, so that its integral gives its coverage (see the
    /// class remarks) and it needs no arrangement.</summary>
    /// <param name="pieces">All pieces of the cell, as its flags counted them.</param>
    /// <returns>Whether it is two-sided.</returns>
    public static bool IsTwoSided(ReadOnlySpan<CellPiece> pieces)
    {
        // Per kind: count, the two numberings' extremes and the extent across (x for down and up, y for right and left).
        Kind down = default, up = default, right = default, left = default;
        var signs = 0;
        foreach (var piece in pieces)
        {
            signs |= piece.Signs;
            if ((piece.Signs & Down) != 0)
            {
                down.Add(piece.Serial, piece.Turned, double.Min(piece.X0, piece.X1), double.Max(piece.X0, piece.X1));
            }
            else if ((piece.Signs & Up) != 0)
            {
                up.Add(piece.Serial, piece.Turned, double.Min(piece.X0, piece.X1), double.Max(piece.X0, piece.X1));
            }

            if ((piece.Signs & Right) != 0)
            {
                right.Add(piece.Serial, piece.Turned, double.Min(piece.Y0, piece.Y1), double.Max(piece.Y0, piece.Y1));
            }
            else if ((piece.Signs & Left) != 0)
            {
                left.Add(piece.Serial, piece.Turned, double.Min(piece.Y0, piece.Y1), double.Max(piece.Y0, piece.Y1));
            }
        }

        return ((signs & Flat) == 0 && Apart(pieces, down, up, Down, Up, across: true))
            || ((signs & Vertical) == 0 && Apart(pieces, right, left, Right, Left, across: false));
    }

    /// <summary>
    /// True when both kinds have pieces, each kind's pieces are one run, and a line strictly between the two kinds meets
    /// the cell's top and bottom sides (<paramref name="across"/>; for right and left: its left and right sides). Tried
    /// first: the line across, from the kinds' extents; then lines along the slope of each of the kinds' pieces.
    /// </summary>
    private static bool Apart(ReadOnlySpan<CellPiece> pieces, in Kind first, in Kind second, int firstSign, int secondSign, bool across)
    {
        if (!first.IsOneRun || !second.IsOneRun)
        {
            return false;
        }

        if (first.Far < second.Near || second.Far < first.Near)
        {
            return true;
        }

        if (pieces.Length > MaxSlopes)
        {
            return false;
        }

        foreach (var piece in pieces)
        {
            var (run, rise) = across ? (piece.X1 - piece.X0, piece.Y1 - piece.Y0) : (piece.Y1 - piece.Y0, piece.X1 - piece.X0);
            if ((piece.Signs & (firstSign | secondSign)) != 0 && rise != 0 && Separated(pieces, run / rise, firstSign, secondSign, across))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when a line u = a + slope · v (u = x, v = y for <paramref name="across"/>, else the other way round) with a and
    /// a + slope both from 0 to 1, so that it meets the two sides v = 0 and v = 1 of the cell, has every end of the pieces
    /// of one kind strictly on one side and every end of the other kind's strictly on the other side.
    /// </summary>
    private static bool Separated(ReadOnlySpan<CellPiece> pieces, double slope, int firstSign, int secondSign, bool across)
    {
        // Per kind the range of u − slope · v over its pieces' ends: a separating a lies strictly between the ranges.
        var (firstLow, firstHigh, secondLow, secondHigh) = (double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity);
        foreach (var piece in pieces)
        {
            var (u0, v0, u1, v1) = across ? (piece.X0, piece.Y0, piece.X1, piece.Y1) : (piece.Y0, piece.X0, piece.Y1, piece.X1);
            var (start, end) = (u0 - (slope * v0), u1 - (slope * v1));
            if ((piece.Signs & firstSign) != 0)
            {
                (firstLow, firstHigh) = (double.Min(firstLow, double.Min(start, end)), double.Max(firstHigh, double.Max(start, end)));
            }
            else if ((piece.Signs & secondSign) != 0)
            {
                (secondLow, secondHigh) = (double.Min(secondLow, double.Min(start, end)), double.Max(secondHigh, double.Max(start, end)));
            }
        }

        // a and a + slope inside [0, 1]: a from max(0, −slope) to min(1, 1 − slope).
        var (low, high) = (double.Max(0, -slope), double.Min(1, 1 - slope));
        return low <= high && (Between(firstHigh, secondLow, low, high) || Between(secondHigh, firstLow, low, high));
    }

    /// <summary>True when some a with <paramref name="after"/> &lt; a &lt; <paramref name="before"/> lies from
    /// <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static bool Between(double after, double before, double low, double high) =>
        after < before && after < high && low < before;

    /// <summary>The most pieces a cell may have for the slanted lines to be tried (one try per piece, each over all the
    /// pieces): 16. A cell with more is not two-sided unless a line across parts it.</summary>
    private const int MaxSlopes = 16;

    /// <summary>The numbers of a cell in one row: <see cref="Stamp"/> names the row they belong to.</summary>
    private readonly record struct Cell(int Stamp, int Count, int Low, int High, int TurnedLow, int TurnedHigh, int Signs);

    /// <summary>The pieces of one kind in a cell.</summary>
    private struct Kind
    {
        private int _count;
        private int _low;
        private int _high;
        private int _turnedLow;
        private int _turnedHigh;

        public double Near { get; private set; }

        public double Far { get; private set; }

        public readonly bool IsOneRun =>
            _count > 0 && ((long)_high - _low + 1 == _count || (long)_turnedHigh - _turnedLow + 1 == _count);

        public void Add(int serial, int turned, double near, double far)
        {
            if (_count++ == 0)
            {
                (_low, _high, _turnedLow, _turnedHigh, Near, Far) = (serial, serial, turned, turned, near, far);
                return;
            }

            (_low, _high, _turnedLow, _turnedHigh) = (int.Min(_low, serial), int.Max(_high, serial), int.Min(_turnedLow, turned), int.Max(_turnedHigh, turned));
            (Near, Far) = (double.Min(Near, near), double.Max(Far, far));
        }
    }
}
