using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Pdf;

/// <summary>Errors from the PDF layer.</summary>
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

        /// <summary>An XMP value with a character XML 1.0 does not allow (§2.2, production [2] Char). Metadata:
        /// Property (prefixed name), Offset.</summary>
        public static readonly ErrorCode InvalidText = _ / "InvalidText";

        /// <summary>A page model that breaks the back-end's contract, whoever produced it: no pages, a missing item, a
        /// glyph the font does not have, a cell span below 1, two attachments with one file name. Metadata: Value.</summary>
        public static readonly ErrorCode InvalidPageModel = _ / "InvalidPageModel";

        /// <summary>A font whose licence bits (OpenType OS/2 fsType) forbid embedding. Metadata: FontName.</summary>
        public static readonly ErrorCode FontEmbeddingNotPermitted = _ / "FontEmbeddingNotPermitted";

        /// <summary>Structure markers in the page model that do not nest: an end without its start, a start
        /// without its end on the same page, or a node that appears under two different parents. Metadata: Node,
        /// Value (the page number).</summary>
        public static readonly ErrorCode InvalidStructure = _ / "InvalidStructure";
    }

    /// <summary>
    /// Input that a conformance profile (<c>PdfProfile</c>) forbids. The engine never weakens a document to make it
    /// conform: it refuses it and names the rule. One branch per standard; each leaf cites its clause.
    /// </summary>
    public static class Conformance
    {
        /// <summary><c>Documents.Pdf.Conformance</c>.</summary>
        public static readonly ErrorCode _ = PdfErrorCodes._ / "Conformance";

        /// <summary>Rules of tagged PDF that every tagged profile (PDF/A-3a, PDF/UA-1) shares.</summary>
        public static class Tagged
        {
            /// <summary><c>Documents.Pdf.Conformance.Tagged</c>.</summary>
            public static readonly ErrorCode _ = Conformance._ / "Tagged";

            /// <summary>Content in the page model that is neither inside a structure marker nor marked as an
            /// artifact, so it can be neither tagged nor marked as decoration (ISO 32000-1 §14.8.2.2 and §14.8.2.4;
            /// ISO 14289-1 §7.1, veraPDF rule 7.1-3: "Content shall be marked as Artifact or tagged as real
            /// content"). Metadata: Value (the page number).</summary>
            public static readonly ErrorCode UnmarkedContent = _ / "UnmarkedContent";
        }

        /// <summary>Rules of PDF/A-3 (ISO 19005-3) at every conformance level.</summary>
        public static class PdfA3
        {
            /// <summary><c>Documents.Pdf.Conformance.PdfA3</c>.</summary>
            public static readonly ErrorCode _ = Conformance._ / "PdfA3";

            /// <summary>An attachment that does not say how it relates to the document (ISO 19005-3 §6.8, veraPDF
            /// rule 6.8-3: "The file specification dictionary shall contain key AFRelationship").
            /// Metadata: Value (the file name), SourceFile, SourceLine.</summary>
            public static readonly ErrorCode AttachmentWithoutRelationship = _ / "AttachmentWithoutRelationship";
        }

        /// <summary>Rules of PDF/UA-1 (ISO 14289-1) that the engine can check from the page model.</summary>
        public static class PdfUa1
        {
            /// <summary><c>Documents.Pdf.Conformance.PdfUa1</c>.</summary>
            public static readonly ErrorCode _ = Conformance._ / "PdfUa1";

            /// <summary>A document without a title (ISO 14289-1 §7.1, veraPDF rule 7.1-9: the metadata "shall
            /// contain a dc:title entry"). Metadata: SourceFile, SourceLine (the document).</summary>
            public static readonly ErrorCode MissingTitle = _ / "MissingTitle";

            /// <summary>A document without a language, so the natural language of its text cannot be determined
            /// (ISO 14289-1 §7.2, veraPDF rule 7.2-34). Metadata: SourceFile, SourceLine (the document).</summary>
            public static readonly ErrorCode MissingLanguage = _ / "MissingLanguage";

            /// <summary>A figure without alternative text and not marked decorative (ISO 14289-1 §7.3, veraPDF rule
            /// 7.3-1: "Figure tags shall include an alternative representation or replacement text").
            /// Metadata: Node, SourceFile, SourceLine (the image).</summary>
            public static readonly ErrorCode FigureWithoutAlternativeText = _ / "FigureWithoutAlternativeText";

            /// <summary>Numbered headings whose first heading is not H1 (ISO 14289-1 §7.4.2, veraPDF rule 7.4.2-1:
            /// "If any heading tags are used, H1 shall be the first"). Metadata: Node, Value (the level),
            /// SourceFile, SourceLine (the heading).</summary>
            public static readonly ErrorCode FirstHeadingNotH1 = _ / "FirstHeadingNotH1";

            /// <summary>A heading that skips a level going down, for example an H3 right after an H1 (ISO 14289-1
            /// §7.4.2, veraPDF rule 7.4.2-1). Metadata: Node, Value (previous and this level), SourceFile,
            /// SourceLine (the heading).</summary>
            public static readonly ErrorCode HeadingLevelSkipped = _ / "HeadingLevelSkipped";

            /// <summary>A table row that does not fill every column of its table, counting spans (ISO 14289-1 §7.2,
            /// veraPDF rules 7.2-41 and 7.2-42: "Table rows shall have the same number of columns"). Metadata: Node,
            /// Value (slots filled / columns), SourceFile, SourceLine (the row).</summary>
            public static readonly ErrorCode IrregularTable = _ / "IrregularTable";
        }
    }
}
