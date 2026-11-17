using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Typesetting.LineBreaking;

namespace Ave.Documents.Typesetting;

/// <summary>
/// Breaks a paragraph into lines with the greedy ("first fit") method browsers use for CSS text: fill each
/// line with as many pieces as fit, then start the next line. The places where a line may end are the break
/// opportunities of the Unicode Line Breaking Algorithm (<see cref="UnicodeLineBreaking"/>, UAX #14).
/// </summary>
/// <remarks>
/// The function works on a whole paragraph — the pieces come from <see cref="BreakableSegments"/> — so an optimal
/// (Knuth–Plass) breaker can later take its place with the same inputs and outputs.
/// </remarks>
public static class GreedyLineBreaker
{
    /// <summary>
    /// Breaks the shaped runs of one paragraph into lines no wider than <paramref name="availableWidth"/>.
    /// </summary>
    /// <remarks>
    /// The rules applied, in CSS terms:
    /// <list type="bullet">
    /// <item>A line ends where the next piece would not fit (<see cref="Fitting.Fits"/>), not counting the spaces at
    /// the piece's end, which would hang at the end of the line. A piece that shows nothing (spaces, invisible
    /// characters, a forced break) never widens the line, so it never ends one: it stays on the line, even on a line
    /// that already overflows with a piece wider than the line.</item>
    /// <item>Spaces at the start and at the end of a line are removed (CSS Text 3 §4.1.2, "Phase II: Trimming and
    /// Positioning"); UAX #14 puts the opportunity AFTER a run of spaces (LB18), so they end the line before.
    /// Invisible characters among those spaces (a soft hyphen, a zero width space, a word joiner) do not keep them, at
    /// either end: the spaces go, the invisible characters stay as text without width, and no line is made of invisible
    /// characters alone. So the lines are the same as without them.</item>
    /// <item>A line always ends after a mandatory break (UAX #14 "!"); an empty line is not made.</item>
    /// <item>A line that breaks after a soft hyphen shows a hyphen there (UAX #14 §5.4; CSS Text 3 §5.3, <c>hyphens: manual</c>): the
    /// invisible soft hyphen is replaced by its <see cref="ShapedGlyph.BreakHyphen"/>. A segment that ends in a soft
    /// hyphen goes on the line only when it fits with that hyphen — LIMIT: so a line may end one segment earlier
    /// than it would if the segment after it also fitted, which would make the hyphen unnecessary.</item>
    /// <item>A piece wider than the whole line is put on a line of its own and overflows it: there is no break
    /// opportunity inside it, and <c>overflow-wrap: normal</c> does not break words (CSS Text 3 §5.4).</item>
    /// </list>
    /// LIMIT: shaping is not redone at line ends. A kerning pair across a break opportunity (a letter and the
    /// space after it) keeps its adjustment on the letter at the end of the line.
    /// </remarks>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <returns>The lines, top to bottom; empty when the paragraph has no visible text.</returns>
    public static ValueList<TextLine> BreakLines(IReadOnlyList<ValueList<ShapedGlyph>> runs, double availableWidth)
    {
        var lines = ImmutableArray.CreateBuilder<TextLine>();
        var line = new List<BreakableSegments.RunGlyph>();
        var lineWidth = 0.0;

        var segments = BreakableSegments.Split(runs);
        for (var index = 0; index < segments.Length; index++)
        {
            // A segment that ends in a soft hyphen needs room for the hyphen only if the line may break after it: not
            // at the end of the paragraph, where no hyphen shows. (A segment that ends in a forced break ends with the
            // break, which has no hyphen, so its HyphenWidth is 0 already.)
            var segment = segments[index];
            var hyphenWidth = index == segments.Length - 1 ? 0 : segment.HyphenWidth;
            // A segment that shows nothing — no visible glyph, and no hyphen at a break after it — never ends a line.
            var showsNothing = segment.IsBlank && hyphenWidth == 0;
            if (line.Count > 0 && !showsNothing && !Fitting.Fits(lineWidth + segment.WidthAtLineEnd + hyphenWidth, availableWidth))
            {
                AddLine(lines, line, endsWithForcedBreak: false, breaksHere: true);
                line.Clear();
                lineWidth = 0;
            }

            // A new line does not start with spaces (at the paragraph start, after a mandatory break, or where it wraps
            // before a zero width space and the spaces after it), also not among invisible characters.
            var glyphs = line.Count == 0 ? WithoutLeadingSpaces(segment.Glyphs) : segment.Glyphs;
            foreach (var glyph in glyphs)
            {
                line.Add(glyph);
                lineWidth += glyph.Glyph.Advance;
            }

            if (segment.EndsWithMandatoryBreak)
            {
                AddLine(lines, line, endsWithForcedBreak: true, breaksHere: true);
                line.Clear();
                lineWidth = 0;
            }
        }

        AddLine(lines, line, endsWithForcedBreak: false, breaksHere: false);
        return lines.ToImmutable();
    }

    /// <summary>The glyphs of a segment without the spaces among the blank glyphs it starts with; the invisible
    /// characters among those stay, and so does a forced break (always a segment's last glyph).</summary>
    private static IEnumerable<BreakableSegments.RunGlyph> WithoutLeadingSpaces(IEnumerable<BreakableSegments.RunGlyph> glyphs)
    {
        static bool Leading(BreakableSegments.RunGlyph glyph) => BreakableSegments.IsBlank(glyph.Glyph);
        return glyphs.TakeWhile(Leading).Where(glyph => !glyph.Glyph.IsSpace).Concat(glyphs.SkipWhile(Leading));
    }

    /// <summary>Adds a line without its trailing spaces, grouping its glyphs into one segment per consecutive
    /// stretch of the same run; when the paragraph breaks after the line (<paramref name="breaksHere"/>) and the
    /// line ends in a soft hyphen, the hyphen shows. Nothing is added when nothing visible and no forced break is
    /// left.</summary>
    private static void AddLine(ImmutableArray<TextLine>.Builder lines, List<BreakableSegments.RunGlyph> line, bool endsWithForcedBreak, bool breaksHere)
    {
        // The hyphen shows only when the line breaks AT the soft hyphen: it is the line's last glyph, with no space or
        // forced break after it (a forced break would be the last glyph, and it has no hyphen). Broken at a later space
        // ("foo\u00AD bar"), the soft hyphen stays invisible. A shown hyphen is visible, so the spaces before it stay.
        var breaks = line.AsEnumerable().Reverse().TakeWhile(glyph => glyph.Glyph.IsLineBreak).Count();
        var body = line.Take(line.Count - breaks).ToList();
        var hyphen = breaksHere && breaks == 0 && body.Count > 0 ? body[^1].Glyph.BreakHyphen : null;

        // The spaces at the end of a line are removed (CSS Text 3 §4.1.2), also before a forced break, and also when
        // invisible characters stand among them: those stay, without width. The break itself stays, so that a line of
        // only a break still makes an empty line.
        var blank = hyphen is null ? body.AsEnumerable().Reverse().TakeWhile(glyph => BreakableSegments.IsBlank(glyph.Glyph)).Count() : 0;
        var glyphs = body.Take(body.Count - blank)
            .Concat(body.Skip(body.Count - blank).Where(glyph => !glyph.Glyph.IsSpace))
            .Concat(line.Skip(line.Count - breaks))
            .ToList();
        if (!glyphs.Any(glyph => glyph.Glyph.IsLineBreak || !BreakableSegments.IsBlank(glyph.Glyph)))
        {
            return;
        }

        if (hyphen is not null)
        {
            glyphs[^1] = glyphs[^1] with { Glyph = hyphen };
        }

        var segments = ImmutableArray.CreateBuilder<LineSegment>();
        var start = 0;
        for (var index = 1; index <= glyphs.Count; index++)
        {
            if (index == glyphs.Count || glyphs[index].Run != glyphs[start].Run)
            {
                segments.Add(new LineSegment(glyphs[start].Run, [.. glyphs.Skip(start).Take(index - start).Select(placed => placed.Glyph)]));
                start = index;
            }
        }

        lines.Add(new TextLine(segments.ToImmutable(), endsWithForcedBreak));
    }
}
