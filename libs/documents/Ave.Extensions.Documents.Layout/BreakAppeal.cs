namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// How good a place for a page break is: the common currency of keep rules, after Chromium's
/// <c>BreakAppeal</c> (docs/architecture.md, Background). CSS Fragmentation 3 §4.4 lists rules that make a break "allowed"; when no allowed break is
/// left the rules are relaxed rather than overflowing, and §4.5 leaves the choice among the remaining breaks to the
/// user agent. This engine follows Chromium: break at the breakpoint with the highest appeal, and among those the
/// one that fits the most content (the latest).
/// </summary>
/// <remarks>
/// Ordered from worst to best, so <c>a &gt; b</c> means "a is the better break". The forced break needs no value: it
/// is always taken.
/// </remarks>
public enum BreakAppeal
{
    /// <summary>
    /// Not a breakpoint by the rules of CSS Fragmentation 3 §4.1 at all, taken only so that content that does not fit
    /// is not lost: before the first content of a box, where nothing separates it from the box's top edge but a margin
    /// or the box's own top padding and border (a class C breakpoint needs a gap between the container's content edge
    /// and the child, and a break between margins and content is no breakpoint). §4.4: "the UA may break anywhere in
    /// order to avoid losing content off the edge of the fragmentainer". Chromium <c>kBreakAppealLastResort</c>, which
    /// it takes in the same places (checked against its print: a paragraph whose top margin leaves no room for its line
    /// at the top of the first page moves to the next page and leaves the first one empty). Every better breakpoint is
    /// preferred, so a box lower on the page moves whole to the next page instead.
    /// </summary>
    LastResort,

    /// <summary>
    /// The break violates an <c>avoid</c> value: <c>break-inside: avoid</c> on a box that would be split, or
    /// <c>break-before</c>/<c>break-after: avoid</c> on the boxes at a break between siblings (Chromium
    /// <c>kBreakAppealViolatingBreakAvoid</c>). Used only when no perfect break exists — so a box that is kept
    /// together but taller than a page still breaks, and pagination always ends.
    /// </summary>
    ViolatesAvoid,

    /// <summary>
    /// The break leaves fewer lines of a paragraph before it than <c>orphans</c> or fewer after it than <c>widows</c> ask
    /// for (CSS Fragmentation 3 §3.3; §4.4 rule 3), and violates nothing else (Chromium
    /// <c>kBreakAppealViolatingOrphansAndWidows</c>). Better than a break that violates an <c>avoid</c> value: §4.4 drops
    /// rule 3 first when the rules do not leave enough breakpoints.
    /// </summary>
    ViolatesOrphansWidows,

    /// <summary>No rule is against this break (Chromium <c>kBreakAppealPerfect</c>).</summary>
    Perfect,
}
