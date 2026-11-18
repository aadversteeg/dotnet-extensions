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
/// there (<see cref="Shaper.ShapeLinePiece"/>).
/// </summary>
/// <param name="run">The run's index.</param>
/// <param name="text">The piece of the run's text.</param>
/// <param name="showsHyphen">True when the piece ends with a soft hyphen where the line breaks, which shows as a hyphen.</param>
/// <returns>The piece's glyphs, or a shaping error.</returns>
public delegate Result<ValueList<ShapedGlyph>, Error> LineShaper(int run, string text, bool showsHyphen);

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
    /// glyphs a shaper gives each line alone. The width a line is measured with is that line's own, so a break that the
    /// shaping of its ends makes too wide is not taken.</item>
    /// </list>
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
        var lines = ImmutableArray.CreateBuilder<TextLine>();
        var segments = paragraph.Segments;
        var lineStart = 0;
        var occupied = false;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;

            // A segment that shows nothing — no visible glyph, and no hyphen at a break after it — never ends a line.
            var showsNothing = segment.IsBlank && (isLast || !EndsWithSoftHyphen(paragraph, segment.End));
            if (occupied && !showsNothing)
            {
                var candidate = Line(paragraph, lineStart, segment.End, breaksHere: !isLast, reshape);
                if (candidate.IsFailure)
                {
                    return Result<ValueList<TextLine>, Error>.Failure(candidate.Error);
                }

                if (!Fitting.Fits(candidate.Value.Sum(glyph => glyph.Glyph.Advance), availableWidth))
                {
                    if (Add(lines, paragraph, lineStart, segment.Start, forced: false, breaksHere: true, reshape) is { } error)
                    {
                        return Result<ValueList<TextLine>, Error>.Failure(error);
                    }

                    lineStart = segment.Start;
                    occupied = false;
                }
            }

            // A new line does not start with spaces: a segment of spaces alone leaves it empty.
            occupied = occupied || segment.HasGlyphsBesideSpaces;
            if (segment.EndsWithMandatoryBreak)
            {
                if (Add(lines, paragraph, lineStart, segment.End, forced: true, breaksHere: true, reshape) is { } error)
                {
                    return Result<ValueList<TextLine>, Error>.Failure(error);
                }

                lineStart = segment.End;
                occupied = false;
            }
        }

        return Add(lines, paragraph, lineStart, paragraph.Text.Length, forced: false, breaksHere: false, reshape) is { } failure
            ? Result<ValueList<TextLine>, Error>.Failure(failure)
            : Result<ValueList<TextLine>, Error>.Success(lines.ToImmutable());
    }

    private static bool EndsWithSoftHyphen(BreakableSegments.Paragraph paragraph, int end) => end > 0 && paragraph.Text[end - 1] == SoftHyphen;

    /// <summary>Adds the line from <paramref name="start"/> to <paramref name="end"/>, one segment per consecutive stretch
    /// of the same run, unless nothing visible and no forced break is on it; an error from shaping its ends again.</summary>
    private static Error? Add(
        ImmutableArray<TextLine>.Builder lines, BreakableSegments.Paragraph paragraph, int start, int end, bool forced, bool breaksHere, LineShaper? reshape)
    {
        var glyphs = Line(paragraph, start, end, breaksHere, reshape);
        if (glyphs.IsFailure)
        {
            return glyphs.Error;
        }

        var all = glyphs.Value;
        if (!all.Any(glyph => glyph.Glyph.IsLineBreak || !BreakableSegments.IsBlank(glyph.Glyph)))
        {
            return null;
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
        return null;
    }

    /// <summary>
    /// The glyphs of the line from <paramref name="start"/> to <paramref name="end"/> in the paragraph's text: its glyphs
    /// without the spaces at its start and end (the invisible characters among them stay, without width), with a hyphen
    /// where it breaks at a soft hyphen (<paramref name="breaksHere"/>; not before a forced break, which stays the line's
    /// last glyph), and its two ends shaped again where they are not safe to break (THE RULE FOR SHAPING AT A BREAK).
    /// </summary>
    private static Result<List<BreakableSegments.RunGlyph>, Error> Line(
        BreakableSegments.Paragraph paragraph, int start, int end, bool breaksHere, LineShaper? reshape)
    {
        // Every glyph whose cluster lies on the line, wholly or (at a break inside a cluster) in part.
        var on = paragraph.Glyphs.Where(glyph => glyph.End > start && glyph.Start < end).ToList();
        var breaks = Enumerable.Reverse(on).TakeWhile(glyph => glyph.Glyph.IsLineBreak).Count();
        var body = on.Take(on.Count - breaks).ToList();

        // The hyphen shows only when the line breaks AT the soft hyphen: it ends the line, with no space or forced break
        // after it. Broken at a later space ("foo­ bar"), the soft hyphen stays invisible. A shown hyphen is
        // visible, so the spaces before it stay.
        var hyphen = breaksHere && breaks == 0 && body.Count > 0 && EndsWithSoftHyphen(paragraph, end);
        var leading = body.TakeWhile(glyph => glyph.Start >= start && BreakableSegments.IsBlank(glyph.Glyph)).Count();
        var trailing = hyphen ? 0 : Enumerable.Reverse(body).Take(body.Count - leading).TakeWhile(glyph => glyph.End <= end && BreakableSegments.IsBlank(glyph.Glyph)).Count();
        var content = body.Skip(leading).Take(body.Count - leading - trailing).ToList();

        var before = body.Take(leading).Where(glyph => !glyph.Glyph.IsSpace).Select(Plain);
        var after = body.Skip(leading + content.Count).Where(glyph => !glyph.Glyph.IsSpace).Select(Plain);
        var lineBreaks = on.Skip(on.Count - breaks).Select(Plain);
        return Content(paragraph, content, start, end, hyphen, reshape)
            .OnSuccessMap(middle => before.Concat(middle).Concat(after).Concat(lineBreaks).ToList());
    }

    private static BreakableSegments.RunGlyph Plain(BreakableSegments.Placed glyph) => new(glyph.Run, glyph.Glyph);

    /// <summary>
    /// The glyphs of a line's content (its first to its last glyph that is not trimmed): the paragraph's glyphs, with the
    /// stretch at each end shaped again where that end is not safe to break, from the end to the nearest safe place (a run
    /// boundary, or a cluster whose first glyph the shaper did not mark). Without a shaper the paragraph's glyphs are used
    /// as they are, and a soft hyphen that ends the line becomes its hyphen glyph (<see cref="ShapedGlyph.BreakHyphen"/>).
    /// </summary>
    private static Result<IEnumerable<BreakableSegments.RunGlyph>, Error> Content(
        BreakableSegments.Paragraph paragraph, List<BreakableSegments.Placed> content, int start, int end, bool hyphen, LineShaper? reshape)
    {
        if (content.Count == 0)
        {
            return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success([]);
        }

        if (reshape is null)
        {
            var glyphs = content.Select(Plain).ToList();
            if (hyphen && glyphs[^1].Glyph.BreakHyphen is { } shown)
            {
                glyphs[^1] = glyphs[^1] with { Glyph = shown };
            }

            return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(glyphs);
        }

        var from = Math.Max(start, content[0].Start);
        var to = Math.Min(end, content[^1].End);
        var safe = paragraph.Glyphs.Where(glyph => !glyph.Glyph.UnsafeToBreak).Select(glyph => glyph.Start).Concat(paragraph.RunStarts).ToHashSet();
        var reshapeLeft = content[0].Start < start || !safe.Contains(from);
        var reshapeRight = content[^1].End > end || hyphen || !safe.Contains(to);
        if (!reshapeLeft && !reshapeRight)
        {
            return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(content.Select(Plain));
        }

        // The stretches to shape again: [from, left) at the start and [right, to) at the end; the paragraph's glyphs between.
        var left = reshapeLeft ? NextSafe(safe, from, to) : from;
        var right = reshapeRight ? PreviousSafe(safe, to, from) : to;
        if (right <= left)
        {
            // The two ends meet: the whole content is shaped again.
            return Pieces(paragraph, from, to, hyphen, reshape);
        }

        var middle = content.Where(glyph => glyph.Start >= left && glyph.Start < right).Select(Plain).ToList();
        return Pieces(paragraph, from, left, hyphen: false, reshape).OnSuccessBind(head =>
            Pieces(paragraph, right, to, hyphen, reshape).OnSuccessMap(tail => head.Concat(middle).Concat(tail)));
    }

    /// <summary>The first safe place after <paramref name="from"/>, at most <paramref name="limit"/>.</summary>
    private static int NextSafe(HashSet<int> safe, int from, int limit)
    {
        var place = from + 1;
        while (place < limit && !safe.Contains(place))
        {
            place++;
        }

        return Math.Min(place, limit);
    }

    /// <summary>The last safe place before <paramref name="to"/>, at least <paramref name="limit"/>.</summary>
    private static int PreviousSafe(HashSet<int> safe, int to, int limit)
    {
        var place = to - 1;
        while (place > limit && !safe.Contains(place))
        {
            place--;
        }

        return Math.Max(place, limit);
    }

    /// <summary>The text from <paramref name="from"/> to <paramref name="to"/> shaped again, one piece per run it crosses
    /// (shaping never joins two runs); the last piece shows the hyphen when <paramref name="hyphen"/>.</summary>
    private static Result<IEnumerable<BreakableSegments.RunGlyph>, Error> Pieces(
        BreakableSegments.Paragraph paragraph, int from, int to, bool hyphen, LineShaper reshape)
    {
        var pieces = new List<BreakableSegments.RunGlyph>();
        for (var run = 0; run < paragraph.RunStarts.Length - 1; run++)
        {
            var (pieceStart, pieceEnd) = (Math.Max(from, paragraph.RunStarts[run]), Math.Min(to, paragraph.RunStarts[run + 1]));
            if (pieceStart >= pieceEnd)
            {
                continue;
            }

            var shaped = reshape(run, paragraph.Text[pieceStart..pieceEnd], hyphen && pieceEnd == to);
            if (shaped.IsFailure)
            {
                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Failure(shaped.Error);
            }

            pieces.AddRange(shaped.Value.Select(glyph => new BreakableSegments.RunGlyph(run, glyph)));
        }

        return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(pieces);
    }
}
