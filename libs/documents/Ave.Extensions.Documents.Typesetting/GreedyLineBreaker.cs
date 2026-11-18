using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Typesetting.LineBreaking;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// Shapes a piece of one run's text as a line shows it, for the line breaker (<see cref="GreedyLineBreaker"/>): the run's
/// font and size, the piece alone, and — where the line breaks at a soft hyphen the piece ends with — the hyphen that shows
/// there (<see cref="Shaper.ShapeLinePiece(string, Fonts.TrueTypeFont, double, bool, LineShapingBudget)"/>).
/// </summary>
/// <remarks>
/// All pieces of one paragraph draw on ONE <paramref name="budget"/>, which the breaker makes for the paragraph and hands
/// to every call: a reshaper must pass it on to the shaper, as the layout does. A reshaper that shapes with a budget of its
/// own per piece gives up the bound on the paragraph's work.
/// </remarks>
/// <param name="run">The run's index.</param>
/// <param name="text">The piece of the run's text.</param>
/// <param name="showsHyphen">True when the piece ends with a soft hyphen where the line breaks, which shows as a hyphen.</param>
/// <param name="budget">The budget of the paragraph the piece belongs to.</param>
/// <returns>The piece's glyphs, or a shaping error (also when the budget is spent).</returns>
public delegate Result<ValueList<ShapedGlyph>, Error> LineShaper(int run, string text, bool showsHyphen, LineShapingBudget budget);

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
    /// <summary>The soft hyphen U+00AD.</summary>
    private const char SoftHyphen = '­';

    /// <summary>
    /// How many earlier break opportunities of one line are tried with the line shaped as it is, after the line measured with
    /// the paragraph's glyphs stopped fitting. Shaping a line's ends again changes its width by a kerning pair or a ligature,
    /// so in real text the first or the second opportunity fits; past this number the line takes its first opportunity,
    /// which always may hold its first piece. It bounds the work of one line by a fixed multiple of its length.
    /// </summary>
    public const int MaxEarlierBreaksTried = 4;

    /// <summary>
    /// Breaks the shaped runs of one paragraph into lines no wider than <paramref name="availableWidth"/>, without
    /// shaping anything again: a line end inside a cluster is not used, and the glyphs at a line's ends are the
    /// paragraph's (for glyphs made by hand, which have no font to shape with). Otherwise as
    /// <see cref="BreakLines(IReadOnlyList{ValueList{ShapedGlyph}}, double, LineShaper)"/>.
    /// </summary>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <returns>The lines, top to bottom; empty when the paragraph has no visible text.</returns>
    public static ValueList<TextLine> BreakLines(IReadOnlyList<ValueList<ShapedGlyph>> runs, double availableWidth) =>
        Break(runs, availableWidth, reshape: null).Value;

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
    /// <item>A line that breaks after a soft hyphen shows a hyphen there (UAX #14 §5.4; CSS Text 3 §5.3, <c>hyphens: manual</c>).
    /// A segment that ends in a soft hyphen goes on the line only when it fits with that hyphen — LIMIT: so a line may
    /// end one segment earlier than it would if the segment after it also fitted, which would make the hyphen
    /// unnecessary.</item>
    /// <item>A piece wider than the whole line is put on a line of its own and overflows it: there is no break
    /// opportunity inside it, and <c>overflow-wrap: normal</c> does not break words (CSS Text 3 §5.4).</item>
    /// <item>THE RULE FOR SHAPING AT A BREAK: each line is shaped as the line it is. The paragraph is shaped once, and
    /// where a line ends or starts at a place the shaper marked unsafe to break (<see cref="ShapedGlyph.UnsafeToBreak"/>:
    /// a kerning pair, a mark on its base), inside a cluster (a ligature across a soft hyphen or a zero width space), or
    /// with a hyphen shown at a soft hyphen, the text from that end to the nearest safe place is shaped again on its own,
    /// with <paramref name="reshape"/>: the end of the line with its hyphen, the start of the next line without what went
    /// before. That is HarfBuzz's design (hb-buffer.h, <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>: "This can be used to
    /// optimize paragraph layout, by avoiding re-shaping of each line after line-breaking"), and the result is the
    /// glyphs a shaper gives each line alone.</item>
    /// <item>WHERE THE LINE ENDS: the breaker measures each candidate line with the paragraph's glyphs (a running sum,
    /// so one step per candidate) and finds the first piece that no longer fits. The line before that piece is then shaped
    /// as it is; when that makes it too wide, the break opportunity before it is tried, up to
    /// <see cref="MaxEarlierBreaksTried"/> of them, as Chromium's line breaker steps back from the place the paragraph's
    /// widths give when the line's own shaping does not fit (<c>ShapingLineBreaker::ShapeLine</c>). A later opportunity
    /// that only the line's own shaping would make fit is not taken.</item>
    /// </list>
    /// <para>BOUNDED WORK: preparing the paragraph is a fixed number of passes over its glyphs; each candidate costs one
    /// step; each line shapes its ends again for at most <see cref="MaxEarlierBreaksTried"/> + 2 candidates, each time
    /// at most the line's own text. All pieces of the paragraph are shaped under one <see cref="LineShapingBudget"/>,
    /// which the breaker makes for this call and hands to <paramref name="reshape"/>; when it is spent, the paragraph is
    /// refused with its error.</para>
    /// </remarks>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <param name="reshape">Shapes a piece of a run again as a line shows it.</param>
    /// <returns>The lines, top to bottom (empty when the paragraph has no visible text), or a shaping error.</returns>
    public static Result<ValueList<TextLine>, Error> BreakLines(IReadOnlyList<ValueList<ShapedGlyph>> runs, double availableWidth, LineShaper reshape) =>
        Break(runs, availableWidth, reshape);

    private static Result<ValueList<TextLine>, Error> Break(IReadOnlyList<ValueList<ShapedGlyph>> runs, double availableWidth, LineShaper? reshape)
    {
        var paragraph = BreakableSegments.Split(runs, canShapeAgain: reshape is not null);
        var shaping = new LineEnds(paragraph, reshape, reshape is null ? null : new LineShapingBudget(paragraph.Text.Length));
        var lines = ImmutableArray.CreateBuilder<TextLine>();
        var segments = paragraph.Segments;
        var candidates = new List<int>();
        var lineStart = 0;
        var occupied = false;
        var index = 0;
        while (index < segments.Length)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;

            // A segment that shows nothing — no visible glyph, and no hyphen at a break after it — never ends a line.
            var showsNothing = segment.IsBlank && (isLast || !EndsWithSoftHyphen(paragraph, segment.End));
            if (occupied && !showsNothing)
            {
                candidates.Add(index);
                if (!Fitting.Fits(shaping.Width(lineStart, segment.End, breaksHere: !isLast), availableWidth))
                {
                    var taken = shaping.LineBefore(lineStart, candidates, availableWidth);
                    if (taken.IsFailure)
                    {
                        return Result<ValueList<TextLine>, Error>.Failure(taken.Error);
                    }

                    Add(lines, taken.Value.Glyphs, forced: false);
                    lineStart = segments[taken.Value.Segment].Start;
                    occupied = false;
                    candidates.Clear();

                    // The pieces from the break on start the next line.
                    index = taken.Value.Segment;
                    continue;
                }
            }

            // A new line does not start with spaces: a segment of spaces alone leaves it empty.
            occupied = occupied || segment.HasGlyphsBesideSpaces;
            if (segment.EndsWithMandatoryBreak)
            {
                var line = shaping.Line(lineStart, segment.End, breaksHere: true);
                if (line.IsFailure)
                {
                    return Result<ValueList<TextLine>, Error>.Failure(line.Error);
                }

                Add(lines, line.Value, forced: true);
                lineStart = segment.End;
                occupied = false;
                candidates.Clear();
            }

            index++;
        }

        var last = shaping.Line(lineStart, paragraph.Text.Length, breaksHere: false);
        if (last.IsFailure)
        {
            return Result<ValueList<TextLine>, Error>.Failure(last.Error);
        }

        Add(lines, last.Value, forced: false);
        return Result<ValueList<TextLine>, Error>.Success(lines.ToImmutable());
    }

    private static bool EndsWithSoftHyphen(BreakableSegments.Paragraph paragraph, int end) => end > 0 && paragraph.Text[end - 1] == SoftHyphen;

    /// <summary>Adds a line, one segment per consecutive stretch of the same run, unless nothing visible and no forced break
    /// is on it.</summary>
    private static void Add(ImmutableArray<TextLine>.Builder lines, List<BreakableSegments.RunGlyph> all, bool forced)
    {
        if (!all.Exists(glyph => glyph.Glyph.IsLineBreak || !BreakableSegments.IsBlank(glyph.Glyph)))
        {
            return;
        }

        var segments = ImmutableArray.CreateBuilder<LineSegment>();
        var first = 0;
        for (var index = 1; index <= all.Count; index++)
        {
            if (index == all.Count || all[index].Run != all[first].Run)
            {
                segments.Add(new LineSegment(all[first].Run, [.. all.Skip(first).Take(index - first).Select(placed => placed.Glyph)]));
                first = index;
            }
        }

        lines.Add(new TextLine(segments.ToImmutable(), forced));
    }

    /// <summary>The line that is taken, and the segment the next line starts with.</summary>
    private sealed record Taken(List<BreakableSegments.RunGlyph> Glyphs, int Segment);

    /// <summary>One line of the paragraph from a start to an end: its glyphs in parts that the paragraph's lookups find in
    /// one step each.</summary>
    /// <param name="First">The first glyph whose cluster lies on the line, wholly or (at a break inside a cluster) in part.</param>
    /// <param name="LeadingEnd">After the spaces and invisible characters the line starts with.</param>
    /// <param name="TrailingStart">The first of the spaces and invisible characters the line ends with.</param>
    /// <param name="BodyEnd">Before the forced breaks the line ends with.</param>
    /// <param name="Last">After the line's last glyph.</param>
    /// <param name="Hyphen">True when the line shows a hyphen at the soft hyphen it ends with.</param>
    private readonly record struct Parts(int First, int LeadingEnd, int TrailingStart, int BodyEnd, int Last, bool Hyphen);

    /// <summary>The lines of one paragraph: their widths measured with the paragraph's glyphs, and their glyphs with the
    /// ends shaped again (THE RULE FOR SHAPING AT A BREAK).</summary>
    private sealed class LineEnds(BreakableSegments.Paragraph paragraph, LineShaper? reshape, LineShapingBudget? budget)
    {
        private readonly ImmutableArray<BreakableSegments.Placed> _glyphs = paragraph.Glyphs;

        /// <summary>The parts of the line from <paramref name="start"/> to <paramref name="end"/> in the paragraph's text:
        /// without the spaces at its start and end (the invisible characters among them stay, without width), with a
        /// hyphen where it breaks at a soft hyphen (<paramref name="breaksHere"/>; not before a forced break, which stays
        /// the line's last glyph).</summary>
        private Parts PartsOf(int start, int end, bool breaksHere)
        {
            var first = paragraph.FirstEndingAfter(start);
            var last = paragraph.FirstStartingAt(end);
            var bodyEnd = last;
            while (bodyEnd > first && _glyphs[bodyEnd - 1].Glyph.IsLineBreak)
            {
                bodyEnd--;
            }

            // The hyphen shows only when the line breaks AT the soft hyphen: it ends the line, with no space or forced break
            // after it. Broken at a later space ("foo­ bar"), the soft hyphen stays invisible. A shown hyphen is
            // visible, so the spaces before it stay.
            var hyphen = breaksHere && bodyEnd == last && bodyEnd > first && EndsWithSoftHyphen(paragraph, end);
            var leadingEnd = first < bodyEnd && _glyphs[first].Start >= start ? Math.Min(paragraph.NextShowing(first), bodyEnd) : first;
            var trailingStart = hyphen || bodyEnd == leadingEnd || _glyphs[bodyEnd - 1].End > end
                ? bodyEnd
                : Math.Max(leadingEnd, paragraph.PreviousShowing(bodyEnd) + 1);
            return new Parts(first, leadingEnd, trailingStart, bodyEnd, last, hyphen);
        }

        /// <summary>The width of the line with the paragraph's glyphs (its hyphen alone): one step.</summary>
        public double Width(int start, int end, bool breaksHere)
        {
            var parts = PartsOf(start, end, breaksHere);
            var hyphen = parts.Hyphen && _glyphs[parts.TrailingStart - 1].Glyph.BreakHyphen is { } shown ? shown.Advance : 0;
            return paragraph.WidthWithoutSpaces(parts.First, parts.LeadingEnd)
                + paragraph.Width(parts.LeadingEnd, parts.TrailingStart)
                + paragraph.WidthWithoutSpaces(parts.TrailingStart, parts.Last)
                + hyphen;
        }

        /// <summary>
        /// The line that ends before one of <paramref name="candidates"/> (segment indices, in order, the last being the
        /// piece that no longer fits): the latest whose line, shaped as it is, fits, trying at most
        /// <see cref="MaxEarlierBreaksTried"/> after the first; otherwise the first, which always may hold the line's
        /// first piece.
        /// </summary>
        public Result<Taken, Error> LineBefore(int start, List<int> candidates, double availableWidth)
        {
            var tried = 0;
            for (var position = candidates.Count - 1; position > 0 && tried <= MaxEarlierBreaksTried; position--, tried++)
            {
                var line = Line(start, paragraph.Segments[candidates[position]].Start, breaksHere: true);
                if (line.IsFailure)
                {
                    return Result<Taken, Error>.Failure(line.Error);
                }

                if (Fitting.Fits(line.Value.Sum(glyph => glyph.Glyph.Advance), availableWidth))
                {
                    return Result<Taken, Error>.Success(new Taken(line.Value, candidates[position]));
                }
            }

            return Line(start, paragraph.Segments[candidates[0]].Start, breaksHere: true)
                .OnSuccessMap(line => new Taken(line, candidates[0]));
        }

        /// <summary>The glyphs of the line from <paramref name="start"/> to <paramref name="end"/>: its parts, its content
        /// with the ends shaped again where they are not safe to break.</summary>
        public Result<List<BreakableSegments.RunGlyph>, Error> Line(int start, int end, bool breaksHere)
        {
            var parts = PartsOf(start, end, breaksHere);
            return Content(parts, start, end).OnSuccessMap(middle =>
            {
                var line = new List<BreakableSegments.RunGlyph>(parts.Last - parts.First);
                AddWithoutSpaces(line, parts.First, parts.LeadingEnd);
                line.AddRange(middle);
                AddWithoutSpaces(line, parts.TrailingStart, parts.Last);
                return line;
            });
        }

        private void AddWithoutSpaces(List<BreakableSegments.RunGlyph> line, int from, int to)
        {
            for (var index = from; index < to; index++)
            {
                if (!_glyphs[index].Glyph.IsSpace)
                {
                    line.Add(Plain(_glyphs[index]));
                }
            }
        }

        private static BreakableSegments.RunGlyph Plain(BreakableSegments.Placed glyph) => new(glyph.Run, glyph.Glyph);

        /// <summary>
        /// The glyphs of a line's content (its first to its last glyph that is not trimmed): the paragraph's glyphs, with the
        /// stretch at each end shaped again where that end is not safe to break, from the end to the nearest safe place (a run
        /// boundary, or a cluster with a glyph the shaper did not mark, <see cref="BreakableSegments.Paragraph.IsSafe"/>).
        /// Without a shaper the paragraph's glyphs are used as they are, and a soft hyphen that ends the line becomes its
        /// hyphen glyph (<see cref="ShapedGlyph.BreakHyphen"/>).
        /// </summary>
        private Result<IEnumerable<BreakableSegments.RunGlyph>, Error> Content(Parts parts, int start, int end)
        {
            var (first, last) = (parts.LeadingEnd, parts.TrailingStart);
            if (first == last)
            {
                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success([]);
            }

            if (reshape is null || budget is null)
            {
                var glyphs = new List<BreakableSegments.RunGlyph>(last - first);
                for (var index = first; index < last; index++)
                {
                    glyphs.Add(Plain(_glyphs[index]));
                }

                if (parts.Hyphen && glyphs[^1].Glyph.BreakHyphen is { } shown)
                {
                    glyphs[^1] = glyphs[^1] with { Glyph = shown };
                }

                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(glyphs);
            }

            var from = Math.Max(start, _glyphs[first].Start);
            var to = Math.Min(end, _glyphs[last - 1].End);
            var reshapeLeft = _glyphs[first].Start < start || !paragraph.IsSafe(from);
            var reshapeRight = _glyphs[last - 1].End > end || parts.Hyphen || !paragraph.IsSafe(to);
            if (!reshapeLeft && !reshapeRight)
            {
                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(Range(first, last));
            }

            // The stretches to shape again: [from, left) at the start and [right, to) at the end; the paragraph's glyphs between.
            var left = reshapeLeft ? Math.Min(paragraph.NextSafe(from), to) : from;
            var right = reshapeRight ? Math.Max(paragraph.PreviousSafe(to), from) : to;
            if (right <= left)
            {
                // The two ends meet: the whole content is shaped again.
                return Pieces(from, to, parts.Hyphen);
            }

            var middle = Range(paragraph.FirstStartingAt(left), paragraph.FirstStartingAt(right));
            return Pieces(from, left, hyphen: false).OnSuccessBind(head =>
                Pieces(right, to, parts.Hyphen).OnSuccessMap(tail => head.Concat(middle).Concat(tail)));
        }

        private List<BreakableSegments.RunGlyph> Range(int from, int to)
        {
            var glyphs = new List<BreakableSegments.RunGlyph>(to - from);
            for (var index = from; index < to; index++)
            {
                glyphs.Add(Plain(_glyphs[index]));
            }

            return glyphs;
        }

        /// <summary>The text from <paramref name="from"/> to <paramref name="to"/> shaped again, one piece per run it crosses
        /// (shaping never joins two runs); the last piece shows the hyphen when <paramref name="hyphen"/>. Every character
        /// handed to the reshaper is charged to the paragraph's budget first.</summary>
        private Result<IEnumerable<BreakableSegments.RunGlyph>, Error> Pieces(int from, int to, bool hyphen)
        {
            if (!budget!.SpendCharacters(to - from))
            {
                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Failure(budget.CharactersSpent());
            }

            var pieces = new List<BreakableSegments.RunGlyph>();
            var runStarts = paragraph.RunStarts;
            for (var run = paragraph.RunOf(from); run < runStarts.Length - 1 && runStarts[run] < to; run++)
            {
                var (pieceStart, pieceEnd) = (Math.Max(from, runStarts[run]), Math.Min(to, runStarts[run + 1]));
                if (pieceStart >= pieceEnd)
                {
                    continue;
                }

                var shaped = reshape!(run, paragraph.Text[pieceStart..pieceEnd], hyphen && pieceEnd == to, budget);
                if (shaped.IsFailure)
                {
                    return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Failure(shaped.Error);
                }

                foreach (var glyph in shaped.Value)
                {
                    pieces.Add(new BreakableSegments.RunGlyph(run, glyph));
                }
            }

            return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(pieces);
        }
    }
}
