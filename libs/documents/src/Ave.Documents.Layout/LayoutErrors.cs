using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Layout;

/// <summary>The errors layout reports, each with the node and its source location.</summary>
internal static class LayoutErrors
{
    public static Error DoesNotFit(Box box) =>
        new Error(
                DocumentErrorCodes.Layout.DoesNotFit,
                $"Box {box.Id} ({box.Role}) does not fit on a page, not even on an empty one, and cannot be broken.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .WithSource(box.Source);

    public static Error InvalidContent(Box box, string message) =>
        new Error(DocumentErrorCodes.Layout.InvalidContent, $"Box {box.Id} ({box.Layout}): {message}")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, box.Layout.ToString())
            .WithSource(box.Source);

    /// <summary>The header and footer of a page leave no height for the body: reported at the larger area.</summary>
    public static Error NoBodyArea(PageTemplate template, int pageNumber)
    {
        var area = template.Header?.Content ?? template.Footer!.Content;
        return new Error(
                DocumentErrorCodes.Layout.DoesNotFit,
                $"The header and footer of page {pageNumber} leave no room for the page's content.")
            .With(ErrorMetadataKeys.Node, area.Id.ToString())
            .WithSource(area.Source);
    }

    public static Error ColumnsDoNotFit(Box table, double needed, double available) =>
        new Error(
                DocumentErrorCodes.Layout.ColumnsDoNotFit,
                $"Box {table.Id} needs {InvariantNumber.Format(needed)}pt for its fixed columns and the narrowest content of the others, "
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
        var areas = new[] { document.Page.FirstPage, document.Page.Template }
            .SelectMany(template => new[] { template?.Header, template?.Footer })
            .Select(area => area?.Content)
            .OfType<Box>();
        var reference = areas.Append(document.Root).SelectMany(Descendants).OfType<CounterRun>()
            .FirstOrDefault(run => run.Counter == PageCounter.PageCount);
        var (node, source) = reference is null ? (document.Root.Id, document.Root.Source) : (reference.Id, reference.Source);
        return new Error(
                DocumentErrorCodes.Layout.NotConverging,
                $"The page count did not settle in {Paginator.MaxPasses} layout passes (page counts: {string.Join(", ", counts)}).")
            .With(ErrorMetadataKeys.Node, node.ToString())
            .With(ErrorMetadataKeys.Value, string.Join(",", counts))
            .WithSource(source);
    }

    private static IEnumerable<Node> Descendants(Node node) =>
        node is Box box ? [box, .. box.Children.SelectMany(Descendants)] : [node];
}
