using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>The entry point of the layout layer: document model in, page model out.</summary>
public static class DocumentLayout
{
    /// <summary>Lays out a document with the engine's layout algorithms and paints it into the page model.</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts its text may use.</param>
    /// <returns>The page document, or the layout error.</returns>
    public static Result<PageDocument, Error> Run(StyledDocument document, FontSet fonts) =>
        Run(document, LayoutEnvironment.Create(fonts));

    /// <summary>Lays out a document with the given environment (for front-ends that add algorithms).</summary>
    /// <param name="document">The document.</param>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <returns>The page document, or the layout error; <c>Documents.Layout.InvalidOptions</c> for a
    /// <see cref="LayoutEnvironment.MaxPages"/> below 1, before any layout.</returns>
    public static Result<PageDocument, Error> Run(StyledDocument document, LayoutEnvironment environment) =>
        environment.MaxPages < 1 ? Result<PageDocument, Error>.Failure(LayoutErrors.InvalidMaxPages(environment.MaxPages))
        : Paginator.Paginate(environment, document)
            .OnSuccessBind(pages => ListMarkers.OutsideSheet(pages) is { } outside ? Result<ValueList<PageFragment>, Error>.Failure(outside) : Result<ValueList<PageFragment>, Error>.Success(pages))
            .OnSuccessBind(pages => RoundedBox.CutBorders(pages) is { } cut ? Result<ValueList<PageFragment>, Error>.Failure(cut) : Result<ValueList<PageFragment>, Error>.Success(pages))
            .OnSuccessBind(pages => DashedBorder.Check(pages) is { } dashed ? Result<ValueList<PageFragment>, Error>.Failure(dashed) : Result<ValueList<PageFragment>, Error>.Success(pages))
            .OnSuccessMap(pages => PagePainter.Paint(document.Metadata, pages));
}
