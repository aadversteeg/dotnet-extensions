using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Layout;

/// <summary>The errors layout reports, each with the node and its source location. A message names the author's
/// element by its role and the line that made it, not by its node number.</summary>
internal static class LayoutErrors
{
    /// <summary>The element a box was made from, for a message: its role and its source.</summary>
    private static string Describe(Box box) => $"The {box.Role} made at {box.Source}";

    public static Error DoesNotFit(Box box) =>
        new Error(
                LayoutErrorCodes.DoesNotFit,
                $"{Describe(box)} does not fit on a page, not even on an empty one, and cannot be broken.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .WithSource(box.Source);

    public static Error InvalidContent(Box box, string message) =>
        new Error(LayoutErrorCodes.InvalidContent, $"{Describe(box)} cannot be laid out: {message}")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, box.Layout.ToString())
            .WithSource(box.Source);

    /// <summary>The header and footer of a page leave no height for the body: reported at the header when the page
    /// has one, otherwise at the footer.</summary>
    public static Error NoBodyArea(PageTemplate template, int pageNumber)
    {
        var area = template.Header?.Content ?? template.Footer!.Content;
        return new Error(
                LayoutErrorCodes.DoesNotFit,
                $"The header and footer of page {pageNumber} leave no room for the page's content.")
            .With(ErrorMetadataKeys.Node, area.Id.ToString())
            .WithSource(area.Source);
    }

    public static Error ColumnsDoNotFit(Box table, double needed, double available) =>
        new Error(
                LayoutErrorCodes.ColumnsDoNotFit,
                $"{Describe(table)} needs {InvariantNumber.Format(needed)}pt for its fixed columns and the narrowest content of the others, "
                + $"but has {InvariantNumber.Format(available)}pt.")
            .With(ErrorMetadataKeys.Node, table.Id.ToString())
            .With(ErrorMetadataKeys.Value, $"{InvariantNumber.Format(needed)}>{InvariantNumber.Format(available)}")
            .WithSource(table.Source);

    /// <summary>
    /// The page count did not settle. Reported at the first page-count reference (in the header and footer areas
    /// first, then the body), or at the root when there is none — then some other computed content is to blame.
    /// </summary>
    public static Error NotConverging(StyledDocument document, ImmutableArray<int> counts)
    {
        var reference = Paginator.PageCountReferences(document).FirstOrDefault();
        var (node, source) = reference is null ? (document.Root.Id, document.Root.Source) : (reference.Id, reference.Source);
        return new Error(
                LayoutErrorCodes.NotConverging,
                $"The page count did not settle in {Paginator.MaxPasses} layout passes (page counts: {string.Join(", ", counts)}).")
            .With(ErrorMetadataKeys.Node, node.ToString())
            .With(ErrorMetadataKeys.Value, string.Join(",", counts))
            .WithSource(source);
    }

}
