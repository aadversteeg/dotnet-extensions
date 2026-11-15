using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;

namespace Ave.Documents.Typesetting.LineBreaking;

/// <summary>
/// Cuts the shaped glyphs of a paragraph into the pieces between break opportunities: the "items" any line
/// breaker chooses from. The greedy breaker (<see cref="GreedyLineBreaker"/>) uses them today; an optimal one
/// (Knuth–Plass) can use the same items later.
/// </summary>
internal static class BreakableSegments
{
    /// <summary>A glyph with the index of the run it came from.</summary>
    public sealed record RunGlyph(int Run, ShapedGlyph Glyph);

    /// <summary>The glyphs between two break opportunities.</summary>
    /// <param name="Glyphs">The glyphs, in order.</param>
    /// <param name="EndsWithMandatoryBreak">True when a line must end after this segment (UAX #14 "!").</param>
    public sealed record Segment(ImmutableArray<RunGlyph> Glyphs, bool EndsWithMandatoryBreak)
    {
        /// <summary>The width of all glyphs.</summary>
        public double Width => Glyphs.Sum(glyph => glyph.Glyph.Advance);

        /// <summary>
        /// The width without the spaces at its end: those spaces "hang" when the line ends after this segment
        /// and are removed (CSS Text 3 §4.1.2), so they do not count when testing whether the segment fits.
        /// </summary>
        public double WidthWithoutTrailingSpaces =>
            Glyphs.Reverse().SkipWhile(glyph => glyph.Glyph.IsSpace).Sum(glyph => glyph.Glyph.Advance);
    }

    /// <summary>
    /// Splits the paragraph at its break opportunities. The opportunities are found on the paragraph's whole text
    /// (the glyphs' texts joined, which is the white-space-collapsed text) by <see cref="UnicodeLineBreaking"/>. A
    /// glyph that shows several characters (a ligature cluster) cannot be split, so an opportunity inside a cluster
    /// is not used.
    /// </summary>
    /// <param name="runs">The shaped glyphs of each run, in order.</param>
    /// <returns>The segments, in order; empty for an empty paragraph.</returns>
    public static ImmutableArray<Segment> Split(IReadOnlyList<ValueList<ShapedGlyph>> runs)
    {
        var glyphs = runs.SelectMany((run, index) => run.Select(glyph => new RunGlyph(index, glyph))).ToImmutableArray();
        var text = string.Concat(glyphs.Select(glyph => glyph.Glyph.Text));
        var opportunities = UnicodeLineBreaking.Find(text).ToDictionary(found => found.Offset, found => found.IsMandatory);

        var segments = ImmutableArray.CreateBuilder<Segment>();
        var current = ImmutableArray.CreateBuilder<RunGlyph>();
        var offset = 0;
        foreach (var glyph in glyphs)
        {
            current.Add(glyph);
            offset += glyph.Glyph.Text.Length;
            if (opportunities.TryGetValue(offset, out var mandatory))
            {
                segments.Add(new Segment(current.ToImmutable(), mandatory && offset < text.Length));
                current.Clear();
            }
        }

        return segments.ToImmutable();
    }
}
