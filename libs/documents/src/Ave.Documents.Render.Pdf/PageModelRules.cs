using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The contract a page model must meet before the back-end writes it, whatever the profile. The back-end is a public
/// library and other producers than this engine's layout may hand it a page model, so it checks what it is given and
/// answers with an error value instead of throwing, or writing a file no reader can open. What a profile requires on
/// top is checked by <see cref="ConformanceRules"/>; how the structure markers nest, by the tagging code.
/// </summary>
internal static class PageModelRules
{
    /// <summary>
    /// Checks the page model and reports every breach together (<c>Documents.Pdf.Write.InvalidPageModel</c>):
    /// <list type="bullet">
    /// <item>at least one page: the page tree of a PDF holds the pages of the document (ISO 32000-1 §7.7.3.2), and a
    /// file without one cannot be read back;</item>
    /// <item>no missing page or item;</item>
    /// <item>every glyph is a glyph of its font (below maxp.numGlyphs), since the font program is embedded from those
    /// glyphs;</item>
    /// <item>a table cell spans at least one column and one row;</item>
    /// <item>no two attachments with one file name: the embedded-files name tree has one entry per name (ISO 32000-1
    /// §7.9.6).</item>
    /// </list>
    /// </summary>
    /// <param name="document">The page document.</param>
    /// <returns>The document unchanged, or the breaches.</returns>
    public static Result<PageDocument, Error> Check(PageDocument document)
    {
        var problems = Problems(document).ToList();
        return problems.Count == 0
            ? Result<PageDocument, Error>.Success(document)
            : Result<PageDocument, Error>.Failure(Errors.Aggregate(problems));
    }

    private static IEnumerable<Error> Problems(PageDocument document)
    {
        if (document.Pages.Count == 0)
        {
            yield return Invalid("The document has no pages; a PDF needs at least one (ISO 32000-1 §7.7.3.2).", "0 pages");
        }

        for (var index = 0; index < document.Pages.Count; index++)
        {
            foreach (var problem in PageProblems(document.Pages[index], index + 1))
            {
                yield return problem;
            }
        }

        foreach (var duplicate in document.Metadata.Attachments.GroupBy(attachment => attachment.FileName).Where(group => group.Count() > 1))
        {
            yield return Invalid($"Two attachments have the file name '{duplicate.Key}'; a name can be embedded once (ISO 32000-1 §7.9.6).", duplicate.Key)
                .WithSource(duplicate.Last().Source);
        }
    }

    private static IEnumerable<Error> PageProblems(Page? page, int number)
    {
        if (page is null)
        {
            yield return Invalid($"Page {number} is missing.", Number(number));
            yield break;
        }

        for (var index = 0; index < page.Items.Count; index++)
        {
            Error? problem = page.Items[index] switch
            {
                null => Invalid($"Item {index + 1} of page {number} is missing.", Number(number)),
                GlyphRun run when run.Glyphs.FirstOrDefault(glyph => glyph.Glyph.Value >= run.Font.GlyphCount) is { } glyph =>
                    Invalid($"Page {number} draws glyph {glyph.Glyph.Value} of font '{run.Font.Names.PostScriptName}', which has {run.Font.GlyphCount} glyphs.", glyph.Glyph.Value.ToString(CultureInfo.InvariantCulture)),
                StructureStart start when start.Span.Columns < 1 || start.Span.Rows < 1 =>
                    Invalid($"Node {start.Node} on page {number} spans {start.Span}; a cell spans at least one column and one row.", start.Span.ToString()).WithSource(start.Source),
                _ => null,
            };
            if (problem is { } found)
            {
                yield return found;
            }
        }
    }

    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static Error Invalid(string message, string value) =>
        new Error(PdfRenderErrorCodes.Write.InvalidPageModel, message).With(ErrorMetadataKeys.Value, value);
}
