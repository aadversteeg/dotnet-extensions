using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf.Tagging;

namespace Ave.Extensions.Documents.Render.Pdf;

/// <summary>One link annotation: a link area with the role of its link.</summary>
/// <param name="Area">The area.</param>
/// <param name="Link">Its link's role: target and description.</param>
/// <param name="Index">Its index among the document's link annotations, in document order.</param>
internal sealed record LinkAnnotation(LinkArea Area, SemanticRole.LinkRole Link, int Index);

/// <summary>The link annotations of a document (ISO 32000-1 §12.5.6.5), read from the same events as the structure tree,
/// in the same order, so the structure tree's object references and the annotations cannot disagree.</summary>
internal static class LinkAnnotations
{
    /// <summary>Per page, its link annotations in content order. A link area's link is the innermost open structure node,
    /// with a link role, as the page-model rules checked (<see cref="PageModelRules"/>).</summary>
    /// <param name="pages">The events of every page.</param>
    /// <returns>The annotations.</returns>
    public static ImmutableArray<ImmutableArray<LinkAnnotation>> Of(IReadOnlyList<ImmutableArray<PageEvent>> pages)
    {
        var index = 0;
        var result = ImmutableArray.CreateBuilder<ImmutableArray<LinkAnnotation>>(pages.Count);
        foreach (var events in pages)
        {
            var open = new Stack<StructureStart>();
            var annotations = ImmutableArray.CreateBuilder<LinkAnnotation>();
            foreach (var pageEvent in events)
            {
                switch (pageEvent)
                {
                    case StructureOpened opened:
                        open.Push(opened.Start);
                        break;
                    case StructureClosed:
                        open.Pop();
                        break;
                    case LinkAreaRead read:
                        annotations.Add(new LinkAnnotation(read.Area, (SemanticRole.LinkRole)open.Peek().Role, index++));
                        break;
                }
            }

            result.Add(annotations.ToImmutable());
        }

        return result.ToImmutable();
    }

    /// <summary>The place of every anchor (<see cref="Anchor"/>): its page's index and its point. The page-model rules
    /// checked that a node has one anchor and that every link to a node has its node's.</summary>
    /// <param name="document">The page document.</param>
    /// <returns>Per node, the page and the point.</returns>
    public static IReadOnlyDictionary<NodeId, (int Page, Point At)> Places(PageDocument document)
    {
        var places = new Dictionary<NodeId, (int Page, Point At)>();
        for (var index = 0; index < document.Pages.Count; index++)
        {
            foreach (var anchor in document.Pages[index].Items.OfType<Anchor>())
            {
                places.TryAdd(anchor.Node, (index, anchor.At));
            }
        }

        return places;
    }

    /// <summary>
    /// The annotation dictionary (ISO 32000-1 §12.5.2, Table 164, and §12.5.6.5, Table 173): <c>/Subtype /Link</c>;
    /// <c>/Rect</c>, the area in default user space (left, bottom, right, top); <c>/F 4</c>, the Print flag alone
    /// (§12.5.3, Table 165), as PDF/A-3 requires of every annotation (veraPDF rules 6.3.2-1 and 6.3.2-2: the F key
    /// present, Print set, Hidden, Invisible, ToggleNoView and NoView clear); <c>/Border [0 0 0]</c>, no border drawn
    /// around the link (Table 164); <c>/Contents</c>, the link's description, which PDF/UA-1 requires of a link (veraPDF
    /// rules 7.18.5-2 and 7.18.1-2), written always; and <c>/A</c>, a URI action (§12.6.4.7, Table 206) with the URI as
    /// its ASCII bytes. No appearance stream: veraPDF rule 6.3.3-1 exempts a link annotation from having one ("annotations
    /// whose Subtype value is Popup or Link"), and a link draws nothing of its own. In a tagged file <c>/StructParent</c>
    /// is the annotation's key in the parent tree (§14.7.4.4), after the pages' keys.
    /// </summary>
    /// <param name="annotation">The annotation.</param>
    /// <param name="pageHeight">The height of its page, for the change to PDF space (y up).</param>
    /// <param name="structParent">Its parent-tree key, or null in an untagged file.</param>
    /// <param name="destination">For a link inside the document: its target's page object and the anchor's top in PDF
    /// space (y up); null for a link to a URI.</param>
    /// <returns>The dictionary.</returns>
    public static PdfDictionary Dictionary(LinkAnnotation annotation, double pageHeight, int? structParent, (PdfReference Page, double Top)? destination)
    {
        var area = annotation.Area.Area;
        return PdfDictionary.Of(
            ("Type", new PdfName("Annot")),
            ("Subtype", new PdfName("Link")),
            ("Rect", PdfArray.OfNumbers(area.Origin.X, pageHeight - area.Origin.Y - area.Size.Height, area.Origin.X + area.Size.Width, pageHeight - area.Origin.Y)),
            ("F", new PdfInteger(4)),
            ("Border", PdfArray.OfNumbers(0, 0, 0)),
            ("Contents", PdfString.FromText(annotation.Link.Description)),
            ("A", annotation.Link.Target is LinkTarget.UriTarget uri
                ? PdfDictionary.Of(
                    ("Type", new PdfName("Action")),
                    ("S", new PdfName("URI")),
                    ("URI", PdfString.FromText(uri.Value)))
                : null),
            ("Dest", destination is { } place
                ? new PdfArray([place.Page, new PdfName("XYZ"), PdfNull.Instance, PdfNumber.From(place.Top), PdfNull.Instance])
                : null),
            ("StructParent", structParent is { } key ? new PdfInteger(key) : null));
    }
}
