using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// A whole document as the author wrote it. An immutable value: it can be stored, shared and asserted on in a
/// unit test without rendering anything — with <c>==</c> (children are held in a <see cref="ValueList{T}"/>, so
/// two documents with the same content are equal) or through the text form of its document model. Made by
/// <see cref="Elements.Document"/>; its text formatting is set with the <see cref="TextModifiers"/>, for
/// example <c>Document([...]).FontFamily("Lato").FontSize(11.Pt())</c>.
/// </summary>
/// <param name="Body">The content, top to bottom.</param>
/// <param name="Page">The page size and margins.</param>
/// <param name="Format">The document's text formatting (only its text part is used); must name a font family.</param>
/// <param name="Title">The title, or null.</param>
/// <param name="Author">The author, or null.</param>
/// <param name="Source">Where the document was created.</param>
public sealed record Document(
    ValueList<Element> Body,
    PageSetup Page,
    Style Format,
    string? Title,
    string? Author,
    SourceLocation Source) : IFormatted<Document>
{
    /// <summary>The document's language as a language tag (for example <c>nl-NL</c>), or null.</summary>
    public string? Language { get; init; }

    /// <summary>The files embedded in the document, in the order they were attached.</summary>
    public ValueList<Attachment> Attachments { get; init; }

    /// <summary>
    /// Embeds a file in the document, for example its data as XML. Made with <see cref="Elements.Attachment"/>;
    /// PDF/A-3 also needs its relationship to the document (<see cref="AttachmentModifiers.WithRelationship"/>).
    /// </summary>
    /// <param name="attachment">The file.</param>
    /// <returns>A copy with the file attached after the ones attached before.</returns>
    public Document Attach(Attachment attachment) => this with { Attachments = [.. Attachments, attachment] };

    /// <inheritdoc/>
    Document IFormatted<Document>.WithFormat(Style format) => this with { Format = format };
}
