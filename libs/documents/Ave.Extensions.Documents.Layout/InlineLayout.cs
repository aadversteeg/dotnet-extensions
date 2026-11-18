using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
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
    /// count, from <see cref="LayoutEnvironment.Page"/>) → collapse white space (CSS Text 3 §4.1.1) → resolve each run's
    /// font and shape it → break into lines (greedy, at UAX #14 opportunities) → stack line boxes from the line the
    /// token names until the next one does not fit. A page break may fall between any two lines (class B
    /// breakpoints, CSS Fragmentation 3 §4.1), under <c>orphans</c> and <c>widows</c> (<see cref="BreakWhereFull"/>).
    /// The lines are recomputed on every call rather than carried in the token: the token stays a plain number,
    /// and the result depends only on the arguments. LIMIT: a paragraph that contains a page counter AND breaks
    /// across pages is broken into lines again on the next page with that page's number, so if the number's width
    /// changes the wrapping, a line may repeat or be skipped at the break.
    /// </remarks>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">A box whose children are all inline runs.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">An <see cref="InlineBreakToken"/>, or null to start.</param>
    /// <returns>The fragment and continuation, NoRoom, or an error (missing font or glyph, with the run's location).</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        var firstLine = incoming switch
        {
            null => 0,
            InlineBreakToken token => token.LineIndex,
            _ => throw new ArgumentException($"Inline received a {incoming.GetType().Name} it did not produce.", nameof(incoming)),
        };

        return ShapeRuns(environment, box).OnSuccessBind(runs => StrutOf(environment, box).OnSuccessBind(strut =>
            SlicedBox.Layout(box, space, incoming, new InlineBreakToken(firstLine), (width, height, breakAt) =>
                StackLines(runs, strut, box.Style, firstLine, width, height, breakAt switch
                {
                    null => null,
                    InlineEarlyBreak line => line,
                    _ => throw new ArgumentException($"Inline received a {breakAt.GetType().Name} it did not report.", nameof(space)),
                }))));
    }

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
    /// with no width at all (every break opportunity is taken) and with unlimited width. Border and padding are added.
    /// </summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="box">A box whose children are all inline runs.</param>
    /// <returns>The widths, or a font error.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        ShapeRuns(environment, box).OnSuccessBind(runs =>
        {
            Result<double, Error> Widest(double width) =>
                BreakLines(runs, width).OnSuccessMap(lines => lines.Select(line => line.Width).DefaultIfEmpty(0).Max());
            return Widest(0).OnSuccessBind(least => Widest(double.PositiveInfinity).OnSuccessMap(most =>
                new IntrinsicWidths(least, most).Plus(SlicedBox.HorizontalInsets(box.Style))));
        });

    /// <summary>The paragraph's lines at a width, each shaped as the line it is: where a line ends or starts at a place
    /// that is not safe to break, its end is shaped again with the run's font (<see cref="GreedyLineBreaker"/>, THE RULE
    /// FOR SHAPING AT A BREAK). A shaping error names the run.</summary>
    private static Result<ValueList<TextLine>, Error> BreakLines(IReadOnlyList<LineBoxes.ShapedRun> runs, double width) =>
        GreedyLineBreaker.BreakLines([.. runs.Select(run => run.Glyphs)], width, (run, text, showsHyphen, budget) =>
            Shaper.ShapeLinePiece(text, runs[run].Font, runs[run].Run.Style.FontSize.Points, showsHyphen, budget).MapError(error => error.WithSource(runs[run].Run.Source)));

    /// <summary>
    /// True when the box's text makes at least one line box: some text is left after white-space collapsing.
    /// A paragraph of only spaces has no line boxes (CSS 2.1 §9.4.2), which makes it an empty box for margin
    /// collapsing (<see cref="MarginCollapsing.IsEmpty"/>). A page counter always shows digits.
    /// </summary>
    /// <param name="box">An inline box.</param>
    /// <returns>Whether it has line boxes.</returns>
    public static bool HasLineBoxes(Box box) =>
        box.Children.Any(child => child is CounterRun || (child is TextRun text && !WhiteSpaceCollapsing.IsAllCollapsible(text.Text)));

    /// <summary>
    /// Resolves every run to its text, collapses white space over the whole paragraph, then finds each run's font
    /// and shapes it. Errors from all runs are reported together, each with the location of the run that caused it.
    /// </summary>
    private static Result<IReadOnlyList<LineBoxes.ShapedRun>, Error> ShapeRuns(LayoutEnvironment environment, Box box)
    {
        if (box.Children.Any(child => child is not (TextRun or CounterRun)))
        {
            return Result<IReadOnlyList<LineBoxes.ShapedRun>, Error>.Failure(
                LayoutErrors.InvalidContent(box, "inline layout needs text-run children; boxes belong in a flow box."));
        }

        var runs = box.Children.Cast<InlineRun>().ToImmutableArray();
        var collapsed = WhiteSpaceCollapsing.Collapse([.. runs.Select(run => TextOf(run, environment.Page))]);
        return runs.Zip(collapsed).TraverseAll(pair =>
        {
            var (run, text) = pair;
            return environment.Fonts.Find(run.Style.FontFamily, run.Style.FontWeight, run.Style.FontStyle)
                .OnSuccessBind(font => Shaper.Shape(text, font, run.Style.FontSize.Points)
                    .OnSuccessMap(glyphs => new LineBoxes.ShapedRun(run, font, glyphs)))
                .MapError(error => error.WithSource(run.Source));
        });
    }

    /// <summary>The text a run shows on the given page.</summary>
    private static string TextOf(InlineRun run, PageContext page) => run switch
    {
        TextRun text => text.Text,
        CounterRun counter => page.Format(counter.Counter),
        _ => throw new InvalidOperationException($"Unknown inline run {run.GetType().Name}; checked before."),
    };

    /// <summary>Breaks the paragraph into lines for the content width and stacks them from
    /// <paramref name="firstLine"/> until the next one does not fit, or up to the early break when one is given. None
    /// when not even that first line fits. The breakpoints are the places between two lines (class B, CSS Fragmentation
    /// 3 §4.1), with <c>orphans</c> and <c>widows</c>: where the next line does not fit, <see cref="BreakWhereFull"/>
    /// decides where the page breaks; the best breakpoint of a complete fragment (<see cref="BestBreakInside"/>) is
    /// reported as its early break.</summary>
    private static Result<Maybe<SlicedBox.Content>, Error> StackLines(
        IReadOnlyList<LineBoxes.ShapedRun> runs, LineBoxes.Strut? strut, BoxStyle style, int firstLine, double contentWidth, double contentHeight, InlineEarlyBreak? breakAt)
    {
        var broken = BreakLines(runs, contentWidth);
        if (broken.IsFailure)
        {
            return Result<Maybe<SlicedBox.Content>, Error>.Failure(broken.Error);
        }

        var lines = broken.Value;
        var placed = ImmutableArray.CreateBuilder<PlacedFragment>();
        var bottom = 0.0;
        for (var index = firstLine; index < lines.Count; index++)
        {
            if (breakAt is { } target && target.LineIndex == index)
            {
                return SlicedBox.Placed(placed.ToImmutable(), bottom, new InlineBreakToken(index), target.Appeal);
            }

            var line = LineBoxes.Build(lines[index], runs, contentWidth, style.TextAlign, isLastLine: index == lines.Count - 1, strut);
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
        return SlicedBox.Placed(placed.ToImmutable(), bottom, null, bestBreak: BestBreakInside(style, firstLine, lines.Count));
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
