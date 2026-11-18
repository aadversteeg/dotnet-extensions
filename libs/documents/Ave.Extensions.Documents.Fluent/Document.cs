using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

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

    /// <summary>The layout policies of the document: <see cref="LayoutProfile.Default"/> unless changed with
    /// <see cref="MonolithicOverflow(DocumentModel.MonolithicOverflow)"/>, <see cref="EmptyRowParts(DocumentModel.EmptyRowParts)"/> or
    /// <see cref="FontMetricRounding(DocumentModel.FontMetricRounding)"/>.</summary>
    public LayoutProfile Profile { get; init; } = LayoutProfile.Default;

    /// <summary>
    /// What layout does with an image or a line taller than the page area: refuse the document (the default), or slice
    /// it over as many pages as it needs, as a browser prints it (<see cref="DocumentModel.MonolithicOverflow"/>).
    /// </summary>
    /// <param name="overflow">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document MonolithicOverflow(MonolithicOverflow overflow) => this with { Profile = Profile with { MonolithicOverflow = overflow } };

    /// <summary>
    /// Whether a table row may be cut where one part of it shows nothing: avoided where moving the row helps (the
    /// default), or allowed wherever its cells can break, as a browser prints it (<see cref="DocumentModel.EmptyRowParts"/>).
    /// </summary>
    /// <param name="parts">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document EmptyRowParts(EmptyRowParts parts) => this with { Profile = Profile with { EmptyRowParts = parts } };

    /// <summary>
    /// Whether the fonts' ascent, descent and line gap are used exactly (the default) or each rounded to a whole CSS
    /// pixel as a browser (Chromium) does, which makes a line of <c>line-height: normal</c> a whole number of pixels
    /// (<see cref="DocumentModel.FontMetricRounding"/>).
    /// </summary>
    /// <param name="rounding">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document FontMetricRounding(FontMetricRounding rounding) => this with { Profile = Profile with { FontMetricRounding = rounding } };

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
