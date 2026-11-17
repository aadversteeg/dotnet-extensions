using Ave.Documents.Core;

namespace Ave.Documents.Pdf;

/// <summary>
/// Everything a PDF file consists of, before it is written: the numbered objects and the trailer entries
/// (ISO 32000-1 §7.5). A value: two files with equal objects, references and identifier are equal (the lists compare
/// item by item).
/// </summary>
/// <param name="Objects">The indirect objects, numbered 1 to N without gaps (any order).</param>
/// <param name="Catalog">The document catalog, the trailer's <c>/Root</c> (§7.7.2).</param>
/// <param name="Info">The document information dictionary, the trailer's <c>/Info</c> (§14.3.3); optional.</param>
/// <param name="FileIdentifier">The file identifier, the trailer's <c>/ID</c> (§14.4). When null, the writer
/// derives it from a hash of the file's content, so the same content always gets the same identifier.</param>
public sealed record PdfFile(
    ValueList<IndirectObject> Objects,
    PdfReference Catalog,
    PdfReference? Info,
    ValueList<byte>? FileIdentifier);
