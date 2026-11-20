using System.Collections.Generic;
using Ave.Extensions.Documents.Css.Syntax;
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
            "text-align", inherited: true, new CssKeyword("start"), "block containers", "start | end | left | right | center | justify | -webkit-left | -webkit-right | -webkit-center",
            PropertySupport.Supported, "CSS Text 3 §6.1",
            ParseTextAlign,
            ComputeTextAlign);

        yield return Keywords(
            "vertical-align", inherited: false, "baseline", ["baseline", "top", "middle", "bottom"], ["sub", "super", "text-top", "text-bottom"],
            "inline-level and table-cell boxes", "CSS 2.1 §10.8.1; CSS Inline 3 §4");

        yield return Keywords(
            "white-space-collapse", inherited: true, "collapse", ["collapse", "preserve", "preserve-breaks", "break-spaces"], ["discard", "preserve-spaces"],
            "text", "CSS Text 4 §3.1");

        yield return Keywords("text-wrap-mode", inherited: true, "wrap", ["wrap", "nowrap"], [], "text", "CSS Text 4 §6.1");

        // The engine is left-to-right only (bidi is out of v0): isolating a box changes nothing in such text, and the
        // user-agent style sheet isolates most block elements (HTML Standard §15.3.5), so isolate is accepted.
        yield return Keywords(
            "unicode-bidi", inherited: false, "normal", ["normal", "isolate"], ["embed", "bidi-override", "isolate-override", "plaintext"],
            "all elements", "CSS Writing Modes 4 §2.2; isolate has no effect in left-to-right text");

        yield return new PropertyDefinition(
            "text-decoration-line", inherited: false, new CssKeyword("none"), "all elements", "none | [ underline || overline || line-through ]", PropertySupport.Supported,
            "CSS Text Decoration 3 §2.1",
            ParseTextDecorationLine,
            AsSpecified);

        yield return Keywords(
            "text-decoration-style", inherited: false, "solid", ["solid"], ["double", "dotted", "dashed", "wavy"], "all elements", "CSS Text Decoration 3 §2.2");

        yield return new PropertyDefinition(
            "text-decoration-color", inherited: false, new CssKeyword("currentcolor"), "all elements", "<color>", PropertySupport.Supported,
            "CSS Text Decoration 3 §2.3",
            reader => Whole(reader, reader.Color(), "text-decoration-color"),
            AsSpecified);

        // Inherited; the value of the text decides, not that of the decorating box.
        yield return Keywords("text-decoration-skip-ink", inherited: true, "auto", ["auto", "none"], ["all"], "all elements", "CSS Text Decoration 4 §2.10.4");

        yield return Keywords(
            "print-color-adjust", inherited: true, "economy", ["economy", "exact"], [], "all elements", "CSS Color Adjust 1 §3.1");
    }

    /// <summary>The lines a decoration draws.</summary>
    private static readonly string[] DecorationLines = ["underline", "overline", "line-through"];

    /// <summary>The other values of text-decoration-line CSS defines: <c>blink</c> (CSS Text Decoration 3 §2.1, drawn as
    /// nothing by browsers) and the error lines of CSS Text Decoration 4, which the engine does not draw.</summary>
    private static readonly string[] RefusedDecorationLines = ["blink", "spelling-error", "grammar-error"];

    /// <summary>text-decoration-line (CSS Text Decoration 3 §2.1): <c>none</c>, or each of underline, overline and
    /// line-through at most once, in any order; the value is the one keyword, or the list of them as written.</summary>
    private static ParsedValue ParseTextDecorationLine(ValueReader reader)
    {
        if (reader.Count == 1 && reader.Keyword("none") is not null)
        {
            return ParsedValue.Of(new CssKeyword("none"));
        }

        var lines = new List<CssValue>();
        var refused = new List<string>();
        while (!reader.AtEnd)
        {
            if (reader.Keyword(DecorationLines) is { } line)
            {
                if (lines.Contains(new CssKeyword(line)))
                {
                    return ParsedValue.Invalid("a line of text-decoration-line given twice");
                }

                lines.Add(new CssKeyword(line));
            }
            else if (reader.Keyword(RefusedDecorationLines) is { } other)
            {
                if (refused.Contains(other))
                {
                    return ParsedValue.Invalid("a line of text-decoration-line given twice");
                }

                refused.Add(other);
            }
            else
            {
                return ParsedValue.Invalid("not a value of text-decoration-line");
            }
        }

        if (refused.Count > 0)
        {
            return ParsedValue.Refused($"'text-decoration-line: {CssSerializer.Shortened(refused[0])}' is not supported");
        }

        return ParsedValue.Of(lines.Count == 1 ? lines[0] : new CssList([.. lines], CommaSeparated: false));
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

        return reader.Count == 1 && reader.Peek is CssToken { Kind: CssTokenKind.String }
            ? ParsedValue.Refused("text-align with a string is not supported")
            : OneKeyword(reader, "text-align", ["start", "end", "left", "right", "center", "justify", "-webkit-left", "-webkit-right", "-webkit-center"], ["justify-all", "-webkit-match-parent", "-webkit-auto"]);
    }

    /// <summary>The computed text-align: <c>-internal-ave-th-center</c> is center when the parent's value is start, else
    /// the parent's value. <c>match-parent</c> (CSS Text 3 §6.1) is not a value: Chromium 159 does not parse it
    /// (css_parser_fast_paths.cc, CSSTextAlignMatchParentEnabled, read in the Chromium 150 source; measured: <c>text-align: match-parent</c> after
    /// <c>text-align: right</c> leaves right).</summary>
    private static ComputedResult ComputeTextAlign(CssValue value, ComputeContext context)
    {
        var parent = context.Inherited(TextAlign) as CssKeyword;
        return value switch
        {
            CssKeyword { Name: HeaderCenter } => parent is null or { Name: "start" } ? new CssKeyword("center") : parent,
            _ => value,
        };
    }
}
