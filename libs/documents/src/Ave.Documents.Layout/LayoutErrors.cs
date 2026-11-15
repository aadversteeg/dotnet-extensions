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
}
