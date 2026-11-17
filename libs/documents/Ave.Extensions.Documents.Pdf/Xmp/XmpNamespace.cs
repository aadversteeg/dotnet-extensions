namespace Ave.Extensions.Documents.Pdf.Xmp;

/// <summary>
/// An XML namespace of XMP properties (ISO 16684-1 §6.2: "All names in XMP shall be XML expanded names, consisting
/// of a namespace URI and a local name") with its customary prefix. The schemas the engine writes are listed here; an extension schema
/// (Factur-X, later) defines its own namespace the same way.
/// </summary>
/// <param name="Prefix">The prefix, for example <c>dc</c>. PDF/A requires the documented prefixes for its own schemas
/// (veraPDF rules 6.6.4-4 to -7 for pdfaid; rule 5-3 of PDF/UA-1 for pdfuaid).</param>
/// <param name="Uri">The namespace URI.</param>
public sealed record XmpNamespace(string Prefix, string Uri)
{
    /// <summary>Dublin Core (title, creator, language, format).</summary>
    public static XmpNamespace DublinCore { get; } = new("dc", "http://purl.org/dc/elements/1.1/");

    /// <summary>The XMP basic schema (dates, creator tool).</summary>
    public static XmpNamespace XmpBasic { get; } = new("xmp", "http://ns.adobe.com/xap/1.0/");

    /// <summary>The Adobe PDF schema (producer, keywords).</summary>
    public static XmpNamespace AdobePdf { get; } = new("pdf", "http://ns.adobe.com/pdf/1.3/");

    /// <summary>The PDF/A identification schema (ISO 19005-3 §6.6.4).</summary>
    public static XmpNamespace PdfAIdentification { get; } = new("pdfaid", "http://www.aiim.org/pdfa/ns/id/");

    /// <summary>The PDF/UA identification schema (ISO 14289-1 §5).</summary>
    public static XmpNamespace PdfUaIdentification { get; } = new("pdfuaid", "http://www.aiim.org/pdfua/ns/id/");

    /// <summary>The PDF/A extension schema container (ISO 19005-3 §6.6.2.3.3, veraPDF rule 6.6.2.3.3-1).</summary>
    public static XmpNamespace PdfAExtension { get; } = new("pdfaExtension", "http://www.aiim.org/pdfa/ns/extension/");

    /// <summary>The fields of one extension schema description.</summary>
    public static XmpNamespace PdfASchema { get; } = new("pdfaSchema", "http://www.aiim.org/pdfa/ns/schema#");

    /// <summary>The fields of one property description in an extension schema.</summary>
    public static XmpNamespace PdfAProperty { get; } = new("pdfaProperty", "http://www.aiim.org/pdfa/ns/property#");

    /// <summary>RDF, the syntax XMP is written in.</summary>
    internal static XmpNamespace Rdf { get; } = new("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");

    /// <summary>The XMP wrapper element <c>x:xmpmeta</c>.</summary>
    internal static XmpNamespace Meta { get; } = new("x", "adobe:ns:meta/");
}
