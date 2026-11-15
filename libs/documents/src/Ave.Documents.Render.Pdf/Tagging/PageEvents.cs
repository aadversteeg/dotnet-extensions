using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf.Tagging;

/// <summary>
/// One page's display list read as a sequence of events: a structure node opens, content is drawn, a node closes.
/// Content between two markers is one <see cref="ContentRun"/>, so every run belongs to exactly one owner — the
/// innermost open structure node, or an artifact. Both the structure tree and the content stream are built from the
/// same events, so they cannot disagree.
/// </summary>
internal abstract record PageEvent
{
    // Only the cases in this file exist.
    private protected PageEvent()
    {
    }
}

/// <summary>A structure node starts.</summary>
/// <param name="Start">Its marker.</param>
internal sealed record StructureOpened(StructureStart Start) : PageEvent;

/// <summary>The innermost open structure node ends.</summary>
internal sealed record StructureClosed : PageEvent
{
    public static StructureClosed Instance { get; } = new();
}

/// <summary>Consecutive content items with the same owner.</summary>
/// <param name="Items">The items, in painting order (glyph runs, rectangles, images; never markers).</param>
/// <param name="Artifact">The artifact the items belong to, or null when they are content of the innermost open
/// structure node (or of nothing, when no node is open).</param>
internal sealed record ContentRun(ImmutableArray<DrawItem> Items, ArtifactKind? Artifact) : PageEvent;

/// <summary>Reads pages into <see cref="PageEvent"/>s and checks that their markers nest.</summary>
internal static class PageEvents
{
    /// <summary>
    /// Reads one page. The markers must follow the page model's rules (<see cref="StructureStart"/>,
    /// <see cref="ArtifactStart"/>): structure markers nest and every start ends on the same page; artifacts do not
    /// nest and hold no structure markers. A page model from the painter always does; a hand-made one may not.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="number">Its page number, for the error message.</param>
    /// <returns>The events, or <c>Documents.Pdf.Write.InvalidStructure</c>.</returns>
    public static Result<ImmutableArray<PageEvent>, Error> Read(Page page, int number)
    {
        var events = ImmutableArray.CreateBuilder<PageEvent>();
        var content = ImmutableArray.CreateBuilder<DrawItem>();
        var open = new Stack<NodeId>();
        ArtifactKind? artifact = null;

        void Flush()
        {
            if (content.Count > 0)
            {
                events.Add(new ContentRun(content.ToImmutable(), artifact));
                content.Clear();
            }
        }

        foreach (var item in page.Items)
        {
            switch (item)
            {
                case StructureStart or StructureEnd when artifact is not null:
                    return Invalid(number, "a structure marker inside an artifact");
                case StructureStart start:
                    Flush();
                    open.Push(start.Node);
                    events.Add(new StructureOpened(start));
                    break;
                case StructureEnd end:
                    if (open.Count == 0 || open.Peek() != end.Node)
                    {
                        return Invalid(number, $"the end of {end.Node} without its start", end.Node);
                    }

                    Flush();
                    open.Pop();
                    events.Add(StructureClosed.Instance);
                    break;
                case ArtifactStart start:
                    if (artifact is not null)
                    {
                        return Invalid(number, "an artifact inside an artifact");
                    }

                    Flush();
                    artifact = start.Kind;
                    break;
                case ArtifactEnd:
                    if (artifact is null)
                    {
                        return Invalid(number, "the end of an artifact without its start");
                    }

                    Flush();
                    artifact = null;
                    break;
                default:
                    content.Add(item);
                    break;
            }
        }

        Flush();
        return open.Count > 0 ? Invalid(number, $"{open.Peek()} without its end", open.Peek())
            : artifact is not null ? Invalid(number, "an artifact without its end")
            : Result<ImmutableArray<PageEvent>, Error>.Success(events.ToImmutable());
    }

    private static Result<ImmutableArray<PageEvent>, Error> Invalid(int page, string problem, NodeId? node = null)
    {
        var error = new Error(PdfErrorCodes.Write.InvalidStructure, $"Page {page} has {problem}.")
            .With(ErrorMetadataKeys.Value, page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Result<ImmutableArray<PageEvent>, Error>.Failure(node is { } id ? error.With(ErrorMetadataKeys.Node, id.ToString()) : error);
    }
}
