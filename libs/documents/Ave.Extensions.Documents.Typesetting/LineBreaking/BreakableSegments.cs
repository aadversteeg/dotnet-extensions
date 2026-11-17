using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting.LineBreaking;

/// <summary>
/// Cuts the shaped glyphs of a paragraph into the pieces between break opportunities: the "items" any line
/// breaker chooses from. The greedy breaker (<see cref="GreedyLineBreaker"/>) uses them today; an optimal one
/// (Knuth–Plass) can use the same items later.
/// </summary>
internal static class BreakableSegments
{
    /// <summary>A glyph with the index of the run it came from.</summary>
    public sealed record RunGlyph(int Run, ShapedGlyph Glyph);

    /// <summary>
    /// True for a glyph that a line end removes or that has no width there: a collapsible space (removed, CSS Text 3
    /// §4.1.2), an invisible character (default ignorable, without advance: a soft hyphen, a zero width space, a word
    /// joiner), or a forced break. At a line end the spaces and the invisible characters among them go together, so
    /// invisible characters between the last word and the end change nothing.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>Whether it is blank at a line end.</returns>
    public static bool IsBlank(ShapedGlyph glyph) => glyph.IsSpace || glyph.IsInvisible || glyph.IsLineBreak;

    /// <summary>The glyphs between two break opportunities.</summary>
    /// <param name="Glyphs">The glyphs, in order.</param>
    /// <param name="EndsWithMandatoryBreak">True when a line must end after this segment (UAX #14 "!").</param>
    public sealed record Segment(ImmutableArray<RunGlyph> Glyphs, bool EndsWithMandatoryBreak)
    {
        /// <summary>The width of the hyphen that shows when a line breaks after this segment: a segment that ends in
        /// a soft hyphen needs room for it at a line end (<see cref="ShapedGlyph.BreakHyphen"/>); otherwise 0.</summary>
        public double HyphenWidth => Glyphs.IsEmpty ? 0 : Glyphs[^1].Glyph.BreakHyphen?.Advance ?? 0;

        /// <summary>
        /// The width when a line ends after this segment: without the blank glyphs at its end (<see cref="IsBlank"/>).
        /// The spaces there "hang" and are removed (CSS Text 3 §4.1.2), so they do not count when testing whether the
        /// segment fits; a forced break ends the line, and the spaces before it go as well; invisible characters among
        /// them have no width and do not keep them.
        /// </summary>
        public double WidthAtLineEnd =>
            Glyphs.Reverse().SkipWhile(glyph => IsBlank(glyph.Glyph)).Sum(glyph => glyph.Glyph.Advance);

        /// <summary>True when the segment holds nothing that shows: only spaces, invisible characters and a forced
        /// break (<see cref="IsBlank"/>).</summary>
        public bool IsBlank => Glyphs.All(glyph => BreakableSegments.IsBlank(glyph.Glyph));
    }

    /// <summary>
    /// Splits the paragraph at its break opportunities. The opportunities are found on the paragraph's whole text
    /// (the glyphs' texts joined, which is the white-space-collapsed text) by <see cref="UnicodeLineBreaking"/>. A
    /// glyph that shows several characters (a ligature cluster) cannot be split, so an opportunity inside a cluster
    /// is not used; and the glyphs of one character are never split either: the later glyphs of a one-to-many
    /// substitution have an empty text (<see cref="ShapedGlyph.Text"/>), so an opportunity is taken only before a
    /// glyph that starts a character.
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
            if (current.Count > 0 && glyph.Glyph.Text.Length > 0 && opportunities.TryGetValue(offset, out var mandatory))
            {
                segments.Add(new Segment(current.ToImmutable(), mandatory));
                current.Clear();
            }

            current.Add(glyph);
            offset += glyph.Glyph.Text.Length;
        }

        // The end of the text is always an opportunity (UAX #14 LB3); a mandatory break there ends nothing.
        if (current.Count > 0)
        {
            segments.Add(new Segment(current.ToImmutable(), false));
        }

        return segments.ToImmutable();
    }
}
