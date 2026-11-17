using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;

namespace Ave.Extensions.Documents.Render.Pdf.Tagging;

/// <summary>A content run of a page together with how it is marked: the two are made together, by the structure tree
/// (or <see cref="StructureTree.Unmarked"/>), so the content stream can never pair a run with another run's mark.</summary>
/// <param name="Run">The run.</param>
/// <param name="Mark">Its mark.</param>
internal sealed record MarkedRun(ContentRun Run, ContentMark Mark);

/// <summary>
/// How one <see cref="ContentRun"/> is marked in the content stream: not at all (untagged PDF), as content of a
/// structure element, or as an artifact. Every marked run is one marked-content sequence of its own, so tagged
/// content and artifacts never nest inside each other (veraPDF rules 7.1-1 and 7.1-2).
/// </summary>
internal abstract record ContentMark
{
    // Only the cases in this file exist.
    private protected ContentMark()
    {
    }

    /// <summary>No marking: the file is not tagged.</summary>
    public static ContentMark None { get; } = new UnmarkedContent();

    /// <summary>
    /// The marked-content sequence around the run's operators, or nothing for <see cref="None"/>:
    /// <c>/P &lt;&lt;/MCID 3&gt;&gt; BDC … EMC</c> for content, whose tag is the structure type and whose MCID links it
    /// to its structure element (ISO 32000-1 §14.7.4.2); <c>/Artifact &lt;&lt;/Type /Pagination /Subtype /Header&gt;&gt;
    /// BDC … EMC</c> for an artifact (§14.8.2.2.2, Table 330).
    /// </summary>
    /// <returns>The begin operation, or null for no marking.</returns>
    public abstract ContentOperation? Begin();

    /// <summary>No marking.</summary>
    private sealed record UnmarkedContent : ContentMark
    {
        public override ContentOperation? Begin() => null;
    }
}

/// <summary>Content of a structure element.</summary>
/// <param name="StructureType">The element's structure type, used as the tag.</param>
/// <param name="Mcid">The marked-content identifier, unique on its page (§14.7.4.2).</param>
internal sealed record TaggedContent(PdfName StructureType, int Mcid) : ContentMark
{
    public override ContentOperation? Begin() =>
        ContentOperators.BeginMarkedContent(StructureType, PdfDictionary.Of(("MCID", new PdfInteger(Mcid))));
}

/// <summary>An artifact.</summary>
/// <param name="Kind">What kind.</param>
internal sealed record ArtifactContent(ArtifactKind Kind) : ContentMark
{
    public override ContentOperation? Begin() => ContentOperators.BeginMarkedContent("Artifact", Kind switch
    {
        ArtifactKind.Layout => PdfDictionary.Of(("Type", new PdfName("Layout"))),
        ArtifactKind.PageHeader => PdfDictionary.Of(("Type", new PdfName("Pagination")), ("Subtype", new PdfName("Header"))),
        ArtifactKind.PageFooter => PdfDictionary.Of(("Type", new PdfName("Pagination")), ("Subtype", new PdfName("Footer"))),
        ArtifactKind.Repeated => PdfDictionary.Of(("Type", new PdfName("Pagination"))),
        _ => throw new System.InvalidOperationException($"Unknown artifact kind {Kind}."),
    });
}
