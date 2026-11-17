using System;
using Ave.Documents.Core;

namespace Ave.Documents.Layout;

/// <summary>
/// Where a box was broken at the end of a page, so its layout can continue on the next page. Break tokens are the
/// only state pagination carries from page to page, so every layout call stays a pure function. Each of the four
/// algorithms defines its own token type and only ever receives tokens it produced itself. Tokens are values: two
/// tokens for the same position are equal.
/// </summary>
public abstract record BreakToken
{
    // Only the algorithms of this assembly define tokens (one type each); a token an algorithm did not produce is a
    // programming error, never input.
    private protected BreakToken()
    {
    }
}

/// <summary>The break token of <see cref="FlowLayout"/>: which child to resume at, and where inside it.</summary>
/// <param name="ChildIndex">The index of the child that continues on the next page.</param>
/// <param name="ChildToken">Where to resume inside that child, or null to lay it out from its start (the break
/// fell before the child).</param>
public sealed record FlowBreakToken(int ChildIndex, BreakToken? ChildToken) : BreakToken;

/// <summary>The break token of <see cref="InlineLayout"/>: the first line of the next fragment.</summary>
/// <param name="LineIndex">The index of the line that starts the next page.</param>
public sealed record InlineBreakToken(int LineIndex) : BreakToken;

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

    /// <summary>Equal when the positions are equal (<see cref="Prepared"/> is not compared).</summary>
    /// <param name="other">The other token.</param>
    /// <returns>True for the same position.</returns>
    public bool Equals(TracksBreakToken? other) =>
        other is not null && NextRow == other.NextRow && Cells.Equals(other.Cells) && RowIsSplit == other.RowIsSplit;

    /// <summary>The hash of the position.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(NextRow, Cells, RowIsSplit);
}

/// <summary>Where one cut cell continues.</summary>
/// <param name="Cell">The cell's index in the table grid (document order).</param>
/// <param name="Token">The cell's own break token, or null when nothing of it has been placed yet.</param>
/// <param name="IsComplete">True when the cell's content is complete and it only goes on covering rows (an empty
/// continuation with its background).</param>
public sealed record CellBreak(int Cell, BreakToken? Token, bool IsComplete);
