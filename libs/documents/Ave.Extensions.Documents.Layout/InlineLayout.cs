using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The inline formatting context: sets a box's inline runs in lines ("boxes are laid out horizontally, one after
/// the other, beginning at the top of a containing block", CSS 2.1 §9.4.2), and splits the paragraph between
/// lines when the page is full.
/// </summary>
public static class InlineLayout
{
    /// <summary>The Inline algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <remarks>
    /// Steps: resolve every run to text (a page counter becomes the number of the page being laid out, or the page
    /// count, from <see cref="LayoutEnvironment.Page"/>) → collapse white space by each run's mode (CSS Text 3 §4.1.1) → resolve each run's
    /// font and shape the runs that share a font as one text (<see cref="ShapingGroups"/>) → break into lines (greedy, at UAX #14 opportunities) → stack line boxes from the line the
    /// token names until the next one does not fit. A page break may fall between any two lines (class B
    /// breakpoints, CSS Fragmentation 3 §4.1), under <c>orphans</c> and <c>widows</c> (<see cref="BreakWhereFull"/>).
    /// The lines are recomputed on every call rather than carried in the token: the token stays a plain number,
    /// and the result depends only on the arguments. LIMIT: a paragraph that contains a page counter AND breaks
    /// across pages is broken into lines again on the next page with that page's number, so if the number's width
    /// changes the wrapping, a line may repeat or be skipped at the break.
    /// </remarks>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">A box whose children are inline runs and atomic inlines (<see cref="LineBoxes.AtomicInline"/>).</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">An <see cref="InlineBreakToken"/>, or null to start.</param>
    /// <returns>The fragment and continuation, NoRoom, or an error (missing font or glyph, with the run's location).</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        if (incoming is not null && InvalidToken(incoming) is { } reason)
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidBreakToken(box, incoming, reason));
        }

        var start = (InlineBreakToken?)incoming ?? new InlineBreakToken(0);

        // A first line taller than the page area that starts the page is sliced when the profile says so (MonolithicSlices).
        var slices = MonolithicSlices.Apply(environment, SlicedBox.ContentIsAtPageStart(box.Style, space, incoming));
        return ShapeRuns(environment, box).OnSuccessBind(items => StrutOf(environment, box).OnSuccessBind(strut =>
            SlicedBox.Layout(box, space, incoming, start, (width, height, breakAt) =>
                LaidOut(environment, items, width).OnSuccessBind(runs =>
                    StackLines(runs, strut, box, start, slices, environment.Profile.FontMetricRounding, width, height, breakAt switch
                    {
                        null => null,
                        InlineEarlyBreak line => line,
                        _ => throw new ArgumentException($"Inline received a {breakAt.GetType().Name} it did not report.", nameof(space)),
                    })))));
    }

    /// <summary>Why a paragraph cannot resume from <paramref name="token"/>, or null when it can: a token of another
    /// algorithm, a negative line index, or a height shown that no slices can have shown (negative, not finite, or shown
    /// without a slice). A line index past the last line is a position the paragraph can be given: the next page breaks
    /// its lines again in that page's width, and the paragraph is then complete. A height shown that is the line's whole
    /// height or more is refused where the line is known, by <see cref="MonolithicSlices.Next"/>.</summary>
    private static string? InvalidToken(BreakToken token) => token switch
    {
        not InlineBreakToken => "it is the token of another layout algorithm",
        InlineBreakToken { LineIndex: < 0 } line => $"line {line.LineIndex} is outside the paragraph",
        InlineBreakToken line when !(line.Shown >= 0 && double.IsFinite(line.Shown)) || line.Slices < 0 || (line.Shown > 0) != (line.Slices > 0) =>
            $"{line.Slices} slices cannot have shown {InvariantNumber.Format(line.Shown)}pt of a line",
        _ => null,
    };

    /// <summary>The paragraph's strut (CSS 2.1 §10.8.1), with its font resolved like a run's; null when the model gives
    /// the paragraph no text properties (<see cref="BoxStyle.Text"/>). A missing font is the error a run would get, at the
    /// paragraph's source.</summary>
    private static Result<LineBoxes.Strut?, Error> StrutOf(LayoutEnvironment environment, Box box) =>
        box.Style.Text is not { } text
            ? Result<LineBoxes.Strut?, Error>.Success(null)
            : environment.Fonts.Find(text.FontFamily, text.FontWeight, text.FontStyle)
                .OnSuccessMap(font => (LineBoxes.Strut?)new LineBoxes.Strut(font, text))
                .MapError(error => error.WithSource(box.Source));

    /// <summary>
    /// The intrinsic widths of a paragraph (CSS Sizing 3 §5.1): the min-content inline size, which for text is the
    /// widest piece that cannot be broken, and the max-content inline size, the widest line when the text only breaks
    /// at forced breaks (CSS Sizing 3 §5.2 leaves the details to CSS 2.1). Both come from the same line breaker as layout: break
    /// with no width at all (every break opportunity is taken) and with unlimited width. Border and padding are added. An
    /// atomic inline counts with its margin box at its own min-content width in the first and its max-content width in the
    /// second (its min-content and max-content contributions, CSS Sizing 3 §5.1), measured by its own algorithm once (case
    /// <c>ii-table</c>: an image and its margins widen both widths of a table column).
    /// </summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">A box whose children are inline runs and atomic inlines.</param>
    /// <returns>The widths, or a font error.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        ShapeRuns(environment, box).OnSuccessBind(items => Measured(environment, items).OnSuccessBind(sized =>
        {
            Result<double, Error> Widest(IReadOnlyList<LineBoxes.Item> runs, double width) =>
                BreakLines(runs, width, box.Style.TextAlign).OnSuccessMap(lines => lines.Select(line => line.Width).DefaultIfEmpty(0).Max());
            return Widest(sized.Least, 0).OnSuccessBind(least => Widest(sized.Most, double.PositiveInfinity).OnSuccessMap(most =>
                new IntrinsicWidths(least, most).Plus(SlicedBox.HorizontalInsets(box.Style))));
        }));

    /// <summary>The paragraph's items with every atomic inline at its min-content width (<c>Least</c>) and at its
    /// max-content width (<c>Most</c>), each plus its horizontal margins: one measurement of each atomic inline.</summary>
    private static Result<(IReadOnlyList<LineBoxes.Item> Least, IReadOnlyList<LineBoxes.Item> Most), Error> Measured(LayoutEnvironment environment, IReadOnlyList<LineBoxes.Item> items) =>
        items.TraverseAll(item => item is LineBoxes.AtomicInline atomic
                ? environment.Measure(atomic.Box).OnSuccessMap(widths => (Least: (LineBoxes.Item)(atomic with { Width = widths.MinContent + atomic.Margins }), Most: (LineBoxes.Item)(atomic with { Width = widths.MaxContent + atomic.Margins })))
                : Result<(LineBoxes.Item Least, LineBoxes.Item Most), Error>.Success((item, item)))
            .OnSuccessMap(pairs => ((IReadOnlyList<LineBoxes.Item>)[.. pairs.Select(pair => pair.Least)], (IReadOnlyList<LineBoxes.Item>)[.. pairs.Select(pair => pair.Most)]));

    /// <summary>
    /// The paragraph's items with every atomic inline laid out by its own algorithm (<see cref="LayoutEnvironment.Layout"/>)
    /// at its shrink-to-fit width, with no limit to its height: it is monolithic, and the line it lands on decides where the
    /// page breaks (CSS Fragmentation 3 §4.1 lists no breakpoint inside an atomic inline). The width is CSS 2.1 §10.3.9's
    /// for an inline-block, "min(max(preferred minimum width, available width), preferred width)", with the content width
    /// less its horizontal margins as the available width; for an image both widths are its used width (§10.3.2: an inline
    /// replaced element is as wide as its image), so an image wider than the line overflows it, as in Chromium (case
    /// <c>ii-wrap</c>). Each atomic inline is measured once and laid out once per layout of the paragraph. One that does not
    /// lay out whole is a bug of its algorithm.
    /// </summary>
    private static Result<IReadOnlyList<LineBoxes.Item>, Error> LaidOut(LayoutEnvironment environment, IReadOnlyList<LineBoxes.Item> items, double contentWidth) =>
        items.TraverseAll(item => item is LineBoxes.AtomicInline atomic
                ? environment.Measure(atomic.Box).OnSuccessBind(widths => environment.Layout(
                    atomic.Box,
                    new ConstraintSpace(Math.Min(Math.Max(widths.MinContent, contentWidth - atomic.Margins), widths.MaxContent), double.PositiveInfinity, IsAtPageStart: true),
                    null))
                    .OnSuccessBind(result => result is LayoutResult.Placed { Continuation: null } placed
                        ? Result<LineBoxes.Item, Error>.Success(atomic with { Fragment = placed.Fragment, Width = placed.Fragment.Size.Width + atomic.Margins })
                        : Result<LineBoxes.Item, Error>.Failure(LayoutErrors.InvalidContent(atomic.Box, "an atomic inline must lay out whole, with no limit to its height.")))
                : Result<LineBoxes.Item, Error>.Success(item))
            .OnSuccessMap(laid => (IReadOnlyList<LineBoxes.Item>)[.. laid]);

    /// <summary>The paragraph's lines at a width, each shaped as the line it is: where a line ends or starts at a place
    /// that is not safe to break, its end is shaped again with the run's font (<see cref="GreedyLineBreaker"/>, THE RULE
    /// FOR SHAPING AT A BREAK). A shaping error names the run.</summary>
    /// <remarks>An atomic inline is one glyph of its margin box's width (<see cref="ShapedGlyph.AtomicInline"/>), never
    /// shaped: both its edges are safe places to break, so the breaker never asks for it again; if it did, it gets the same
    /// glyph.</remarks>
    private static Result<ValueList<TextLine>, Error> BreakLines(IReadOnlyList<LineBoxes.Item> runs, double width, TextAlign align) =>
        GreedyLineBreaker.BreakLines([.. runs.Select(LineBoxes.GlyphsOf)], WhiteSpaceOf(runs), width, (run, text, showsHyphen, budget) => runs[run] switch
            {
                LineBoxes.ShapedRun shaped => Shaper.ShapeLinePiece(text, shaped.Font, shaped.Run.Style.FontSize.Points, showsHyphen, budget).MapError(error => error.WithSource(shaped.Run.Source)),
                var atomic => Result<ValueList<ShapedGlyph>, Error>.Success(LineBoxes.GlyphsOf(atomic)),
            },
            LineEndAtSpace(align));

    /// <summary>The white-space properties of each item for the line breaker: a run's from its style; an atomic inline has no
    /// spaces, so the initial values.</summary>
    private static RunWhiteSpace[] WhiteSpaceOf(IReadOnlyList<LineBoxes.Item> runs) =>
        [.. runs.Select(item => item is LineBoxes.ShapedRun shaped ? new RunWhiteSpace(shaped.Run.Style.WhiteSpaceCollapse) : RunWhiteSpace.Initial)];

    /// <summary>What the line breaker does at a line end at a collapsible space (<see cref="LineEndShaping"/>), from the
    /// paragraph's alignment, as Chromium decides whether a line "needs an accurate end position" (Blink
    /// <c>LineInfo::ComputeNeedsAccurateEndPosition</c>, core/layout/inline/line_info.cc): <c>start</c> does not, so the
    /// paragraph's glyphs are kept; <c>end</c>, <c>center</c> and <c>justify</c> do. The engine lays out left-to-right text
    /// only, so <see cref="TextAlign.Left"/> is <c>start</c> here; in right-to-left text it would be <c>end</c>. The model has
    /// no text decoration, which needs an accurate end position too, when it comes.</summary>
    private static LineEndShaping LineEndAtSpace(TextAlign align) =>
        align == TextAlign.Left ? LineEndShaping.KeepParagraphGlyphs : LineEndShaping.ShapeAgainBeforeSpace;

    /// <summary>
    /// True when the box's text makes at least one line box: some text is left after white-space collapsing.
    /// A paragraph of only collapsible spaces has no line boxes (CSS 2.1 §9.4.2), which makes it an empty box for margin
    /// collapsing (<see cref="MarginCollapsing.IsEmpty"/>). A page counter always shows digits, and an atomic inline always
    /// makes a line (CSS 2.1 §9.4.2: line boxes that contain no "replaced elements" among other content are the ones that
    /// do not exist).
    /// </summary>
    /// <param name="box">An inline box.</param>
    /// <returns>Whether it has line boxes.</returns>
    public static bool HasLineBoxes(Box box) =>
        box.Children.Any(child => child is CounterRun or Box || (child is TextRun text && !WhiteSpaceCollapsing.IsAllCollapsible(text.Text, text.Style.WhiteSpaceCollapse)));

    /// <summary>
    /// Resolves every run to its text, collapses white space over the whole paragraph, finds each run's font, then shapes
    /// the runs in groups (<see cref="ShapingGroups"/>): the runs of one group as one text, so a kerning pair or a ligature
    /// across the border of two runs is kept, and every run gets its own glyphs back. Errors from all runs are reported
    /// together, each with the location of the run that caused it.
    /// </summary>
    /// <remarks>
    /// THE RULE FOR AN ATOMIC INLINE AMONG THE RUNS: a child box laid out by <see cref="LayoutAlgorithmId.Image"/> is an
    /// atomic inline (CSS Display 3, Appendix A: "An inline-level box that is replaced (such as an image) or that establishes
    /// a new formatting context (such as an inline-block or inline-table) and cannot split across lines"), one item of the
    /// line (<see cref="LineBoxes.AtomicInline"/>).
    /// For white-space collapsing it is the object replacement character U+FFFC, as in Chromium's inline text (Blink
    /// <c>InlineItemsBuilder::AppendAtomicInline</c>): not a space, so a space on each side of it is kept (case
    /// <c>ii-baseline</c>: "ab", a space, the image, a space, "cd"), and a space at a line end next to it goes as at any line
    /// end. It ends the shaping group before it and the text after it starts a new one. Any other box among the runs is
    /// refused (<see cref="ModelCheck"/> refuses it first).
    /// </remarks>
    private static Result<IReadOnlyList<LineBoxes.Item>, Error> ShapeRuns(LayoutEnvironment environment, Box box)
    {
        if (box.Children.FirstOrDefault(child => child is not (TextRun or CounterRun) && !(child is Box image && image.Layout == LayoutAlgorithmId.Image)) is { } other)
        {
            return Result<IReadOnlyList<LineBoxes.Item>, Error>.Failure(
                LayoutErrors.InvalidContent(box, other is Box
                    ? "a box among the text runs must be an image (an atomic inline laid out by the image algorithm); other boxes belong in a flow box."
                    : "inline layout needs text runs and images as children."));
        }

        var collapsed = WhiteSpaceCollapsing.Collapse([.. box.Children.Select(child => child is InlineRun run
            ? (TextOf(run, environment.Page), run.Style.WhiteSpaceCollapse)
            : (ObjectReplacement, WhiteSpaceCollapse.Collapse))]);

        // The paragraph in stretches: the runs between two atomic inlines are shaped in groups, an atomic inline is an item
        // of its own.
        var stretches = new List<Result<IEnumerable<LineBoxes.Item>, Error>>();
        var members = new List<GroupMember>();
        void EndStretch()
        {
            if (members.Count > 0)
            {
                stretches.Add(ShapingGroups([.. members])
                    .TraverseAll(group => group[0].Font.IsFailure ? Result<IEnumerable<LineBoxes.ShapedRun>, Error>.Failure(group[0].Font.Error) : ShapeGroup(group))
                    .OnSuccessMap(shaped => shaped.SelectMany(group => group).Cast<LineBoxes.Item>()));
                members.Clear();
            }
        }

        foreach (var (child, text) in box.Children.Zip(collapsed))
        {
            if (child is InlineRun run)
            {
                members.Add(new GroupMember(run, text,
                    environment.Fonts.Find(run.Style.FontFamily, run.Style.FontWeight, run.Style.FontStyle).MapError(error => error.WithSource(run.Source))));
                continue;
            }

            EndStretch();
            stretches.Add(Result<IEnumerable<LineBoxes.Item>, Error>.Success([new LineBoxes.AtomicInline((Box)child, Fragment: null, Width: 0)]));
        }

        EndStretch();
        return stretches.TraverseAll(stretch => stretch)
            .OnSuccessMap(items => (IReadOnlyList<LineBoxes.Item>)[.. items.SelectMany(stretch => stretch)]);
    }

    /// <summary>An atomic inline's text for white-space collapsing: the object replacement character U+FFFC.</summary>
    private static readonly string ObjectReplacement = ShapedGlyph.ObjectReplacementCharacter.ToString();

    /// <summary>A run of the paragraph with its collapsed text and its font, or the error of a font that was not found.</summary>
    private sealed record GroupMember(InlineRun Run, string Text, Result<TrueTypeFont, Error> Font);

    /// <summary>
    /// THE RULE FOR SHAPING ACROSS RUNS: the groups of the paragraph's runs that are shaped as one text each. A run joins the
    /// group before it when it is set in the same font as the group's first run with text: the same family, size, weight and
    /// style, which choose and size the font. Its colour and its line height do not matter. A run without text joins the
    /// group it lies in. CSS Text 3 §7.3, "Shaping Across Element Boundaries": "Text shaping must not be broken across inline
    /// box boundaries when there is no effective change in formatting, or if the only formatting changes do not affect the
    /// glyphs"; it "should not be broken across inline box boundaries otherwise, if it is reasonable and possible", which
    /// leaves a change of font to the user agent. As Chromium does it (Blink <c>InlineNode::ShapeText</c> and
    /// <c>ShouldBreakShapingBeforeText</c>, core/layout/inline/inline_node.cc: adjacent text items are shaped together while
    /// their fonts are equal, an item without text is passed over, and an item that starts with a zero width non-joiner
    /// U+200C starts a new text). Cases <c>sr-same-font</c> and <c>sr-font-changes</c>. Chromium also breaks the text at an
    /// inline box with inline padding, border or margin or another vertical alignment (<c>ShouldBreakShapingBeforeBox</c>);
    /// the model's runs have none of these. A run whose font is not found is a group of its own.
    /// </summary>
    /// <param name="members">The paragraph's runs, in order.</param>
    /// <returns>The groups, in order; together they hold every run once.</returns>
    private static IReadOnlyList<IReadOnlyList<GroupMember>> ShapingGroups(ImmutableArray<GroupMember> members)
    {
        var groups = new List<IReadOnlyList<GroupMember>>();
        var current = new List<GroupMember>();
        GroupMember? first = null;
        foreach (var member in members)
        {
            var joins = current.Count > 0 && member.Font.IsSuccess && current[0].Font.IsSuccess
                && (member.Text.Length == 0 || first is null || (SameFont(first.Run.Style, member.Run.Style) && member.Text[0] != ZeroWidthNonJoiner));
            if (!joins && current.Count > 0)
            {
                groups.Add(current);
                (current, first) = ([], null);
            }

            current.Add(member);
            first ??= member.Text.Length > 0 ? member : null;
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    /// <summary>The zero width non-joiner U+200C.</summary>
    private const char ZeroWidthNonJoiner = '\u200C';

    /// <summary>True when two runs' text is set in the same font at the same size (<see cref="ShapingGroups"/>).</summary>
    private static bool SameFont(TextStyle one, TextStyle other) =>
        string.Equals(one.FontFamily, other.FontFamily, StringComparison.Ordinal)
        && one.FontSize == other.FontSize && one.FontWeight == other.FontWeight && one.FontStyle == other.FontStyle;

    /// <summary>
    /// Shapes the runs of one group (<see cref="ShapingGroups"/>) as one text, in the font and size of its first run with
    /// text, and gives every run the glyphs whose cluster starts in its text; every run keeps its own font. A ligature across the border of two runs is kept, by the run with its first character, as
    /// Chromium splits the shared result ("When multiple code units shape to one glyph, such as ligatures, the item that has
    /// its first code unit keeps the glyph", <c>InlineNode::ShapeText</c>): it is drawn in that run's colour, its text is
    /// every character it shows, and the next run puts no glyph there (case <c>sr-same-font</c>, "fix" with the i in another
    /// colour). When the group's text cannot be shaped, each run is shaped alone, so that every error names the run that
    /// caused it; an error that only the whole group has (a shaping limit) names the group's first run with text.
    /// </summary>
    /// <param name="group">The runs of one group; every run has a font.</param>
    /// <returns>Every run of the group with its glyphs, or the errors.</returns>
    private static Result<IEnumerable<LineBoxes.ShapedRun>, Error> ShapeGroup(IReadOnlyList<GroupMember> group)
    {
        // The text is shaped in the font of the first run with text; a run without text keeps its own font, which its empty
        // inline box counts for the line's height with (LineBoxes.Build). A group of runs without text shapes nothing.
        if (group.FirstOrDefault(member => member.Text.Length > 0) is not { } shaping)
        {
            return Result<IEnumerable<LineBoxes.ShapedRun>, Error>.Success([.. group.Select(member => new LineBoxes.ShapedRun(member.Run, member.Font.Value, []))]);
        }

        var (font, size) = (shaping.Font.Value, shaping.Run.Style.FontSize.Points);
        Result<IEnumerable<LineBoxes.ShapedRun>, Error> Alone(GroupMember member) =>
            Shaper.Shape(member.Text, member.Font.Value, member.Run.Style.FontSize.Points)
                .OnSuccessMap(glyphs => (IEnumerable<LineBoxes.ShapedRun>)[new LineBoxes.ShapedRun(member.Run, member.Font.Value, glyphs)])
                .MapError(error => error.WithSource(member.Run.Source));
        if (group.Count == 1)
        {
            return Alone(group[0]);
        }

        var shaped = Shaper.Shape(string.Concat(group.Select(member => member.Text)), font, size);
        if (shaped.IsFailure)
        {
            var alone = group.TraverseAll(Alone);
            return Result<IEnumerable<LineBoxes.ShapedRun>, Error>.Failure(alone.IsFailure ? alone.Error : shaped.Error.WithSource(shaping.Run.Source));
        }

        // One pass over the glyphs. `offset` is where the next glyph's cluster starts; a glyph without text belongs to the
        // cluster before it.
        var glyphs = shaped.Value;
        var runs = new List<LineBoxes.ShapedRun>(group.Count);
        var (next, offset, end) = (0, 0, 0);
        foreach (var member in group)
        {
            end += member.Text.Length;
            var start = next;
            while (next < glyphs.Length && (offset < end || (next > start && glyphs[next].Text.Length == 0)))
            {
                offset += glyphs[next].Text.Length;
                next++;
            }

            runs.Add(new LineBoxes.ShapedRun(member.Run, member.Font.Value, [.. glyphs.Skip(start).Take(next - start)]));
        }

        return Result<IEnumerable<LineBoxes.ShapedRun>, Error>.Success(runs);
    }

    /// <summary>The text a run shows on the given page.</summary>
    private static string TextOf(InlineRun run, PageContext page) => run switch
    {
        TextRun text => text.Text,
        CounterRun counter => page.Format(counter.Counter),
        _ => throw new InvalidOperationException($"Unknown inline run {run.GetType().Name}; checked before."),
    };

    /// <summary>Breaks the paragraph into lines for the content width and stacks them from the line
    /// <paramref name="start"/> names until the next one does not fit, or up to the early break when one is given. None
    /// when not even that first line fits — unless it starts the page and <paramref name="slices"/> is set, or earlier
    /// pages showed slices of it: then this page shows its next slice (<see cref="MonolithicSlices"/>), and the lines
    /// after the last slice follow below it. The breakpoints are the places between two lines (class B, CSS Fragmentation
    /// 3 §4.1), with <c>orphans</c> and <c>widows</c>: where the next line does not fit, <see cref="BreakWhereFull"/>
    /// decides where the page breaks; the best breakpoint of a complete fragment (<see cref="BestBreakInside"/>) is
    /// reported as its early break.</summary>
    private static Result<Maybe<SlicedBox.Content>, Error> StackLines(
        IReadOnlyList<LineBoxes.Item> runs, LineBoxes.Strut? strut, Box box, InlineBreakToken start, bool slices, FontMetricRounding rounding, double contentWidth, double contentHeight, InlineEarlyBreak? breakAt)
    {
        var style = box.Style;
        var firstLine = start.LineIndex;
        var broken = BreakLines(runs, contentWidth, style.TextAlign);
        if (broken.IsFailure)
        {
            return Result<Maybe<SlicedBox.Content>, Error>.Failure(broken.Error);
        }

        var lines = broken.Value;
        var empty = LineBoxes.EmptyRunsByLine(lines, runs);
        var placed = ImmutableArray.CreateBuilder<PlacedFragment>();
        var bottom = 0.0;
        for (var index = firstLine; index < lines.Count; index++)
        {
            if (breakAt is { } target && target.LineIndex == index)
            {
                return SlicedBox.Placed(placed.ToImmutable(), bottom, new InlineBreakToken(index), target.Appeal);
            }

            var line = LineBoxes.Build(lines[index], runs, contentWidth, style.TextAlign, isLastLine: index == lines.Count - 1, strut, rounding, empty[index]);
            var shown = index == firstLine ? start.Shown : 0;
            if (shown > 0 || (placed.Count == 0 && slices && !Fitting.Fits(line.Size.Height, contentHeight)))
            {
                var sliceCount = index == firstLine ? start.Slices : 0;
                var next = MonolithicSlices.Next(box, start, line, shown, sliceCount, contentHeight);
                if (next.IsFailure || next.Value.HasNoValue)
                {
                    return next.IsFailure ? Result<Maybe<SlicedBox.Content>, Error>.Failure(next.Error) : SlicedBox.NothingFits;
                }

                var (slice, shownAfter) = next.Value.Value;
                placed.Add(new PlacedFragment(new Point(0, bottom), slice));
                bottom += slice.Size.Height;
                if (shownAfter is { } after)
                {
                    return SlicedBox.Placed(placed.ToImmutable(), bottom, new InlineBreakToken(index) { Shown = after, Slices = sliceCount + 1 });
                }

                continue;
            }

            if (!Fitting.Fits(bottom + line.Size.Height, contentHeight))
            {
                if (placed.Count == 0)
                {
                    return SlicedBox.NothingFits;
                }

                var (before, appeal) = BreakWhereFull(style, firstLine, index, lines.Count);
                var kept = placed.Take(before - firstLine).ToImmutableArray();
                var height = kept.Length == 0 ? 0 : kept[^1].Offset.Y + kept[^1].Fragment.Size.Height;
                return SlicedBox.Placed([.. kept], height, new InlineBreakToken(before), appeal);
            }

            placed.Add(new PlacedFragment(new Point(0, bottom), line));
            bottom += line.Size.Height;
        }

        // The best breakpoint of the complete fragment, reported for a parent that may have to break here instead (a
        // break before the fragment's first line is no breakpoint of the fragment).
        return SlicedBox.Placed(placed.ToImmutable(), bottom, null, bestBreak: BestBreakInside(style, firstLine, lines.Count), atEnd: new InlineBreakToken(lines.Count));
    }

    /// <summary>
    /// THE RULE FOR ORPHANS AND WIDOWS where the page is full (CSS Fragmentation 3 §3.3; §4.4 rule 3: "Breaking at a
    /// class B break point is allowed only if the number of line boxes between the break and the start of the enclosing
    /// block box is the value of orphans or more, and the number of line boxes between the break and the end of the box
    /// is the value of widows or more"), as Chromium applies it (<c>third_party/blink/renderer/core/layout/
    /// block_layout_algorithm.cc</c>, read for its rule, which the reference prints): the line <paramref name="full"/> no
    /// longer fits, and this fragment holds the lines from <paramref name="firstLine"/> up to it.
    /// <list type="bullet">
    /// <item>Fewer lines than <c>orphans</c> on the page — on a page that continues the paragraph, fewer than both values
    /// ask for, since its lines follow a break as well ("what was seen as trailing widows in the previous fragment is
    /// essentially orphans for us now") — break there, as a violation: a better break before the paragraph moves it to
    /// the next page whole.</item>
    /// <item>Enough lines follow (<c>widows</c>): break there, a perfect break.</item>
    /// <item>Too few follow: move lines to the next page so that <c>widows</c> follow, but keep at least <c>orphans</c>
    /// on the page — orphans win where the paragraph is too short for both, which is how Chromium breaks a three-line
    /// paragraph with the initial values (case <c>c-few-lines</c>: two lines stay, one moves) — a perfect break.</item>
    /// </list>
    /// </summary>
    /// <param name="style">The paragraph's style.</param>
    /// <param name="firstLine">The first line on this page.</param>
    /// <param name="full">The first line that does not fit.</param>
    /// <param name="lineCount">The paragraph's lines.</param>
    /// <returns>The line the page breaks before, and the appeal of that break.</returns>
    internal static (int Before, BreakAppeal Appeal) BreakWhereFull(BoxStyle style, int firstLine, int full, int lineCount)
    {
        var needed = firstLine == 0 ? style.Orphans : Math.Max(style.Orphans, style.Widows);
        if (full - firstLine < needed)
        {
            return (full, BreakAppeal.ViolatesOrphansWidows);
        }

        if (lineCount - full >= style.Widows)
        {
            return (full, BreakAppeal.Perfect);
        }

        var lines = lineCount - firstLine;
        return (firstLine + Math.Max(lines - style.Widows, Math.Min(lines, style.Orphans)), BreakAppeal.Perfect);
    }

    /// <summary>
    /// The best breakpoint inside a complete fragment, for a parent that may have to break there instead (Chromium's
    /// <c>UpdateEarlyBreakBetweenLines</c>, same file): the latest break that leaves <c>widows</c> lines after it and keeps
    /// <c>orphans</c> before it, a perfect break; where the fragment has too few lines for both, the break before its
    /// last line, a violation of orphans and widows. None for a fragment of one line: a break before its first line is
    /// no breakpoint of the fragment.
    /// </summary>
    /// <param name="style">The paragraph's style.</param>
    /// <param name="firstLine">The first line on this page.</param>
    /// <param name="lineCount">The paragraph's lines.</param>
    /// <returns>The early break, or null.</returns>
    internal static InlineEarlyBreak? BestBreakInside(BoxStyle style, int firstLine, int lineCount)
    {
        var lines = lineCount - firstLine;
        if (lines < 2)
        {
            return null;
        }

        var before = Math.Max(lines - style.Widows, Math.Min(lines - 1, style.Orphans));
        return before < style.Orphans || lines - before < style.Widows
            ? new InlineEarlyBreak(lineCount - 1, BreakAppeal.ViolatesOrphansWidows)
            : new InlineEarlyBreak(firstLine + before, BreakAppeal.Perfect);
    }
}
