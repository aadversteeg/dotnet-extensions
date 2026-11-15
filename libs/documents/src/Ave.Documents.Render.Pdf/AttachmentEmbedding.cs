using System.Collections.Immutable;
using Ave.Documents.Core;
using Ave.Documents.Pdf;

namespace Ave.Documents.Render.Pdf;

/// <summary>Embeds an attached file (ISO 32000-1 §7.11.4): a file specification that points at an embedded file stream.</summary>
internal static class AttachmentEmbedding
{
    /// <summary>
    /// The two objects of one attachment:
    /// <list type="bullet">
    /// <item>the file specification (§7.11.3, Table 44): <c>/F</c> and <c>/UF</c> both hold the file name (PDF/A-3 and
    /// PDF/UA require both, veraPDF rules 6.8-2 and 7.11-1), <c>/Desc</c> the description, <c>/EF</c> the stream under
    /// both names, and <c>/AFRelationship</c> how the file relates to the document (ISO 19005-3 §6.8, rule 6.8-3; ISO
    /// 32000-2 §7.11.3);</item>
    /// <item>the embedded file stream (§7.11.4, Table 45): <c>/Subtype</c> the media type as a name (rule 6.8-1), and
    /// <c>/Params</c> (Table 46) with the size and, when known, the modification date.</item>
    /// </list>
    /// </summary>
    /// <param name="attachment">The attachment.</param>
    /// <param name="numbers">Its object numbers.</param>
    /// <returns>The objects.</returns>
    public static ImmutableArray<IndirectObject> Build(Attachment attachment, AttachmentObjectNumbers numbers)
    {
        var name = PdfString.FromText(attachment.FileName);
        var stream = new PdfReference(numbers.EmbeddedFile);
        return
        [
            new IndirectValue(numbers.FileSpecification, PdfDictionary.Of(
                ("Type", new PdfName("Filespec")),
                ("F", name),
                ("UF", name),
                ("Desc", attachment.Description is { } description ? PdfString.FromText(description) : null),
                ("EF", PdfDictionary.Of(("F", stream), ("UF", stream))),
                ("AFRelationship", attachment.Relationship is { } relationship ? new PdfName(relationship.ToString()) : null))),
            new IndirectStream(numbers.EmbeddedFile, new PdfStream(
                PdfDictionary.Of(
                    ("Type", new PdfName("EmbeddedFile")),
                    ("Subtype", new PdfName(attachment.MediaType)),
                    ("Params", PdfDictionary.Of(
                        ("Size", new PdfInteger(attachment.Content.Count)),
                        ("ModDate", attachment.ModificationDate is { } modified ? PdfDate.Format(modified) : null)))),
                attachment.Content.Items,
                StreamEncoding.Flate)),
        ];
    }

    /// <summary>The key of an attachment in the embedded-files name tree: its file name (§7.7.4, Table 31).</summary>
    /// <param name="attachment">The attachment.</param>
    /// <returns>The key.</returns>
    public static PdfString Key(Attachment attachment) => PdfString.FromText(attachment.FileName);
}
