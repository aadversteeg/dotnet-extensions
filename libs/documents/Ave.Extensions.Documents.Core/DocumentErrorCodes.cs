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

    /// <summary>The last diagnostic of a reader (a parser, a style resolution) that found more than its limit of
    /// diagnostics: the rest are counted, not kept, since each costs memory and a hostile text can make one per
    /// character. A refusal for the front-ends that classify diagnostics. Metadata: Value (the number left out),
    /// Refusals (how many of them refuse the document), SourceFile. A reader that takes in the diagnostics of another
    /// adds that one's counts to its own, so the last summary counts every diagnostic left out.</summary>
    public static readonly ErrorCode TooManyDiagnostics = _ / "TooManyDiagnostics";

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

            /// <summary>A <c>line-height</c> that is negative ("Negative values are illegal", CSS 2.1 §10.8.1), not a finite
            /// number, or larger than the front-end allows. Metadata: Value, Property.</summary>
            public static readonly ErrorCode LineHeight = _ / "LineHeight";

            /// <summary>A <c>white-space-collapse</c> or <c>text-wrap-mode</c> value its enum does not have. Metadata: Value,
            /// Property.</summary>
            public static readonly ErrorCode WhiteSpace = _ / "WhiteSpace";

            /// <summary>A text decoration with a line its enum does not have, a decorating font that is not valid (as
            /// <see cref="FontFamily"/>, <see cref="FontSize"/> and <see cref="FontWeight"/> say for a run's own font), or a run
            /// with more decorations than <c>ModelLimits.MaxDecorations</c>. Metadata: Value, Property.</summary>
            public static readonly ErrorCode TextDecoration = _ / "TextDecoration";

            /// <summary>An <c>orphans</c> value that is not a positive whole number (CSS Fragmentation 3 §3.3: "Only
            /// positive integers are allowed"). Metadata: Value, Property.</summary>
            public static readonly ErrorCode Orphans = _ / "Orphans";

            /// <summary>A <c>widows</c> value that is not a positive whole number (CSS Fragmentation 3 §3.3). Metadata:
            /// Value, Property.</summary>
            public static readonly ErrorCode Widows = _ / "Widows";

            /// <summary>A document without a layout profile, or with a policy value its enum does not have.
            /// Metadata: Value, Property.</summary>
            public static readonly ErrorCode LayoutProfile = _ / "LayoutProfile";

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

            /// <summary>A row of page-margin boxes that is not valid: one that is not the content of a header or footer
            /// area with a fixed height, a child that is not a box or has no place in the row, two children at the same
            /// place, or a box with a place in a row outside one. Metadata: Value, Property.</summary>
            public static readonly ErrorCode MarginBoxes = _ / "MarginBoxes";

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

            /// <summary>A box among the text runs of an inline box that this version cannot set in a line: one laid out by
            /// another algorithm than the image algorithm, or one with page break properties, which apply to block-level
            /// boxes only. Metadata: Value, Property.</summary>
            public static readonly ErrorCode AtomicInline = _ / "AtomicInline";

            /// <summary>A tab in a run whose white space is preserved (<c>white-space-collapse: preserve</c>): it would move
            /// the text to the next tab stop, and tab stops are not built. Metadata: Value, Property, Offset.</summary>
            public static readonly ErrorCode PreservedTab = _ / "PreservedTab";

            /// <summary>A width, minimum or maximum width or an auto horizontal margin where layout does not take it: on a
            /// box laid out by another algorithm than flow or inline, or on a box inside a table or a row of page-margin
            /// boxes, whose columns are sized by the intrinsic widths of their content (a sized box's share in them is not
            /// built). Metadata: Value, Property.</summary>
            public static readonly ErrorCode Size = _ / "Size";
        }
    }
}
