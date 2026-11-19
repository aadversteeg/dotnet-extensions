using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// The white-space properties of one run that line breaking reads (<see cref="GreedyLineBreaker"/>): whether its spaces are
/// preserved, which decides whether a space at a line end is removed or hangs.
/// </summary>
/// <param name="Collapse">The run's <c>white-space-collapse</c> (CSS Text 4).</param>
public readonly record struct RunWhiteSpace(WhiteSpaceCollapse Collapse)
{
    /// <summary>The CSS initial values: white space collapses.</summary>
    public static RunWhiteSpace Initial => new(WhiteSpaceCollapse.Collapse);

    /// <summary>True when the run's spaces are preserved (<see cref="WhiteSpaceCollapse.Preserve"/>): not collapsible, so
    /// not removed at a line's start, and hanging at its end.</summary>
    public bool PreservesSpaces => Collapse == WhiteSpaceCollapse.Preserve;
}
