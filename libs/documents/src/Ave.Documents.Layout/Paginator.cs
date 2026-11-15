using System;
using System.Collections.Immutable;
using Ave.Documents.Core;
using Ave.Documents.Model;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// The pagination driver: lays the root box out page after page, feeding each page's outgoing break token
/// into the next, until no token is left (research §6.1: "the paginator calls the root algorithm once per page
/// with the previous page's outgoing break token").
/// </summary>
public static class Paginator
{
    /// <summary>
    /// Lays out the document on as many pages as it needs. Every page is the page style's size; the root box
    /// is laid out in the page area (the page minus its margins, CSS Paged Media 3 §3.1), with its own margins
    /// applied like those of any other block.
    /// </summary>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <param name="document">The document.</param>
    /// <returns>One fragment per page, or the first layout error.</returns>
    /// <exception cref="InvalidOperationException">An algorithm broke the layout contract: it reported NoRoom at
    /// the start of a page, or returned the token it was given. Without this check pagination could loop
    /// forever.</exception>
    public static Result<ImmutableArray<PageFragment>, Error> Paginate(LayoutEnvironment environment, DocumentModel document)
    {
        var page = document.Page;
        if (!(page.AreaWidth.Points > 0 && page.AreaHeight.Points > 0))
        {
            return Result<ImmutableArray<PageFragment>, Error>.Failure(
                new Error(DocumentErrorCodes.Input.Invalid.PageSize, "The page margins leave no page area for content.")
                    .With(ErrorMetadataKeys.Value, $"{page.Width}x{page.Height} margin={page.Margins}"));
        }

        var pageSize = new Size(page.Width.Points, page.Height.Points);
        var pageAreaOrigin = new Point(page.Margins.Left.Points, page.Margins.Top.Points);
        var pages = ImmutableArray.CreateBuilder<PageFragment>();
        BreakToken? token = null;
        do
        {
            var outcome = InFlowChild.Place(
                environment, document.Root, page.AreaWidth.Points, top: 0, page.AreaHeight.Points, isAtPageStart: true, token);
            if (outcome.IsFailure)
            {
                return Result<ImmutableArray<PageFragment>, Error>.Failure(outcome.Error);
            }

            if (outcome.Value.HasNoValue)
            {
                throw new InvalidOperationException("A layout algorithm returned NoRoom at the start of a page; it must place content or fail.");
            }

            var placement = outcome.Value.Value;
            if (placement.Continuation is not null && placement.Continuation == token)
            {
                throw new InvalidOperationException("A layout algorithm made no progress: it returned the break token it was given.");
            }

            var root = placement.Fragment with { Offset = placement.Fragment.Offset + pageAreaOrigin };
            pages.Add(new PageFragment(pageSize, root));
            token = placement.Continuation;
        }
        while (token is not null);

        return Result<ImmutableArray<PageFragment>, Error>.Success(pages.ToImmutable());
    }
}
