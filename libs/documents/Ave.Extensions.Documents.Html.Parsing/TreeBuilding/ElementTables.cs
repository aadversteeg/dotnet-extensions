using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>
/// The fixed lists of tree construction (HTML Standard §13.2.4.2, §13.2.6.1, §13.2.6.3, §13.2.6.4.1, §13.2.6.5): which
/// elements are special, which are formatting elements, the scope boundaries, the implied end tags, the SVG and MathML
/// name adjustments and the quirks-mode doctypes. One place, so each rule reads one list.
/// </summary>
internal static class ElementTables
{
    /// <summary>The HTML elements in the special category (§13.2.4.2), as Chromium 159 has them: without search, which
    /// the standard lists and Chromium does not (review 39 HD-4, measured: a list item or formatting element stays open
    /// through it).</summary>
    private static readonly FrozenSet<string> SpecialHtml = FrozenSet.Create(StringComparer.Ordinal,
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br",
        "button", "caption", "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed",
        "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6",
        "head", "header", "hgroup", "hr", "html", "iframe", "img", "input", "keygen", "li", "link", "listing", "main",
        "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript", "object", "ol", "p", "param", "plaintext",
        "pre", "script", "section", "select", "source", "style", "summary", "table", "tbody", "td",
        "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp");

    /// <summary>The formatting elements (§13.2.4.2).</summary>
    public static readonly FrozenSet<string> Formatting = FrozenSet.Create(StringComparer.Ordinal,
        "a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u");

    /// <summary>The HTML elements that end the default scope (§13.2.4.2, "has an element in scope").</summary>
    private static readonly FrozenSet<string> ScopeHtml = FrozenSet.Create(StringComparer.Ordinal,
        "applet", "caption", "html", "table", "td", "th", "marquee", "object", "select", "template");

    /// <summary>The elements popped by "generate implied end tags" (§13.2.6.3).</summary>
    public static readonly FrozenSet<string> ImpliedEndTags = FrozenSet.Create(StringComparer.Ordinal,
        "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc");

    /// <summary>The elements popped by "generate all implied end tags thoroughly" (§13.2.6.3).</summary>
    public static readonly FrozenSet<string> ImpliedEndTagsThoroughly = FrozenSet.Create(StringComparer.Ordinal,
        "caption", "colgroup", "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc", "tbody", "td",
        "tfoot", "th", "thead", "tr");

    /// <summary>The elements that may stay open at the end of the body without a parse error (§13.2.6.4.7, end-of-file
    /// and the body and html end tags).</summary>
    public static readonly FrozenSet<string> MayStayOpen = FrozenSet.Create(StringComparer.Ordinal,
        "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc", "tbody", "td", "tfoot", "th", "thead",
        "tr", "body", "html");

    /// <summary>Whether an element is in the special category.</summary>
    public static bool IsSpecial(MutableElement element) => element.Namespace switch
    {
        ElementNamespace.Html => SpecialHtml.Contains(element.LocalName),
        ElementNamespace.MathMl => element.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml",
        _ => element.LocalName is "foreignObject" or "desc" or "title",
    };

    /// <summary>The kinds of scope of §13.2.4.2.</summary>
    public enum Scope
    {
        /// <summary>"has an element in scope".</summary>
        Default,

        /// <summary>"in list item scope": also <c>ol</c> and <c>ul</c>.</summary>
        ListItem,

        /// <summary>"in button scope": also <c>button</c>.</summary>
        Button,

        /// <summary>"in table scope": only <c>html</c>, <c>table</c>, <c>template</c>.</summary>
        Table,
    }

    /// <summary>Whether an element ends a scope of that kind.</summary>
    public static bool EndsScope(MutableElement element, Scope scope)
    {
        if (scope == Scope.Table)
        {
            return element.Namespace == ElementNamespace.Html && element.LocalName is "html" or "table" or "template";
        }

        var inDefault = element.Namespace switch
        {
            ElementNamespace.Html => ScopeHtml.Contains(element.LocalName),
            ElementNamespace.MathMl => element.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml",
            _ => element.LocalName is "foreignObject" or "desc" or "title",
        };

        return inDefault || scope switch
        {
            Scope.ListItem => element.Namespace == ElementNamespace.Html && element.LocalName is "ol" or "ul",
            Scope.Button => element.IsHtml("button"),
            _ => false,
        };
    }

    /// <summary>A MathML text integration point (§13.2.6).</summary>
    public static bool IsMathMlTextIntegrationPoint(MutableElement element) =>
        element.Namespace == ElementNamespace.MathMl && element.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext";

    /// <summary>An HTML integration point (§13.2.6): an annotation-xml whose start tag had encoding text/html or
    /// application/xhtml+xml, or an SVG foreignObject, desc or title.</summary>
    public static bool IsHtmlIntegrationPoint(MutableElement element)
    {
        if (element.Namespace == ElementNamespace.MathMl && element.LocalName == "annotation-xml")
        {
            var encoding = element.Token.Attribute("encoding");
            return encoding is not null && (Ascii.EqualsIgnoreCase(encoding, "text/html") || Ascii.EqualsIgnoreCase(encoding, "application/xhtml+xml"));
        }

        return element.Namespace == ElementNamespace.Svg && element.LocalName is "foreignObject" or "desc" or "title";
    }

    /// <summary>The start tags that break out of foreign content (§13.2.6.5); <c>font</c> only with color, face or size.</summary>
    public static readonly FrozenSet<string> BreakOutOfForeignContent = FrozenSet.Create(StringComparer.Ordinal,
        "b", "big", "blockquote", "body", "br", "center", "code", "dd", "div", "dl", "dt", "em", "embed", "h1", "h2",
        "h3", "h4", "h5", "h6", "head", "hr", "i", "img", "li", "listing", "menu", "meta", "nobr", "ol", "p", "pre",
        "ruby", "s", "small", "span", "strong", "strike", "sub", "sup", "table", "tt", "u", "ul", "var");

    /// <summary>SVG element names whose case the parser restores (§13.2.6.5).</summary>
    public static readonly FrozenDictionary<string, string> SvgElementNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["altglyph"] = "altGlyph", ["altglyphdef"] = "altGlyphDef", ["altglyphitem"] = "altGlyphItem",
        ["animatecolor"] = "animateColor", ["animatemotion"] = "animateMotion", ["animatetransform"] = "animateTransform",
        ["clippath"] = "clipPath", ["feblend"] = "feBlend", ["fecolormatrix"] = "feColorMatrix",
        ["fecomponenttransfer"] = "feComponentTransfer", ["fecomposite"] = "feComposite",
        ["feconvolvematrix"] = "feConvolveMatrix", ["fediffuselighting"] = "feDiffuseLighting",
        ["fedisplacementmap"] = "feDisplacementMap", ["fedistantlight"] = "feDistantLight",
        ["fedropshadow"] = "feDropShadow", ["feflood"] = "feFlood", ["fefunca"] = "feFuncA", ["fefuncb"] = "feFuncB",
        ["fefuncg"] = "feFuncG", ["fefuncr"] = "feFuncR", ["fegaussianblur"] = "feGaussianBlur", ["feimage"] = "feImage",
        ["femerge"] = "feMerge", ["femergenode"] = "feMergeNode", ["femorphology"] = "feMorphology",
        ["feoffset"] = "feOffset", ["fepointlight"] = "fePointLight", ["fespecularlighting"] = "feSpecularLighting",
        ["fespotlight"] = "feSpotLight", ["fetile"] = "feTile", ["feturbulence"] = "feTurbulence",
        ["foreignobject"] = "foreignObject", ["glyphref"] = "glyphRef", ["lineargradient"] = "linearGradient",
        ["radialgradient"] = "radialGradient", ["textpath"] = "textPath",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>SVG attribute names whose case the parser restores ("adjust SVG attributes", §13.2.6.1).</summary>
    public static readonly FrozenDictionary<string, string> SvgAttributeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["attributename"] = "attributeName", ["attributetype"] = "attributeType", ["basefrequency"] = "baseFrequency",
        ["baseprofile"] = "baseProfile", ["calcmode"] = "calcMode", ["clippathunits"] = "clipPathUnits",
        ["diffuseconstant"] = "diffuseConstant", ["edgemode"] = "edgeMode", ["filterunits"] = "filterUnits",
        ["glyphref"] = "glyphRef", ["gradienttransform"] = "gradientTransform", ["gradientunits"] = "gradientUnits",
        ["kernelmatrix"] = "kernelMatrix", ["kernelunitlength"] = "kernelUnitLength", ["keypoints"] = "keyPoints",
        ["keysplines"] = "keySplines", ["keytimes"] = "keyTimes", ["lengthadjust"] = "lengthAdjust",
        ["limitingconeangle"] = "limitingConeAngle", ["markerheight"] = "markerHeight", ["markerunits"] = "markerUnits",
        ["markerwidth"] = "markerWidth", ["maskcontentunits"] = "maskContentUnits", ["maskunits"] = "maskUnits",
        ["numoctaves"] = "numOctaves", ["pathlength"] = "pathLength", ["patterncontentunits"] = "patternContentUnits",
        ["patterntransform"] = "patternTransform", ["patternunits"] = "patternUnits", ["pointsatx"] = "pointsAtX",
        ["pointsaty"] = "pointsAtY", ["pointsatz"] = "pointsAtZ", ["preservealpha"] = "preserveAlpha",
        ["preserveaspectratio"] = "preserveAspectRatio", ["primitiveunits"] = "primitiveUnits", ["refx"] = "refX",
        ["refy"] = "refY", ["repeatcount"] = "repeatCount", ["repeatdur"] = "repeatDur",
        ["requiredextensions"] = "requiredExtensions", ["requiredfeatures"] = "requiredFeatures",
        ["specularconstant"] = "specularConstant", ["specularexponent"] = "specularExponent",
        ["spreadmethod"] = "spreadMethod", ["startoffset"] = "startOffset", ["stddeviation"] = "stdDeviation",
        ["stitchtiles"] = "stitchTiles", ["surfacescale"] = "surfaceScale", ["systemlanguage"] = "systemLanguage",
        ["tablevalues"] = "tableValues", ["targetx"] = "targetX", ["targety"] = "targetY", ["textlength"] = "textLength",
        ["viewbox"] = "viewBox", ["viewtarget"] = "viewTarget", ["xchannelselector"] = "xChannelSelector",
        ["ychannelselector"] = "yChannelSelector", ["zoomandpan"] = "zoomAndPan",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The attributes "adjust foreign attributes" turns into namespaced ones (§13.2.6.1): prefix, local name,
    /// namespace.</summary>
    public static readonly FrozenDictionary<string, (string? Prefix, string LocalName, AttributeNamespace Namespace)> ForeignAttributes =
        new Dictionary<string, (string?, string, AttributeNamespace)>(StringComparer.Ordinal)
        {
            ["xlink:actuate"] = ("xlink", "actuate", AttributeNamespace.XLink),
            ["xlink:arcrole"] = ("xlink", "arcrole", AttributeNamespace.XLink),
            ["xlink:href"] = ("xlink", "href", AttributeNamespace.XLink),
            ["xlink:role"] = ("xlink", "role", AttributeNamespace.XLink),
            ["xlink:show"] = ("xlink", "show", AttributeNamespace.XLink),
            ["xlink:title"] = ("xlink", "title", AttributeNamespace.XLink),
            ["xlink:type"] = ("xlink", "type", AttributeNamespace.XLink),
            ["xml:lang"] = ("xml", "lang", AttributeNamespace.Xml),
            ["xml:space"] = ("xml", "space", AttributeNamespace.Xml),
            ["xmlns"] = (null, "xmlns", AttributeNamespace.Xmlns),
            ["xmlns:xlink"] = ("xmlns", "xlink", AttributeNamespace.Xmlns),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The public identifiers that put a document in quirks mode when the identifier starts with them
    /// (§13.2.6.4.1), lowercase for an ASCII case-insensitive comparison.</summary>
    public static readonly string[] QuirksPublicIdPrefixes =
    [
        "+//silmaril//dtd html pro v0r11 19970101//", "-//as//dtd html 3.0 aswedit + extensions//",
        "-//advasoft ltd//dtd html 3.0 aswedit + extensions//", "-//ietf//dtd html 2.0 level 1//",
        "-//ietf//dtd html 2.0 level 2//", "-//ietf//dtd html 2.0 strict level 1//", "-//ietf//dtd html 2.0 strict level 2//",
        "-//ietf//dtd html 2.0 strict//", "-//ietf//dtd html 2.0//", "-//ietf//dtd html 2.1e//", "-//ietf//dtd html 3.0//",
        "-//ietf//dtd html 3.2 final//", "-//ietf//dtd html 3.2//", "-//ietf//dtd html 3//", "-//ietf//dtd html level 0//",
        "-//ietf//dtd html level 1//", "-//ietf//dtd html level 2//", "-//ietf//dtd html level 3//",
        "-//ietf//dtd html strict level 0//", "-//ietf//dtd html strict level 1//", "-//ietf//dtd html strict level 2//",
        "-//ietf//dtd html strict level 3//", "-//ietf//dtd html strict//", "-//ietf//dtd html//",
        "-//metrius//dtd metrius presentational//", "-//microsoft//dtd internet explorer 2.0 html strict//",
        "-//microsoft//dtd internet explorer 2.0 html//", "-//microsoft//dtd internet explorer 2.0 tables//",
        "-//microsoft//dtd internet explorer 3.0 html strict//", "-//microsoft//dtd internet explorer 3.0 html//",
        "-//microsoft//dtd internet explorer 3.0 tables//", "-//netscape comm. corp.//dtd html//",
        "-//netscape comm. corp.//dtd strict html//", "-//o'reilly and associates//dtd html 2.0//",
        "-//o'reilly and associates//dtd html extended 1.0//", "-//o'reilly and associates//dtd html extended relaxed 1.0//",
        "-//sq//dtd html 2.0 hotmetal + extensions//",
        "-//softquad software//dtd hotmetal pro 6.0::19990601::extensions to html 4.0//",
        "-//softquad//dtd hotmetal pro 4.0::19971010::extensions to html 4.0//", "-//spyglass//dtd html 2.0 extended//",
        "-//sun microsystems corp.//dtd hotjava html//", "-//sun microsystems corp.//dtd hotjava strict html//",
        "-//w3c//dtd html 3 1995-03-24//", "-//w3c//dtd html 3.2 draft//", "-//w3c//dtd html 3.2 final//",
        "-//w3c//dtd html 3.2//", "-//w3c//dtd html 3.2s draft//", "-//w3c//dtd html 4.0 frameset//",
        "-//w3c//dtd html 4.0 transitional//", "-//w3c//dtd html experimental 19960712//",
        "-//w3c//dtd html experimental 970421//", "-//w3c//dtd w3 html//", "-//w3o//dtd w3 html 3.0//",
        "-//webtechs//dtd mozilla html 2.0//", "-//webtechs//dtd mozilla html//",
    ];
}
