using System;
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
/// The inline formatting context: sets a box's text runs in lines ("boxes are laid out horizontally, one after
/// the other, beginning at the top of a containing block", CSS 2.1 §9.4.2), and splits the paragraph between
/// lines when the page is full.
/// </summary>
public static class InlineLayout
{
    /// <summary>The Inline algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <remarks>
    /// Steps: collapse white space (CSS Text 3 §4.1.1) → resolve each run's font and shape it → break into lines
    /// (greedy, at spaces) → stack line boxes from the line the token names until the next one does not fit.
    /// A page break may fall between any two lines (class B breakpoints, CSS Fragmentation 3 §4.1). Stage 0 does
    /// not apply <c>orphans</c>/<c>widows</c> (CSS initial value 2), so a page may hold a single line of a
    /// paragraph; that arrives with break appeal in the pagination stage.
    /// The lines are recomputed on every call rather than carried in the token: the token stays a plain number,
    /// and the result depends only on the arguments.
    /// </remarks>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <param name="box">A box whose children are all text runs.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">An <see cref="InlineBreakToken"/>, or null to start.</param>
    /// <returns>The fragment and continuation, NoRoom, or an error (missing font or glyph, with the run's location).</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming)
    {
        if (box.Children.Any(child => child is not TextRun))
        {
            return Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidContent(box, "inline layout needs text-run children; boxes belong in a flow box."));
        }

        var firstLine = incoming switch
        {
            null => 0,
            InlineBreakToken token => token.LineIndex,
            _ => throw new ArgumentException($"Inline received a {incoming.GetType().Name} it did not produce.", nameof(incoming)),
        };

        return ShapeRuns(environment, [.. box.Children.Cast<TextRun>()]).OnSuccessBind(runs =>
            SlicedBox.Layout(box, space, incoming, (width, height) => StackLines(runs, box.Style.TextAlign, firstLine, width, height)));
    }

    /// <summary>
    /// True when the box's text makes at least one line box: some text is left after white-space collapsing.
    /// A paragraph of only spaces has no line boxes (CSS 2.1 §9.4.2), which makes it an empty box for margin
    /// collapsing (<see cref="MarginCollapsing.IsEmpty"/>).
    /// </summary>
    /// <param name="box">An inline box.</param>
    /// <returns>Whether it has line boxes.</returns>
    public static bool HasLineBoxes(Box box) =>
        !box.Children.OfType<TextRun>().All(run => WhiteSpaceCollapsing.IsAllCollapsible(run.Text));

    /// <summary>
    /// Collapses white space over the whole paragraph, then resolves and shapes every run. Errors from all runs
    /// are reported together, each with the location of the run that caused it.
    /// </summary>
    private static Result<ImmutableArray<LineBoxes.ShapedRun>, Error> ShapeRuns(LayoutEnvironment environment, ImmutableArray<TextRun> runs)
    {
        var collapsed = WhiteSpaceCollapsing.Collapse([.. runs.Select(run => run.Text)]);
        return runs.Zip(collapsed).TraverseAll(pair =>
        {
            var (run, text) = pair;
            return environment.Fonts.Find(run.Style.FontFamily, run.Style.FontWeight)
                .OnSuccessBind(font => Shaper.Shape(text, font, run.Style.FontSize.Points)
                    .OnSuccessMap(glyphs => new LineBoxes.ShapedRun(run, font, glyphs)))
                .MapError(error => error.WithSource(run.Source));
        });
    }

    /// <summary>Breaks the paragraph into lines for the content width and stacks them from
    /// <paramref name="firstLine"/> until the next one does not fit. None when not even that first line fits.</summary>
    private static Result<Maybe<SlicedBox.Content>, Error> StackLines(
        ImmutableArray<LineBoxes.ShapedRun> runs, TextAlign align, int firstLine, double contentWidth, double contentHeight)
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
