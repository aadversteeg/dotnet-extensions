using System.Linq;
using Ave.Documents.Model;

namespace Ave.Documents.Layout;

/// <summary>Forced page breaks between boxes (CSS Fragmentation 3 §3.1, <c>break-before</c>).</summary>
internal static class ForcedBreaks
{
    /// <summary>
    /// True when a page break is forced before the box, either by its own <c>break-before: page</c> or by one
    /// on its first child: "break-before … on the first child … [is] propagated to the parent" (CSS
    /// Fragmentation 3 §3.1), because a break is only possible between siblings.
    /// </summary>
    /// <remarks>
    /// Flow honours a forced break only between two children of which the first is on the current page. A
    /// forced break before the very first box of the document, or before the child a page resumes at, would only
    /// produce an empty page, so it has no effect — the same outcome as propagating the break up to the root,
    /// where there is no earlier sibling to break after.
    /// </remarks>
    public static bool Before(Box box) =>
        box.Style.BreakBefore == BreakBefore.Page
        || (box.Children.FirstOrDefault() is Box first && Before(first));
}
