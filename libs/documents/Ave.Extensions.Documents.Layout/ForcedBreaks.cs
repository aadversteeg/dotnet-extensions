using System.Linq;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>Forced page breaks between boxes (CSS Fragmentation 3 §3.1, <c>break-before</c>).</summary>
internal static class ForcedBreaks
{
    /// <summary>
    /// True when a page break is forced before the box, either by its own <c>break-before: page</c> or by one
    /// on its first child: "a break-before value on a first in-flow child box is propagated to its container" (CSS
    /// Fragmentation 3 §3.1.1), because a break is only possible between siblings.
    /// </summary>
    /// <remarks>
    /// Flow honours a forced break between two children of which the first is on the current page, when there is
    /// content on the page before the break: in the same box, or before the box (the box does not start the page).
    /// Content that is empty — a paragraph of collapsed white space, a box without children — does not count. A
    /// forced break with nothing before it on the page, such as one before the very first box of the document or
    /// before the child a page resumes at, would only produce an empty page, so it has no effect — the same outcome
    /// as propagating the break up to the root, where there is no earlier sibling to break after.
    /// </remarks>
    public static bool Before(Box box) =>
        box.Style.BreakBefore == BreakBetween.Page
        || (box.Children.FirstOrDefault() is Box first && Before(first));
}
