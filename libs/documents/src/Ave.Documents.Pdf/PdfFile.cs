using System.Collections.Immutable;

namespace Ave.Documents.Pdf;

/// <summary>
/// Everything a PDF file consists of, before it is written: the numbered objects and the trailer entries
/// (ISO 32000-1 §7.5).
/// </summary>
/// <param name="Objects">The indirect objects, numbered 1 to N without gaps (any order).</param>
/// <param name="Catalog">The document catalog, the trailer's <c>/Root</c> (§7.7.2).</param>
/// <param name="Info">The document information dictionary, the trailer's <c>/Info</c> (§14.3.3); optional.</param>
/// <param name="FileIdentifier">The file identifier, the trailer's <c>/ID</c> (§14.4). When null, the writer
/// derives it from a hash of the file's content, so the same content always gets the same identifier.</param>
public sealed record PdfFile(
    ImmutableArray<IndirectObject> Objects,
    PdfReference Catalog,
    PdfReference? Info,
    ImmutableArray<byte>? FileIdentifier);
