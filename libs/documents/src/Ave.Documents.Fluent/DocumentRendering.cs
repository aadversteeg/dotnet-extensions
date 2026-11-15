using System.Collections.Immutable;
using Ave.Documents.Fonts;
using Ave.Documents.Layout;
using Ave.Documents.Render.Pdf;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fluent;

/// <summary>The whole pipeline in one call, for callers that just want the PDF.</summary>
public static class DocumentRendering
{
    /// <summary>
    /// Renders a document to PDF: translate to the document model → lay out and paint the page model → write
    /// the PDF. Each step returns errors as values; the first failing step's error is returned.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="fonts">The fonts the document may use.</param>
    /// <param name="options">The PDF file's creation date and identifier.</param>
    /// <returns>The PDF bytes, or the error with the source location of the call that caused it.</returns>
    public static Result<ImmutableArray<byte>, Error> ToPdf(this Document document, FontSet fonts, PdfRenderOptions options) =>
        DocumentTranslator.ToModel(document)
            .OnSuccessBind(model => DocumentLayout.Run(model, fonts))
            .OnSuccessBind(pages => PdfRenderer.Render(pages, options));
}
