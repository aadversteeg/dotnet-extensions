using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Pdf;

namespace Ave.Extensions.Documents.Render.Pdf.Tagging;

/// <summary>Writes a <see cref="StructureTree"/> as PDF objects: the structure tree root, the parent tree and the elements.</summary>
internal static class StructureObjects
{
    /// <summary>
    /// The objects of the logical structure:
    /// <list type="bullet">
    /// <item>the structure tree root (ISO 32000-1 §14.7.2, Table 322) with the top-level elements as kids, the parent
    /// tree, and <c>/ParentTreeNextKey</c> one past the last key used;</item>
    /// <item>the parent tree (§14.7.4.4): a number tree whose key for each page is the page's <c>/StructParents</c> value
    /// (its index) and whose value is an array of the element owning each MCID on that page, so a reader can go from
    /// content back to its element; and after the pages' keys one key per link annotation, its <c>/StructParent</c> value
    /// (page count + its index), whose value is its link's element itself, not an array: an annotation is a content item
    /// of its own, with one parent element;</item>
    /// <item>one structure element per node (Table 323): <c>/S</c> its type, <c>/P</c> its parent, <c>/Pg</c> its first
    /// page, <c>/K</c> its kids — child elements by reference, its content on its first page as plain MCIDs, and on
    /// other pages as marked-content references with their own <c>/Pg</c> (§14.7.4.2, Table 324) — plus <c>/Alt</c> for a
    /// figure (§14.9.3) and <c>/A</c>, the attributes of table cells and the bounding box of figures and tables
    /// (<see cref="StructureTypes.Attributes"/>); a link's element holds an object reference to each of its annotations
    /// (<c>/Type /OBJR /Obj … /Pg …</c>, §14.7.4.3, Table 325), as a <c>Link</c> element holds its link annotations by object
    /// references beside its content (§14.8.4.4.2), which veraPDF rule 7.18.5-1 checks for PDF/UA-1.</item>
    /// </list>
    /// </summary>
    /// <param name="tree">The tree.</param>
    /// <param name="bounds">Per element, its bounding box or null (<see cref="ElementBounds"/>).</param>
    /// <param name="plan">The object numbers.</param>
    /// <returns>The objects.</returns>
    public static ImmutableArray<IndirectObject> Build(StructureTree tree, ImmutableArray<UserSpaceBox?> bounds, ObjectPlan plan) =>
    [
        new IndirectValue(plan.StructureTreeRoot, PdfDictionary.Of(
            ("Type", new PdfName("StructTreeRoot")),
            ("K", new PdfArray([.. tree.TopLevel.Select(index => new PdfReference(plan.StructureElement(index)))])),
            ("ParentTree", new PdfReference(plan.ParentTree)),
            ("ParentTreeNextKey", new PdfInteger(tree.ParentTree.Length + tree.AnnotationOwners.Length)))),
        new IndirectValue(plan.ParentTree, PdfTrees.NumberTree([
            .. tree.ParentTree.Select((owners, page) =>
                (page, (PdfObject)new PdfArray([.. owners.Select(index => new PdfReference(plan.StructureElement(index)))]))),
            .. tree.AnnotationOwners.Select((owner, annotation) =>
                (tree.ParentTree.Length + annotation, (PdfObject)new PdfReference(plan.StructureElement(owner)))),
        ])),
        .. tree.Elements.Select((element, index) => new IndirectValue(plan.StructureElement(index), Element(element, bounds[index], plan))),
    ];

    private static PdfDictionary Element(StructureElement element, UserSpaceBox? box, ObjectPlan plan) => PdfDictionary.Of(
        ("Type", new PdfName("StructElem")),
        ("S", element.Type),
        ("P", new PdfReference(element.Parent is { } parent ? plan.StructureElement(parent) : plan.StructureTreeRoot)),
        ("Pg", new PdfReference(plan.Page(element.FirstPage).Page)),
        ("K", new PdfArray([.. element.Kids.Select(kid => Kid(kid, element.FirstPage, plan))])),
        ("Alt", element.Start.Role is SemanticRole.FigureRole { AlternativeText: { } text } ? PdfString.FromText(text) : null),
        ("A", StructureTypes.Attributes(element.Start.Role, element.Start.Span, box)));

    private static PdfObject Kid(StructureKid kid, int elementPage, ObjectPlan plan) => kid switch
    {
        ElementKid child => new PdfReference(plan.StructureElement(child.Index)),
        ContentKid content when content.Page == elementPage => new PdfInteger(content.Mcid),
        AnnotationKid annotation => PdfDictionary.Of(
            ("Type", new PdfName("OBJR")),
            ("Pg", new PdfReference(plan.Page(annotation.Page).Page)),
            ("Obj", new PdfReference(plan.Annotation(annotation.Annotation)))),
        ContentKid content => PdfDictionary.Of(
            ("Type", new PdfName("MCR")),
            ("Pg", new PdfReference(plan.Page(content.Page).Page)),
            ("MCID", new PdfInteger(content.Mcid))),
        _ => throw new System.InvalidOperationException($"Unknown structure kid {kid.GetType().Name}."),
    };
}
