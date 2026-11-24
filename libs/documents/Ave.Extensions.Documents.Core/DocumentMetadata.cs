namespace Ave.Extensions.Documents.Core;

/// <summary>
/// What a document says about itself, as opposed to what it shows: title, author, description, language, and the files that travel
/// with it. Every layer carries the same value from the front-end to the back-end (document model, page model), so the
/// PDF document information, its XMP metadata and its embedded files all come from one place.
/// </summary>
/// <param name="Title">The title, or null. PDF/UA requires one (ISO 14289-1 §7.1).</param>
/// <param name="Author">The author, or null.</param>
public sealed record DocumentMetadata(string? Title, string? Author)
{
    /// <summary>
    /// The natural language of the document as a language tag (BCP 47, for example <c>nl-NL</c>), or null. Written as
    /// the catalog's <c>/Lang</c> (ISO 32000-1 §14.9.2) and as XMP <c>dc:language</c>; screen readers use it to pick a
    /// voice. PDF/UA requires it (ISO 14289-1 §7.2). Front-ends check the tag's form.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// What the document is about, in a sentence or a few, or null. Written as the document information's
    /// <c>/Subject</c> (ISO 32000-1 §14.3.3) and as XMP <c>dc:description</c>, the same text in both.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>The files embedded in the document, in the order they were attached.</summary>
    public ValueList<Attachment> Attachments { get; init; }
}
