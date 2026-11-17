using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Layout;

/// <summary>The errors layout reports, each with the node and its source location. A message names the author's
/// element by its role and the line that made it, not by its node number.</summary>
internal static class LayoutErrors
{
    /// <summary>The element a box was made from, for a message: its role and its source.</summary>
    private static string Describe(Box box) => $"The {box.Role} made at {box.Source}";

    /// <summary>
    /// A box of which not even the first piece fits, where nothing before it on the page can move to a later page
    /// (<see cref="ConstraintSpace.IsAtPageStart"/>). The message says what does not fit and how much room it had. It
    /// does not claim the box would not fit on an empty page: LIMIT (NF-1, docs/architecture.md, Known limits) — after
    /// only leading space (its own top margin, or empty boxes with margins) at the document start or after a forced page
    /// break the box counts as at the start of the page too, and it might fit on the next one.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="available">The height it had, from the top of its border box (zero or less: none).</param>
    public static Error DoesNotFit(Box box, double available) =>
        new Error(
                LayoutErrorCodes.DoesNotFit,
                $"{Describe(box)} does not fit in {Room(available)} and cannot be broken; nothing before it on the page can move to a later page.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(available))
            .WithSource(box.Source);

    /// <summary>A header or footer area's content that does not fit in the area: an area never breaks.</summary>
    /// <param name="content">The area's content.</param>
    /// <param name="height">The area's height: its fixed height, or the page area's.</param>
    public static Error AreaDoesNotFit(Box content, double height) =>
        new Error(
                LayoutErrorCodes.DoesNotFit,
                $"{Describe(content)} does not fit in the {InvariantNumber.Format(height)}pt of its header or footer area, and an area never breaks.")
            .With(ErrorMetadataKeys.Node, content.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(height))
            .WithSource(content.Source);

    private static string Room(double available) =>
        available > 0 ? $"the {InvariantNumber.Format(available)}pt left for it on the page" : "the page, which has no room left for it,";

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
