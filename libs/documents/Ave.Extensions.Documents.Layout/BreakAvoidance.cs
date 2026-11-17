using System.Linq;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Layout;

/// <summary>
/// The appeal of breakpoints under the <c>avoid</c> values of CSS Fragmentation 3: <c>break-before</c> and
/// <c>break-after</c> (§3.1) for a break between siblings (class A, §4.1), <c>break-inside</c> (§3.2) for a break
/// inside a box. §4.4 "Unforced Breaks" has four rules. Rule 1: "A fragmented flow may break at a class A break point
/// only if all the break-after and break-before values applicable to this break point allow it, which is when at least
/// one of them forces a break or when none of them forbid it". Rule 2: with all of them auto, not when "a common
/// ancestor of all the elements has a break-inside value of avoid". Rule 3 is orphans and widows (not implemented).
/// Rule 4: class B and C breaks only when "the break-inside property of all ancestors is auto". The rules are then
/// relaxed in order — first rule 3 is dropped, and if that still does not lead to sufficient break points, rules 1, 2
/// and 4 — so here a disallowed break keeps a lower appeal instead of being impossible, and is still used when nothing
/// better exists.
/// </summary>
internal static class BreakAvoidance
{
    /// <summary>
    /// The appeal of a break between two adjacent siblings. The values that generate it are those of the box before
    /// and its last descendants, and of the box after and its first descendants: a value on a first or last child
    /// propagates to its parent's edge (§3.1.1: "a break-after value on a last in-flow child box is propagated to its
    /// container"), as <see cref="ForcedBreaks.Before"/> does for <c>page</c>.
    /// </summary>
    /// <param name="before">The sibling before the break.</param>
    /// <param name="after">The sibling after the break.</param>
    /// <returns>The appeal.</returns>
    public static BreakAppeal Between(Box before, Box after) =>
        AvoidsAfter(before) || AvoidsBefore(after) ? BreakAppeal.ViolatesAvoid : BreakAppeal.Perfect;

    /// <summary>
    /// The appeal of a break inside a box, given the appeal of the break its own layout chose: lowered to
    /// <see cref="BreakAppeal.ViolatesAvoid"/> when the box has <c>break-inside: avoid</c>.
    /// </summary>
    /// <param name="box">The box that breaks.</param>
    /// <param name="achieved">The appeal of the break inside its content.</param>
    /// <returns>The appeal.</returns>
    public static BreakAppeal Inside(Box box, BreakAppeal achieved) =>
        box.Style.BreakInside == BreakInside.Avoid ? BreakAppeal.ViolatesAvoid : achieved;

    /// <summary>The worse of two appeals.</summary>
    public static BreakAppeal Worst(BreakAppeal first, BreakAppeal second) => first < second ? first : second;

    private static bool AvoidsBefore(Box box) =>
        box.Style.BreakBefore == BreakBetween.Avoid || (box.Children.FirstOrDefault() is Box first && AvoidsBefore(first));

    private static bool AvoidsAfter(Box box) =>
        box.Style.BreakAfter == BreakBetween.Avoid || (box.Children.LastOrDefault() is Box last && AvoidsAfter(last));
}
