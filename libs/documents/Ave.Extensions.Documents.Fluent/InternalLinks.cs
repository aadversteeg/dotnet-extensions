using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// The anchors of the body (<see cref="ParagraphElement.Anchor"/>) and the links to them (<see cref="Elements.LinkTo"/>),
/// collected while the body is translated. A link may come before its anchor, so each link is built with a provisional
/// target and gets its node when the whole body is built (<see cref="Resolve"/>, by <see cref="LinkTargets"/>).
/// </summary>
internal sealed class InternalLinks
{
    /// <summary>The provisional target of a link to an anchor; <see cref="Resolve"/> replaces every one or fails.</summary>
    private static readonly LinkTarget Provisional = LinkTarget.Node(new NodeId(-1));

    private readonly Dictionary<string, (NodeId Node, SourceLocation Source)> anchors = new(System.StringComparer.Ordinal);
    private readonly List<(NodeId Link, string Anchor, SourceLocation Source)> links = [];

    /// <summary>The problems of a paragraph's anchor and of its links to anchors: a name of 1 to
    /// <see cref="Elements.MaxAnchorLength"/> characters, and neither in a page area (<paramref name="links"/> null).</summary>
    public static IEnumerable<Error> Problems(ParagraphElement paragraph, InternalLinks? links)
    {
        IEnumerable<(string Name, SourceLocation Source, string Property)> names =
        [
            .. paragraph.AnchorName is { } anchor ? [(anchor, paragraph.Source, "anchor")] : System.Array.Empty<(string, SourceLocation, string)>(),
            .. paragraph.Spans.Where(span => span.LinkAnchor is not null).Select(span => (span.LinkAnchor!, span.Source, "link")),
        ];
        foreach (var (name, source, property) in names)
        {
            if (name.Length is 0 or > Elements.MaxAnchorLength)
            {
                yield return InputChecks.Problem(DocumentErrorCodes.Input.Invalid.Link, $"An anchor's name has 1 to {Elements.MaxAnchorLength} characters; this one has {name.Length}.", name.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), property, source);
            }
            else if (links is null)
            {
                yield return InputChecks.Problem(DocumentErrorCodes.Input.Invalid.Link, $"The {property} '{name}' stands in a page header or footer; anchors and links to them stand only in the body.", name, property, source);
            }
        }
    }

    /// <summary>Records an anchor; the error when another paragraph has the name already.</summary>
    public Error? Anchor(string name, NodeId node, SourceLocation source)
    {
        if (anchors.TryGetValue(name, out var first))
        {
            return InputChecks.Problem(DocumentErrorCodes.Input.Invalid.Link, $"The anchor '{name}' is given twice (first at {first.Source}); a name leads to one place.", name, "anchor", source);
        }

        anchors.Add(name, (node, source));
        return null;
    }

    /// <summary>Records a link to an anchor and returns its provisional target.</summary>
    public LinkTarget Link(NodeId link, string anchor, SourceLocation source)
    {
        links.Add((link, anchor, source));
        return Provisional;
    }

    /// <summary>The body with every link to an anchor leading to the anchor's paragraph; a link to a name no paragraph of
    /// the body has is refused at the link.</summary>
    public Result<Box, Error> Resolve(Box body)
    {
        var missing = links.Where(link => !anchors.ContainsKey(link.Anchor))
            .Select(link => InputChecks.Problem(DocumentErrorCodes.Input.Invalid.Link, $"The link leads to the anchor '{link.Anchor}', which no paragraph or heading of the body has.", link.Anchor, "link", link.Source))
            .ToList();
        if (missing.Count > 0)
        {
            return Result<Box, Error>.Failure(missing.Count == 1 ? missing[0] : Errors.Aggregate(missing));
        }

        return Result<Box, Error>.Success(LinkTargets.Resolve(body, links.ToDictionary(link => link.Link, link => (LinkTarget?)LinkTarget.Node(anchors[link.Anchor].Node))));
    }
}
