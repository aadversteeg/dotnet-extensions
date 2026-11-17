using System;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// A file embedded in the document (ISO 32000-1 §7.11.4, embedded file streams), for example the machine-readable XML
/// of an invoice. A value: two attachments with the same name, bytes and properties are equal.
/// </summary>
/// <param name="FileName">The file name a reader shows and saves it under, for example <c>timesheet.xml</c>.</param>
/// <param name="Content">The file's bytes.</param>
/// <param name="MediaType">The MIME media type, for example <c>application/xml</c>; PDF/A-3 requires it (ISO 19005-3
/// §6.8, veraPDF rule 6.8-1). Use <c>application/octet-stream</c> when it is not known.</param>
/// <param name="Source">Where the attachment was made; reported with every error about it.</param>
public sealed record Attachment(string FileName, ValueList<byte> Content, string MediaType, SourceLocation Source)
{
    /// <summary>A description for people, or null; written as the file specification's <c>/Desc</c>.</summary>
    public string? Description { get; init; }

    /// <summary>When the file was last changed, or null; written as the embedded file's <c>/Params /ModDate</c>.
    /// An input, never read from the clock, so output stays deterministic.</summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>How the file relates to the document, or null when not said. PDF/A-3 requires it (ISO 19005-3 §6.8,
    /// veraPDF rule 6.8-3), so a PDF/A-3 profile refuses an attachment without one.</summary>
    public AttachmentRelationship? Relationship { get; init; }
}
