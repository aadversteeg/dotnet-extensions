using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// Errors of the HTML front-end's style resolution, under <c>Documents.Html</c> next to the parser's
/// (<c>Ave.Extensions.Documents.Html.Parsing.HtmlErrorCodes</c>). The CSS reader's diagnostics keep their own codes
/// (<c>Documents.Css</c>). Which diagnostic refuses a document: <see cref="HtmlStyleDiagnostics.ClassOf"/>.
/// </summary>
public static class HtmlStyleErrorCodes
{
    /// <summary><c>Documents.Html</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Html";

    /// <summary>Valid HTML the front-end does not implement: refused by default.</summary>
    public static class Unsupported
    {
        /// <summary><c>Documents.Html.Unsupported</c>.</summary>
        public static readonly ErrorCode _ = HtmlStyleErrorCodes._ / "Unsupported";

        /// <summary>A presentational attribute the front-end does not map (HTML Standard §15), or whose mapping is a
        /// value the engine does not support (<c>align=left</c> on an image makes it float). Metadata: Value (the
        /// attribute), SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Attribute = _ / "Attribute";

        /// <summary>An element outside the v0 set: a form control that would be drawn (input, button, select, textarea,
        /// meter, progress have a native appearance and widget styles the engine does not have); an element Chromium
        /// gives rules or a layout of its own (details, summary, option, svg, math …); an element whose user-agent
        /// style the engine does not support (sub, del …). Metadata: Value (the element's name), SourceFile,
        /// SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Element = _ / "Element";

        /// <summary>A script a browser would run (a script element with no type, a JavaScript type, module or
        /// importmap; an event handler attribute such as <c>onload</c>): the engine runs no scripts, so what the script
        /// changes would be missing. Metadata: Value (<c>script</c> or the attribute), SourceFile, SourceLine,
        /// SourceColumn.</summary>
        public static readonly ErrorCode Script = _ / "Script";

        /// <summary>A document in quirks mode (no doctype, or a legacy one): its rendering has rules the engine does not
        /// implement (HTML Standard §15.3.9 margin collapsing, the quirks of CSS). Metadata: SourceFile.</summary>
        public static readonly ErrorCode QuirksMode = _ / "QuirksMode";
    }

    /// <summary>Parts of the document that do not apply, as in a browser: listed, never refused.</summary>
    public static class Ignored
    {
        /// <summary><c>Documents.Html.Ignored</c>.</summary>
        public static readonly ErrorCode _ = HtmlStyleErrorCodes._ / "Ignored";

        /// <summary>A style or link element whose style sheet does not apply: an alternate or disabled link, a type
        /// other than CSS, media that do not match, a title outside the preferred style sheet set. Metadata: Value (the
        /// element's name), SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode StyleSheet = _ / "StyleSheet";
    }

    /// <summary>Resources the document names.</summary>
    public static class Resource
    {
        /// <summary><c>Documents.Html.Resource</c>.</summary>
        public static readonly ErrorCode _ = HtmlStyleErrorCodes._ / "Resource";

        /// <summary>A style sheet (a link element or an @import) the caller's resolver did not give, or the caller gave
        /// no resolver: the document would be drawn without the styles it names. Refused. Metadata: Value (the URL),
        /// SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode NotFound = _ / "NotFound";
    }
}

/// <summary>The one place that says which diagnostics of style resolution refuse a document.</summary>
public static class HtmlStyleDiagnostics
{
    /// <summary>The class of a diagnostic: a CSS diagnostic as <see cref="Css.CssDiagnostics.ClassOf"/> says; the
    /// HTML front-end's unsupported constructs and missing resources refuse; the HTML parser's parse errors are
    /// warnings (the parser recovered as the standard says, as a browser does).</summary>
    /// <param name="code">The diagnostic's code.</param>
    /// <returns>Its class.</returns>
    public static Css.DiagnosticClass ClassOf(ErrorCode code)
    {
        if (code == HtmlStyleErrorCodes.Unsupported._ || code.IsChildOf(HtmlStyleErrorCodes.Unsupported._)
            || code == HtmlStyleErrorCodes.Resource.NotFound)
        {
            return Css.DiagnosticClass.Refusal;
        }

        if (code == HtmlStyleErrorCodes.Ignored._ || code.IsChildOf(HtmlStyleErrorCodes.Ignored._))
        {
            return Css.DiagnosticClass.Ignored;
        }

        return Css.CssDiagnostics.ClassOf(code);
    }
}
