using Ave.Documents.Pdf;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// Errors the PDF back-end raises: a page model that breaks the back-end's contract, and input a conformance profile
/// forbids. They share the <c>Documents.Pdf</c> branches with the PDF writer's own errors (<see cref="PdfErrorCodes"/>),
/// so a caller sees one PDF error tree, but each code is defined next to the code that raises it.
/// </summary>
public static class PdfRenderErrorCodes
{
    /// <summary>Input that cannot be written as valid PDF.</summary>
    public static class Write
    {
        /// <summary><c>Documents.Pdf.Write</c>, the branch shared with the writer.</summary>
        public static readonly ErrorCode _ = PdfErrorCodes.Write._;

        /// <summary>A page model that breaks the back-end's contract, whoever produced it (the list is on
        /// <c>PageModelRules</c>). Metadata: Value, and SourceFile, SourceLine where the page model has a location.</summary>
        public static readonly ErrorCode InvalidPageModel = _ / "InvalidPageModel";

        /// <summary>A font whose licence bits (OpenType OS/2 fsType) forbid embedding. Metadata: FontName.</summary>
        public static readonly ErrorCode FontEmbeddingNotPermitted = _ / "FontEmbeddingNotPermitted";

        /// <summary>Render options the back-end cannot write with: no profile, or a PDF/A level outside the enum.
        /// Metadata: Value.</summary>
        public static readonly ErrorCode InvalidOptions = _ / "InvalidOptions";

        /// <summary>Structure markers in the page model that do not nest: an end without its start, a start
        /// without its end on the same page, or a node that appears under two different parents. Metadata: Node,
        /// Value (the page number).</summary>
        public static readonly ErrorCode InvalidStructure = _ / "InvalidStructure";
    }

    /// <summary>
    /// Input that a conformance profile (<c>PdfProfile</c>) forbids. The engine never weakens a document to make it
    /// conform: it refuses it and names the rule. One branch per standard; each leaf names its rule.
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
            /// artifact, so it can be neither tagged nor marked as decoration (veraPDF rule 7.1-3: "Content shall be
            /// marked as Artifact or tagged as real content"). Metadata: Value (the page number).</summary>
            public static readonly ErrorCode UnmarkedContent = _ / "UnmarkedContent";

            /// <summary>A document language that is not a language tag (veraPDF rules 6.7.4-1 and 7.2-29: the value
            /// of /Lang "shall be a language identifier"; the pattern is <c>LanguageTags</c>). Metadata: Value.</summary>
            public static readonly ErrorCode InvalidLanguage = _ / "InvalidLanguage";

            /// <summary>A glyph whose text a ToUnicode map cannot carry: U+0000, U+FEFF or U+FFFE (veraPDF rules
            /// 6.2.11.7.2-2 and 7.21.7-2: the Unicode values "shall all be greater than zero (0), but not equal to
            /// either U+FEFF or U+FFFE"). Metadata: Value (the page number).</summary>
            public static readonly ErrorCode UnmappableText = _ / "UnmappableText";
        }

        /// <summary>Rules on the glyphs a page shows that PDF/A-3 (every level) and PDF/UA-1 share.</summary>
        public static class Glyphs
        {
            /// <summary><c>Documents.Pdf.Conformance.Glyphs</c>.</summary>
            public static readonly ErrorCode _ = Conformance._ / "Glyphs";

            /// <summary>A glyph run that shows glyph 0, the .notdef glyph (veraPDF rules 6.2.11.8-1 and 7.21.8-1: no
            /// "reference to the .notdef glyph from any of the text showing operators"). Metadata: Value (the page
            /// number).</summary>
            public static readonly ErrorCode NotdefGlyph = _ / "NotdefGlyph";
        }

        /// <summary>Rules of PDF/A-3 (ISO 19005-3) at every conformance level.</summary>
        public static class PdfA3
        {
            /// <summary><c>Documents.Pdf.Conformance.PdfA3</c>.</summary>
            public static readonly ErrorCode _ = Conformance._ / "PdfA3";

            /// <summary>An attachment that does not say how it relates to the document (veraPDF rule 6.8-3: "The
            /// file specification dictionary shall contain key AFRelationship"). Metadata: Value (the file name),
            /// SourceFile, SourceLine.</summary>
            public static readonly ErrorCode AttachmentWithoutRelationship = _ / "AttachmentWithoutRelationship";

            /// <summary>An attachment media type outside the form PDF/A-3 accepts: <c>type/subtype</c> of letters,
            /// digits and <c>_ - + .</c> (veraPDF rule 6.8-1, pattern <c>^[-\w+\.]+\/[-\w+\.]+$</c>). Metadata: Value,
            /// SourceFile, SourceLine.</summary>
            public static readonly ErrorCode MediaType = _ / "MediaType";

            /// <summary>A page side outside 3 to 14 400 units (veraPDF rule 6.1.13-11: "the size of any of the page
            /// boundaries … shall not be less than 3 units in either direction, nor shall it be greater than 14 400
            /// units"). Metadata: Value (the page number and size).</summary>
            public static readonly ErrorCode PageSize = _ / "PageSize";

            /// <summary>A text that would be written as a string longer than 32 767 bytes (veraPDF rule 6.1.13-3).
            /// Metadata: Property, Value (the length in bytes), and SourceFile, SourceLine where known.</summary>
            public static readonly ErrorCode TextTooLong = _ / "TextTooLong";

            /// <summary>A value that would be written as a name longer than 127 bytes: a media type or a font name
            /// (veraPDF rule 6.1.13-4). Metadata: Property, Value.</summary>
            public static readonly ErrorCode NameTooLong = _ / "NameTooLong";

            /// <summary>Under level A, a glyph whose text lies in the Unicode Private Use Area: level A requires
            /// replacement text for it (veraPDF rule 6.2.11.7.3-1), which the page model cannot carry. Metadata: Value
            /// (the page number and code point).</summary>
            public static readonly ErrorCode PrivateUseText = _ / "PrivateUseText";

            /// <summary>A file of more than 8 388 607 indirect objects (veraPDF rule 6.1.13-7). Metadata: Value.</summary>
            public static readonly ErrorCode TooManyObjects = _ / "TooManyObjects";
        }

        /// <summary>Rules of PDF/UA-1 (ISO 14289-1) that the engine can check from the page model.</summary>
        public static class PdfUa1
        {
            /// <summary><c>Documents.Pdf.Conformance.PdfUa1</c>.</summary>
            public static readonly ErrorCode _ = Conformance._ / "PdfUa1";

            /// <summary>A document without a title (veraPDF rule 7.1-9: the metadata "shall contain a dc:title
            /// entry"). Metadata: SourceFile, SourceLine (the document).</summary>
            public static readonly ErrorCode MissingTitle = _ / "MissingTitle";

            /// <summary>A document without a language, so the natural language of its text cannot be determined
            /// (veraPDF rule 7.2-34). Metadata: SourceFile, SourceLine (the document).</summary>
            public static readonly ErrorCode MissingLanguage = _ / "MissingLanguage";

            /// <summary>A figure without alternative text and not marked decorative (veraPDF rule 7.3-1: "Figure tags
            /// shall include an alternative representation or replacement text"). Metadata: Node, SourceFile,
            /// SourceLine (the image).</summary>
            public static readonly ErrorCode FigureWithoutAlternativeText = _ / "FigureWithoutAlternativeText";

            /// <summary>Numbered headings whose first heading is not H1 (veraPDF rule 7.4.2-1). Metadata: Node, Value
            /// (the level), SourceFile, SourceLine (the heading).</summary>
            public static readonly ErrorCode FirstHeadingNotH1 = _ / "FirstHeadingNotH1";

            /// <summary>A heading that skips a level going down, for example an H3 right after an H1 (veraPDF rule
            /// 7.4.2-1). Metadata: Node, Value (previous and this level), SourceFile, SourceLine (the heading).</summary>
            public static readonly ErrorCode HeadingLevelSkipped = _ / "HeadingLevelSkipped";

            /// <summary>A table row that does not fill every column of its table, counting spans, or cells that
            /// overlap (veraPDF rules 7.2-42 and 7.2-43: "Table rows shall have the same number of columns (taking into account
            /// column spans)"; 7.2-41 for the columns, 7.2-15 for overlapping cells).
            /// Metadata: Node, Value (slots filled / columns), SourceFile, SourceLine (the row).</summary>
            public static readonly ErrorCode IrregularTable = _ / "IrregularTable";

            /// <summary>A table with a head (THead) and no body (TBody) (veraPDF rule 7.2-14: "If Table element contains
            /// THead kid, Table element should contain one or more TBody kids"). Metadata: Node, SourceFile, SourceLine
            /// (the table).</summary>
            public static readonly ErrorCode TableHeadWithoutBody = _ / "TableHeadWithoutBody";

            /// <summary>A table whose grid, as wide as its first row and as long as its rows, has more slots than the
            /// regularity check fills (<c>TableRegularity.MaxGridSlots</c>, 4 194 304: a thousand columns of four thousand
            /// rows). Metadata: Node, Value (columns x rows), SourceFile, SourceLine (the table).</summary>
            public static readonly ErrorCode TableTooLarge = _ / "TableTooLarge";
        }
    }
}
