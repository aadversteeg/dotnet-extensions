using System;
using System.Collections.Generic;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents;

/// <summary>The errors of the back-ends as the caller of an entry reads them: with the document's own places.</summary>
internal static class RenderErrors
{
    /// <summary>The longest image name a message quotes.</summary>
    private const int MaxNameLength = 80;

    /// <summary>A raster refusal of an image (<see cref="ErrorMetadataKeys.Image"/>) with the image's name and place in
    /// the document, its source and node; every other error as it is.</summary>
    /// <param name="error">The error of the raster back-end.</param>
    /// <param name="document">The document the pages were laid out from.</param>
    public static Error ImagePlace(Error error, StyledDocument document)
    {
        if (error.Metadata is not { } metadata || !metadata.TryGetValue(ErrorMetadataKeys.Image, out var digest) || Find(document, digest as string) is not { } image)
        {
            return error;
        }

        var name = image.Name is { } url ? $" '{Shortened(url)}'" : string.Empty;
        var place = image.Source.Column > 0
            ? $"{image.Source}:{image.Source.Column.ToString(CultureInfo.InvariantCulture)}"
            : image.Source.ToString();
        const string start = "The image (";
        var message = error.Message.StartsWith(start, StringComparison.Ordinal)
            ? $"The image{name} at {place} ({error.Message[start.Length..]}"
            : error.Message;
        return Renamed(error, message, null).With(ErrorMetadataKeys.Node, image.Id.ToString()).WithSource(image.Source);
    }

    /// <summary>The image node of the document whose image has the digest, or null; the body and the page areas are
    /// walked with an explicit stack.</summary>
    private static ImageNode? Find(StyledDocument document, string? digest)
    {
        if (digest is null)
        {
            return null;
        }

        var pending = new Stack<Node>();
        pending.Push(document.Root);
        foreach (var template in new[] { document.Page.Template, document.Page.FirstPage })
        {
            foreach (var area in new[] { template?.Header, template?.Footer })
            {
                if (area is not null)
                {
                    pending.Push(area.Content);
                }
            }
        }

        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case ImageNode image when image.Image.Id.Digest == digest:
                    return image;
                case Box box:
                    foreach (var child in box.Children)
                    {
                        pending.Push(child);
                    }

                    break;
                case InlineGroup group:
                    foreach (var child in group.Children)
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        return null;
    }

    private static string Shortened(string text) => text.Length <= MaxNameLength ? text : string.Concat(text.AsSpan(0, MaxNameLength - 1), "…");

    private static Error Renamed(Error error, string message, string? property)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in error.Metadata ?? new Dictionary<string, object>())
        {
            metadata[key] = value;
        }

        if (property is not null)
        {
            metadata[ErrorMetadataKeys.Property] = property;
        }

        return new Error(error.Code, message, metadata, error.Inner);
    }
}
