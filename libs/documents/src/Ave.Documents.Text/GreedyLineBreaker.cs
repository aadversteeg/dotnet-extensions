using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;

namespace Ave.Documents.Text;

/// <summary>
/// Breaks a paragraph into lines with the greedy ("first fit") method browsers use for CSS text: fill each
/// line with as many words as fit, then start the next line.
/// </summary>
/// <remarks>
/// Stage 0 break opportunities are the collapsed spaces U+0020 only. The Unicode line breaking algorithm
/// (UAX #14: hyphens, dashes, CJK, …) replaces <see cref="SplitIntoWords"/> in stage 1. The function works on a
/// whole paragraph rather than line by line, so an optimal (Knuth-Plass) breaker can later take its place
/// with the same inputs and outputs.
/// </remarks>
public static class GreedyLineBreaker
{
    private sealed record PlacedGlyph(int Run, ShapedGlyph Glyph);

    private sealed record Word(ImmutableArray<PlacedGlyph> Glyphs, PlacedGlyph? SpaceBefore)
    {
        public double Width => Glyphs.Sum(placed => placed.Glyph.Advance);
    }

    /// <summary>
    /// Breaks the shaped runs of one paragraph into lines no wider than <paramref name="availableWidth"/>.
    /// </summary>
    /// <remarks>
    /// The rules applied, in CSS terms:
    /// <list type="bullet">
    /// <item>A line ends where the next word, plus the space before it, would not fit (<see cref="Fitting.Fits"/>).</item>
    /// <item>The space at which a line breaks belongs to neither line: spaces at the start of a line are removed
    /// and spaces at its end are removed (CSS Text 3 §4.1.2, "Phase II: Trimming and Positioning").</item>
    /// <item>A word wider than the whole line is put on a line of its own and overflows it: there is no break
    /// opportunity inside it, and <c>overflow-wrap: normal</c> does not break words (CSS Text 3 §5.5).</item>
    /// </list>
    /// </remarks>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <returns>The lines, top to bottom; empty when the paragraph has no visible text.</returns>
    public static ImmutableArray<TextLine> BreakLines(IReadOnlyList<ImmutableArray<ShapedGlyph>> runs, double availableWidth)
    {
        var lines = ImmutableArray.CreateBuilder<TextLine>();
        var line = new List<PlacedGlyph>();
        var lineWidth = 0.0;
        foreach (var word in SplitIntoWords(runs))
        {
            var spaceWidth = word.SpaceBefore?.Glyph.Advance ?? 0;
            if (line.Count > 0 && Fitting.Fits(lineWidth + spaceWidth + word.Width, availableWidth))
            {
                line.Add(word.SpaceBefore!);
                line.AddRange(word.Glyphs);
                lineWidth += spaceWidth + word.Width;
                continue;
            }

            if (line.Count > 0)
            {
                lines.Add(ToLine(line));
            }

            line = [.. word.Glyphs];
            lineWidth = word.Width;
        }

        if (line.Count > 0)
        {
            lines.Add(ToLine(line));
        }

        return lines.ToImmutable();
    }

    /// <summary>
    /// Cuts the paragraph at its spaces into words. A word may span several runs ("bold" in the middle of a
    /// word). Each word remembers the space before it; a space after the last word has no word and is dropped.
    /// </summary>
    private static IEnumerable<Word> SplitIntoWords(IReadOnlyList<ImmutableArray<ShapedGlyph>> runs)
    {
        var word = ImmutableArray.CreateBuilder<PlacedGlyph>();
        PlacedGlyph? spaceBefore = null;
        var glyphs = runs.SelectMany((run, index) => run.Select(glyph => new PlacedGlyph(index, glyph)));
        foreach (var placed in glyphs)
        {
            if (!placed.Glyph.IsSpace)
            {
                word.Add(placed);
                continue;
            }

            if (word.Count > 0)
            {
                yield return new Word(word.ToImmutable(), spaceBefore);
                word.Clear();
            }

            spaceBefore = placed;
        }

        if (word.Count > 0)
        {
            yield return new Word(word.ToImmutable(), spaceBefore);
        }
    }

    /// <summary>Groups a line's glyphs into one segment per consecutive stretch of the same run.</summary>
    private static TextLine ToLine(IReadOnlyList<PlacedGlyph> glyphs)
    {
        var segments = ImmutableArray.CreateBuilder<LineSegment>();
        var start = 0;
        for (var index = 1; index <= glyphs.Count; index++)
        {
            if (index == glyphs.Count || glyphs[index].Run != glyphs[start].Run)
            {
                var stretch = glyphs.Skip(start).Take(index - start).Select(placed => placed.Glyph);
                segments.Add(new LineSegment(glyphs[start].Run, [.. stretch]));
                start = index;
            }
        }

        return new TextLine(segments.ToImmutable());
    }
}
