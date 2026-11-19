using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Lays out the header and footer areas of one page (<see cref="PageTemplate"/>). Each is laid out like a CSS
/// page-margin box (CSS Paged Media 3 §5: content laid out in a box of its own on every page), as the root of its own
/// block formatting context (the engine's choice; the section does not say), as wide as the page area, with the same
/// algorithms as the body — so it may hold tables, images and page counters. It never breaks: an area that does not
/// fit on its page is <c>Documents.Layout.DoesNotFit</c>.
/// </summary>
internal static class PageAreas
{
    /// <summary>One laid-out area.</summary>
    /// <param name="Fragment">Its fragment, positioned relative to the area's top-left corner; null for no area.</param>
    /// <param name="Height">The height it takes from the page area: its fixed height, or its content's height
    /// including the content's margins.</param>
    public sealed record Area(PlacedFragment? Fragment, double Height)
    {
        /// <summary>No area: nothing drawn, no height taken.</summary>
        public static Area None { get; } = new(null, 0);
    }

    /// <summary>Lays out the template's header and footer for the page the environment names.</summary>
    /// <param name="environment">The environment of this page.</param>
    /// <param name="template">The page's template.</param>
    /// <param name="width">The page area's width.</param>
    /// <param name="height">The page area's height.</param>
    /// <returns>Header and footer, or the first error.</returns>
    public static Result<(Area Header, Area Footer), Error> LayOut(LayoutEnvironment environment, PageTemplate template, double width, double height) =>
        LayOut(environment, template.Header, width, height).OnSuccessBind(header =>
            LayOut(environment, template.Footer, width, height).OnSuccessMap(footer => (header, footer)));

    /// <summary>
    /// One area: its content is placed with its top margin inside the area (a new formatting context keeps its
    /// margins, CSS 2.1 §8.3.1) and may use the whole page area, or the fixed height when one is set.
    /// </summary>
    private static Result<Area, Error> LayOut(LayoutEnvironment environment, PageArea? area, double width, double pageAreaHeight)
    {
        if (area is null)
        {
            return Result<Area, Error>.Success(Area.None);
        }

        var content = area.Content;
        var available = area.Height?.Points ?? pageAreaHeight;
        var margin = content.Style.Margin;
        return InFlowChild.Place(environment, content, width, margin.Top.Points, available, available, isAtPageStart: true, resume: null, isPageRoot: true)
            .OnSuccessBind(placement =>
            {
                // At the start of an empty area NoRoom cannot happen (the contract turns it into DoesNotFit); a
                // continuation means the content is taller than the area, and an area never continues on a next page.
                if (placement.HasNoValue || placement.Value.Continuation is not null)
                {
                    return Result<Area, Error>.Failure(LayoutErrors.AreaDoesNotFit(content, available));
                }

                // The content was laid out in the area's height after its top margin, so it fits there; what can still
                // pass a fixed height is the bottom margin below it.
                var fragment = placement.Value.Fragment;
                var used = margin.Top.Points + fragment.Fragment.Size.Height + margin.Bottom.Points;
                return area.Height is { } fixedHeight && !Fitting.Fits(used, fixedHeight.Points)
                    ? Result<Area, Error>.Failure(LayoutErrors.AreaDoesNotFit(content, fixedHeight.Points))
                    : Result<Area, Error>.Success(new Area(fragment, area.Height?.Points ?? used));
            });
    }
}
