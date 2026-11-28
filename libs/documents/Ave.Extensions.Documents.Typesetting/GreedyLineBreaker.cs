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
        Break(runs, [], availableWidth, reshape: null, _ => LineEndShaping.KeepParagraphGlyphs).Value;

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
    /// <item>An atomic inline (<see cref="ShapedGlyph.IsAtomicInline"/>, an image in the line) is a piece of its own: a line
    /// may break before and after it (<see cref="LineBreaking.BreakableSegments.Split"/>), it is never shaped again, and it
    /// is wider than nothing only by its own advance.</item>
    /// <item>A piece wider than the whole line is put on a line of its own and overflows it: there is no break
    /// opportunity inside it, and <c>overflow-wrap: normal</c> does not break words (CSS Text 3 §5.4).</item>
    /// <item>THE RULE FOR SHAPING AT A BREAK, Chromium's (Blink <c>ShapingLineBreaker::ShapeLine</c>: "Unless the break
    /// opportunity is at a safe-to-break boundary (as identified by HarfBuzz) the beginning and/or end of the line is
    /// reshaped to account for differences caused by breaking"): the paragraph is shaped once, and a line keeps the
    /// paragraph's glyphs except at an end that is not safe to break — a place the shaper marked
    /// (<see cref="ShapedGlyph.UnsafeToBreak"/>: a kerning pair, a mark on its base, as HarfBuzz marks them,
    /// hb-buffer.h <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>) or a place inside a cluster (a ligature across a soft hyphen or a
    /// zero width space); the start of a run that the paragraph shaped with the run before it is such a place when the
    /// shaper marked it. There the text from that end to the nearest safe place, or to the run's own border, is shaped
    /// again on its own, with
    /// <paramref name="reshape"/>: the start of a wrapped line without what went before, the end of a line without what
    /// follows. So a line can differ from shaping its text alone: the middle keeps the paragraph's glyphs, and the half of a
    /// ligature cut at the end is shaped without the rest. At a collapsible space the end is kept or shaped again before
    /// the space as <see cref="LineEndShaping"/> says. The hyphen shown at a soft hyphen is shaped alone and put after
    /// the line (<see cref="ShapedGlyph.BreakHyphen"/>; Blink <c>HyphenResult::Shape</c>): it is never kerned with the
    /// letter before it.</item>
    /// <item>WHERE THE LINE ENDS: the breaker measures each candidate line with the paragraph's glyphs (a running sum,
    /// so one step per candidate; a ligature cut by the break counts for its share of characters, the hyphen not at all)
    /// and finds the first piece that no longer fits. The line before that piece is then shaped as it is, with its hyphen;
    /// when that makes it too wide, the break opportunity before it is tried, up to <see cref="MaxEarlierBreaksTried"/> of
    /// them, as Chromium's line breaker steps back from the place the paragraph's glyphs give when the line's own shaping
    /// does not fit (<c>ShapingLineBreaker::ShapeLine</c>). A line before a forced break is checked the same way.</item>
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
        Break(runs, [], availableWidth, reshape, _ => LineEndShaping.KeepParagraphGlyphs);

    /// <summary>
    /// Breaks the shaped runs of one paragraph into lines no wider than <paramref name="availableWidth"/>, doing
    /// <paramref name="atSpace"/> at the end of a line that ends at a collapsible space; otherwise as
    /// <see cref="BreakLines(IReadOnlyList{ValueList{ShapedGlyph}}, double, LineShaper)"/>, which keeps the paragraph's
    /// glyphs there, as for start-aligned text.
    /// </summary>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <param name="reshape">Shapes a piece of a run again as a line shows it.</param>
    /// <param name="atSpace">What is done at a line end at a collapsible space.</param>
    /// <returns>The lines, top to bottom (empty when the paragraph has no visible text), or a shaping error.</returns>
    public static Result<ValueList<TextLine>, Error> BreakLines(
        IReadOnlyList<ValueList<ShapedGlyph>> runs, double availableWidth, LineShaper reshape, LineEndShaping atSpace) =>
        Break(runs, [], availableWidth, reshape, _ => atSpace);

    /// <summary>
    /// Breaks the shaped runs of one paragraph into lines no wider than <paramref name="availableWidth"/>, each run by its
    /// white-space properties (<paramref name="modes"/>); otherwise as
    /// <see cref="BreakLines(IReadOnlyList{ValueList{ShapedGlyph}}, double, LineShaper, LineEndShaping)"/>, which breaks
    /// every run with the initial values.
    /// </summary>
    /// <remarks>
    /// THE RULE FOR PRESERVED SPACES AT A LINE'S ENDS (CSS Text 3 §4.1.2, "Phase II: Trimming and Positioning": "the UA must
    /// (unconditionally) hang this sequence, unless the sequence is followed by a forced line break, in which case it must
    /// conditionally hang the sequence instead"; §8.2, a glyph that conditionally hangs "is not considered when measuring the
    /// line's contents for fit; however, any part of it that does not fit is considered to hang"), as Chromium
    /// does it (Blink <c>LineInfo::ComputeTrailingSpaceWidth</c>, core/layout/inline/line_info.cc): a preserved space is not
    /// removed at the start of a line (case <c>ws-pre-wrap</c>: "   lead" starts three spaces in; <c>ws-lead</c>: spaces alone
    /// before a soft wrap make a line of their own). At the end of a line the preserved spaces hang (<see cref="ShapedGlyph.Hangs"/>):
    /// they are not measured for fit, and their advance is only the part that stays inside the line. At a soft wrap that is
    /// nothing, they hang whole ("unconditional"; <c>ws-hang-pre-wrap</c>: right-aligned and justified lines end with the
    /// word before them at the edge). Before a forced break and at the paragraph's end they hang only as far as they do not
    /// fit ("conditional", Chromium: "only the part of the trailing spaces that overflow the line actually hang"; cases
    /// <c>ws-hang-pre-wrap</c> and <c>ws-forced</c>: "aa" and four spaces right-aligned in a wide line ends the spaces' width
    /// before the edge, with twenty spaces in a 40pt line "aa" starts at the left edge). So the min-content width does not
    /// count them and the max-content width does (§8.2: "not taken into account when computing min-content sizes …, but they
    /// are taken into account for max-content sizes"; case <c>ws-intrinsic</c>).
    /// </remarks>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="modes">The white-space properties of each run, in order (a run without an entry has the initial values).</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <param name="reshape">Shapes a piece of a run again as a line shows it.</param>
    /// <param name="atSpace">What is done at a line end at a collapsible space.</param>
    /// <returns>The lines, top to bottom (empty when the paragraph has no visible text), or a shaping error.</returns>
    public static Result<ValueList<TextLine>, Error> BreakLines(
        IReadOnlyList<ValueList<ShapedGlyph>> runs, IReadOnlyList<RunWhiteSpace> modes, double availableWidth, LineShaper reshape, LineEndShaping atSpace) =>
        Break(runs, modes, availableWidth, reshape, _ => atSpace);

    /// <summary>
    /// Breaks the shaped runs of one paragraph into lines as
    /// <see cref="BreakLines(IReadOnlyList{ValueList{ShapedGlyph}}, IReadOnlyList{RunWhiteSpace}, double, LineShaper, LineEndShaping)"/>,
    /// with what is done at a line end at a collapsible space given per run (<paramref name="atSpace"/>): the run of that
    /// space decides, as Chromium asks the item the line ends in whether it needs an accurate end position (Blink
    /// <c>NeedsAccurateEndPosition(const InlineItem&amp;)</c>, core/layout/inline/line_breaker.cc: an item with a text
    /// decoration does).
    /// </summary>
    /// <param name="runs">The shaped glyphs of each run, in order, after white-space collapsing.</param>
    /// <param name="modes">The white-space properties of each run, in order (a run without an entry has the initial values).</param>
    /// <param name="availableWidth">The width of the line box, in points.</param>
    /// <param name="reshape">Shapes a piece of a run again as a line shows it.</param>
    /// <param name="atSpace">What is done at a line end at a collapsible space, per run, in order (a run without an entry
    /// keeps the paragraph's glyphs).</param>
    /// <param name="textItemStep">The step each text item's width is rounded up to, in points; 0 (the default) keeps the
    /// widths exact. THE RULE FOR THE TEXT ITEM WIDTH: a text item is the part of one run that lies on one line, its
    /// <see cref="LineSegment"/>; with a positive step its width is its glyphs' advances rounded up to the step
    /// (<see cref="LineSegment.Width"/>), and a line fits when the sum of its items' widths fits, as Blink's line breaker
    /// sums each item's <c>ShapeResult::SnappedWidth</c> (<c>LayoutUnit::FromFloatCeil</c>, 1/64 px;
    /// <c>core/layout/inline/line_breaker.cc</c>, <c>LineBreaker::HandleText</c>). An atomic inline is not a text item: its
    /// width stays as it is. The running sum that finds the first piece that no longer fits stays exact; the line it
    /// finds is checked with the rounded widths, and an earlier break is tried when that does not fit (WHERE THE LINE
    /// ENDS). Cases <c>ta-items</c>, <c>ta-items-right</c> and <c>ta-items-fit</c> of <c>tools/reference/chromium</c>.</param>
    /// <param name="tailoring">Which rules find the places where a line may end: the standard's (the default) or those of
    /// Chromium's print (<see cref="LineBreaking.ChromiumLineBreaking"/>).</param>
    /// <returns>The lines, top to bottom (empty when the paragraph has no visible text), or a shaping error.</returns>
    public static Result<ValueList<TextLine>, Error> BreakLines(
        IReadOnlyList<ValueList<ShapedGlyph>> runs, IReadOnlyList<RunWhiteSpace> modes, double availableWidth, LineShaper reshape, IReadOnlyList<LineEndShaping> atSpace,
        double textItemStep = 0, LineBreakTailoring tailoring = LineBreakTailoring.None) =>
        Break(runs, modes, availableWidth, reshape, run => run < atSpace.Count ? atSpace[run] : LineEndShaping.KeepParagraphGlyphs, textItemStep, tailoring);

    private static Result<ValueList<TextLine>, Error> Break(
        IReadOnlyList<ValueList<ShapedGlyph>> runs, IReadOnlyList<RunWhiteSpace> modes, double availableWidth, LineShaper? reshape, Func<int, LineEndShaping> atSpace,
        double textItemStep = 0, LineBreakTailoring tailoring = LineBreakTailoring.None)
    {
        var paragraph = BreakableSegments.Split(runs, modes, canShapeAgain: reshape is not null, tailoring);
        var shaping = new LineEnds(paragraph, reshape, reshape is null ? null : new LineShapingBudget(paragraph.Text.Length), atSpace, availableWidth);
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
                if (!Fitting.Fits(shaping.Width(lineStart, segment.End), availableWidth))
                {
                    var taken = shaping.LineBefore(lineStart, candidates, availableWidth, textItemStep);
                    if (taken.IsFailure)
                    {
                        return Result<ValueList<TextLine>, Error>.Failure(taken.Error);
                    }

                    Add(lines, paragraph.Rules, taken.Value.Glyphs, forced: false, textItemStep);
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

                // The line before a forced break, shaped as it is, may not fit where its glyphs in the paragraph did: then it
                // breaks before one of its pieces, as any other line.
                if (candidates.Count > 0 && candidates[^1] == index && !Fitting.Fits(WidthOf(line.Value, textItemStep), availableWidth))
                {
                    var taken = shaping.LineBefore(lineStart, candidates, availableWidth, textItemStep);
                    if (taken.IsFailure)
                    {
                        return Result<ValueList<TextLine>, Error>.Failure(taken.Error);
                    }

                    Add(lines, paragraph.Rules, taken.Value.Glyphs, forced: false, textItemStep);
                    lineStart = segments[taken.Value.Segment].Start;
                    occupied = false;
                    candidates.Clear();
                    index = taken.Value.Segment;
                    continue;
                }

                Add(lines, paragraph.Rules, line.Value, forced: true, textItemStep);
                lineStart = segment.End;
                occupied = false;
                candidates.Clear();
            }

            index++;
        }

        // The last line, checked as the line before a forced break: its end is shaped again before a space it ends with.
        while (true)
        {
            var last = shaping.Line(lineStart, paragraph.Text.Length, breaksHere: false);
            if (last.IsFailure)
            {
                return Result<ValueList<TextLine>, Error>.Failure(last.Error);
            }

            if (candidates.Count == 0 || Fitting.Fits(WidthOf(last.Value, textItemStep), availableWidth))
            {
                Add(lines, paragraph.Rules, last.Value, forced: false, textItemStep);
                return Result<ValueList<TextLine>, Error>.Success(lines.ToImmutable());
            }

            // Too wide: break before one of its pieces, and lay out the rest as the last line again.
            var taken = shaping.LineBefore(lineStart, candidates, availableWidth, textItemStep);
            if (taken.IsFailure)
            {
                return Result<ValueList<TextLine>, Error>.Failure(taken.Error);
            }

            Add(lines, paragraph.Rules, taken.Value.Glyphs, forced: false, textItemStep);
            var restart = taken.Value.Segment;
            lineStart = segments[restart].Start;
            candidates.RemoveAll(candidate => candidate <= restart);
        }
    }

    private static bool EndsWithSoftHyphen(BreakableSegments.Paragraph paragraph, int end) => end > 0 && paragraph.Text[end - 1] == SoftHyphen;

    /// <summary>The width of a line's glyphs: per consecutive stretch of one run (a text item) its advances, rounded up to
    /// <paramref name="textItemStep"/> unless it is an atomic inline (THE RULE FOR THE TEXT ITEM WIDTH), as
    /// <see cref="Add"/> makes its segments.</summary>
    private static double WidthOf(List<BreakableSegments.RunGlyph> line, double textItemStep)
    {
        if (textItemStep <= 0)
        {
            return line.Sum(glyph => glyph.Glyph.Advance);
        }

        var (width, item, first) = (0.0, 0.0, 0);
        for (var index = 0; index < line.Count; index++)
        {
            item += line[index].Glyph.Advance;
            if (index == line.Count - 1 || line[index + 1].Run != line[first].Run)
            {
                width += LineSegment.RoundedUp(item, StepOf(line, first, index + 1, textItemStep));
                (item, first) = (0, index + 1);
            }
        }

        return width;
    }

    /// <summary>The step of the stretch of one run from <paramref name="from"/> to <paramref name="to"/>: none for an atomic
    /// inline or the edge of an inline box (<see cref="ShapedGlyph.Edge"/>, an item of its own), else <paramref name="textItemStep"/>.</summary>
    private static double StepOf(List<BreakableSegments.RunGlyph> line, int from, int to, double textItemStep)
    {
        for (var index = from; index < to; index++)
        {
            if (line[index].Glyph.IsAtomicInline || line[index].Glyph.Edge != InlineBoxEdge.None)
            {
                return 0;
            }
        }

        return textItemStep;
    }

    /// <summary>Adds a line, one segment per consecutive stretch of the same run, unless nothing visible, no preserved space
    /// and no forced break is on it; each segment of text gets <paramref name="textItemStep"/> (THE RULE FOR THE TEXT ITEM
    /// WIDTH).</summary>
    private static void Add(ImmutableArray<TextLine>.Builder lines, BreakableSegments.LineEndRules rules, List<BreakableSegments.RunGlyph> all, bool forced, double textItemStep)
    {
        if (!all.Exists(glyph => glyph.Glyph.IsLineBreak || !rules.IsBlank(glyph.Run, glyph.Glyph)))
        {
            return;
        }

        var segments = ImmutableArray.CreateBuilder<LineSegment>();
        var first = 0;
        for (var index = 1; index <= all.Count; index++)
        {
            if (index == all.Count || all[index].Run != all[first].Run)
            {
                segments.Add(new LineSegment(all[first].Run, [.. all.Skip(first).Take(index - first).Select(placed => placed.Glyph)], StepOf(all, first, index, textItemStep)));
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
    private sealed class LineEnds(BreakableSegments.Paragraph paragraph, LineShaper? reshape, LineShapingBudget? budget, Func<int, LineEndShaping> atSpace, double availableWidth)
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

        /// <summary>
        /// Where the line from <paramref name="start"/> to <paramref name="end"/> ends with the paragraph's glyphs: the
        /// width of its glyphs without the spaces at its ends, in one step. A cluster cut by the start or the end counts
        /// for the share of its characters that lies on the line, as Chromium finds a position inside a ligature
        /// (<c>ShapeResult::OffsetForPosition</c>). The hyphen a soft hyphen would show is not counted: it is added when
        /// the line is shaped as it is (Blink <c>LineBreaker::BreakText</c> adds the hyphen to the line it found).
        /// </summary>
        public double Width(int start, int end)
        {
            var parts = PartsOf(start, end, breaksHere: false);
            var width = paragraph.WidthWithoutSpaces(parts.First, parts.LeadingEnd)
                + paragraph.Width(parts.LeadingEnd, parts.TrailingStart)
                + paragraph.WidthWithoutSpaces(parts.TrailingStart, parts.Last);
            if (parts.Last > parts.First && _glyphs[parts.Last - 1].End > end)
            {
                width -= Outside(paragraph.FirstStartingAt(_glyphs[parts.Last - 1].Start), parts.Last, end, fromStart: false);
            }

            if (parts.First < parts.Last && _glyphs[parts.First].Start < start)
            {
                width -= Outside(parts.First, paragraph.FirstStartingAt(_glyphs[parts.First].End), start, fromStart: true);
            }

            return width;
        }

        /// <summary>The share of the cluster of glyphs <paramref name="from"/> to <paramref name="to"/> that lies off the line
        /// cut at <paramref name="cut"/>: before the cut when <paramref name="fromStart"/>, else after it.</summary>
        private double Outside(int from, int to, int cut, bool fromStart)
        {
            var (clusterStart, clusterEnd) = (_glyphs[from].Start, _glyphs[from].End);
            var off = fromStart ? cut - clusterStart : clusterEnd - cut;
            return paragraph.Width(from, to) * off / (clusterEnd - clusterStart);
        }

        /// <summary>
        /// The line that ends before one of <paramref name="candidates"/> (segment indices, in order, the last being the
        /// piece that no longer fits): the latest whose line, shaped as it is, fits, trying at most
        /// <see cref="MaxEarlierBreaksTried"/> after the first; otherwise the first, which always may hold the line's
        /// first piece.
        /// </summary>
        public Result<Taken, Error> LineBefore(int start, List<int> candidates, double availableWidth, double textItemStep)
        {
            var tried = 0;
            for (var position = candidates.Count - 1; position > 0 && tried <= MaxEarlierBreaksTried; position--, tried++)
            {
                var line = Line(start, paragraph.Segments[candidates[position]].Start, breaksHere: true);
                if (line.IsFailure)
                {
                    return Result<Taken, Error>.Failure(line.Error);
                }

                if (Fitting.Fits(WidthOf(line.Value, textItemStep), availableWidth))
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
                var trailing = line.Count;
                AddWithoutSpaces(line, parts.TrailingStart, parts.Last);
                Hang(line, trailing, conditionally: parts.BodyEnd < parts.Last || end == paragraph.Text.Length);
                return line;
            });
        }

        /// <summary>The preserved spaces among the line's trailing glyphs (from <paramref name="trailing"/> on) hang: at a soft
        /// wrap their advance is 0; before a forced break or at the paragraph's end (<paramref name="conditionally"/>) they keep
        /// the part of their advance that still fits in the line (THE RULE FOR PRESERVED SPACES AT A LINE'S ENDS).</summary>
        private void Hang(List<BreakableSegments.RunGlyph> line, int trailing, bool conditionally)
        {
            var rules = paragraph.Rules;
            if (!line.Skip(trailing).Any(glyph => rules.Hangs(glyph.Run, glyph.Glyph)))
            {
                return;
            }

            var room = availableWidth - line.Take(trailing).Sum(glyph => glyph.Glyph.Advance);
            for (var index = trailing; index < line.Count; index++)
            {
                var (run, glyph) = (line[index].Run, line[index].Glyph);
                if (!rules.Hangs(run, glyph))
                {
                    room -= glyph.Advance;
                    continue;
                }

                var kept = conditionally ? Math.Clamp(room, 0, glyph.Advance) : 0;
                room -= kept;
                line[index] = line[index] with { Glyph = glyph with { Advance = kept, Hangs = true, Overhang = glyph.Advance - kept } };
            }
        }

        private void AddWithoutSpaces(List<BreakableSegments.RunGlyph> line, int from, int to)
        {
            for (var index = from; index < to; index++)
            {
                if (!paragraph.Rules.IsCollapsibleSpace(_glyphs[index].Run, _glyphs[index].Glyph))
                {
                    line.Add(Plain(_glyphs[index]));
                }
            }
        }

        private static BreakableSegments.RunGlyph Plain(BreakableSegments.Placed glyph) => new(glyph.Run, glyph.Glyph);

        /// <summary>
        /// The glyphs of a line's content (its first to its last glyph that is not trimmed): the paragraph's glyphs, with the
        /// stretch at each end shaped again where that end is not safe to break, from the end to the nearest safe place (a run
        /// boundary, or a cluster none of whose glyphs the shaper marked, <see cref="BreakableSegments.Paragraph.IsSafe"/>).
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

                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(WithHyphen(glyphs, parts.Hyphen));
            }

            var from = Math.Max(start, _glyphs[first].Start);
            var to = Math.Min(end, _glyphs[last - 1].End);
            var reshapeLeft = _glyphs[first].Start < start || !paragraph.IsSafe(from);
            var reshapeRight = _glyphs[last - 1].End > end || (!KeepsEnd(parts, end) && !paragraph.IsSafe(to));
            if (!reshapeLeft && !reshapeRight)
            {
                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Success(WithHyphen(Range(first, last), parts.Hyphen));
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
            if (right == to)
            {
                // Only the start is shaped again; the paragraph's soft hyphen at the end becomes the hyphen.
                return Pieces(from, left, hyphen: false).OnSuccessMap(head => head.Concat(WithHyphen(middle, parts.Hyphen)));
            }

            return Pieces(from, left, hyphen: false).OnSuccessBind(head =>
                Pieces(right, to, parts.Hyphen).OnSuccessMap(tail => head.Concat(middle).Concat(tail)));
        }

        /// <summary>The glyphs with the soft hyphen they end with drawn as its hyphen (<see cref="ShapedGlyph.BreakHyphen"/>,
        /// shaped alone), when <paramref name="hyphen"/>.</summary>
        private static List<BreakableSegments.RunGlyph> WithHyphen(List<BreakableSegments.RunGlyph> glyphs, bool hyphen)
        {
            if (hyphen && glyphs.Count > 0 && glyphs[^1].Glyph.BreakHyphen is { } shown)
            {
                glyphs[^1] = glyphs[^1] with { Glyph = shown };
            }

            return glyphs;
        }

        /// <summary>True when the line keeps the paragraph's glyphs at its end although that place is not safe to break: it
        /// ends at a collapsible space whose run has <see cref="LineEndShaping.KeepParagraphGlyphs"/>, and not before a forced break
        /// or at the paragraph's end (<see cref="LineEndShaping"/>).</summary>
        private bool KeepsEnd(Parts parts, int end) =>
            parts.BodyEnd == parts.Last
            && end < paragraph.Text.Length
            && paragraph.HasSpace(parts.TrailingStart, parts.BodyEnd)
            && atSpace(_glyphs[parts.TrailingStart].Run) == LineEndShaping.KeepParagraphGlyphs;

        private List<BreakableSegments.RunGlyph> Range(int from, int to)
        {
            var glyphs = new List<BreakableSegments.RunGlyph>(to - from);
            for (var index = from; index < to; index++)
            {
                glyphs.Add(Plain(_glyphs[index]));
            }

            return glyphs;
        }

        /// <summary>The text from <paramref name="from"/> to <paramref name="to"/> shaped again, one piece per run it crosses:
        /// the end of a line is shaped again within its run, also where the paragraph shaped that run with the run before or
        /// after it, as Chromium shapes a line's end again within one item's result (<c>ShapingLineBreaker::ShapeLine</c>;
        /// case <c>sr-line-left</c>: a wrapped line's first "T" is shaped without the "o" of the next run). The last piece
        /// shows the hyphen when <paramref name="hyphen"/>. Every character handed to the reshaper is charged to the
        /// paragraph's budget first.</summary>
        private Result<IEnumerable<BreakableSegments.RunGlyph>, Error> Pieces(int from, int to, bool hyphen)
        {
            if (!budget!.SpendCharacters(to - from))
            {
                return Result<IEnumerable<BreakableSegments.RunGlyph>, Error>.Failure(budget.CharactersSpent());
            }

            var pieces = new List<BreakableSegments.RunGlyph>();
            var runStarts = paragraph.RunStarts;
            var first = paragraph.RunOf(from);
            while (first > 0 && runStarts[first - 1] == runStarts[first])
            {
                first--;
            }

            for (var run = first; run < runStarts.Length - 1 && (runStarts[run] < to || runStarts[run + 1] == to); run++)
            {
                var (pieceStart, pieceEnd) = (Math.Max(from, runStarts[run]), Math.Min(to, runStarts[run + 1]));
                if (runStarts[run] == runStarts[run + 1])
                {
                    // A run without text shapes nothing; the edge of an inline box it holds stays where its cluster lies.
                    pieces.AddRange(paragraph.EdgesOf(run, from, to));
                    continue;
                }

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
