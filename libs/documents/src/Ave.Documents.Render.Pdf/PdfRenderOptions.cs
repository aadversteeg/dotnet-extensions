using System;
using System.Collections.Immutable;
using Ave.Documents.Pdf;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The inputs of the PDF file that are not part of the document. Nothing here is read from the clock or the
/// machine, so the same document and options always give the same bytes.
/// </summary>
/// <param name="CreationDate">Written as the document's creation and modification date (ISO 32000-1 §14.3.3), in the
/// document information and in the XMP metadata.</param>
/// <param name="FileIdentifier">The file identifier (§14.4), at least 16 bytes; null to derive it from a hash
/// of the file's content.</param>
public sealed record PdfRenderOptions(DateTimeOffset CreationDate, ImmutableArray<byte>? FileIdentifier = null)
{
    /// <summary>The standards the file is written to; <see cref="PdfProfile.Plain"/> unless set.</summary>
    public PdfProfile Profile { get; init; } = PdfProfile.Plain;

    /// <summary>The value of the /Producer entry: the software that wrote the file.</summary>
    public const string Producer = "Ave.Documents";
}
