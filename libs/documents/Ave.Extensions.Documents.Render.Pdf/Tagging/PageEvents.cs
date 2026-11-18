using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Render.Pdf.Tagging;

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

/// <summary>Consecutive content items with the same owner, under the same clips.</summary>
/// <param name="Items">The items, in painting order (glyph runs, rectangles, images; never markers).</param>
/// <param name="Artifact">The artifact the items belong to, or null when they are content of the innermost open
/// structure node (or of nothing, when no node is open).</param>
internal sealed record ContentRun(ImmutableArray<DrawItem> Items, ArtifactKind? Artifact) : PageEvent
{
    /// <summary>The clips open around the items (<see cref="ClipStart"/>), outermost first; empty when none is.</summary>
    public ImmutableArray<Rect> Clips { get; init; } = [];
}

/// <summary>Reads pages into <see cref="PageEvent"/>s and checks that their markers nest.</summary>
internal static class PageEvents
{
    /// <summary>
    /// The most clips open one inside the other: 27. Every clip is one <c>q</c> in the content stream
    /// (<see cref="PageContentStream"/>), and every drawing inside it adds one more <c>q</c> of its own (a rectangle, an
    /// image and a glyph run each draw inside one <c>q</c>…<c>Q</c>, never deeper), so 27 clips make 28 levels of
    /// <c>q</c>: the most ISO 32000-1 Annex C (Table C.1, "q/Q nesting depth") names as a limit of every PDF reader, and
    /// what ISO 19005-3 §6.1.13 allows a PDF/A file ("shall not nest q/Q pairs by more than 28 nesting levels"). The same
    /// for every profile. Deeper nesting is <c>Documents.Pdf.Write.InvalidStructure</c>; the layout never opens more
    /// than one clip.
    /// </summary>
    public const int MaxClipDepth = 27;

    /// <summary>
    /// Reads one page, and checks THE NESTING RULE FOR MARKERS of the page model (<see cref="DrawItem"/>): one stack of
    /// open structure, artifact and clip markers, where every end closes the innermost open marker, which must be a start
    /// of its kind (for a structure end, of its node); nothing is left open at the end of the page; no artifact or
    /// structure marker opens inside an artifact; at most <see cref="MaxClipDepth"/> clips are open at once. A page model
    /// from the painter always nests; a hand-made one may not.
    /// Clips are not events of their own: each content run carries the clips open around it, so the structure is built
    /// as if there were none, and the content stream opens and closes them between runs.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="number">Its page number, for the error message.</param>
    /// <returns>The events, or <c>Documents.Pdf.Write.InvalidStructure</c>.</returns>
    public static Result<ImmutableArray<PageEvent>, Error> Read(Page page, int number)
    {
        var events = ImmutableArray.CreateBuilder<PageEvent>();
        var content = ImmutableArray.CreateBuilder<DrawItem>();
        var open = new Stack<DrawItem>();
        ArtifactKind? artifact = null;
        var clips = ImmutableArray<Rect>.Empty;

        void Flush()
        {
            if (content.Count > 0)
            {
                events.Add(new ContentRun(content.ToImmutable(), artifact) { Clips = clips });
                content.Clear();
            }
        }

        foreach (var item in page.Items)
        {
            switch (item)
            {
                case StructureStart or StructureEnd or ArtifactStart when artifact is not null:
                    return Invalid(number, item is ArtifactStart ? "an artifact inside an artifact" : "a structure marker inside an artifact");
                case ClipStart when clips.Length >= MaxClipDepth:
                    return Invalid(number, $"more than {MaxClipDepth} clips open one inside the other");
                case StructureStart or ArtifactStart or ClipStart:
                    Flush();
                    open.Push(item);
                    (artifact, clips) = item switch
                    {
                        ArtifactStart start => (start.Kind, clips),
                        ClipStart clip => (artifact, clips.Add(clip.Area)),
                        _ => (artifact, clips),
                    };
                    if (item is StructureStart opened)
                    {
                        events.Add(new StructureOpened(opened));
                    }

                    break;
                case StructureEnd or ArtifactEnd or ClipEnd:
                    if (EndProblem(item, open.Count > 0 ? open.Peek() : null) is { } problem)
                    {
                        return Invalid(number, problem, (item as StructureEnd)?.Node);
                    }

                    Flush();
                    open.Pop();
                    (artifact, clips) = item switch
                    {
                        ArtifactEnd => (null, clips),
                        ClipEnd => (artifact, clips.RemoveAt(clips.Length - 1)),
                        _ => (artifact, clips),
                    };
                    if (item is StructureEnd)
                    {
                        events.Add(StructureClosed.Instance);
                    }

                    break;
                default:
                    content.Add(item);
                    break;
            }
        }

        Flush();
        return open.Count == 0 ? Result<ImmutableArray<PageEvent>, Error>.Success(events.ToImmutable())
            : open.Peek() switch
            {
                StructureStart start => Invalid(number, $"{start.Node} without its end", start.Node),
                ArtifactStart => Invalid(number, "an artifact without its end"),
                _ => Invalid(number, "a clip without its end"),
            };
    }

    /// <summary>Why an end marker does not close the innermost open marker, or null when it does.</summary>
    private static string? EndProblem(DrawItem end, DrawItem? innermost) => (end, innermost) switch
    {
        (StructureEnd closing, StructureStart start) when start.Node == closing.Node => null,
        (ArtifactEnd, ArtifactStart) or (ClipEnd, ClipStart) => null,
        (StructureEnd closing, _) => $"the end of {closing.Node} without its start" + Inside(innermost),
        (ArtifactEnd, _) => "the end of an artifact without its start" + Inside(innermost),
        _ => "the end of a clip without its start" + Inside(innermost),
    };

    private static string Inside(DrawItem? innermost) => innermost switch
    {
        null => string.Empty,
        StructureStart start => $" (inside {start.Node})",
        ArtifactStart => " (inside an artifact)",
        _ => " (inside a clip)",
    };

    private static Result<ImmutableArray<PageEvent>, Error> Invalid(int page, string problem, NodeId? node = null)
    {
        var error = new Error(PdfRenderErrorCodes.Write.InvalidStructure, $"Page {page} has {problem}.")
            .With(ErrorMetadataKeys.Value, page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Result<ImmutableArray<PageEvent>, Error>.Failure(node is { } id ? error.With(ErrorMetadataKeys.Node, id.ToString()) : error);
    }
}
