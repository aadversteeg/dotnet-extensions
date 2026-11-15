using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Core;

/// <summary>
/// The error-code tree of the engine, defined once. Codes are paths (Ave.Extensions.ErrorPaths): a caller can
/// match a whole branch with <c>error.Is(DocumentErrorCodes.Font._)</c> or one leaf with
/// <c>error.Code == DocumentErrorCodes.Font.GlyphMissing</c>. Each nested class's <c>_</c> is its branch.
/// </summary>
/// <remarks>
/// Which metadata each error carries is listed per code; the keys are in <see cref="ErrorMetadataKeys"/>.
/// Errors about a document node always carry <see cref="ErrorMetadataKeys.SourceFile"/> and
/// <see cref="ErrorMetadataKeys.SourceLine"/>.
/// </remarks>
public static class DocumentErrorCodes
{
    /// <summary>The root of every engine error: <c>Documents</c>.</summary>
    public static readonly ErrorCode _ = new("Documents");

    /// <summary>Errors in the input a front-end received from its caller.</summary>
    public static class Input
    {
        /// <summary><c>Documents.Input</c>.</summary>
        public static readonly ErrorCode _ = DocumentErrorCodes._ / "Input";

        /// <summary>A value the input language forbids.</summary>
        public static class Invalid
        {
            /// <summary><c>Documents.Input.Invalid</c>.</summary>
            public static readonly ErrorCode _ = Input._ / "Invalid";

            /// <summary>A length that is not a finite number, or negative where only zero or more is allowed.
            /// Metadata: Value, Property.</summary>
            public static readonly ErrorCode Length = _ / "Length";

            /// <summary>A font size that is not a positive finite number. Metadata: Value.</summary>
            public static readonly ErrorCode FontSize = _ / "FontSize";

            /// <summary>A font weight outside 1–1000 (CSS Fonts 4 §2.2). Metadata: Value.</summary>
            public static readonly ErrorCode FontWeight = _ / "FontWeight";

            /// <summary>No font family set, or an empty one.</summary>
            public static readonly ErrorCode FontFamily = _ / "FontFamily";

            /// <summary>A heading level outside 1–6. Metadata: Value.</summary>
            public static readonly ErrorCode HeadingLevel = _ / "HeadingLevel";

            /// <summary>A page whose size or margins leave no area for content. Metadata: Value.</summary>
            public static readonly ErrorCode PageSize = _ / "PageSize";

            /// <summary>A page break that has no content after it in its container.</summary>
            public static readonly ErrorCode PageBreak = _ / "PageBreak";

            /// <summary>Text that is not valid UTF-16 (an unpaired surrogate). Metadata: Offset.</summary>
            public static readonly ErrorCode Text = _ / "Text";

            /// <summary>A property set on an element it does not apply to, for example box properties in the
            /// style of a text span. Metadata: Value, Property.</summary>
            public static readonly ErrorCode NotApplicable = _ / "NotApplicable";

            /// <summary>Column definitions that are not valid: none at all, a fill weight that is not a positive
            /// number, or a negative or non-finite fixed width. Metadata: Value, Property.</summary>
            public static readonly ErrorCode Columns = _ / "Columns";

            /// <summary>A table whose rows do not fit its columns: a cell span below 1, more cells than columns, a
            /// row span past the end of its row group, header rows after body rows, or a row without cells.
            /// Metadata: Value, Property.</summary>
            public static readonly ErrorCode Table = _ / "Table";

            /// <summary>An image size that is not a positive finite length. Metadata: Value, Property.</summary>
            public static readonly ErrorCode ImageSize = _ / "ImageSize";
        }

        /// <summary>A value the input language allows but this version of the engine does not implement yet.</summary>
        public static class Unsupported
        {
            /// <summary><c>Documents.Input.Unsupported</c>.</summary>
            public static readonly ErrorCode _ = Input._ / "Unsupported";

            /// <summary>A negative margin (allowed by CSS, not implemented yet). Metadata: Value, Property.</summary>
            public static readonly ErrorCode NegativeMargin = _ / "NegativeMargin";
        }
    }

    /// <summary>Errors about fonts.</summary>
    public static class Font
    {
        /// <summary><c>Documents.Font</c>.</summary>
        public static readonly ErrorCode _ = DocumentErrorCodes._ / "Font";

        /// <summary>A font file that cannot be read. Metadata: SourceFile, SourceLine (the loading call).</summary>
        public static class Parse
        {
            /// <summary><c>Documents.Font.Parse</c>.</summary>
            public static readonly ErrorCode _ = Font._ / "Parse";

            /// <summary>The data ends before a structure it announces. Metadata: Table, Offset.</summary>
            public static readonly ErrorCode Truncated = _ / "Truncated";

            /// <summary>Not a TrueType font file (for example a CFF-based OpenType font). Metadata: Value.</summary>
            public static readonly ErrorCode UnsupportedFormat = _ / "UnsupportedFormat";

            /// <summary>A table that this reader needs is absent. Metadata: Table.</summary>
            public static readonly ErrorCode MissingTable = _ / "MissingTable";

            /// <summary>A table contains a value that the OpenType specification does not allow.
            /// Metadata: Table, Value.</summary>
            public static readonly ErrorCode InvalidValue = _ / "InvalidValue";
        }

        /// <summary>
        /// The text contains a character the font has no glyph for. Never silently dropped.
        /// Metadata: FontName, Character, CodePoint, SourceFile, SourceLine.
        /// </summary>
        public static readonly ErrorCode GlyphMissing = _ / "GlyphMissing";

        /// <summary>No font in the font set matches the family and weight asked for.
        /// Metadata: FontName, Value (the weight), SourceFile, SourceLine.</summary>
        public static readonly ErrorCode NotFound = _ / "NotFound";
    }

    /// <summary>Errors about images (decoding the bytes a caller passed in).</summary>
    public static class Image
    {
        /// <summary><c>Documents.Image</c>.</summary>
        public static readonly ErrorCode _ = DocumentErrorCodes._ / "Image";

        /// <summary>Bytes that are not a well-formed PNG or JPEG file: data ends early, a checksum or a value is
        /// wrong. Metadata: Value, Offset.</summary>
        public static readonly ErrorCode Invalid = _ / "Invalid";

        /// <summary>A well-formed image in a variant this version does not decode, named in metadata Value (for
        /// example <c>PNG interlaced (Adam7)</c> or <c>JPEG arithmetic coding</c>), or a format that is neither PNG
        /// nor JPEG.</summary>
        public static readonly ErrorCode Unsupported = _ / "Unsupported";
    }

    /// <summary>Errors from layout.</summary>
    public static class Layout
    {
        /// <summary><c>Documents.Layout</c>.</summary>
        public static readonly ErrorCode _ = DocumentErrorCodes._ / "Layout";

        /// <summary>Content that can never fit on a page, not even on an empty one, and cannot be broken.
        /// Metadata: Node, SourceFile, SourceLine.</summary>
        public static readonly ErrorCode DoesNotFit = _ / "DoesNotFit";

        /// <summary>A node whose layout algorithm is unknown, or whose children that algorithm cannot lay out.
        /// Metadata: Node, Value, SourceFile, SourceLine.</summary>
        public static readonly ErrorCode InvalidContent = _ / "InvalidContent";

        /// <summary>Table columns whose fixed widths and minimum content widths together are wider than the
        /// space the table has. Metadata: Node, Value (needed and available width), SourceFile, SourceLine.</summary>
        public static readonly ErrorCode ColumnsDoNotFit = _ / "ColumnsDoNotFit";

        /// <summary>Computed content (the page count) that does not settle: every layout pass gives a different
        /// number of pages, up to the pass limit. Metadata: Node, Value (the page counts per pass), SourceFile,
        /// SourceLine of the first page-count reference.</summary>
        public static readonly ErrorCode NotConverging = _ / "NotConverging";
    }

    /// <summary>Errors from the PDF layer.</summary>
    public static class Pdf
    {
        /// <summary><c>Documents.Pdf</c>.</summary>
        public static readonly ErrorCode _ = DocumentErrorCodes._ / "Pdf";

        /// <summary>Input that cannot be written as valid PDF.</summary>
        public static class Write
        {
            /// <summary><c>Documents.Pdf.Write</c>.</summary>
            public static readonly ErrorCode _ = Pdf._ / "Write";

            /// <summary>A number that is NaN or infinite; PDF numbers are finite (ISO 32000-1 §7.3.3).
            /// Metadata: Value.</summary>
            public static readonly ErrorCode InvalidNumber = _ / "InvalidNumber";

            /// <summary>Indirect objects not numbered 1..N without gaps or duplicates. Metadata: Value.</summary>
            public static readonly ErrorCode InvalidObjectNumbers = _ / "InvalidObjectNumbers";

            /// <summary>A font whose licence bits (OpenType OS/2 fsType) forbid embedding. Metadata: FontName.</summary>
            public static readonly ErrorCode FontEmbeddingNotPermitted = _ / "FontEmbeddingNotPermitted";
        }
    }
}
