using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// White-space processing, per run by its <see cref="WhiteSpaceCollapse"/> (CSS Text 3 §4.1.1, "Phase I: Collapsing and
/// Transformation").
/// </summary>
public static class WhiteSpaceCollapsing
{
    /// <summary>The line separator U+2028: the forced line break of the model, which a preserved segment break becomes.</summary>
    public const char LineSeparator = '\u2028';

    /// <summary>Collapses white space across runs that all collapse it (<see cref="WhiteSpaceCollapse.Collapse"/>, the CSS
    /// initial value); otherwise as the overload with a mode per run.</summary>
    /// <param name="runs">The text of each run, in order.</param>
    /// <returns>The collapsed text of each run; same count and order.</returns>
    public static ImmutableArray<string> Collapse(IReadOnlyList<string> runs) =>
        Collapse([.. runs.Select(run => (run, WhiteSpaceCollapse.Collapse))]);

    /// <summary>
    /// Collapses white space across the runs of one paragraph, each run by its own mode, in one pass over the characters:
    /// <list type="bullet">
    /// <item><see cref="WhiteSpaceCollapse.Collapse"/>: every sequence of spaces, tabs and line breaks becomes a single space
    /// U+0020, also when the sequence crosses the boundary between two runs: "Any collapsible space immediately following
    /// another collapsible space—even one outside the boundary of the inline containing that space, provided both spaces are
    /// within the same inline formatting context—is collapsed". The space is kept in the run where the sequence starts. For
    /// Latin text a segment break becomes a space (§4.1.3).</item>
    /// <item><see cref="WhiteSpaceCollapse.PreserveBreaks"/>: the same for spaces and tabs, but a line feed is a preserved segment
    /// break, a forced line break (§4.1.3: "segment breaks are not collapsible and are instead transformed into a preserved line
    /// feed"; CSS Text 4 §5.1: "preserved segment breaks … must be treated as forced line breaks"): it becomes the line
    /// separator U+2028, the model's forced break, and the collapsible spaces
    /// after it are removed ("Any sequence of collapsible spaces and tabs immediately preceding or following a segment break is
    /// removed"); the spaces before it end the line and go there (§4.1.2), so they are not looked for again here.</item>
    /// <item><see cref="WhiteSpaceCollapse.Preserve"/>: nothing collapses; a space stays a space that is not collapsible, so it
    /// neither collapses with a collapsible space beside it nor stops one from being kept (case <c>ws-border</c>: "a ", two
    /// preserved spaces, "b" shows three spaces), and a line feed is a forced line break, U+2028. A carriage return is "treated
    /// identically to spaces (U+0020) in all respects" (§4), so it is a preserved space. A tab is refused before
    /// (<c>ModelCheck</c>).</item>
    /// </list>
    /// As Chromium reads the modes (Blink <c>InlineItemsBuilder::AppendText</c>, <c>AppendCollapseWhitespace</c>,
    /// <c>AppendPreserveNewline</c> and <c>AppendPreserveWhitespace</c>, core/layout/inline/inline_items_builder.cc: a line feed
    /// that is preserved is appended as a forced break). Spaces at the start and end of a line are removed later, by line
    /// breaking (§4.1.2), where preserved spaces hang instead (<see cref="GreedyLineBreaker"/>).
    /// </summary>
    /// <remarks>
    /// Collapsible white space is U+0020, tab U+0009, the segment break line feed U+000A, and carriage return U+000D,
    /// which CSS Text 3 §4 says is "treated identically to spaces (U+0020) in all respects". Other spaces, such as the
    /// no-break space U+00A0, are not collapsible and stay as they are.
    /// </remarks>
    /// <param name="runs">The text of each run with its mode, in order.</param>
    /// <returns>The collapsed text of each run; same count and order.</returns>
    public static ImmutableArray<string> Collapse(IReadOnlyList<(string Text, WhiteSpaceCollapse Mode)> runs)
    {
        // One pass over all characters of the paragraph; whether the previous character was a collapsible space carries over
        // from one run to the next, which is what makes collapsing work across run boundaries (CSS Text 3 §4.1.1: a
        // collapsible space following another is collapsed "even one outside the boundary of the inline containing
        // that space, provided both spaces are within the same inline formatting context").
        var result = ImmutableArray.CreateBuilder<string>(runs.Count);
        var previousWasSpace = false;
        foreach (var (run, mode) in runs)
        {
            var collapsed = new StringBuilder(run.Length);
            foreach (var character in run)
            {
                if (mode == WhiteSpaceCollapse.Preserve || (mode == WhiteSpaceCollapse.PreserveBreaks && character == '\n'))
                {
                    var isBreak = character == '\n';
                    collapsed.Append(isBreak ? LineSeparator : character == '\r' ? ' ' : character);

                    // After a preserved segment break the collapsible spaces that follow are removed.
                    previousWasSpace = isBreak && mode == WhiteSpaceCollapse.PreserveBreaks;
                    continue;
                }

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

    /// <summary>
    /// True when the text is nothing but collapsible white space (or empty): after collapsing and trimming at
    /// line ends nothing of it is left, so on its own it makes no line box (CSS Text 3 §4.1.2, CSS 2.1 §9.4.2).
    /// </summary>
    /// <param name="text">The text of a run.</param>
    /// <returns>Whether only collapsible white space is in it.</returns>
    public static bool IsAllCollapsible(string text) => IsAllCollapsible(text, WhiteSpaceCollapse.Collapse);

    /// <summary>
    /// True when the text is nothing but white space that collapses under <paramref name="mode"/> (or empty), so on its own it
    /// makes no line box (CSS Text 3 §4.1.2, CSS 2.1 §9.4.2). A preserved space or a preserved segment break makes one: a
    /// paragraph of spaces only under <c>pre</c> or <c>pre-wrap</c>, and one of a line feed only under <c>pre-line</c>, take a
    /// line in Chromium (cases <c>ws-pre</c>, <c>ws-pre-wrap</c>, <c>ws-pre-line</c>).
    /// </summary>
    /// <param name="text">The text of a run.</param>
    /// <param name="mode">The run's mode.</param>
    /// <returns>Whether only white space that collapses is in it.</returns>
    public static bool IsAllCollapsible(string text, WhiteSpaceCollapse mode) => mode switch
    {
        WhiteSpaceCollapse.Preserve => text.Length == 0,
        WhiteSpaceCollapse.PreserveBreaks => text.All(character => character != '\n' && IsCollapsible(character)),
        _ => text.All(IsCollapsible),
    };

    private static bool IsCollapsible(char character) => character is ' ' or '\t' or '\n' or '\r';
}
