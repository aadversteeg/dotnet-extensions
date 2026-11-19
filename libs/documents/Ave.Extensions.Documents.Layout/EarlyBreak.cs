namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// A breakpoint a layout found before the place where its fragment ended: the best one inside a fragment, by appeal and
/// then the latest, after Chromium's <c>EarlyBreak</c> ("Possible early unforced breakpoint. This represents a possible
/// (and good) location to break. In cases where we run out of space at an unideal location, we may want to go back and
/// break here instead", <c>third_party/blink/renderer/core/layout/early_break.h</c>; docs/architecture.md, Background).
/// A box reports it with a complete fragment (<see cref="LayoutResult.Placed.BestBreak"/>); a parent that runs out of
/// room at a worse break lays the box out once more with the early break in its constraint space
/// (<see cref="ConstraintSpace.BreakAt"/>), and the box breaks exactly there. A value, like a break token: each
/// algorithm defines its own kind, and only ever receives the kind it reported.
/// </summary>
public abstract record EarlyBreak
{
    // Only the algorithms of this assembly define early breaks (one kind each), as for BreakToken.
    private protected EarlyBreak(BreakAppeal appeal) => Appeal = appeal;

    /// <summary>The breakpoint's appeal, the box's own keep rules included.</summary>
    public BreakAppeal Appeal { get; }
}

/// <summary>An early break of <see cref="FlowLayout"/>: before a child (a class A breakpoint, CSS Fragmentation 3 §4.1), or
/// inside it, at the child's own early break.</summary>
public sealed record FlowEarlyBreak : EarlyBreak
{
    /// <summary>An early break before or inside a child.</summary>
    /// <param name="childIndex">The child the break comes before or is inside.</param>
    /// <param name="inside">The child's early break, or null for the break before the child.</param>
    /// <param name="appeal">The breakpoint's appeal: for a break inside the child, the child's lowered by the child's
    /// own <c>break-inside</c>.</param>
    internal FlowEarlyBreak(int childIndex, EarlyBreak? inside, BreakAppeal appeal)
        : base(appeal) => (ChildIndex, Inside) = (childIndex, inside);

    /// <summary>The child the break comes before or is inside.</summary>
    public int ChildIndex { get; }

    /// <summary>The child's early break, or null for the break before the child.</summary>
    public EarlyBreak? Inside { get; }
}

/// <summary>An early break of <see cref="InlineLayout"/>: before a line (a class B breakpoint, CSS Fragmentation 3 §4.1).</summary>
public sealed record InlineEarlyBreak : EarlyBreak
{
    /// <summary>An early break before a line.</summary>
    /// <param name="lineIndex">The line that starts the next page.</param>
    /// <param name="appeal">The breakpoint's appeal.</param>
    internal InlineEarlyBreak(int lineIndex, BreakAppeal appeal)
        : base(appeal) => LineIndex = lineIndex;

    /// <summary>The line that starts the next page.</summary>
    public int LineIndex { get; }
}

/// <summary>An early break of <see cref="TracksLayout"/>: before a body row that starts a unit (a class A breakpoint
/// between rows, CSS Fragmentation 3 §4.1). A cut inside a row depends on where the page ends, so it is not offered as an
/// early break (docs/architecture.md, Known limits).</summary>
public sealed record TracksEarlyBreak : EarlyBreak
{
    /// <summary>An early break before a row.</summary>
    /// <param name="row">The body row that starts the next page.</param>
    /// <param name="appeal">The breakpoint's appeal.</param>
    internal TracksEarlyBreak(int row, BreakAppeal appeal)
        : base(appeal) => Row = row;

    /// <summary>The body row that starts the next page.</summary>
    public int Row { get; }

    /// <summary>True when the break lies inside a row group kept together (<c>break-inside: avoid</c>): between two rows of
    /// the group, not before its first row. A break the table lays itself out again to is then lowered by that keep
    /// (<see cref="BreakAvoidance.Kept"/>), as the table applies the group's <c>break-inside</c> from outside, each time
    /// it looks (Chromium <c>CalculateBreakAppealInside</c>, <c>fragmentation_utils.cc</c>).</summary>
    internal bool InKeptGroup { get; init; }
}
