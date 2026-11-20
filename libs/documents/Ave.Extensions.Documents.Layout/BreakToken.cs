using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Where a box was broken at the end of a page, so its layout can continue on the next page. Break tokens are the
/// only state pagination carries from page to page, so every layout call stays a pure function. Each of the four
/// algorithms defines its own token type; pagination hands each box the token that box's own layout returned. A caller
/// of an algorithm can build a token, or pass one box's token to another box: the algorithm checks the token it is
/// given, and one it cannot have returned for the box (another algorithm's kind, a position the box does not have) is
/// the error <c>Documents.Layout.InvalidBreakToken</c>, never an exception. Tokens are values: two tokens for the same
/// position are equal.
/// </summary>
public abstract record BreakToken
{
    // Only the algorithms of this assembly define token types (one each).
    private protected BreakToken()
    {
    }
}

/// <summary>The break token of <see cref="FlowLayout"/>: which child to resume at, and where inside it.</summary>
/// <param name="ChildIndex">The index of the child that continues on the next page.</param>
/// <param name="ChildToken">Where to resume inside that child, or null to lay it out from its start (the break
/// fell before the child).</param>
public sealed record FlowBreakToken(int ChildIndex, BreakToken? ChildToken) : BreakToken
{
    /// <summary>
    /// True when a forced break before the child ended the page (<see cref="ForcedBreaks"/>): then the margins after
    /// the break take space on the next page ("adjoining margins before the break are truncated, but margins after the
    /// break are preserved", CSS Fragmentation 3 §5.2). False after every unforced break, where the margins adjoining
    /// it are truncated — also when the page after a forced break ends before the same child again, so its margin takes
    /// space on one page only (Chromium <c>AdjustMarginsForFragmentation</c>: "Leading block margins are truncated if
    /// they come right after an unforced break"). Part of the position: two tokens differ when only this differs.
    /// </summary>
    public bool IsForced { get; init; }
}

/// <summary>The break token of <see cref="InlineLayout"/>: the first line of the next fragment.</summary>
/// <param name="LineIndex">The index of the line that starts the next page.</param>
public sealed record InlineBreakToken(int LineIndex) : BreakToken
{
    /// <summary>How much of the line's height earlier pages showed, when the line is taller than the page area and is
    /// sliced (<see cref="MonolithicOverflow.Slice"/>, <see cref="SliceFragment"/>); 0 when the line starts on the next
    /// page.</summary>
    public double Shown { get; init; }

    /// <summary>How many slices of the line earlier pages showed (at most <see cref="MonolithicSlices.MaxSlices"/>); 0 when
    /// the line starts on the next page.</summary>
    public int Slices { get; init; }
}

/// <summary>The break token of <see cref="ImageLayout"/>: the image is still to be placed, on the next page, after a
/// last-resort break before it left only the box's top padding and border on this one; or the image is taller than the
/// page area and sliced, and the next page shows the next part of it (<see cref="Shown"/>).</summary>
public sealed record ImageBreakToken : BreakToken
{
    private ImageBreakToken(double shown, int slices) => (Shown, Slices) = (shown, slices);

    /// <summary>The image is still to be placed from its top.</summary>
    public static ImageBreakToken Instance { get; } = new(0, 0);

    /// <summary>How much of the image's height earlier pages showed as slices (<see cref="MonolithicOverflow.Slice"/>,
    /// <see cref="SliceFragment"/>); 0 when the image is still to be placed from its top.</summary>
    public double Shown { get; }

    /// <summary>How many slices of the image earlier pages showed (at most <see cref="MonolithicSlices.MaxSlices"/>); 0
    /// when the image is still to be placed from its top.</summary>
    public int Slices { get; }

    /// <summary>The token after the <paramref name="slices"/> slices that showed <paramref name="shown"/> of the image.</summary>
    /// <param name="shown">The height shown so far, more than 0.</param>
    /// <param name="slices">The slices shown so far, 1 or more.</param>
    /// <returns>The token.</returns>
    internal static ImageBreakToken After(double shown, int slices) => new(shown, slices);
}

/// <summary>The break token of <see cref="TracksLayout"/>: the first body row of the next fragment, and the cells that
/// were cut by the page end and continue there.</summary>
/// <param name="NextRow">The first body row of the next fragment (after the repeated header rows).</param>
/// <param name="Cells">The cells that cover <paramref name="NextRow"/> but started earlier: a cell spanning rows across
/// the break, or every cell of a row that broke inside.</param>
/// <param name="RowIsSplit">True when the break fell inside <paramref name="NextRow"/> (between the lines of its
/// cells), false when it fell before it.</param>
public sealed record TracksBreakToken(int NextRow, ValueList<CellBreak> Cells, bool RowIsSplit) : BreakToken
{
    /// <summary>
    /// The table as the first page prepared it — its grid, grid lines and column widths — so the next page does not
    /// build them again: a table's columns are the same on every page (CSS 2.1 §17.5.2 computes them once for the
    /// table), and building them per page made the layout of a long table grow with the square of its rows. Not part
    /// of the position: two tokens at the same place are equal whatever they carry.
    /// </summary>
    internal TablePreparation? Prepared { get; init; }

    /// <summary>
    /// True when the page this token ends showed body content other than slices of a piece taller than the page (a
    /// body cell that draws something outside a <see cref="SliceFragment"/>). Under <see cref="MonolithicOverflow.Slice"/>
    /// a piece that does not fit below a repeated header row is sliced there only after a page that showed no such body
    /// content (<see cref="TablePage"/>, remarks): the row moved once, and the page with only the header shows that
    /// moving gains nothing. Part of the position.
    /// </summary>
    public bool BodyShown { get; init; } = true;

    /// <summary>
    /// Whether the table's header rows are repeated at the top of its later fragments, as its first fragment decided
    /// (<see cref="TablePage"/>, RepeatsHeader): a later page does not decide again. Null in a token not made by the
    /// layout; then the page it resumes on decides, as a first page does. Part of the position.
    /// </summary>
    public bool? RepeatsHeader { get; init; }

    /// <summary>
    /// How much of the table passed the end of the page this token ends, below a row placed whole, under
    /// <see cref="TableEndOverflow.ContinuesOnNextPage"/>: the next fragment starts with that height. Where the row was the
    /// table's last (<see cref="NextRow"/> is the end of the table) the next fragment is that tall and holds no row; at a
    /// break between rows its rows follow below it. 0 when nothing passed. Part of the position.
    /// </summary>
    public double Passed { get; init; }

    /// <summary>True when part of a cell's border box passed the page end with <see cref="Passed"/>: the next fragment
    /// then draws the grid lines that passed; otherwise it draws nothing. Part of the position.</summary>
    public bool PaintsPassed { get; init; }

    /// <summary>
    /// The grid lines that passed the page end, as the page that ended drew them, moved up so that the page end is their
    /// top and cut there: what the next fragment draws when <see cref="PaintsPassed"/> is true. Empty in a token not made
    /// by the layout; then the next fragment draws nothing. Not part of the position, as <see cref="Prepared"/>.
    /// </summary>
    internal ValueList<PlacedFragment> PassedLines { get; init; }

    /// <summary>Equal when the positions are equal (<see cref="Prepared"/> and <see cref="PassedLines"/> are not compared).</summary>
    /// <param name="other">The other token.</param>
    /// <returns>True for the same position.</returns>
    public bool Equals(TracksBreakToken? other) =>
        other is not null && NextRow == other.NextRow && Cells.Equals(other.Cells) && RowIsSplit == other.RowIsSplit && BodyShown == other.BodyShown
        && RepeatsHeader == other.RepeatsHeader && Passed.Equals(other.Passed) && PaintsPassed == other.PaintsPassed;

    /// <summary>The hash of the position.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(NextRow, Cells, RowIsSplit, BodyShown, RepeatsHeader, Passed, PaintsPassed);
}

/// <summary>Where one cut cell continues.</summary>
/// <param name="Cell">The cell's index in the table grid (document order).</param>
/// <param name="Token">The cell's own break token, or null when nothing of it has been placed yet.</param>
/// <param name="IsComplete">True when the cell's content is complete and it only goes on covering rows (an empty
/// continuation with its background).</param>
public sealed record CellBreak(int Cell, BreakToken? Token, bool IsComplete);
