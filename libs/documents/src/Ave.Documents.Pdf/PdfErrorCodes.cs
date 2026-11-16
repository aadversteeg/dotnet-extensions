using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Pdf;

/// <summary>Errors the PDF writer raises. The back-end (Render.Pdf) adds its own codes to the same branches.</summary>
public static class PdfErrorCodes
{
    /// <summary><c>Documents.Pdf</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Pdf";

    /// <summary>Input that cannot be written as valid PDF.</summary>
    public static class Write
    {
        /// <summary><c>Documents.Pdf.Write</c>.</summary>
        public static readonly ErrorCode _ = PdfErrorCodes._ / "Write";

        /// <summary>A number that is NaN or infinite; PDF numbers are finite (ISO 32000-1 §7.3.3).
        /// Metadata: Value.</summary>
        public static readonly ErrorCode InvalidNumber = _ / "InvalidNumber";

        /// <summary>Indirect objects not numbered 1..N without gaps or duplicates. Metadata: Value.</summary>
        public static readonly ErrorCode InvalidObjectNumbers = _ / "InvalidObjectNumbers";

        /// <summary>An object the writer cannot write as given: a dictionary with two entries of one key (§7.3.7:
        /// "Multiple entries in the same dictionary shall not have the same key"), or a stream dictionary that sets
        /// /Length or /Filter, which the writer sets itself. Metadata: Value.</summary>
        public static readonly ErrorCode InvalidObject = _ / "InvalidObject";

        /// <summary>An object with arrays and dictionaries nested deeper than <see cref="PdfSerializer.MaxNestingDepth"/>.
        /// Metadata: Value (the limit).</summary>
        public static readonly ErrorCode NestedTooDeep = _ / "NestedTooDeep";

        /// <summary>An XMP value with a character XML 1.0 does not allow (§2.2, production [2] Char). Metadata:
        /// Property (prefixed name), Offset.</summary>
        public static readonly ErrorCode InvalidText = _ / "InvalidText";

        /// <summary>A file identifier that is empty or unset (an empty byte string cannot identify a file; veraPDF
        /// rule 6.1.3-1 needs a non-empty /ID).</summary>
        public static readonly ErrorCode InvalidFileIdentifier = _ / "InvalidFileIdentifier";

        /// <summary>An XMP property whose name is not an XML name (XML Namespaces 1.0, NCName), so the packet cannot
        /// be written. Metadata: Property.</summary>
        public static readonly ErrorCode InvalidPropertyName = _ / "InvalidPropertyName";
    }
}
