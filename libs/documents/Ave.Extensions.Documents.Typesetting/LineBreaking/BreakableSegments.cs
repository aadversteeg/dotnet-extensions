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

    /// <summary>A paragraph prepared for line breaking.</summary>
    /// <param name="Text">The paragraph's text: the glyphs' texts joined, which is the white-space-collapsed text.</param>
    /// <param name="Glyphs">Every glyph, in order, with its place.</param>
    /// <param name="RunStarts">The offset of every run's first character, and the text's length at the end.</param>
    /// <param name="Segments">The segments, in order; empty for an empty paragraph.</param>
    public sealed record Paragraph(string Text, ImmutableArray<Placed> Glyphs, ImmutableArray<int> RunStarts, ImmutableArray<Segment> Segments)
    {
        /// <summary>True when <paramref name="offset"/> starts or ends a run: a place where shaping already stops, so
        /// always safe to break.</summary>
        public bool IsRunBoundary(int offset) => RunStarts.Contains(offset);
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
        var clusterStarts = placed.Select(glyph => glyph.Start).ToHashSet();
        var segments = ImmutableArray.CreateBuilder<Segment>();
        var start = 0;
        foreach (var opportunity in UnicodeLineBreaking.Find(text))
        {
            var usable = opportunity.Offset < text.Length && (clusterStarts.Contains(opportunity.Offset) || canShapeAgain);
            if (opportunity.Offset > start && usable)
            {
                segments.Add(SegmentOf(placed, start, opportunity.Offset, opportunity.IsMandatory));
                start = opportunity.Offset;
            }
        }

        // The end of the text is always an opportunity (UAX #14 LB3); a mandatory break there ends nothing.
        if (start < text.Length)
        {
            segments.Add(SegmentOf(placed, start, text.Length, false));
        }

        return new Paragraph(text, placed, runStarts.ToImmutable(), segments.ToImmutable());
    }

    private static Segment SegmentOf(ImmutableArray<Placed> glyphs, int start, int end, bool mandatory)
    {
        var inside = glyphs.Where(glyph => glyph.Start >= start && glyph.Start < end).ToList();
        var startsInsideCluster = glyphs.Any(glyph => glyph.Start < start && glyph.End > start);
        return new Segment(
            start,
            end,
            mandatory,
            IsBlank: !startsInsideCluster && inside.All(glyph => IsBlank(glyph.Glyph)),
            HasGlyphsBesideSpaces: startsInsideCluster || inside.Any(glyph => !glyph.Glyph.IsSpace));
    }
}
