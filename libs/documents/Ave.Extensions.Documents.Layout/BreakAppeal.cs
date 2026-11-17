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
    /// The break violates an <c>avoid</c> value: <c>break-inside: avoid</c> on a box that would be split, or
    /// <c>break-before</c>/<c>break-after: avoid</c> on the boxes at a break between siblings (Chromium
    /// <c>kBreakAppealViolatingBreakAvoid</c>). Used only when no perfect break exists — so a box that is kept
    /// together but taller than a page still breaks, and pagination always ends.
    /// </summary>
    ViolatesAvoid,

    /// <summary>No rule is against this break (Chromium <c>kBreakAppealPerfect</c>).</summary>
    Perfect,
}
