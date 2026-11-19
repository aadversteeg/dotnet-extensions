using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// The white-space properties of one run that line breaking reads (<see cref="GreedyLineBreaker"/>): whether its spaces are
/// preserved, which decides whether a space at a line end is removed or hangs, and whether it wraps.
/// </summary>
/// <param name="Collapse">The run's <c>white-space-collapse</c> (CSS Text 4 §4.1).</param>
/// <param name="Wrap">The run's <c>text-wrap-mode</c> (CSS Text 4 §5.1).</param>
public readonly record struct RunWhiteSpace(WhiteSpaceCollapse Collapse, TextWrapMode Wrap = TextWrapMode.Wrap)
{
    /// <summary>The CSS initial values: white space collapses, and text wraps.</summary>
    public static RunWhiteSpace Initial => new(WhiteSpaceCollapse.Collapse);

    /// <summary>True when the run's spaces are preserved (<see cref="WhiteSpaceCollapse.Preserve"/>,
    /// <see cref="WhiteSpaceCollapse.BreakSpaces"/>): not collapsible, so not removed at a line's start.</summary>
    public bool PreservesSpaces => WhiteSpaceCollapsing.PreservesSpaces(Collapse);

    /// <summary>True when the run's spaces never hang and a line may break after each of them
    /// (<see cref="WhiteSpaceCollapse.BreakSpaces"/>).</summary>
    public bool BreaksSpaces => Collapse == WhiteSpaceCollapse.BreakSpaces;

    /// <summary>True when the run's text wraps at its soft wrap opportunities (<see cref="TextWrapMode.Wrap"/>).</summary>
    public bool Wraps => Wrap == TextWrapMode.Wrap;
}
