using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The pagination driver: lays the root box out page after page — the root algorithm is called once per page with the
/// previous page's outgoing break token — until no token is left; applies the page template of each page;
/// and repeats the whole layout until the page count that computed content shows is the page count it produces.
/// </summary>
public static class Paginator
{
    /// <summary>The most layout passes the page-count fixpoint makes: five, the cap Typst uses for the same fixpoint
    /// (docs/architecture.md, Background).</summary>
    public const int MaxPasses = 5;

    /// <summary>
    /// Lays out the document on as many pages as it needs.
    /// </summary>
    /// <remarks>
    /// <para><b>Fixpoint.</b> A <see cref="CounterRun"/> for the page count needs the number of pages before the pages
    /// exist, and its digits can change line breaks and so the number of pages. So the whole document is laid out with
    /// an assumed count (first 1), and laid out again with the count that pass produced, until a pass produces the
    /// count it assumed, at most <see cref="MaxPasses"/> times. Such a pass is stable: layout is a pure function of the document and
    /// the assumed count, so the next pass would repeat it exactly. Page numbers need no fixpoint: pages are laid out
    /// in order, so each page knows its own number. A document that shows no page count is laid out once; one that does
    /// settles in one pass when it has one page, otherwise in at least two.</para>
    /// <para>Not settling within <see cref="MaxPasses"/> passes is <c>Documents.Layout.NotConverging</c>, with the
    /// source location of the first page-count reference and the page count of every pass.</para>
    /// </remarks>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <param name="document">The document.</param>
    /// <returns>One fragment per page, or the first layout error.</returns>
    /// <exception cref="InvalidOperationException">An algorithm broke the layout contract (see
    /// <see cref="LayOutPages"/>). Without this check pagination could loop forever.</exception>
    public static Result<ValueList<PageFragment>, Error> Paginate(LayoutEnvironment environment, StyledDocument document)
    {
        var page = document.Page;
        if (!(page.AreaWidth.Points > 0 && page.AreaHeight.Points > 0))
        {
            return Result<ValueList<PageFragment>, Error>.Failure(
                new Error(DocumentErrorCodes.Input.Invalid.PageSize, "The page margins leave no page area for content.")
                    .With(ErrorMetadataKeys.Value, $"{page.Width}x{page.Height} margin={page.Margins}"));
        }

        // Without a page count shown anywhere, no pass can change what an earlier one laid out: one pass is final.
        if (!PageCountReferences(document).Any())
        {
            return LayOutPages(environment, document, pageCount: 1);
        }

        var counts = ImmutableArray.CreateBuilder<int>();
        var assumedCount = 1;
        for (var pass = 1; pass <= MaxPasses; pass++)
        {
            var pages = LayOutPages(environment, document, assumedCount);
            if (pages.IsFailure || pages.Value.Count == assumedCount)
            {
                return pages;
            }

            counts.Add(pages.Value.Count);
            assumedCount = pages.Value.Count;
        }

        return Result<ValueList<PageFragment>, Error>.Failure(LayoutErrors.NotConverging(document, counts.ToImmutable()));
    }

    /// <summary>The page-count references of a document, in the header and footer areas first, then the body: what
    /// makes the fixpoint necessary.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The references, in that order.</returns>
    internal static IEnumerable<CounterRun> PageCountReferences(StyledDocument document) =>
        new[] { document.Page.FirstPage, document.Page.Template }
            .SelectMany(template => new[] { template?.Header, template?.Footer })
            .Select(area => area?.Content)
            .OfType<Box>()
            .Append(document.Root)
            .SelectMany(Descendants)
            .OfType<CounterRun>()
            .Where(run => run.Counter == PageCounter.PageCount);

    private static IEnumerable<Node> Descendants(Node node) =>
        node is Box box ? [box, .. box.Children.SelectMany(Descendants)] : [node];

    /// <summary>
    /// One layout pass: every page with the given page count. Each page is the page style's size. Its page area (the
    /// page minus its margins, CSS Paged Media 3 §3: "The content area of a page box is called the page area") holds, from the top, the template's header area, the body and
    /// the footer area. The areas are laid out first (they may show the page number), and the root box goes in the
    /// height they leave, as the root of the block formatting context, with its own margins inside.
    /// </summary>
    /// <exception cref="InvalidOperationException">An algorithm reported NoRoom at the start of a page, or returned
    /// the token it was given.</exception>
    private static Result<ValueList<PageFragment>, Error> LayOutPages(LayoutEnvironment environment, StyledDocument document, int pageCount)
    {
        var page = document.Page;
        var pageSize = new Size(page.Width.Points, page.Height.Points);
        var (areaWidth, areaHeight) = (page.AreaWidth.Points, page.AreaHeight.Points);
        var pages = ImmutableArray.CreateBuilder<PageFragment>();
        BreakToken? token = null;
        do
        {
            var number = pages.Count + 1;
            var onPage = environment.OnPage(new PageContext(number, pageCount));
            var template = page.TemplateFor(number);
            var areas = PageAreas.LayOut(onPage, template, areaWidth, areaHeight);
            if (areas.IsFailure)
            {
                return Result<ValueList<PageFragment>, Error>.Failure(areas.Error);
            }

            var (header, footer) = areas.Value;
            var bodyHeight = areaHeight - header.Height - footer.Height;
            if (!(bodyHeight > 0))
            {
                return Result<ValueList<PageFragment>, Error>.Failure(LayoutErrors.NoBodyArea(template, number));
            }

            // The root establishes the block formatting context, so its margins never collapse with its
            // children's (CSS 2.1 §8.3.1). Its top margin belongs to its first fragment only (slice, CSS
            // Fragmentation 3 §5.4); on later pages the root continues at the top of the body area. Below its top margin
            // the root does not start the page, so content that does not fit there moves on (ConstraintSpace).
            var top = token is null ? document.Root.Style.Margin.Top.Points : 0;
            var outcome = InFlowChild.Place(onPage, document.Root, areaWidth, top, bodyHeight, isAtPageStart: !(top > 0), token, isPageRoot: true);
            if (outcome.IsFailure)
            {
                return Result<ValueList<PageFragment>, Error>.Failure(outcome.Error);
            }

            if (outcome.Value.HasNoValue)
            {
                // Only the root's own top margin can leave no room for its top border and padding: the root cannot move
                // to another page.
                if (top > 0)
                {
                    return Result<ValueList<PageFragment>, Error>.Failure(LayoutErrors.DoesNotFit(document.Root, bodyHeight - top));
                }

                throw new InvalidOperationException("A layout algorithm returned NoRoom at the start of a page; it must place content or fail.");
            }

            var placement = outcome.Value.Value;
            if (placement.Continuation is not null && placement.Continuation == token)
            {
                throw new InvalidOperationException("A layout algorithm made no progress: it returned the break token it was given.");
            }

            var origin = new Point(page.Margins.Left.Points, page.Margins.Top.Points);
            pages.Add(new PageFragment(
                pageSize,
                Moved(placement.Fragment, origin + new Point(0, header.Height)),
                header.Fragment is { } headerFragment ? Moved(headerFragment, origin) : null,
                footer.Fragment is { } footerFragment ? Moved(footerFragment, origin + new Point(0, areaHeight - footer.Height)) : null));
            token = placement.Continuation;
        }
        while (token is not null);

        return Result<ValueList<PageFragment>, Error>.Success(pages.ToImmutable());
    }

    private static PlacedFragment Moved(PlacedFragment fragment, Point by) => fragment with { Offset = fragment.Offset + by };
}
