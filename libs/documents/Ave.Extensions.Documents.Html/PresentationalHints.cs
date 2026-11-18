using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;

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
/// the attribute's text parsed as that one property's value and nothing else (review 39 M15).</remarks>
internal static class PresentationalHints
{
    private static readonly string[] RowGroups = ["thead", "tbody", "tfoot"];

    /// <summary>The elements whose width and height (and often align, border, hspace, vspace) are hints the front-end
    /// does not map: they are not in the v0 set (§15.4.1, §15.4.3).</summary>
    private static readonly string[] EmbeddedContent = ["iframe", "embed", "object", "video", "canvas", "input", "marquee", "applet", "frame"];

    /// <summary>The attributes mapped and refused, by element, for docs/html-css-profile.md: the same cases as
    /// <see cref="For"/>, in words (a test generates the document from this table).</summary>
    internal static readonly (string Elements, string Mapped, string Refused)[] Documented =
    [
        ("body", "bgcolor, text, link (the colour of a elements that are links), marginheight/topmargin and marginwidth/leftmargin (the last of each pair in the tag)", "background"),
        ("table", "width, height, bgcolor, bordercolor, border (outset, or solid with a bordercolor that is not empty), cellspacing, align=center", "align=left/right (float), background, frame, rules"),
        ("caption", "align=top", "align=bottom/left/right"),
        ("col, colgroup", "width, align (-webkit-left/right/center, or one text-align value)", ""),
        ("thead, tbody, tfoot, tr", "height, bgcolor, align (-webkit-left/right/center, absmiddle as center, or one text-align value), valign", "background"),
        ("td, th", "width, height, bgcolor, align, valign, nowrap; the table's cellpadding (the integer it starts with, at least 0) and border (1px inset, or solid with bordercolor)", "background"),
        ("div, p", "align (-webkit-left/right/center, or one text-align value)", ""),
        ("h1–h6", "align (middle as center, or one text-align value)", ""),
        ("hr", "align, color (a solid border, in that colour when it is one), noshade (gray), size (the integer it starts with, 0 when none), width", ""),
        ("img", "width, height (and aspect-ratio auto w / h), border (solid; a width that is not a non-negative integer is 0), hspace, vspace, align=top/baseline/absmiddle/abscenter/bottom/absbottom", "align=left/right (float), texttop, middle/center"),
        ("ol, ul, li", "type", "ol start, ol reversed, li value"),
        ("pre", "wrap", ""),
        ("br", "", "clear"),
        ("font", "color, face (one font-family value), size", ""),
        ("legend", "", "align"),
        ("iframe, embed, object, video, canvas, input, marquee, applet, frame", "", "width, height, align, hspace, vspace, border"),
        ("every element", "", "contenteditable (empty, true or plaintext-only)"),
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

        string? Attr(string name) => html.Attribute(name);

        void Refuse(string attribute, string why)
        {
            var at = html.Attributes.First(item => item.Namespace == AttributeNamespace.None && item.LocalName == attribute);
            refused.Add(new Error(HtmlStyleErrorCodes.Unsupported.Attribute, $"The attribute {attribute}=\"{at.Value}\" on {html.LocalName} {why}, which is not supported.")
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

        // The attribute's text as the one value of one property, as Chromium 159 reads face and align: no hint when
        // it is not one value (a ';' or '!important' in it, a CSS-wide keyword); the cascade refuses a value its grammar
        // does not read.
        void AsValue(string attribute, string property)
        {
            if (Attr(attribute) is { } text && SingleValue(property, text, sourceName, html) is { } declaration)
            {
                raw.Add(declaration);
            }
        }

        // §15.3.8: the align of table parts and div aligns the text, and the block children ("align descendants"),
        // which is what Chromium's -webkit- values do. Chromium 159 compares the keywords ASCII case-insensitively
        // without trimming and reads any other value as text-align (" center" is center, "end" is end; measured,
        // review 39 F5).
        void BlockAlign()
        {
            switch (Attr("align") is { } align ? Ascii.ToLower(align) : null)
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
                    AsValue("align", "text-align");
                    break;
            }
        }

        // An editable element wraps its text as Chromium's editor does (overflow-wrap: break-word, line-break:
        // after-white-space, measured): contenteditable empty, true or plaintext-only.
        if (Attr("contenteditable") is { } editable && Ascii.ToLower(editable) is "" or "true" or "plaintext-only")
        {
            Refuse("contenteditable", "makes the text wrap as editable text");
        }

        void VerticalAlign()
        {
            if (Attr("valign") is { } valign && Ascii.ToLower(valign.Trim()) is ("top" or "middle" or "bottom" or "baseline") and var value)
            {
                hints.Add(("vertical-align", value));
            }
        }

        switch (html.LocalName)
        {
            case "body":
                Color("bgcolor", "background-color");
                Color("text", "color");
                RefuseIfPresent("background", "sets a background image");
                // §15.3.2: each pair of margins, from the last of its two attributes in the tag that holds a non-negative
                // integer (Chromium 159 maps them in attribute order, so the later one wins; measured, review 39).
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
            // Chromium 159 colours a elements only with the body's link attribute, not area (measured, review 39).
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
                        // outset; an empty bordercolor is outset, measured, review 39).
                        hints.Add(("border-style", Attr("bordercolor") is { Length: > 0 } ? "solid" : "outset"));
                    }
                }

                switch (Attr("align") is { } tableAlign ? Ascii.ToLower(tableAlign.Trim()) : null)
                {
                    case "left" or "right":
                        Refuse("align", "makes the table float");
                        break;
                    case "center":
                        hints.Add(("margin-left", "auto"));
                        hints.Add(("margin-right", "auto"));
                        break;
                }

                break;
            case "caption":
                switch (Attr("align") is { } captionAlign ? Ascii.ToLower(captionAlign.Trim()) : null)
                {
                    case "bottom":
                        Refuse("align", "puts the caption below the table");
                        break;
                    case "left" or "right":
                        Refuse("align", "aligns the caption beside the table");
                        break;
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
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                // Chromium 159: middle is center, any other value is read as text-align (measured, review 39).
                if (Attr("align") is { } textAlign && Ascii.ToLower(textAlign) == "middle")
                {
                    hints.Add(("text-align", "center"));
                }
                else
                {
                    AsValue("align", "text-align");
                }

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
                // integer is 0 (border="x" and border alone, measured, review 39).
                if (Attr("border") is { } imageBorder)
                {
                    var imageBorderWidth = AttributeValues.NonNegativeInteger(imageBorder) ?? 0;
                    hints.AddRange(new[] { "top", "right", "bottom", "left" }.Select(side => ($"border-{side}-width", $"{imageBorderWidth}px")));
                    hints.Add(("border-style", "solid"));
                }

                var imageAlign = Attr("align") is { } imageAlignValue ? Ascii.ToLower(imageAlignValue.Trim()) : null;
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
                if (Attr("clear") is { } clear && Ascii.ToLower(clear) is "left" or "right" or "all" or "both")
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

        if (hints.Count == 0 && raw.Count == 0)
        {
            return ([], refused);
        }

        // The values the code made are lengths, colours and keywords only, so their text holds no attribute content.
        var text = string.Join("; ", hints.Select(hint => $"{hint.Property}: {hint.Value}"));
        var parsed = CssParser.ParseDeclarations(text, new CssSourceOptions(sourceName) { Origin = html.Position });
        return ([.. parsed.Value, .. raw], refused);
    }

    /// <summary>An attribute's text as the value of one property: the declaration when the text is one value (CSS
    /// Syntax 3 finds one declaration of that property in it, not important, no CSS-wide keyword, no syntax error);
    /// null otherwise, as Chromium 159 then makes no hint.</summary>
    private static Declaration? SingleValue(string property, string text, string sourceName, HtmlElement html)
    {
        var parsed = CssParser.ParseDeclarations($"{property}: {text}", new CssSourceOptions(sourceName) { Origin = html.Position });
        return parsed.Value is [var only] && parsed.Diagnostics.IsEmpty && !only.Important && only.Name == property
               && !only.Value.All(value => value is CssToken { Kind: CssTokenKind.Whitespace })
               && !IsWideKeyword(only)
            ? only
            : null;
    }

    private static bool IsWideKeyword(Declaration declaration) =>
        declaration.Value.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList() is [CssToken { Kind: CssTokenKind.Ident } word]
        && Ascii.ToLower(word.Text) is "initial" or "inherit" or "unset" or "revert" or "revert-layer";

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

        // Chromium 159: a cellpadding that is not empty is the integer it starts with, at least 0, and 0 when it has
        // none (-1 and abc are 0, 10% is 10; measured, review 39).
        if (table.Element.Attribute("cellpadding") is { Length: > 0 } cellpadding)
        {
            var pixels = Math.Max(0, AttributeValues.Integer(cellpadding) ?? 0);
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
        switch (hr.Attribute("align") is { } align ? Ascii.ToLower(align.Trim()) : null)
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

        // A color attribute makes the border solid also when it is no colour (color="", measured, review 39).
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
        // bottom border, 5abc is 5; measured, review 39 F5).
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
        var lower = Ascii.ToLower(type);
        var unordered = element.LocalName is "ul" or "li" && lower is "none" or "disc" or "circle" or "square" ? lower : null;
        if ((ordered ?? unordered) is { } style)
        {
            hints.Add(("list-style-type", style));
        }
    }
}
