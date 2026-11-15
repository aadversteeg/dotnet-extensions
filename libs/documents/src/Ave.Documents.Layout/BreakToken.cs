using Ave.Documents.Core;

namespace Ave.Documents.Layout;

/// <summary>
/// Where a box was broken at the end of a page, so its layout can continue on the next page (research §7:
/// "break tokens are the only pagination state; layout stays pure"). Each algorithm defines its own token type
/// and only ever receives tokens it produced itself. Open, so that algorithms outside this assembly can define
/// theirs. Tokens are values: two tokens for the same position are equal.
/// </summary>
public abstract record BreakToken;

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
public sealed record TracksBreakToken(int NextRow, ValueList<CellBreak> Cells, bool RowIsSplit) : BreakToken;

/// <summary>Where one cut cell continues.</summary>
/// <param name="Cell">The cell's index in the table grid (document order).</param>
/// <param name="Token">The cell's own break token, or null when nothing of it has been placed yet.</param>
/// <param name="IsComplete">True when the cell's content is complete and it only goes on covering rows (an empty
/// continuation with its background).</param>
public sealed record CellBreak(int Cell, BreakToken? Token, bool IsComplete);
