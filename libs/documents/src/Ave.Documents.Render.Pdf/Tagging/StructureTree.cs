using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf.Tagging;

/// <summary>
/// The logical structure of a tagged PDF (ISO 32000-1 §14.7), derived from the page model's structure markers: one
/// structure element per document node, the element tree in document order, and the link between each element and
/// its content on the pages (marked-content identifiers, §14.7.4).
/// </summary>
/// <param name="Elements">Every element, in the order its node first opens; an element's index is its position.</param>
/// <param name="TopLevel">The indexes of the elements without a parent: the children of the structure tree root.</param>
/// <param name="ParentTree">Per page, the element of each marked-content identifier on it (index = MCID): the parent
/// tree's array for that page (§14.7.4.4).</param>
/// <param name="Marks">Per page, how each of its <see cref="ContentRun"/>s is marked, in order.</param>
internal sealed record StructureTree(
    ImmutableArray<StructureElement> Elements,
    ImmutableArray<int> TopLevel,
    ImmutableArray<ImmutableArray<int>> ParentTree,
    ImmutableArray<ImmutableArray<MarkedRun>> Marks)
{
    /// <summary>
    /// Builds the tree. Reading order is document order (the order of the markers), which is also the order of the
    /// kids of every element (§14.7.2: "the order of the kids … is the logical order"). A node split over pages is ONE
    /// element: it is reopened on the next page and its content there is added after its earlier content. Every
    /// content run outside an artifact gets the next MCID of its page and becomes a kid of the innermost open element.
    /// </summary>
    /// <param name="pages">The events of every page.</param>
    /// <returns>The tree; <c>Documents.Pdf.Conformance.Tagged.UnmarkedContent</c> for content outside every structure
    /// node and every artifact; <c>Documents.Pdf.Write.InvalidStructure</c> for a node under two parents or a decorative
    /// node in the structure.</returns>
    public static Result<StructureTree, Error> Build(IReadOnlyList<ImmutableArray<PageEvent>> pages)
    {
        var elements = new List<ElementUnderConstruction>();
        var indexOfNode = new Dictionary<NodeId, int>();
        var topLevel = ImmutableArray.CreateBuilder<int>();
        var parentTree = ImmutableArray.CreateBuilder<ImmutableArray<int>>();
        var marks = ImmutableArray.CreateBuilder<ImmutableArray<MarkedRun>>();

        for (var page = 0; page < pages.Count; page++)
        {
            var open = new Stack<int>();
            var owners = ImmutableArray.CreateBuilder<int>();
            var pageMarks = ImmutableArray.CreateBuilder<MarkedRun>();
            foreach (var pageEvent in pages[page])
            {
                switch (pageEvent)
                {
                    case StructureOpened opened:
                        int? parent = open.Count > 0 ? open.Peek() : null;
                        if (indexOfNode.TryGetValue(opened.Start.Node, out var existing))
                        {
                            if (elements[existing].Parent != parent)
                            {
                                return Invalid(opened.Start, page, "appears under two different parents");
                            }

                            open.Push(existing);
                            break;
                        }

                        if (StructureTypes.Of(opened.Start.Role) is not { } type)
                        {
                            return Invalid(opened.Start, page, $"has the role {opened.Start.Role}, which is not document structure");
                        }

                        var index = elements.Count;
                        elements.Add(new ElementUnderConstruction(opened.Start, type, parent, page));
                        indexOfNode[opened.Start.Node] = index;
                        if (parent is { } parentIndex)
                        {
                            elements[parentIndex].Kids.Add(new ElementKid(index));
                        }
                        else
                        {
                            topLevel.Add(index);
                        }

                        open.Push(index);
                        break;
                    case StructureClosed:
                        open.Pop();
                        break;
                    case ContentRun { Artifact: { } kind } run:
                        pageMarks.Add(new MarkedRun(run, new ArtifactContent(kind)));
                        break;
                    case ContentRun when open.Count == 0:
                        return Result<StructureTree, Error>.Failure(
                            new Error(DocumentErrorCodes.Pdf.Conformance.Tagged.UnmarkedContent, $"Page {page + 1} has content outside every structure node and every artifact.")
                                .With(ErrorMetadataKeys.Value, (page + 1).ToString(CultureInfo.InvariantCulture)));
                    case ContentRun tagged:
                        var owner = elements[open.Peek()];
                        owner.Kids.Add(new ContentKid(page, owners.Count));
                        pageMarks.Add(new MarkedRun(tagged, new TaggedContent(owner.Type, owners.Count)));
                        owners.Add(open.Peek());
                        break;
                }
            }

            parentTree.Add(owners.ToImmutable());
            marks.Add(pageMarks.ToImmutable());
        }

        return Result<StructureTree, Error>.Success(new StructureTree(
            [.. elements.Select(element => element.Build())],
            topLevel.ToImmutable(),
            parentTree.ToImmutable(),
            marks.ToImmutable()));
    }

    /// <summary>The marks of untagged output: every run unmarked.</summary>
    /// <param name="pages">The events of every page.</param>
    /// <returns>Per page, <see cref="ContentMark.None"/> for each run.</returns>
    public static ImmutableArray<ImmutableArray<MarkedRun>> Unmarked(IReadOnlyList<ImmutableArray<PageEvent>> pages) =>
        [.. pages.Select(events => events.OfType<ContentRun>().Select(run => new MarkedRun(run, ContentMark.None)).ToImmutableArray())];

    private static Result<StructureTree, Error> Invalid(StructureStart start, int page, string problem) =>
        Result<StructureTree, Error>.Failure(
            new Error(DocumentErrorCodes.Pdf.Write.InvalidStructure, $"Node {start.Node} on page {page + 1} {problem}.")
                .With(ErrorMetadataKeys.Node, start.Node.ToString())
                .With(ErrorMetadataKeys.Value, (page + 1).ToString(CultureInfo.InvariantCulture))
                .WithSource(start.Source));

    /// <summary>An element while the pages are read; its kids grow as its content appears.</summary>
    private sealed record ElementUnderConstruction(StructureStart Start, PdfName Type, int? Parent, int FirstPage)
    {
        public List<StructureKid> Kids { get; } = [];

        public StructureElement Build() => new(Start, Type, Parent, FirstPage, [.. Kids]);
    }
}

/// <summary>One structure element (§14.7.2, Table 323).</summary>
/// <param name="Start">The marker of its node: id, role, source location, span.</param>
/// <param name="Type">Its structure type.</param>
/// <param name="Parent">The index of its parent element, or null for a child of the structure tree root.</param>
/// <param name="FirstPage">The page (0-based) its node first appears on: the element's <c>/Pg</c>.</param>
/// <param name="Kids">Its child elements and content, in reading order.</param>
internal sealed record StructureElement(StructureStart Start, PdfName Type, int? Parent, int FirstPage, ImmutableArray<StructureKid> Kids);

/// <summary>A kid of a structure element.</summary>
internal abstract record StructureKid
{
    // Only the cases in this file exist.
    private protected StructureKid()
    {
    }
}

/// <summary>A child element.</summary>
/// <param name="Index">Its index.</param>
internal sealed record ElementKid(int Index) : StructureKid;

/// <summary>A marked-content sequence on a page (§14.7.4.2).</summary>
/// <param name="Page">The page, 0-based.</param>
/// <param name="Mcid">Its marked-content identifier on that page.</param>
internal sealed record ContentKid(int Page, int Mcid) : StructureKid;
