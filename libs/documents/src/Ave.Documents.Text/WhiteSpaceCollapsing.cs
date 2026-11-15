using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Ave.Documents.Text;

/// <summary>
/// White-space processing for <c>white-space: normal</c>, the CSS initial value (CSS Text 3 §4.1.1, "Phase I:
/// Collapsing and Transformation").
/// </summary>
public static class WhiteSpaceCollapsing
{
    /// <summary>
    /// Collapses white space across the runs of one paragraph. Every sequence of spaces, tabs and line breaks
    /// becomes a single space U+0020, also when the sequence crosses the boundary between two runs: "Any
    /// collapsible space immediately following another collapsible space—even one outside the boundary of the
    /// inline containing that space, provided both spaces are within the same inline formatting context—is
    /// collapsed". The space is kept in the run where the sequence starts. Spaces at the start and end of a line
    /// are removed later, by line breaking (§4.1.2).
    /// </summary>
    /// <remarks>
    /// Collapsible white space is U+0020, tab U+0009 and the segment breaks line feed U+000A and carriage return
    /// U+000D, which CSS Text 3 §4.1 treats "identically to spaces". For Latin text a segment break becomes a
    /// space (§4.1.3). Other spaces, such as the no-break space U+00A0, are not collapsible and stay as they are.
    /// </remarks>
    /// <param name="runs">The text of each run, in order.</param>
    /// <returns>The collapsed text of each run; same count and order.</returns>
    public static ImmutableArray<string> Collapse(IReadOnlyList<string> runs)
    {
        // One pass over all characters of the paragraph; "previous character was white space" carries over
        // from one run to the next, which is what makes collapsing work across run boundaries.
        var result = ImmutableArray.CreateBuilder<string>(runs.Count);
        var previousWasSpace = false;
        foreach (var run in runs)
        {
            var collapsed = new StringBuilder(run.Length);
            foreach (var character in run)
            {
                var isSpace = IsCollapsible(character);
                if (!isSpace || !previousWasSpace)
                {
                    collapsed.Append(isSpace ? ' ' : character);
                }

                previousWasSpace = isSpace;
            }

            result.Add(collapsed.ToString());
        }

        return result.MoveToImmutable();
    }

    private static bool IsCollapsible(char character) => character is ' ' or '\t' or '\n' or '\r';
}
