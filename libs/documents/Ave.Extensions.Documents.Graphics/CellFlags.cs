using System;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// Per cell of one row: whether the integral of the winding number (<see cref="AreaRasterizer"/>) gives the cell's
/// coverage exactly under both fill rules, or the cell is flagged and needs its exact arrangement.
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
/// <para><b>What the rule may get wrong, and which way.</b> A flagged cell whose integral was exact after all is only
/// work: its arrangement gives the same area. A cell the rule would wrongly call clean is the one thing it must not do,
/// so every choice above counts more pieces, or breaks a run sooner, rather than fewer.</para>
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

    private readonly int[] _count;
    private readonly int[] _low;
    private readonly int[] _high;
    private readonly int[] _turnedLow;
    private readonly int[] _turnedHigh;
    private readonly int[] _signs;

    /// <summary>Room for <paramref name="cells"/> cells.</summary>
    /// <param name="cells">The cells of a row.</param>
    public CellFlags(int cells)
    {
        _count = new int[cells];
        _low = new int[cells];
        _high = new int[cells];
        _turnedLow = new int[cells];
        _turnedHigh = new int[cells];
        _signs = new int[cells];
    }

    /// <summary>The number of cells there is room for.</summary>
    public int Length => _count.Length;

    /// <summary>Forgets the first <paramref name="cells"/> cells' pieces.</summary>
    /// <param name="cells">How many.</param>
    public void Clear(int cells)
    {
        Array.Clear(_count, 0, cells);
        Array.Clear(_signs, 0, cells);
    }

    /// <summary>Counts a piece in a cell; a cell outside the room is not one of the row's and is left out.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="serial">The edge's number along its polygon.</param>
    /// <param name="turned">The edge's number counted from half way round.</param>
    /// <param name="signs">The piece's directions: <see cref="Down"/> or <see cref="Up"/> (neither for a horizontal one),
    /// and <see cref="Right"/>, <see cref="Left"/> or <see cref="Vertical"/>.</param>
    public void Mark(int cell, int serial, int turned, int signs)
    {
        if ((uint)cell >= (uint)_count.Length)
        {
            return;
        }

        if (_count[cell]++ == 0)
        {
            (_low[cell], _high[cell], _turnedLow[cell], _turnedHigh[cell]) = (serial, serial, turned, turned);
        }
        else
        {
            (_low[cell], _high[cell]) = (int.Min(_low[cell], serial), int.Max(_high[cell], serial));
            (_turnedLow[cell], _turnedHigh[cell]) = (int.Min(_turnedLow[cell], turned), int.Max(_turnedHigh[cell], turned));
        }

        _signs[cell] |= signs;
    }

    /// <summary>The number of pieces counted in a cell.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The count.</returns>
    public int Pieces(int cell) => _count[cell];

    /// <summary>True when the cell's integral may not give its coverage (see the class remarks).</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>Whether it is flagged.</returns>
    public bool IsFlagged(int cell)
    {
        var count = _count[cell];
        if (count == 0)
        {
            return false;
        }

        // Widened: the numbers may lie anywhere in the range of an int.
        var oneRun = (long)_high[cell] - _low[cell] + 1 == count || (long)_turnedHigh[cell] - _turnedLow[cell] + 1 == count;
        if (!oneRun)
        {
            return true;
        }

        var vertically = _signs[cell] & (Down | Up);
        var horizontally = _signs[cell] & (Right | Left | Vertical);
        return !(count <= 2 || vertically == Down || vertically == Up || horizontally == Right || horizontally == Left);
    }
}
