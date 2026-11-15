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
