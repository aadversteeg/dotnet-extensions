using System.Collections.Generic;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>Text properties: colour, alignment, white space, decoration.</summary>
public static partial class PropertyRegistry
{
    private static IEnumerable<PropertyDefinition> TextProperties()
    {
        yield return new PropertyDefinition(
            "color", inherited: true, new CssColorValue(CssColor.Black), "all elements and text", "<color>", PropertySupport.Supported,
            "CSS Color 4 §3.1; the initial value is black (HTML Standard §15.3.4)",
            reader => Whole(reader, reader.Color(), "color"),
            ComputeColor);

        yield return new PropertyDefinition(
            "text-align", inherited: true, new CssKeyword("start"), "block containers", "start | end | left | right | center | justify | match-parent | -webkit-left | -webkit-right | -webkit-center",
            PropertySupport.Supported, "CSS Text 3 §6.1",
            ParseTextAlign,
            ComputeTextAlign);

        yield return Keywords(
            "vertical-align", inherited: false, "baseline", ["baseline", "top", "middle", "bottom"], ["sub", "super", "text-top", "text-bottom"],
            "inline-level and table-cell boxes", "CSS 2.1 §10.8.1; CSS Inline 3 §4");

        yield return Keywords(
            "white-space-collapse", inherited: true, "collapse", ["collapse", "preserve"], ["discard", "preserve-breaks", "preserve-spaces", "break-spaces"],
            "text", "CSS Text 4 §3.1");

        yield return Keywords("text-wrap-mode", inherited: true, "wrap", ["wrap", "nowrap"], [], "text", "CSS Text 4 §6.1");

        // The engine is left-to-right only (bidi is out of v0): isolating a box changes nothing in such text, and the
        // user-agent style sheet isolates most block elements (HTML Standard §15.3.5), so isolate is accepted.
        yield return Keywords(
            "unicode-bidi", inherited: false, "normal", ["normal", "isolate"], ["embed", "bidi-override", "isolate-override", "plaintext"],
            "all elements", "CSS Writing Modes 4 §2.2; isolate has no effect in left-to-right text");

        yield return new PropertyDefinition(
            "text-decoration-line", inherited: false, new CssKeyword("none"), "all elements", "none | underline", PropertySupport.Supported,
            "CSS Text Decoration 3 §2.1",
            reader => reader.Count == 1 ? OneKeyword(reader, "text-decoration-line", ["none", "underline"], ["overline", "line-through", "blink"])
                : AllKeywords(reader, ["underline", "overline", "line-through", "blink"]) ? ParsedValue.Refused("more than one text decoration line is not supported") : ParsedValue.Invalid("not a value of text-decoration-line"),
            AsSpecified);

        yield return Keywords(
            "text-decoration-style", inherited: false, "solid", ["solid"], ["double", "dotted", "dashed", "wavy"], "all elements", "CSS Text Decoration 3 §2.2");

        yield return new PropertyDefinition(
            "text-decoration-color", inherited: false, new CssKeyword("currentcolor"), "all elements", "<color>", PropertySupport.Supported,
            "CSS Text Decoration 3 §2.3",
            reader => Whole(reader, reader.Color(), "text-decoration-color"),
            AsSpecified);

        yield return Keywords(
            "print-color-adjust", inherited: true, "economy", ["economy", "exact"], [], "all elements", "CSS Color Adjust 1 §3.1");
    }

    /// <summary>Whether every value is one of these keywords.</summary>
    private static bool AllKeywords(ValueReader reader, string[] keywords)
    {
        var mark = reader.Mark;
        var all = true;
        while (!reader.AtEnd)
        {
            if (reader.Keyword(keywords) is null)
            {
                all = false;
                break;
            }
        }

        reader.Reset(mark);
        return all;
    }

    /// <summary>The computed colour (CSS Color 4 §3.1, §6.4): <c>currentcolor</c> in <c>color</c> itself is the inherited
    /// value; in other properties it stays the keyword and is resolved where it is used.</summary>
    private static ComputedResult ComputeColor(CssValue value, ComputeContext context) =>
        value is CssKeyword { Name: "currentcolor" } ? context.Inherited(Color) : value;

    /// <summary>The text-align keyword of th in the engine's user-agent style sheet (its own name).</summary>
    private const string HeaderCenter = "-internal-ave-th-center";

    /// <summary>text-align (CSS Text 3 §6.1), with Chromium's <c>-webkit-left</c>, <c>-webkit-right</c> and
    /// <c>-webkit-center</c>: they align the text as left, right and center do, and also align the block children whose
    /// width leaves room ("align descendants", HTML Standard §15.2); the HTML align attribute and the caption of the
    /// user-agent style sheet use them. Layout must do both, or refuse them. The engine's own keyword
    /// <c>-internal-ave-th-center</c>, for th in its user-agent style sheet (HTML Standard §15.3.8: centred where the
    /// parent's text-align is its initial value), is accepted from that sheet only, which the parser knows by the
    /// keyword's form: the cascade drops every keyword starting with -internal- from an author's sheet, as Chromium
    /// drops its own.</summary>
    private static ParsedValue ParseTextAlign(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword(HeaderCenter) is not null)
        {
            return ParsedValue.Of(new CssKeyword(HeaderCenter));
        }

        return reader.Count == 1 && reader.Peek is Syntax.CssToken { Kind: Syntax.CssTokenKind.String }
            ? ParsedValue.Refused("text-align with a string is not supported")
            : OneKeyword(reader, "text-align", ["start", "end", "left", "right", "center", "justify", "match-parent", "-webkit-left", "-webkit-right", "-webkit-center"], ["justify-all", "-webkit-match-parent", "-webkit-auto"]);
    }

    /// <summary>The computed text-align: <c>match-parent</c> is the parent's computed value (CSS Text 3 §6.1 resolves
    /// start and end against the parent's direction; Chromium 159 keeps them as they are, measured, and the engine is
    /// left-to-right only, where start is left); <c>-internal-ave-th-center</c> is center when the parent's value is
    /// start, else the parent's value.</summary>
    private static ComputedResult ComputeTextAlign(CssValue value, ComputeContext context)
    {
        var parent = context.Inherited(TextAlign) as CssKeyword;
        return value switch
        {
            CssKeyword { Name: "match-parent" } => parent?.Name switch
            {
                HeaderCenter => new CssKeyword("center"),
                _ => parent ?? new CssKeyword("start"),
            },
            CssKeyword { Name: HeaderCenter } => parent is null or { Name: "start" } ? new CssKeyword("center") : parent,
            _ => value,
        };
    }
}
