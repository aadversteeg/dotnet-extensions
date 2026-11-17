using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The root of the engine's error-code tree and the branch every layer shares: errors in the input. Codes are paths
/// (Ave.Extensions.ErrorPaths): a caller can match a whole branch with <c>error.Is(DocumentErrorCodes.Input._)</c>
/// or one leaf with <c>error.Code == DocumentErrorCodes.Input.Invalid.Length</c>. Each nested class's <c>_</c> is its
/// branch. Each library defines its own branch next to the code that raises it, under this root:
/// <c>FontErrorCodes</c> (Ave.Extensions.Documents.Fonts, <c>Documents.Font</c>), <c>ImageErrorCodes</c> (Ave.Extensions.Documents.Images,
/// <c>Documents.Image</c>), <c>LayoutErrorCodes</c> (Ave.Extensions.Documents.Layout, <c>Documents.Layout</c>) and
/// <c>PdfErrorCodes</c> (Ave.Extensions.Documents.Pdf, <c>Documents.Pdf</c>), so a new back-end rule never edits this file.
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

            /// <summary>Text with a character text cannot hold (<see cref="TextCharacters"/>): a control character
            /// other than tab and the forced line breaks, a noncharacter, or an unpaired surrogate. Metadata: Offset,
            /// and Value, Property, SourceFile, SourceLine when the front-end found it.</summary>
            public static readonly ErrorCode Text = _ / "Text";

            /// <summary>Metadata text — the title, the author, an attachment's file name or description — with a
            /// character XML 1.0 does not allow (§2.2, production [2] Char: a C0 control character other than tab, line
            /// feed and carriage return, U+FFFE, U+FFFF or an unpaired surrogate). The XMP metadata is XML, so such
            /// text cannot be written. Metadata: Property, Offset.</summary>
            public static readonly ErrorCode MetadataText = _ / "MetadataText";

            /// <summary>Elements nested deeper than the front-end allows (<c>InputLimits.MaxNestingDepth</c> in
            /// Ave.Extensions.Documents.Fluent), at the first element past the limit. Metadata: Value (the limit).</summary>
            public static readonly ErrorCode TooDeep = _ / "TooDeep";

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

            /// <summary>A language that is not a language tag of the form BCP 47 describes (letters, then subtags
            /// of letters and digits joined by hyphens, for example <c>nl-NL</c>; ISO 32000-1 §14.9.2.2, "a Language-Tag as defined in RFC 3066").
            /// Metadata: Value.</summary>
            public static readonly ErrorCode Language = _ / "Language";

            /// <summary>An attachment without a file name, with a media type that is not of the form
            /// <c>type/subtype</c>, or attached twice under the same name. Metadata: Value, Property.</summary>
            public static readonly ErrorCode Attachment = _ / "Attachment";

            /// <summary>An image that is both given alternative text and marked decorative. Metadata: Value.</summary>
            public static readonly ErrorCode ImageDescription = _ / "ImageDescription";
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
}
