using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>What a <see cref="LayoutAlgorithm"/> returns when it does not fail: a fragment, or "no room here".</summary>
public abstract record LayoutResult
{
    private LayoutResult()
    {
    }

    /// <summary>The box placed (part of) its content on this page.</summary>
    /// <param name="Fragment">The box's fragment on this page.</param>
    /// <param name="Continuation">Where to resume on the next page, or null when the box is complete.</param>
    /// <param name="Appeal">How good the break is that ends this fragment (<see cref="BreakAppeal.Perfect"/> when the
    /// box is complete). A parent compares it with its own earlier breakpoints and may prefer one of those.</param>
    public sealed record Placed(BoxFragment Fragment, BreakToken? Continuation, BreakAppeal Appeal = BreakAppeal.Perfect) : LayoutResult
    {
        /// <summary>For a complete fragment, the best breakpoint inside it (by appeal, then the latest), or null when it
        /// has none: where a parent may ask the box to break instead, by laying it out once more with this early break
        /// (<see cref="ConstraintSpace.BreakAt"/>). Null for a fragment that breaks: the box has already chosen the best
        /// of its own breakpoints there.</summary>
        public EarlyBreak? BestBreak { get; init; }

        /// <summary>For a fragment that breaks, true when the box broke at a breakpoint it chose before the place where it
        /// ran out of room: one of its own early breaks, because a better break existed inside it than at the page end,
        /// or because its bottom padding and border did not fit below its content. Its parent keeps this break and does
        /// not compare it with the parent's own earlier breakpoints, as Chromium's parent lays itself out again to such a
        /// box's early break (<c>kNeedsEarlierBreak</c>, <c>third_party/blink/renderer/core/layout/block_layout_algorithm.cc</c>);
        /// a grandparent compares as usual.</summary>
        public bool KeepsItsBreak { get; internal init; }
    }

    /// <summary>Nothing of the box fits in the space left on this page; the parent moves it to the next page.</summary>
    public sealed record NoRoom : LayoutResult
    {
        /// <summary>The one value.</summary>
        public static NoRoom Instance { get; } = new();
    }

    /// <summary>The successful result "placed this fragment, continue here".</summary>
    /// <param name="fragment">The box's fragment on this page.</param>
    /// <param name="continuation">Where to resume on the next page, or null when the box is complete.</param>
    /// <param name="appeal">How good the break is that ends the fragment.</param>
    /// <param name="bestBreak">For a complete fragment, the best breakpoint inside it (<see cref="Placed.BestBreak"/>).</param>
    /// <returns>A successful <see cref="Placed"/> result.</returns>
    public static Result<LayoutResult, Error> Place(BoxFragment fragment, BreakToken? continuation, BreakAppeal appeal = BreakAppeal.Perfect, EarlyBreak? bestBreak = null) =>
        Place(fragment, continuation, appeal, bestBreak, keepsItsBreak: false);

    /// <summary>The successful result "placed this fragment, continue here", for a box that may keep its break
    /// (<see cref="Placed.KeepsItsBreak"/>).</summary>
    internal static Result<LayoutResult, Error> Place(BoxFragment fragment, BreakToken? continuation, BreakAppeal appeal, EarlyBreak? bestBreak, bool keepsItsBreak) =>
        Result<LayoutResult, Error>.Success(new Placed(fragment, continuation, appeal)
        {
            BestBreak = continuation is null ? bestBreak : null,
            KeepsItsBreak = continuation is not null && keepsItsBreak,
        });

    /// <summary>
    /// The rule every algorithm applies when not even the first piece of a box fits. At the start of a page
    /// (<see cref="ConstraintSpace.IsAtPageStart"/>: nothing at all above the box on the page) the box will not fit on
    /// any later page either, because all pages have the same size: that is <see cref="LayoutErrorCodes.DoesNotFit"/>,
    /// with the box's source location and the room it had. Anywhere else it is <see cref="NoRoom"/>, and the box moves to
    /// the next page, where the margins above it are truncated at the break (CSS Fragmentation 3 §5.2).
    /// </summary>
    /// <param name="box">The box that does not fit.</param>
    /// <param name="space">The space it was given.</param>
    /// <returns>NoRoom, or the DoesNotFit error.</returns>
    public static Result<LayoutResult, Error> NothingFits(Box box, ConstraintSpace space) =>
        space.IsAtPageStart
            ? Result<LayoutResult, Error>.Failure(LayoutErrors.DoesNotFit(box, space.AvailableHeight))
            : Result<LayoutResult, Error>.Success(NoRoom.Instance);
}
