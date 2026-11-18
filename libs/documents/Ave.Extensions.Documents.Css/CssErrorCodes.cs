using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css;

/// <summary>
/// Errors and diagnostics of the CSS reader, under <c>Documents.Css</c>. Every diagnostic carries SourceFile (the name
/// the caller gave the style sheet), SourceLine and SourceColumn.
/// </summary>
/// <remarks>
/// <para>Four classes, told apart by their branch (<see cref="CssDiagnostics.ClassOf"/> is the one function that says
/// which):</para>
/// <list type="bullet">
/// <item><see cref="Parse"/>: a syntax error CSS Syntax 3 recovers from; and <see cref="Invalid"/>: a declaration,
/// selector or rule that CSS says to drop. A browser does the same, so these are warnings.</item>
/// <item><see cref="Ignored"/>: something valid that has no effect in a static paged document (<c>cursor</c>, a
/// transition, <c>:hover</c>); listed, not refused.</item>
/// <item><see cref="Unsupported"/>: valid CSS this engine does not implement; and <see cref="TooLarge"/> and
/// <see cref="TooDeep"/>: input past the reader's limits. A front-end refuses the document by default, because the
/// result would differ from a browser's.</item>
/// </list>
/// </remarks>
public static class CssErrorCodes
{
    /// <summary><c>Documents.Css</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Css";

    /// <summary>Syntax errors the parser recovered from as CSS Syntax 3 says (warnings).</summary>
    public static class Parse
    {
        /// <summary><c>Documents.Css.Parse</c>.</summary>
        public static readonly ErrorCode _ = CssErrorCodes._ / "Parse";

        /// <summary>A parse error of CSS Syntax 3 §4 or §5 (an unclosed comment or string, a bad URL, a stray bracket, a
        /// rule without a block). Metadata: Value (what it was), SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Syntax = _ / "Syntax";
    }

    /// <summary>Things CSS says to drop (warnings): the rest of the style sheet is used.</summary>
    public static class Invalid
    {
        /// <summary><c>Documents.Css.Invalid</c>.</summary>
        public static readonly ErrorCode _ = CssErrorCodes._ / "Invalid";

        /// <summary>A declaration whose value does not match the property's grammar (CSS Syntax 3 §5.5.6, "valid in the
        /// current context"). Metadata: Property, Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Value = _ / "Value";

        /// <summary>A property with a vendor prefix of an engine other than Chromium's (<c>-moz-</c>, <c>-ms-</c>,
        /// <c>-o-</c>): Chromium does not know it and drops the declaration. Metadata: Property, SourceFile, SourceLine,
        /// SourceColumn.</summary>
        public static readonly ErrorCode Property = _ / "Property";

        /// <summary>A selector that does not parse, so its whole rule is dropped (Selectors 4 §3.9). Metadata: Value,
        /// SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Selector = _ / "Selector";

        /// <summary>An at-rule whose prelude or place is not valid (an <c>@import</c> after a style rule, an
        /// <c>@media</c> query list that does not parse), dropped. Metadata: Value, SourceFile, SourceLine,
        /// SourceColumn.</summary>
        public static readonly ErrorCode Rule = _ / "Rule";

        /// <summary>A value that is invalid at computed-value time (CSS Variables 1 §3.1): a <c>var()</c> that refers to
        /// a missing or cyclic custom property without a fallback, or whose substitution does not match the property's
        /// grammar. The property then behaves as <c>unset</c>, as in a browser. Metadata: Property, Value, SourceFile,
        /// SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode ComputedValue = _ / "ComputedValue";
    }

    /// <summary>Valid CSS with no effect in a static paged document: ignored by rule, listed in docs/html-css-profile.md.</summary>
    public static class Ignored
    {
        /// <summary><c>Documents.Css.Ignored</c>.</summary>
        public static readonly ErrorCode _ = CssErrorCodes._ / "Ignored";

        /// <summary>A property that changes nothing on paper (<c>cursor</c>, transitions, animations …). Metadata:
        /// Property, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Property = _ / "Property";

        /// <summary>An at-rule that only drives what is ignored by rule (<c>@keyframes</c>, for animations). Metadata:
        /// Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode AtRule = _ / "AtRule";

        /// <summary>A selector with a dynamic state (<c>:hover</c>, <c>:focus</c> …) that never matches in a static
        /// document. Metadata: Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Selector = _ / "Selector";
    }

    /// <summary>Valid CSS this engine does not implement: refused by default (docs/html-css-profile.md lists what is
    /// supported).</summary>
    public static class Unsupported
    {
        /// <summary><c>Documents.Css.Unsupported</c>.</summary>
        public static readonly ErrorCode _ = CssErrorCodes._ / "Unsupported";

        /// <summary>A property the engine does not support (and that is not ignored by rule). Metadata: Property,
        /// SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Property = _ / "Property";

        /// <summary>A value of a supported property that the engine does not support (<c>display: flex</c>, a dashed
        /// border). Metadata: Property, Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Value = _ / "Value";

        /// <summary>A selector that is valid but outside the supported set (<c>:has()</c>, a pseudo-element). Metadata:
        /// Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Selector = _ / "Selector";

        /// <summary>An at-rule the engine does not support (<c>@container</c>, <c>@property</c> …). Metadata: Value,
        /// SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode AtRule = _ / "AtRule";

        /// <summary>A media feature other than the width and height features (Media Queries 4). Metadata: Value,
        /// SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode MediaFeature = _ / "MediaFeature";

        /// <summary>A <c>calc()</c> that still mixes a percentage with a length after computing, or a math function
        /// other than calc, min, max and clamp. Metadata: Property, Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Calc = _ / "Calc";

        /// <summary>Style sheet bytes in an encoding other than UTF-8 and UTF-16 (named by <c>@charset</c> or by the
        /// caller) with bytes outside ASCII: the reader decodes only those two. Metadata: Value (the encoding label),
        /// SourceFile.</summary>
        public static readonly ErrorCode Encoding = _ / "Encoding";

        /// <summary>A colour function other than rgb(), rgba(), hsl() and hsla() (CSS Color 4: lab(), color() …).
        /// Metadata: Property, Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode ColorFunction = _ / "ColorFunction";
    }

    /// <summary>A style sheet longer than <see cref="CssInputLimits.MaxCharacters"/>, more imported style sheets than
    /// <see cref="CssInputLimits.MaxImportedSheets"/>, or a <c>var()</c> substitution longer than
    /// <see cref="CssInputLimits.MaxSubstitutionValues"/>. A refusal: a browser would have read it. Metadata: Value (the
    /// limit), SourceFile.</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";

    /// <summary>Blocks, functions or nested rules deeper than <see cref="CssInputLimits.MaxDepth"/>. A refusal, as
    /// <see cref="TooLarge"/>. Metadata: Value (the limit), SourceFile, SourceLine, SourceColumn.</summary>
    public static readonly ErrorCode TooDeep = _ / "TooDeep";
}
