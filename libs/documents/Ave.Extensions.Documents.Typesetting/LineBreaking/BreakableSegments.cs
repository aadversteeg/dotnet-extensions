using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting.LineBreaking;

/// <summary>
/// A paragraph's shaped glyphs with the place of each in the paragraph's text, and the pieces of the text between break
/// opportunities: the "items" any line breaker chooses from. The greedy breaker (<see cref="GreedyLineBreaker"/>) uses
/// them today; an optimal one (Knuth–Plass) can use the same items later.
/// </summary>
/// <remarks>
/// Everything is found in passes over the text and the glyphs that each look at every item once, so preparing a
/// paragraph costs work in proportion to its length; the lookups the breaker makes afterwards (<see cref="Paragraph"/>)
/// take one step each.
/// </remarks>
internal static class BreakableSegments
{
    /// <summary>A glyph with the index of the run it came from.</summary>
    public sealed record RunGlyph(int Run, ShapedGlyph Glyph);

    /// <summary>A glyph of the paragraph with the place of its cluster in the paragraph's text.</summary>
    /// <param name="Run">The run it came from.</param>
    /// <param name="Glyph">The glyph.</param>
    /// <param name="Start">The offset of its cluster's first character in the paragraph's text.</param>
    /// <param name="End">The offset after its cluster's last character (the later glyphs of a cluster share it).</param>
    public sealed record Placed(int Run, ShapedGlyph Glyph, int Start, int End);

    /// <summary>
    /// True for a glyph that a line end removes or that has no width there: a collapsible space (removed, CSS Text 3
    /// §4.1.2), an invisible character (default ignorable, without advance: a soft hyphen, a zero width space, a word
    /// joiner), or a forced break. At a line end the spaces and the invisible characters among them go together, so
    /// invisible characters between the last word and the end change nothing.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>Whether it is blank at a line end.</returns>
    public static bool IsBlank(ShapedGlyph glyph) => glyph.IsSpace || glyph.IsInvisible || glyph.IsLineBreak;

    /// <summary>The text between two break opportunities.</summary>
    /// <param name="Start">The offset of its first character in the paragraph's text.</param>
    /// <param name="End">The offset after its last character: the opportunity that ends it.</param>
    /// <param name="EndsWithMandatoryBreak">True when a line must end after this segment (UAX #14 "!").</param>
    /// <param name="IsBlank">True when the segment holds nothing that shows: its glyphs are spaces, invisible characters
    /// and a forced break (<see cref="BreakableSegments.IsBlank"/>), and it does not start inside another cluster.</param>
    /// <param name="HasGlyphsBesideSpaces">True when something of the segment stays at the start of a line: a glyph that
    /// is not a collapsible space, or a part of a cluster.</param>
    public sealed record Segment(int Start, int End, bool EndsWithMandatoryBreak, bool IsBlank, bool HasGlyphsBesideSpaces);

    /// <summary>A paragraph prepared for line breaking, with the lookups the breaker needs, each answered in one step.</summary>
    public sealed class Paragraph
    {
        private readonly int[] _firstEndingAfter;
        private readonly int[] _firstStartingAt;
        private readonly double[] _pens;
        private readonly double[] _spacePens;
        private readonly int[] _spaces;
        private readonly int[] _nextShowing;
        private readonly int[] _previousShowing;
        private readonly bool[] _safe;
        private readonly int[] _nextSafe;
        private readonly int[] _previousSafe;
        private readonly int[] _runOf;
        private readonly ShapedGlyph?[] _hyphens;

        /// <summary>Prepares the lookups: one pass over the glyphs or over the text for each.</summary>
        /// <param name="text">The paragraph's text: the glyphs' texts joined, which is the white-space-collapsed text.</param>
        /// <param name="glyphs">Every glyph, in order, with its place.</param>
        /// <param name="runStarts">The offset of every run's first character, and the text's length at the end.</param>
        /// <param name="segments">The segments, in order; empty for an empty paragraph.</param>
        public Paragraph(string text, ImmutableArray<Placed> glyphs, ImmutableArray<int> runStarts, ImmutableArray<Segment> segments)
        {
            (Text, Glyphs, RunStarts, Segments) = (text, glyphs, runStarts, segments);
            var length = text.Length;
            var count = glyphs.Length;
            (_firstEndingAfter, _firstStartingAt) = (new int[length + 1], new int[length + 1]);
            var (ending, starting) = (0, 0);
            for (var offset = 0; offset <= length; offset++)
            {
                // Starts and ends never decrease along the glyphs, so one pointer each walks them once.
                while (ending < count && glyphs[ending].End <= offset)
                {
                    ending++;
                }

                while (starting < count && glyphs[starting].Start < offset)
                {
                    starting++;
                }

                (_firstEndingAfter[offset], _firstStartingAt[offset]) = (ending, starting);
            }

            (_pens, _spacePens, _spaces) = (new double[count + 1], new double[count + 1], new int[count + 1]);
            for (var index = 0; index < count; index++)
            {
                var glyph = glyphs[index].Glyph;
                _pens[index + 1] = _pens[index] + glyph.Advance;
                _spacePens[index + 1] = _spacePens[index] + (glyph.IsSpace ? glyph.Advance : 0);
                _spaces[index + 1] = _spaces[index] + (glyph.IsSpace ? 1 : 0);
            }

            (_nextShowing, _previousShowing) = (new int[count + 1], new int[count + 1]);
            _nextShowing[count] = count;
            for (var index = count - 1; index >= 0; index--)
            {
                _nextShowing[index] = IsBlank(glyphs[index].Glyph) ? _nextShowing[index + 1] : index;
            }

            // _previousShowing[i]: the last glyph before i that is not blank, -1 for none.
            _previousShowing[0] = -1;
            for (var index = 0; index < count; index++)
            {
                _previousShowing[index + 1] = IsBlank(glyphs[index].Glyph) ? _previousShowing[index] : index;
            }

            // A safe place: the start of a cluster none of whose glyphs the shaper marked unsafe to break (HarfBuzz marks
            // every glyph of a cluster alike; a lookup marks the clusters after its first glyph's own). The start of a run
            // that was shaped with the run before it is safe only so; that of a run shaped on its own is never marked.
            var safe = new bool[length + 1];
            for (var index = 0; index < count;)
            {
                var start = glyphs[index].Start;
                var marked = false;
                for (; index < count && glyphs[index].Start == start; index++)
                {
                    marked |= glyphs[index].Glyph.UnsafeToBreak;
                }

                safe[start] = !marked;
            }

            safe[0] = safe[length] = true;
            _safe = safe;

            // A stretch shaped again ends at a safe place or at a run boundary, whichever comes first: it never crosses into
            // another run.
            var stop = (bool[])safe.Clone();
            foreach (var start in runStarts)
            {
                stop[start] = true;
            }

            (_nextSafe, _previousSafe) = (new int[length + 1], new int[length + 1]);
            _nextSafe[length] = length;
            for (var offset = length - 1; offset >= 0; offset--)
            {
                _nextSafe[offset] = stop[offset + 1] ? offset + 1 : _nextSafe[offset + 1];
            }

            for (var offset = 1; offset <= length; offset++)
            {
                _previousSafe[offset] = stop[offset - 1] ? offset - 1 : _previousSafe[offset - 1];
            }

            _runOf = new int[length + 1];
            var run = 0;
            for (var offset = 0; offset <= length; offset++)
            {
                while (run + 2 < runStarts.Length && runStarts[run + 1] <= offset)
                {
                    run++;
                }

                _runOf[offset] = run;
            }

            _hyphens = new ShapedGlyph?[Math.Max(0, runStarts.Length - 1)];
            foreach (var glyph in glyphs)
            {
                _hyphens[glyph.Run] ??= glyph.Glyph.BreakHyphen;
            }
        }

        /// <summary>The paragraph's text: the glyphs' texts joined, which is the white-space-collapsed text.</summary>
        public string Text { get; }

        /// <summary>Every glyph, in order, with its place.</summary>
        public ImmutableArray<Placed> Glyphs { get; }

        /// <summary>The offset of every run's first character, and the text's length at the end.</summary>
        public ImmutableArray<int> RunStarts { get; }

        /// <summary>The segments, in order; empty for an empty paragraph.</summary>
        public ImmutableArray<Segment> Segments { get; }

        /// <summary>The first glyph whose cluster ends after <paramref name="offset"/>: the first glyph of a line that
        /// starts there, also when the line starts inside a cluster.</summary>
        public int FirstEndingAfter(int offset) => _firstEndingAfter[offset];

        /// <summary>The first glyph whose cluster starts at or after <paramref name="offset"/>: the end, exclusive, of the
        /// glyphs of a line that ends there.</summary>
        public int FirstStartingAt(int offset) => _firstStartingAt[offset];

        /// <summary>The sum of the advances of the glyphs from <paramref name="from"/> to <paramref name="to"/>, exclusive.</summary>
        public double Width(int from, int to) => _pens[to] - _pens[from];

        /// <summary>The same without the collapsible spaces.</summary>
        public double WidthWithoutSpaces(int from, int to) => Width(from, to) - (_spacePens[to] - _spacePens[from]);

        /// <summary>True when a collapsible space is among the glyphs from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public bool HasSpace(int from, int to) => _spaces[to] > _spaces[from];

        /// <summary>The first glyph at or after <paramref name="index"/> that is not blank (<see cref="IsBlank"/>), or the
        /// glyph count.</summary>
        public int NextShowing(int index) => _nextShowing[index];

        /// <summary>The last glyph before <paramref name="index"/> that is not blank, or -1.</summary>
        public int PreviousShowing(int index) => _previousShowing[index];

        /// <summary>True when the text may be broken at <paramref name="offset"/> without shaping either side again: the start
        /// or end of the text, or the start of a cluster none of whose glyphs the shaper marked <see cref="ShapedGlyph.UnsafeToBreak"/>.
        /// A run boundary is no exception: where two runs were shaped as one text, the glyph after the boundary carries the
        /// shaper's mark like any other, as Chromium asks the item's own result whether its start is safe to break
        /// (<c>ShapingLineBreaker::ShapeLine</c>, <c>IsStartSafeToBreak</c>).</summary>
        public bool IsSafe(int offset) => _safe[offset];

        /// <summary>The first safe place or run boundary after <paramref name="offset"/> (the text's length when there is none):
        /// where a stretch shaped again from <paramref name="offset"/> ends.</summary>
        public int NextSafe(int offset) => _nextSafe[offset];

        /// <summary>The last safe place or run boundary before <paramref name="offset"/> (0 is one): where a stretch shaped
        /// again up to <paramref name="offset"/> starts.</summary>
        public int PreviousSafe(int offset) => _previousSafe[offset];

        /// <summary>The hyphen a soft hyphen of <paramref name="run"/> shows (shaped alone), or null when the run has no soft
        /// hyphen of its own (one inside a ligature has none) or its font no hyphen.</summary>
        public ShapedGlyph? HyphenOf(int run) => _hyphens[run];

        /// <summary>The run of the character at <paramref name="offset"/> (at the text's end, the last run).</summary>
        public int RunOf(int offset) => _runOf[offset];
    }

    /// <summary>
    /// Prepares a paragraph: places every glyph in the text and splits the text at its break opportunities, found on the
    /// paragraph's whole text by <see cref="UnicodeLineBreaking"/>. The glyphs' texts are the text in its order: a
    /// ligature shows its components and the glyphs it skipped (<c>GlyphSubstitution</c>), the later glyphs of a cluster
    /// show nothing. An opportunity inside a cluster — at a soft hyphen a ligature skipped — is used only when
    /// <paramref name="canShapeAgain"/>, since a line can end there only if both sides are shaped again; without it, as
    /// before the later glyphs of one character (a one-to-many substitution), the opportunity is not used.
    /// </summary>
    /// <param name="runs">The shaped glyphs of each run, in order.</param>
    /// <param name="canShapeAgain">Whether the breaker may shape pieces of the text again.</param>
    /// <returns>The paragraph.</returns>
    public static Paragraph Split(IReadOnlyList<ValueList<ShapedGlyph>> runs, bool canShapeAgain)
    {
        var glyphs = ImmutableArray.CreateBuilder<Placed>();
        var runStarts = ImmutableArray.CreateBuilder<int>();
        var offset = 0;
        for (var run = 0; run < runs.Count; run++)
        {
            runStarts.Add(offset);
            var glyphsOfRun = runs[run];
            for (var index = 0; index < glyphsOfRun.Count; index++)
            {
                // A cluster: one glyph with text, and the glyphs after it that have none.
                var length = glyphsOfRun[index].Text.Length;
                var last = index;
                while (last + 1 < glyphsOfRun.Count && glyphsOfRun[last + 1].Text.Length == 0)
                {
                    last++;
                }

                for (var member = index; member <= last; member++)
                {
                    glyphs.Add(new Placed(run, glyphsOfRun[member], offset, offset + length));
                }

                offset += length;
                index = last;
            }
        }

        runStarts.Add(offset);
        var placed = glyphs.ToImmutable();
        var text = string.Concat(placed.Select(glyph => glyph.Glyph.Text));
        var clusterStarts = new bool[text.Length + 1];
        foreach (var glyph in placed)
        {
            clusterStarts[glyph.Start] = true;
        }

        var segments = ImmutableArray.CreateBuilder<Segment>();
        var start = 0;
        var cursor = 0;
        foreach (var opportunity in UnicodeLineBreaking.Find(text))
        {
            var usable = opportunity.Offset < text.Length && (clusterStarts[opportunity.Offset] || canShapeAgain);
            if (opportunity.Offset > start && usable)
            {
                segments.Add(SegmentOf(placed, ref cursor, start, opportunity.Offset, opportunity.IsMandatory));
                start = opportunity.Offset;
            }
        }

        // The end of the text is always an opportunity (UAX #14 LB3); a mandatory break there ends nothing.
        if (start < text.Length)
        {
            segments.Add(SegmentOf(placed, ref cursor, start, text.Length, false));
        }

        return new Paragraph(text, placed, runStarts.ToImmutable(), segments.ToImmutable());
    }

    /// <summary>The segment from <paramref name="start"/> to <paramref name="end"/>. The segments come in order, so
    /// <paramref name="cursor"/> — the first glyph whose cluster starts at or after the segment's start — only moves
    /// forward, and every glyph is looked at once over all segments.</summary>
    private static Segment SegmentOf(ImmutableArray<Placed> glyphs, ref int cursor, int start, int end, bool mandatory)
    {
        while (cursor < glyphs.Length && glyphs[cursor].Start < start)
        {
            cursor++;
        }

        // A cluster that starts before the segment and reaches into it: the glyph before the cursor.
        var startsInsideCluster = cursor > 0 && glyphs[cursor - 1].End > start;
        var (allBlank, beside) = (true, false);
        for (var index = cursor; index < glyphs.Length && glyphs[index].Start < end; index++)
        {
            allBlank &= IsBlank(glyphs[index].Glyph);
            beside |= !glyphs[index].Glyph.IsSpace;
        }

        return new Segment(start, end, mandatory, IsBlank: !startsInsideCluster && allBlank, HasGlyphsBesideSpaces: startsInsideCluster || beside);
    }
}
