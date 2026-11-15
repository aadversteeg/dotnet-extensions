using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

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
    /// breakpoints, CSS Fragmentation 3 §4.1). <c>orphans</c>/<c>widows</c> (CSS initial value 2) are not applied
    /// yet, so a page may hold a single line of a paragraph.
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

        return ShapeRuns(environment, box).OnSuccessBind(runs =>
            SlicedBox.Layout(box, space, incoming, (width, height) => StackLines(runs, box.Style.TextAlign, firstLine, width, height)));
    }

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
        ShapeRuns(environment, box).OnSuccessMap(runs =>
        {
            ValueList<ValueList<ShapedGlyph>> glyphs = [.. runs.Select(run => run.Glyphs)];
            double Widest(double width) => GreedyLineBreaker.BreakLines(glyphs, width).Select(line => line.Width).DefaultIfEmpty(0).Max();
            return new IntrinsicWidths(Widest(0), Widest(double.PositiveInfinity)).Plus(SlicedBox.HorizontalInsets(box.Style));
        });

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
    /// <paramref name="firstLine"/> until the next one does not fit. None when not even that first line fits.</summary>
    private static Result<Maybe<SlicedBox.Content>, Error> StackLines(
        IReadOnlyList<LineBoxes.ShapedRun> runs, TextAlign align, int firstLine, double contentWidth, double contentHeight)
    {
        var lines = GreedyLineBreaker.BreakLines([.. runs.Select(run => run.Glyphs)], contentWidth);
        var placed = ImmutableArray.CreateBuilder<PlacedFragment>();
        var bottom = 0.0;
        for (var index = firstLine; index < lines.Count; index++)
        {
            var line = LineBoxes.Build(lines[index], runs, contentWidth, align, isLastLine: index == lines.Count - 1);
            if (!Fitting.Fits(bottom + line.Size.Height, contentHeight))
            {
                return placed.Count == 0
                    ? SlicedBox.NothingFits
                    : SlicedBox.Placed(placed.ToImmutable(), bottom, new InlineBreakToken(index));
            }

            placed.Add(new PlacedFragment(new Point(0, bottom), line));
            bottom += line.Size.Height;
        }

        return SlicedBox.Placed(placed.ToImmutable(), bottom, null);
    }
}
