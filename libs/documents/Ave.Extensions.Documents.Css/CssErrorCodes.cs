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
/// <item><see cref="Parse"/>: a syntax error CSS Syntax 3 recovers from; and <see cref="Invalid"/>: the few things
/// above CSS Syntax 3 that the engine knows Chromium drops as well (a rule in the wrong place, another engine's
/// property, a <c>var()</c> cycle). The result is the browser's, so these are warnings.</item>
/// <item><see cref="Ignored"/>: something valid that has no effect in a static paged document (<c>cursor</c>, a
/// transition, <c>:hover</c>); listed, not refused.</item>
/// <item><see cref="Unsupported"/>: valid CSS this engine does not implement, and input above CSS Syntax 3 that the
/// engine cannot read: a value that does not match its grammar of a known property, a selector or media query that
/// does not parse, an unknown at-rule. That input is either invalid (a browser drops it) or valid CSS beyond this
/// profile (a browser uses it); the engine cannot tell which, so its one answer is "not valid or not supported in
/// this profile". And <see cref="TooLarge"/> and <see cref="TooDeep"/>: input past the reader's limits. A front-end
/// refuses the document by default, because the result could differ from a browser's.</item>
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

    /// <summary>Things CSS says to drop and Chromium drops as well (warnings): the rest of the style sheet is
    /// used.</summary>
    public static class Invalid
    {
        /// <summary><c>Documents.Css.Invalid</c>.</summary>
        public static readonly ErrorCode _ = CssErrorCodes._ / "Invalid";

        /// <summary>A declaration Chromium drops for certain: a top-level <c>!</c> that is not <c>!important</c>
        /// (CSS Syntax 3 §8.2, the old <c>!ie</c> hack); a keyword that starts with <c>-internal-</c> in an author or
        /// presentational-hint declaration (Chromium keeps those for its own style sheet). Metadata: Property, Value,
        /// SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Value = _ / "Value";

        /// <summary>A property with a vendor prefix of an engine other than Chromium's (<c>-moz-</c>, <c>-ms-</c>,
        /// <c>-o-</c>): Chromium does not know it and drops the declaration. Metadata: Property, SourceFile, SourceLine,
        /// SourceColumn.</summary>
        public static readonly ErrorCode Property = _ / "Property";

        /// <summary>A rule in a place CSS does not allow (an <c>@import</c> after a style rule, an <c>@import</c> or
        /// <c>@charset</c> inside a block, declarations directly in a top-level conditional rule), dropped; and an
        /// <c>@import</c> that is not followed (the caller follows no imports, or the sheet imports itself). Metadata:
        /// Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode Rule = _ / "Rule";

        /// <summary>A value that is invalid at computed-value time (CSS Variables 1 §3.1) for a reason Chromium shares:
        /// a <c>var()</c> that refers to a missing or cyclic custom property without a fallback, or a substitution longer
        /// than Chromium's limit. The property then behaves as <c>unset</c>, as in a browser. (A substitution that does
        /// not match the engine's grammar is <see cref="Unsupported.Value"/>.) Metadata: Property, Value, SourceFile,
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
        /// border), or that does not match the engine's grammar of the property ("not valid or not supported in this
        /// profile": <c>vertical-align: 5px</c>, <c>color: 12px</c>), also after <c>var()</c> substitution; and a
        /// <c>@font-face</c> descriptor the engine does not read. Metadata: Property, Value, SourceFile, SourceLine,
        /// SourceColumn.</summary>
        public static readonly ErrorCode Value = _ / "Value";

        /// <summary>A selector that is valid but outside the supported set (<c>:has()</c>, a pseudo-element), or one that
        /// does not parse ("not valid or not supported in this profile"). Metadata: Value, SourceFile, SourceLine,
        /// SourceColumn.</summary>
        public static readonly ErrorCode Selector = _ / "Selector";

        /// <summary>An at-rule the engine does not support (<c>@container</c>, <c>@property</c> …), an unknown at-rule,
        /// or a known one whose prelude the engine cannot read ("not valid or not supported in this profile").
        /// Metadata: Value, SourceFile, SourceLine, SourceColumn.</summary>
        public static readonly ErrorCode AtRule = _ / "AtRule";

        /// <summary>A media feature other than the width and height features (Media Queries 4), or a media feature or
        /// query the engine cannot read ("not valid or not supported in this profile"). Metadata: Value, SourceFile,
        /// SourceLine, SourceColumn.</summary>
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

    /// <summary>Input past one of the limits of <see cref="CssInputLimits"/>: a style sheet longer than
    /// <see cref="CssInputLimits.MaxCharacters"/>, more style sheets than <see cref="CssInputLimits.MaxStyleSheets"/>,
    /// more index entries than <see cref="CssInputLimits.MaxCascadeEntries"/>, more cascade work than
    /// <see cref="CssInputLimits.MaxCascadeWork"/>, more substitution than <see cref="CssInputLimits.MaxSubstitutionWork"/>,
    /// or a substitution too close to Chromium's limit to tell. The reader stops there: the result is this error, in
    /// every mode. Metadata: Value (the limit), SourceFile.</summary>
    public static readonly ErrorCode TooLarge = _ / "TooLarge";

    /// <summary>Blocks, functions or nested rules deeper than <see cref="CssInputLimits.MaxDepth"/> (also a value nested
    /// that deep by var() substitution), or selectors nested deeper than <see cref="CssInputLimits.MaxSelectorNesting"/>.
    /// The reader stops there, as for <see cref="TooLarge"/>. Metadata: Value (the limit), SourceFile, SourceLine, SourceColumn.</summary>
    public static readonly ErrorCode TooDeep = _ / "TooDeep";
}
