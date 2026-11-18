using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// Errors and diagnostics of the HTML parser, under <c>Documents.Html</c>. The style resolution of
/// Ave.Extensions.Documents.Html adds its own codes under the same branch.
/// </summary>
/// <remarks>
/// A parse error is not a failure: the HTML Standard defines how the parser recovers from every one (§13.2.2), and a
/// browser builds the same tree, so the parser reports it as a diagnostic and goes on. Only the limits and the
/// encoding rule end a parse. Every diagnostic and error carries SourceFile (the name the caller gave the text),
/// SourceLine and SourceColumn.
/// </remarks>
public static class HtmlErrorCodes
{
    /// <summary><c>Documents.Html</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Html";

    /// <summary>Parse errors the parser recovered from as the standard says (diagnostics, not failures).</summary>
    public static class Parse
    {
        /// <summary><c>Documents.Html.Parse</c>.</summary>
        public static readonly ErrorCode _ = HtmlErrorCodes._ / "Parse";

        /// <summary>A parse error. Metadata: Value (its name: for the tokenizer and the input stream the name the
        /// standard gives it in §13.2.2, for example <c>eof-in-tag</c>; for tree construction, where the standard names
        /// none, a name of this parser in the same style, for example <c>unexpected-end-tag</c>), SourceFile,
        /// SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Syntax = _ / "Syntax";
    }

    /// <summary>Bytes whose encoding is not UTF-8: a <c>meta</c> element or an XML declaration names another encoding
    /// (HTML Standard §13.2.3.2 and §13.2.6.4.4), or the bytes are not UTF-8 and nothing declares an encoding (a browser
    /// would detect one); the parser does not guess. Metadata: Value (the label, or <c>(undeclared)</c>), SourceFile,
    /// SourceLine, SourceColumn.</summary>
    public static readonly ErrorCode Encoding = _ / "Encoding";

    /// <summary>A text longer than <see cref="HtmlInputLimits.MaxCharacters"/> (or bytes longer than that many).
    /// Metadata: Value (the limit), SourceFile.</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";

    /// <summary>More open elements than <see cref="HtmlInputLimits.MaxDepth"/>; the parse stops at the first element
    /// past the limit. Metadata: Value (the limit), SourceFile, SourceLine, SourceColumn.</summary>
    public static readonly ErrorCode TooDeep = _ / "TooDeep";
}
