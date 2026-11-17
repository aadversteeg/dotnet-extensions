using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

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
    public sealed record Placed(BoxFragment Fragment, BreakToken? Continuation, BreakAppeal Appeal = BreakAppeal.Perfect) : LayoutResult;

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
    /// <returns>A successful <see cref="Placed"/> result.</returns>
    public static Result<LayoutResult, Error> Place(BoxFragment fragment, BreakToken? continuation, BreakAppeal appeal = BreakAppeal.Perfect) =>
        Result<LayoutResult, Error>.Success(new Placed(fragment, continuation, appeal));

    /// <summary>
    /// The rule every algorithm applies when not even the first piece of a box fits. At the start of a page
    /// the box will not fit on any later page either, because all pages have the same size: that is
    /// <see cref="LayoutErrorCodes.DoesNotFit"/>, with the box's source location and the room it had. Anywhere else it
    /// is <see cref="NoRoom"/>, and the box moves to the next page. LIMIT (NF-1, docs/architecture.md, Known limits): a
    /// box after only leading space (its own top margin, or empty boxes with margins), at the document start or after a
    /// forced page break, counts as at the start of the page as well, so it is refused although it might fit on the next
    /// page, where that space would be gone.
    /// </summary>
    /// <param name="box">The box that does not fit.</param>
    /// <param name="space">The space it was given.</param>
    /// <returns>NoRoom, or the DoesNotFit error.</returns>
    public static Result<LayoutResult, Error> NothingFits(Box box, ConstraintSpace space) =>
        space.IsAtPageStart
            ? Result<LayoutResult, Error>.Failure(LayoutErrors.DoesNotFit(box, space.AvailableHeight))
            : Result<LayoutResult, Error>.Success(NoRoom.Instance);
}
