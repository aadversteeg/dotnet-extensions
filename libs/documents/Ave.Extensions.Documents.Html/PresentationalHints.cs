using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// The presentational hints of HTML elements (HTML Standard §15.2–§15.4): attributes the standard maps to CSS, made
/// into declarations of the presentational-hint origin (CSS Cascade 5 §6.5). Only values the engine supports are
/// made; an attribute whose mapping the engine does not support (a float, a background image, list numbering, table
/// frames and rules) is reported at the attribute as <see cref="HtmlStyleErrorCodes.Unsupported.Attribute"/>, so the
/// problem points at what the author wrote rather than at a hint.
/// </summary>
/// <remarks>An attribute never speaks CSS: a hint is a value the code makes (a length, a colour, a keyword), or, where
/// Chromium 159 reads the attribute as one CSS value (<c>face</c>, an <c>align</c> that is not one of its keywords),
/// the attribute's text parsed as that one property's value and nothing else.</remarks>
internal static class PresentationalHints
{
    private static readonly string[] RowGroups = ["thead", "tbody", "tfoot"];

    /// <summary>The elements whose width and height (and often align, border, hspace, vspace) are hints the front-end
    /// does not map: they are not in the v0 set (§15.4.1, §15.4.3).</summary>
    private static readonly string[] EmbeddedContent = ["iframe", "embed", "object", "video", "canvas", "input", "marquee", "applet", "frame"];

    /// <summary>The elements whose align is mapped or refused by a rule of their own, or not mapped at all (button,
    /// select, textarea); every other element maps it as text-align.</summary>
    private static readonly string[] OwnAlign = ["html", "head", "body", "table", "caption", "col", "colgroup", "thead", "tbody", "tfoot", "tr", "td", "th", "div", "p", "hr", "img", "legend", "button", "select", "textarea"];

    /// <summary>The attributes mapped and refused, by element, for docs/html-css-profile.md: the same cases as
    /// <see cref="For"/>, in words (a test generates the document from this table).</summary>
    internal static readonly (string Elements, string Mapped, string Refused)[] Documented =
    [
        ("html, head", "", "align"),
        ("body", "bgcolor, text, link (the colour of a elements that are links), marginheight/topmargin and marginwidth/leftmargin (the last of each pair in the tag)", "align, background"),
        ("table", "width, height, bgcolor, bordercolor, border (outset, or solid with a bordercolor that is not empty), cellspacing, align=center", "align=left/right/inline-start/inline-end (float), background, frame, rules"),
        ("caption", "align=top", "align=bottom"),
        ("col, colgroup", "width, align (-webkit-left/right/center, or one text-align value)", ""),
        ("thead, tbody, tfoot, tr", "height, bgcolor, align (-webkit-left/right/center, absmiddle as center, or one text-align value), valign", "background"),
        ("td, th", "width, height, bgcolor, align, valign, nowrap; the table's cellpadding (1px without one; the integer it starts with, at least 0, at most 65,535) and border (1px inset, or solid with bordercolor)", "background"),
        ("div, p", "align (-webkit-left/right/center, or one text-align value)", ""),
        ("hr", "align, color (a solid border, in that colour when it is one), noshade (gray), size (the integer it starts with, 0 when none), width", ""),
        ("img", "width, height (and aspect-ratio auto w / h), border (solid; a width that is not a non-negative integer is 0), hspace, vspace, align=top/baseline/absmiddle/abscenter/bottom/absbottom", "align=left/right (float), texttop, middle/center; alt text with a size when the image is not there (no src, or the resource resolver gives no PNG or JPEG)"),
        ("ol, ul, li", "type", "ol start, ol reversed, li value"),
        ("pre", "wrap", ""),
        ("br", "", "clear"),
        ("font", "color, face (one font-family value), size", ""),
        ("legend", "", "align"),
        ("iframe, embed, object, video, canvas, input, marquee, applet, frame", "", "width, height, align, hspace, vspace, border"),
        ("meta", "", "http-equiv=Content-Security-Policy in head; name=color-scheme with a content other than normal or light"),
        ("link", "", "charset other than utf-8 (on any link)"),
        ("every other element (h1–h6, lists, sections, phrasing, custom and unknown elements) but button, select and textarea", "align (middle as center, or one text-align value)", ""),
        ("every element but embed", "hidden (display: none; until-found: content-visibility: hidden)", ""),
        ("every element", "", "contenteditable (empty, true or plaintext-only), autofocus"),
    ];

    /// <summary>The hint declarations of one element, as CSS text, and the attributes refused.</summary>
    /// <param name="element">The element.</param>
    /// <param name="body">The document's body element, for the link colour of §15.3.2.</param>
    /// <param name="sourceName">The document's name.</param>
    /// <returns>The declarations and the diagnostics.</returns>
    public static (ValueList<Declaration> Declarations, List<Error> Diagnostics) For(StyleElement element, HtmlElement? body, string sourceName)
    {
        var hints = new List<(string Property, string Value)>();
        var raw = new List<Declaration>();
        var refused = new List<Error>();
        var html = element.Element;
        if (html.Namespace != ElementNamespace.Html)
        {
            return ([], refused);
        }

        string? Attr(string name) => element.GetAttribute(name);

        void Refuse(string attribute, string why)
        {
            var at = element.Attributes.Find(attribute)!;
            refused.Add(new Error(HtmlStyleErrorCodes.Unsupported.Attribute, $"The attribute {attribute}=\"{InputQuote.Of(at.Value)}\" on {html.LocalName} {why}, which is not supported.")
                .With(ErrorMetadataKeys.Value, attribute)
                .WithSource(sourceName, at.Position));
        }

        void Color(string attribute, params string[] properties)
        {
            if (Attr(attribute) is { } value && AttributeValues.LegacyColor(value) is { } color)
            {
                hints.AddRange(properties.Select(property => (property, AttributeValues.Css(color))));
            }
        }

        void Dimension(string attribute, string property, bool ignoringZero)
        {
            if (Attr(attribute) is { } value && (ignoringZero ? AttributeValues.NonzeroDimension(value) : AttributeValues.Dimension(value)) is { } dimension)
            {
                hints.Add((property, dimension.ToString()));
            }
        }

        void Pixels(string attribute, params string[] properties)
        {
            if (Attr(attribute) is { } value && AttributeValues.NonNegativeInteger(value) is { } pixels)
            {
                hints.AddRange(properties.Select(property => (property, $"{pixels}px")));
            }
        }

        void RefuseIfPresent(string attribute, string why)
        {
            if (Attr(attribute) is not null)
            {
                Refuse(attribute, why);
            }
        }

        // The attribute's text as the one value of one property, as Chromium 159 reads face, align and valign: no hint
        // when it is not one value (a ';' or '!important' in it); the cascade refuses a value its grammar does not read.
        // initial, inherit and unset are values of align and valign, not of face; revert and revert-layer make no hint
        // (measured; a hint is the lowest author declaration, so either rolls back to the user-agent value anyway).
        void AsValue(string attribute, string property, bool wideKeywords = false)
        {
            if (Attr(attribute) is { } text && SingleValue(property, text, sourceName, html, wideKeywords) is { } declaration)
            {
                raw.Add(declaration);
            }
        }

        // §15.3.8: the align of table parts and div aligns the text, and the block children ("align descendants"),
        // which is what Chromium's -webkit- values do. Chromium 159 compares the keywords ASCII case-insensitively
        // without trimming and reads any other value as text-align (" center" is center, "end" is end;
        // measured).
        void BlockAlign()
        {
            switch (Attr("align") is { } align ? InfraAscii.ToLower(align) : null)
            {
                case null:
                    break;
                case "left":
                    hints.Add(("text-align", "-webkit-left"));
                    break;
                case "right":
                    hints.Add(("text-align", "-webkit-right"));
                    break;
                case "center" or "middle":
                    hints.Add(("text-align", "-webkit-center"));
                    break;
                case "absmiddle" when html.LocalName is not ("div" or "p"):
                    hints.Add(("text-align", "center"));
                    break;
                default:
                    AsValue("align", "text-align", wideKeywords: true);
                    break;
            }
        }

        // An editable element wraps its text as Chromium's editor does (overflow-wrap: break-word, line-break:
        // after-white-space, measured): contenteditable empty, true or plaintext-only.
        if (Attr("contenteditable") is { } editable && InfraAscii.ToLower(editable) is "" or "true" or "plaintext-only")
        {
            Refuse("contenteditable", "makes the text wrap as editable text");
        }

        // Chromium 159 (HTMLTablePartElement::CollectStyleForPresentationAttribute, html_table_part_element.cc): top,
        // middle, bottom and baseline compared ASCII case-insensitively without trimming, any other value read as
        // vertical-align (" top" is top, "sub" is sub; measured). An identifier that is none of vertical-align's
        // keywords (VerticalAlign::ParseSingleValue, longhands_custom.cc: baseline to -webkit-baseline-middle of
        // css_value_keywords.json5) nor a CSS-wide keyword is no value there, so no hint (valign=center; measured).
        void VerticalAlign()
        {
            if (Attr("valign") is not { } valign)
            {
                return;
            }

            if (InfraAscii.ToLower(valign) is ("top" or "middle" or "bottom" or "baseline") and var value)
            {
                hints.Add(("vertical-align", value));
            }
            else if (!(IsIdentifier(CssTrim(valign)) && !VerticalAlignKeywords.Contains(InfraAscii.ToLower(CssTrim(valign)))))
            {
                AsValue("valign", "vertical-align", wideKeywords: true);
            }
        }

        // Chromium 159 (HTMLElement::CollectStyleForPresentationAttribute, html_element.cc): hidden is display: none, or
        // content-visibility: hidden when it is until-found (ASCII case-insensitive), on every HTML element but embed,
        // which maps hidden itself (HTMLEmbedElement). As a hint, an author's revert rolls it back (measured).
        if (Attr("hidden") is { } hiddenValue && html.LocalName != "embed")
        {
            hints.Add(InfraAscii.ToLower(hiddenValue) == "until-found" ? ("content-visibility", "hidden") : ("display", "none"));
        }

        switch (html.LocalName)
        {
            case "html" or "head":
                // Chromium 159 aligns the text by the align of html, head and body (measured), which §15 does not map.
                RefuseIfPresent("align", "aligns the text of the element and of what inherits from it");
                break;
            case "body":
                RefuseIfPresent("align", "aligns the text of the element and of what inherits from it");
                Color("bgcolor", "background-color");
                Color("text", "color");
                RefuseIfPresent("background", "sets a background image");
                // §15.3.2: each pair of margins, from the last of its two attributes in the tag that holds a non-negative
                // integer (Chromium 159 maps them in attribute order, so the later one wins; measured).
                foreach (var attribute in html.Attributes)
                {
                    if (attribute.Namespace == AttributeNamespace.None && AttributeValues.NonNegativeInteger(attribute.Value) is { } margin)
                    {
                        var sides = attribute.LocalName switch
                        {
                            "marginheight" or "topmargin" => new[] { "margin-top", "margin-bottom" },
                            "marginwidth" or "leftmargin" => ["margin-left", "margin-right"],
                            _ => [],
                        };
                        hints.RemoveAll(hint => sides.Contains(hint.Property));
                        hints.AddRange(sides.Select(side => (side, $"{margin}px")));
                    }
                }

                break;
            // Chromium 159 colours a elements only with the body's link attribute, not area (measured).
            case "a" when element.IsLink && body?.Attribute("link") is { } link && AttributeValues.LegacyColor(link) is { } linkColor:
                hints.Add(("color", AttributeValues.Css(linkColor)));
                break;
            case "table":
                Dimension("width", "width", ignoringZero: true);
                Dimension("height", "height", ignoringZero: false);
                Color("bgcolor", "background-color");
                Color("bordercolor", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color");
                Pixels("cellspacing", "border-spacing");
                RefuseIfPresent("background", "sets a background image");
                RefuseIfPresent("frame", "draws table frames");
                RefuseIfPresent("rules", "draws table rules");
                if (Attr("border") is { } border)
                {
                    var width = AttributeValues.NonNegativeInteger(border) ?? 1;
                    hints.AddRange(new[] { "top", "right", "bottom", "left" }.Select(side => ($"border-{side}-width", $"{width}px")));
                    if (width != 0)
                    {
                        // Chromium: solid when the table has a bordercolor that is not empty, else outset (§15.3.8 says
                        // outset; an empty bordercolor is outset, measured).
                        hints.Add(("border-style", Attr("bordercolor") is { Length: > 0 } ? "solid" : "outset"));
                    }
                }

                // Chromium 159 (HTMLTableElement::CollectStyleForPresentationAttribute, html_table_element.cc): center,
                // compared ASCII case-insensitively without trimming, centres the table; any other value is read as
                // float (" center" is no float, " left" floats; measured).
                if (Attr("align") is { } tableAlign && InfraAscii.ToLower(tableAlign) == "center")
                {
                    hints.Add(("margin-left", "auto"));
                    hints.Add(("margin-right", "auto"));
                }
                else if (Attr("align") is { } floatAlign && InfraAscii.ToLower(CssTrim(floatAlign)) is "left" or "right" or "inline-start" or "inline-end")
                {
                    Refuse("align", "makes the table float");
                }

                break;
            case "caption":
                // Chromium 159 (HTMLTableCaptionElement::CollectStyleForPresentationAttribute,
                // html_table_caption_element.cc) reads the value as caption-side: " bottom" is bottom, left, right and
                // "\u3000bottom" are no value (measured).
                if (Attr("align") is { } captionAlign && InfraAscii.ToLower(CssTrim(captionAlign)) == "bottom")
                {
                    Refuse("align", "puts the caption below the table");
                }

                break;
            case "col" or "colgroup":
                Dimension("width", "width", ignoringZero: false);
                BlockAlign();
                break;
            case "thead" or "tbody" or "tfoot" or "tr":
                Dimension("height", "height", ignoringZero: false);
                Color("bgcolor", "background-color");
                RefuseIfPresent("background", "sets a background image");
                BlockAlign();
                VerticalAlign();
                break;
            case "td" or "th":
                Dimension("width", "width", ignoringZero: true);
                Dimension("height", "height", ignoringZero: true);
                Color("bgcolor", "background-color");
                RefuseIfPresent("background", "sets a background image");
                BlockAlign();
                VerticalAlign();
                if (Attr("nowrap") is not null)
                {
                    hints.Add(("white-space", "nowrap"));
                }

                CellHints(element, hints);
                break;
            case "div":
                BlockAlign();
                break;
            case "p":
                // Chromium maps p's align as div's (-webkit- values, measured); the standard says text-align only.
                BlockAlign();
                break;
            case "hr":
                HorizontalRule(html, hints);
                break;
            case "img":
                Dimension("width", "width", ignoringZero: false);
                Dimension("height", "height", ignoringZero: false);
                // §15.4.3: width and height map to aspect-ratio, auto w / h, when both are lengths.
                if (Attr("width") is { } ratioWidth && AttributeValues.Dimension(ratioWidth) is { IsPercentage: false } w
                    && Attr("height") is { } ratioHeight && AttributeValues.Dimension(ratioHeight) is { IsPercentage: false } h)
                {
                    hints.Add(("aspect-ratio", $"auto {w.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} / {h.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}"));
                }

                Dimension("hspace", "margin-left", ignoringZero: false);
                Dimension("hspace", "margin-right", ignoringZero: false);
                Dimension("vspace", "margin-top", ignoringZero: false);
                Dimension("vspace", "margin-bottom", ignoringZero: false);
                // Chromium 159: any border attribute makes the border solid, a width that is not a non-negative
                // integer is 0 (border="x" and border alone, measured).
                if (Attr("border") is { } imageBorder)
                {
                    var imageBorderWidth = AttributeValues.NonNegativeInteger(imageBorder) ?? 0;
                    hints.AddRange(new[] { "top", "right", "bottom", "left" }.Select(side => ($"border-{side}-width", $"{imageBorderWidth}px")));
                    hints.Add(("border-style", "solid"));
                }

                // Compared ASCII case-insensitively without trimming (" top" is no hint; measured).
                var imageAlign = Attr("align") is { } imageAlignValue ? InfraAscii.ToLower(imageAlignValue) : null;
                switch (imageAlign)
                {
                    case "left" or "right":
                        Refuse("align", "makes the image float");
                        break;
                    // Chromium 159 (measured): bottom is baseline, absbottom is bottom.
                    case "top" or "baseline":
                        hints.Add(("vertical-align", imageAlign!));
                        break;
                    case "bottom":
                        hints.Add(("vertical-align", "baseline"));
                        break;
                    case "absbottom":
                        hints.Add(("vertical-align", "bottom"));
                        break;
                    case "absmiddle" or "abscenter":
                        hints.Add(("vertical-align", "middle"));
                        break;
                    case "texttop":
                        Refuse("align", "aligns the image with the top of the text");
                        break;
                    case "center" or "middle":
                        Refuse("align", "aligns the image's middle with the baseline");
                        break;
                }

                break;
            case "ol" or "ul" or "li":
                ListType(html, hints);
                if (html.LocalName == "ol")
                {
                    RefuseIfPresent("start", "sets where the list's numbering starts");
                    RefuseIfPresent("reversed", "numbers the list backwards");
                }

                if (html.LocalName == "li")
                {
                    RefuseIfPresent("value", "sets the item's number");
                }

                break;
            case "pre":
                if (Attr("wrap") is not null)
                {
                    hints.Add(("white-space", "pre-wrap"));
                }

                break;
            case "br":
                if (Attr("clear") is { } clear && InfraAscii.ToLower(clear) is "left" or "right" or "all" or "both")
                {
                    Refuse("clear", "clears floats");
                }

                break;
            case "font":
                Color("color", "color");
                AsValue("face", "font-family");

                if (Attr("size") is { } size && AttributeValues.LegacyFontSize(size) is { } keyword)
                {
                    hints.Add(("font-size", keyword));
                }

                break;
            case "legend":
                RefuseIfPresent("align", "aligns the legend");
                break;
            default:
                if (EmbeddedContent.Contains(html.LocalName))
                {
                    foreach (var attribute in new[] { "width", "height", "align", "hspace", "vspace", "border" })
                    {
                        RefuseIfPresent(attribute, "is a presentational hint of an element outside the supported set");
                    }
                }

                break;
        }

        // Every other element maps align as Chromium's HTMLElement does (html_element.cc,
        // CollectStyleForPresentationAttribute): middle, compared ASCII case-insensitively, is center, any other value
        // is read as text-align; button, select and textarea do not map it (their IsPresentationAttribute). Measured on
        // 48 elements, headings, lists, sections, phrasing, custom and unknown ones among them.
        if (!OwnAlign.Contains(html.LocalName) && !EmbeddedContent.Contains(html.LocalName))
        {
            if (Attr("align") is { } textAlign && InfraAscii.ToLower(textAlign) == "middle")
            {
                hints.Add(("text-align", "center"));
            }
            else
            {
                AsValue("align", "text-align", wideKeywords: true);
            }
        }

        if (hints.Count == 0 && raw.Count == 0)
        {
            return ([], refused);
        }

        // The values the code made are lengths, colours and keywords only, so their text holds no attribute content.
        var text = string.Join("; ", hints.Select(hint => $"{hint.Property}: {hint.Value}"));
        var parsed = CssParser.ParseDeclarations(text, new CssSourceOptions(sourceName) { Origin = html.Position }).Match(read => read.Value, _ => []);
        return ([.. parsed, .. raw], refused);
    }

    /// <summary>An attribute's text as the value of one property: the declaration when the text is one value (CSS
    /// Syntax 3 finds one declaration of that property in it, not important, no syntax error, no CSS-wide keyword but
    /// initial, inherit and unset where <paramref name="wideKeywords"/>); null otherwise, as Chromium 159 then makes no
    /// hint.</summary>
    private static Declaration? SingleValue(string property, string text, string sourceName, HtmlElement html, bool wideKeywords)
    {
        // Chromium 159 parses the text as a value, not as declarations: a ';' outside a string or comment makes it no
        // value (measured: align="justify;" is no hint), and the end of the text ends a string or a comment (CSS Syntax 3
        // §4.3.2, §4.3.5; measured: face="'unterminated" is the family unterminated).
        if (HasSemicolon(text))
        {
            return null;
        }

        var parsed = CssParser.ParseDeclarations($"{property}: {text}", new CssSourceOptions(sourceName) { Origin = html.Position })
            .Match(read => read, _ => new CssResult<ValueList<Declaration>>([], []));
        var endedByText = parsed.Diagnostics.All(error => error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Value) is "eof-in-string" or "eof-in-comment");
        return parsed.Value is [var only] && endedByText && !only.Important && only.Name == property
               && !only.Value.All(value => value is CssToken { Kind: CssTokenKind.Whitespace })
               && (!IsWideKeyword(only) || (wideKeywords && WideKeyword(only) is "initial" or "inherit" or "unset"))
            ? only
            : null;
    }

    /// <summary>Whether the text holds a ';' outside a string, a comment and an escape (CSS Syntax 3 §4.3).</summary>
    private static bool HasSemicolon(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    break;
                case '"' or '\'':
                    var quote = text[i];
                    for (i++; i < text.Length && text[i] != quote && text[i] != '\n'; i++)
                    {
                        if (text[i] == '\\')
                        {
                            i++;
                        }
                    }

                    break;
                case '/' when i + 1 < text.Length && text[i + 1] == '*':
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 1;
                    break;
                case ';':
                    return true;
            }
        }

        return false;
    }

    private static bool IsWideKeyword(Declaration declaration) =>
        WideKeyword(declaration) is "initial" or "inherit" or "unset" or "revert" or "revert-layer";

    private static string? WideKeyword(Declaration declaration) =>
        declaration.Value.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList() is [CssToken { Kind: CssTokenKind.Ident } word]
            ? InfraAscii.ToLower(word.Text)
            : null;

    /// <summary>The keywords Chromium 159 reads as a value of vertical-align, and the CSS-wide keywords.</summary>
    private static readonly string[] VerticalAlignKeywords =
        ["baseline", "middle", "sub", "super", "text-top", "text-bottom", "top", "bottom", "-webkit-baseline-middle", "initial", "inherit", "unset", "revert", "revert-layer"];

    /// <summary>Whether the text is one CSS identifier without escapes (CSS Syntax 3 §4.3.9): an optional '-', then a
    /// letter, '_' or a code point above U+007F (or a second '-'), then those, digits and '-'.</summary>
    private static bool IsIdentifier(string text)
    {
        static bool Start(char c) => char.IsAsciiLetter(c) || c == '_' || c > '\u007F';
        var i = text.StartsWith('-') ? 1 : 0;
        if (i >= text.Length || !(Start(text[i]) || text[i] == '-'))
        {
            return false;
        }

        for (i++; i < text.Length; i++)
        {
            if (!(Start(text[i]) || char.IsAsciiDigit(text[i]) || text[i] == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The text without CSS white space at its ends (CSS Syntax 3 §4.2: space, tab, line feed, carriage return,
    /// form feed), as Chromium's CSS parser trims a value.</summary>
    private static string CssTrim(string text) => text.Trim(' ', '\t', '\n', '\r', '\f');

    /// <summary>The hints a cell gets from its table (§15.3.8): the cellpadding of the table it is a cell of (the
    /// nearest table around it) as padding; and, when that table has a border attribute that is not zero and the cell's
    /// row is a child of the table or of one of its row groups, a 1px inset border.</summary>
    private static void CellHints(StyleElement cell, List<(string, string)> hints)
    {
        var table = cell.Parent;
        while (table is not null && !(table.IsHtml && table.LocalName == "table"))
        {
            table = table.Parent;
        }

        if (table is null)
        {
            return;
        }

        // Chromium 159 (HTMLTableElement::ParseAttribute and CreateSharedCellStyle, html_table_element.cc): the padding of
        // a cell in a table is the table's hint, 1px without a cellpadding or with an empty one; a cellpadding that is not
        // empty is the integer it starts with, at least 0 and at most 65,535, and 0 when it has none (-1 and abc are 0,
        // 10% is 10; measured); 0 is no hint. As a hint, an author's revert rolls it back to 0 (measured).
        var pixels = table.Element.Attribute("cellpadding") is { Length: > 0 } cellpadding
            ? Math.Min(ushort.MaxValue, Math.Max(0, AttributeValues.Integer(cellpadding) ?? 0))
            : 1;
        if (pixels != 0)
        {
            hints.AddRange(new[] { "top", "right", "bottom", "left" }.Select(side => ($"padding-{side}", $"{pixels}px")));
        }

        var row = cell.Parent;
        var rowParent = row?.Parent;
        var structured = row is { IsHtml: true, LocalName: "tr" }
            && (rowParent == table || (rowParent is { IsHtml: true } group && RowGroups.Contains(group.LocalName) && group.Parent == table));
        if (structured && table.Element.Attribute("border") is { } border && AttributeValues.NonNegativeInteger(border) is not 0)
        {
            // Chromium: solid in the table's colour when the table has a bordercolor, else inset (§15.3.8).
            hints.Add(("border-width", "1px"));
            hints.Add(("border-style", table.Element.Attribute("bordercolor") is { Length: > 0 } ? "solid" : "inset"));
            hints.Add(("border-color", "inherit"));
        }
    }

    /// <summary>§15.3.11: align, color, noshade, size and width of hr, as Chromium 159 maps them (measured; it differs
    /// from the standard's text): color sets a solid border and the background in that colour, noshade the same in gray;
    /// a size of 1 or less removes the bottom border, a larger one sets the height to size − 2 pixels.</summary>
    private static void HorizontalRule(HtmlElement hr, List<(string, string)> hints)
    {
        // Compared ASCII case-insensitively without trimming (" left" is no hint; measured).
        switch (hr.Attribute("align") is { } align ? InfraAscii.ToLower(align) : null)
        {
            case "left":
                hints.Add(("margin-left", "0"));
                hints.Add(("margin-right", "auto"));
                break;
            case "right":
                hints.Add(("margin-left", "auto"));
                hints.Add(("margin-right", "0"));
                break;
            case "center":
                hints.Add(("margin-left", "auto"));
                hints.Add(("margin-right", "auto"));
                break;
        }

        // A color attribute makes the border solid also when it is no colour (color="", measured).
        if (hr.Attribute("color") is { } value)
        {
            hints.Add(("border-style", "solid"));
            if (AttributeValues.LegacyColor(value) is { } color)
            {
                hints.Add(("border-color", AttributeValues.Css(color)));
                hints.Add(("background-color", AttributeValues.Css(color)));
            }
        }
        else if (hr.Attribute("noshade") is not null)
        {
            hints.Add(("border-style", "solid"));
            hints.Add(("border-color", "gray"));
            hints.Add(("background-color", "gray"));
        }

        // Chromium 159 reads size as the integer it starts with, 0 when it has none (abc and the empty value remove the
        // bottom border, 5abc is 5; measured).
        if (hr.Attribute("size") is { } size)
        {
            var pixels = AttributeValues.Integer(size) ?? 0;
            hints.Add(pixels <= 1 ? ("border-bottom-width", "0") : ("height", $"{pixels - 2}px"));
        }

        if (hr.Attribute("width") is { } width && AttributeValues.Dimension(width) is { } dimension)
        {
            hints.Add(("width", dimension.ToString()));
        }
    }

    /// <summary>§15.3.7: the type attribute of ol, ul and li as list-style-type (1, a, A, i, I case-sensitively; none,
    /// disc, circle, square case-insensitively).</summary>
    private static void ListType(HtmlElement element, List<(string, string)> hints)
    {
        if (element.Attribute("type") is not { } type)
        {
            return;
        }

        var ordered = element.LocalName is "ol" or "li" ? type switch
        {
            "1" => "decimal",
            "a" => "lower-alpha",
            "A" => "upper-alpha",
            "i" => "lower-roman",
            "I" => "upper-roman",
            _ => null,
        } : null;
        var lower = InfraAscii.ToLower(type);
        var unordered = element.LocalName is "ul" or "li" && lower is "none" or "disc" or "circle" or "square" ? lower : null;
        if ((ordered ?? unordered) is { } style)
        {
            hints.Add(("list-style-type", style));
        }
    }
}
