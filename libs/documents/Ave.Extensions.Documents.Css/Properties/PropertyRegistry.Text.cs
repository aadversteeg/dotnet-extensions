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
            "text-align", inherited: true, new CssKeyword("start"), "block containers", "start | end | left | right | center | justify | match-parent",
            PropertySupport.Supported, "CSS Text 3 §6.1",
            ParseTextAlign,
            ComputeTextAlign);

        yield return Keywords(
            "vertical-align", inherited: false, "baseline", ["baseline", "top", "middle", "bottom"], ["sub", "super", "text-top", "text-bottom"],
            "inline-level and table-cell boxes", "CSS 2.1 §10.8.1; CSS Inline 3 §4");

        yield return Keywords(
            "white-space", inherited: true, "normal", ["normal", "pre", "nowrap", "pre-wrap"], ["pre-line", "break-spaces"],
            "text", "CSS Text 3 §3");

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

    /// <summary>text-align (CSS Text 3 §6.1). The user agent's <c>-internal-center</c> for th (HTML Standard §15.3.8:
    /// centred where the parent's text-align is its initial value) is accepted from the user-agent style sheet only,
    /// which the parser knows by the keyword's form: authors cannot write a name starting with -internal- in a way that
    /// matters, since Chromium rejects it for them too.</summary>
    private static ParsedValue ParseTextAlign(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("-internal-center") is not null)
        {
            return ParsedValue.Of(new CssKeyword("-internal-center"));
        }

        return reader.Count == 1 && reader.Peek is Syntax.CssToken { Kind: Syntax.CssTokenKind.String }
            ? ParsedValue.Refused("text-align with a string is not supported")
            : OneKeyword(reader, "text-align", ["start", "end", "left", "right", "center", "justify", "match-parent"], ["justify-all"]);
    }

    /// <summary>The computed text-align: <c>match-parent</c> is the parent's computed value with start and end made left
    /// and right (CSS Text 3 §6.1; the engine is left-to-right only); <c>-internal-center</c> is center when the parent's
    /// value is start, else the parent's value.</summary>
    private static ComputedResult ComputeTextAlign(CssValue value, ComputeContext context)
    {
        var parent = context.Inherited(TextAlign) as CssKeyword;
        return value switch
        {
            CssKeyword { Name: "match-parent" } => parent?.Name switch
            {
                "start" => new CssKeyword("left"),
                "end" => new CssKeyword("right"),
                "-internal-center" => new CssKeyword("center"),
                _ => parent ?? new CssKeyword("left"),
            },
            CssKeyword { Name: "-internal-center" } => parent is null or { Name: "start" } ? new CssKeyword("center") : parent,
            _ => value,
        };
    }
}
