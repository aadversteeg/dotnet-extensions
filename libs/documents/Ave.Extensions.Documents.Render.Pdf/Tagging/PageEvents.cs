using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;

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

/// <summary>A link area (<see cref="PageModel.LinkArea"/>) inside the structure marker of its link, the innermost open
/// node, as the page-model rules checked: one link annotation.</summary>
/// <param name="Area">The area.</param>
internal sealed record LinkAreaRead(LinkArea Area) : PageEvent;

/// <summary>Consecutive content items with the same owner, under the same clips.</summary>
/// <param name="Items">The items, in painting order (glyph runs, rectangles, images; never markers).</param>
/// <param name="Artifact">The artifact the items belong to, or null when they are content of the innermost open
/// structure node (or of nothing, when no node is open).</param>
internal sealed record ContentRun(ImmutableArray<DrawItem> Items, ArtifactKind? Artifact) : PageEvent
{
    /// <summary>The clips open around the items (<see cref="ClipStart"/>), outermost first; empty when none is.</summary>
    public ImmutableArray<Rect> Clips { get; init; } = [];
}

/// <summary>Reads pages into <see cref="PageEvent"/>s.</summary>
internal static class PageEvents
{
    /// <summary>
    /// Reads one page whose markers nest by THE NESTING RULE FOR MARKERS of the page model (<see cref="DrawItem"/>),
    /// which <c>PageModelRules</c> checked before (<c>MarkerNesting</c>, for every back-end). Clips are not events of
    /// their own: each content run carries the clips open around it, so the structure is built as if there were none,
    /// and the content stream opens and closes them between runs.
    /// </summary>
    /// <param name="page">The page, checked.</param>
    /// <returns>The events.</returns>
    /// <exception cref="InvalidOperationException">The page's markers do not nest: the page was not checked.</exception>
    public static ImmutableArray<PageEvent> Read(Page page)
    {
        var events = ImmutableArray.CreateBuilder<PageEvent>();
        var content = ImmutableArray.CreateBuilder<DrawItem>();
        var depth = 0;
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
                case StructureStart or ArtifactStart or ClipStart:
                    Flush();
                    depth++;
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
                    Flush();
                    depth = depth > 0 ? depth - 1 : throw Unchecked();
                    (artifact, clips) = item switch
                    {
                        ArtifactEnd => (null, clips),
                        ClipEnd => (artifact, clips.Length > 0 ? clips.RemoveAt(clips.Length - 1) : throw Unchecked()),
                        _ => (artifact, clips),
                    };
                    if (item is StructureEnd)
                    {
                        events.Add(StructureClosed.Instance);
                    }

                    break;
                case LinkArea link:
                    Flush();
                    events.Add(new LinkAreaRead(link));
                    break;
                case Anchor:
                    // An anchor draws nothing and marks nothing: the writer reads it from the page model for the
                    // destinations (LinkAnnotations.Places).
                    break;
                default:
                    content.Add(item);
                    break;
            }
        }

        Flush();
        return depth == 0 ? events.ToImmutable() : throw Unchecked();
    }

    private static InvalidOperationException Unchecked() =>
        new("The page's markers do not nest; PageModelRules checks them before the PDF back-end reads them.");
}
