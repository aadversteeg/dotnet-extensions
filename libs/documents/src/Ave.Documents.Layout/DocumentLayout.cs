using Ave.Documents.Fonts;
using Ave.Documents.Model;
using Ave.Documents.Page;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>The entry point of the layout layer: document model in, page model out.</summary>
public static class DocumentLayout
{
    /// <summary>Lays out a document with the stage-0 algorithms and paints it into the page model.</summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts its text may use.</param>
    /// <returns>The page document, or the layout error.</returns>
    public static Result<PageDocument, Error> Run(DocumentModel document, FontSet fonts) =>
        Run(document, LayoutEnvironment.Create(fonts));

    /// <summary>Lays out a document with the given environment (for front-ends that add algorithms).</summary>
    /// <param name="document">The document.</param>
    /// <param name="environment">The fonts and algorithms.</param>
    /// <returns>The page document, or the layout error.</returns>
    public static Result<PageDocument, Error> Run(DocumentModel document, LayoutEnvironment environment) =>
        Paginator.Paginate(environment, document).OnSuccessMap(pages => PagePainter.Paint(document.Metadata, pages));
}
