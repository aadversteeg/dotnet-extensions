using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>Box properties: display, margins, padding, borders, background colour, sizing.</summary>
public static partial class PropertyRegistry
{
    private static readonly string[] Sides = ["top", "right", "bottom", "left"];

    /// <summary>The display values of CSS Display 3 §2 and CSS 2.1 the engine supports.</summary>
    private static readonly string[] SupportedDisplay =
    [
        "block", "inline", "list-item", "none", "table", "table-row-group", "table-header-group", "table-footer-group",
        "table-row", "table-cell", "table-column-group", "table-column", "table-caption",
    ];

    /// <summary>The display values CSS defines that are refused (CSS Display 3 §2, CSS Flexbox 1, CSS Grid 1, CSS Ruby 1).</summary>
    private static readonly string[] RefusedDisplay =
    [
        "inline-block", "inline-table", "flex", "inline-flex", "grid", "inline-grid", "contents", "flow-root", "run-in",
        "ruby", "ruby-base", "ruby-text", "ruby-base-container", "ruby-text-container", "math", "inline-list-item",
        "flow", "-webkit-box", "-webkit-inline-box", "block-math", "inline-math",
    ];

    private static IEnumerable<PropertyDefinition> BoxProperties()
    {
        yield return new PropertyDefinition(
            "display", inherited: false, new CssKeyword("inline"), "all elements", string.Join(" | ", SupportedDisplay), PropertySupport.Supported, "CSS Display 3 §2",
            reader => reader.Count == 1 ? OneKeyword(reader, "display", SupportedDisplay, RefusedDisplay)
                : ParsedValue.Refused("the multi-keyword syntax of display is not supported"),
            ComputeDisplay);

        foreach (var side in Sides)
        {
            yield return new PropertyDefinition(
                $"margin-{side}", inherited: false, Zero, "all elements except internal table elements", "<length-percentage [0,∞]> | auto",
                PropertySupport.Supported, "CSS Box Model 3 §5.1",
                reader => ParseMargin(reader, $"margin-{side}"),
                (value, context) => context.LengthPercentage(value));
        }

        foreach (var side in Sides)
        {
            yield return new PropertyDefinition(
                $"padding-{side}", inherited: false, Zero, "all elements except internal table rows, row groups, columns and column groups",
                "<length-percentage [0,∞]>", PropertySupport.Supported, "CSS Box Model 3 §5.2",
                reader => Whole(reader, reader.Length(percentages: true, nonNegative: true, $"padding-{side}"), $"padding-{side}"),
                (value, context) => context.LengthPercentage(value));
        }

        // Styles before widths: a border width computes to zero when its style is none (CSS Backgrounds 3 §3.3).
        // inset and outset are in because the user-agent style sheet draws hr with them (HTML Standard §15.3.11): the
        // layout paints them in two shades as Chromium does. hidden is none, except that it wins in collapsed borders.
        foreach (var side in Sides)
        {
            yield return Keywords(
                $"border-{side}-style", inherited: false, "none", ["none", "hidden", "solid", "inset", "outset"], ["dotted", "dashed", "double", "groove", "ridge"],
                "all elements", "CSS Backgrounds 3 §3.2");
        }

        foreach (var side in Sides)
        {
            yield return new PropertyDefinition(
                $"border-{side}-width", inherited: false, new CssLength(Length.FromPoints(2.25)), "all elements", "<length [0,∞]> | thin | medium | thick",
                PropertySupport.Supported, "CSS Backgrounds 3 §3.3",
                reader => ParseBorderWidth(reader, $"border-{side}-width"),
                (value, context) => ComputeBorderWidth(value, context, side));
        }

        foreach (var side in Sides)
        {
            yield return new PropertyDefinition(
                $"border-{side}-color", inherited: false, new CssKeyword("currentcolor"), "all elements", "<color>", PropertySupport.Supported,
                "CSS Backgrounds 3 §3.1",
                reader => Whole(reader, reader.Color(), $"border-{side}-color"),
                AsSpecified);
        }

        yield return new PropertyDefinition(
            "background-color", inherited: false, new CssColorValue(CssColor.Transparent), "all elements", "<color>", PropertySupport.Supported,
            "CSS Backgrounds 3 §2.1",
            reader => Whole(reader, reader.Color(), "background-color"),
            AsSpecified);

        yield return new PropertyDefinition(
            "width", inherited: false, new CssKeyword("auto"), "all elements but non-replaced inline elements, table rows, and row groups",
            "auto | <length-percentage [0,∞]>", PropertySupport.Supported, "CSS Sizing 3 §3.1",
            reader => ParseSize(reader, "width", "auto"),
            (value, context) => context.LengthPercentage(value));

        yield return new PropertyDefinition(
            "min-width", inherited: false, new CssKeyword("auto"), "all elements that accept width or height", "auto | <length-percentage [0,∞]>",
            PropertySupport.Supported, "CSS Sizing 3 §3.2",
            reader => ParseSize(reader, "min-width", "auto"),
            (value, context) => context.LengthPercentage(value));

        yield return new PropertyDefinition(
            "max-width", inherited: false, new CssKeyword("none"), "all elements that accept width or height", "none | <length-percentage [0,∞]>",
            PropertySupport.Supported, "CSS Sizing 3 §3.2",
            reader => ParseSize(reader, "max-width", "none"),
            (value, context) => context.LengthPercentage(value));

        yield return new PropertyDefinition(
            "height", inherited: false, new CssKeyword("auto"), "all elements but non-replaced inline elements, table columns, and column groups",
            "auto | <length-percentage [0,∞]>", PropertySupport.Supported, "CSS Sizing 3 §3.1",
            reader => ParseSize(reader, "height", "auto"),
            (value, context) => context.LengthPercentage(value));

        yield return Keywords("box-sizing", inherited: false, "content-box", ["content-box", "border-box"], [], "all elements that accept width or height", "CSS Box Model 3 §4.3");

        // hidden and clip clip the content to the padding box; the user-agent style sheet sets them on hr and img.
        // A paged document does not scroll, so scroll and auto are refused (Chromium prints them clipped as well, but
        // with scroll bars on screen media).
        foreach (var axis in new[] { "x", "y" })
        {
            yield return Keywords($"overflow-{axis}", inherited: false, "visible", ["visible", "hidden", "clip"], ["scroll", "auto", "overlay"],
                "block containers, flex containers, grid containers", "CSS Overflow 3 §3.1");
        }
    }

    private static CssValue Zero => new CssLength(Length.Zero);

    /// <summary>The whole value must be the one item read.</summary>
    private static ParsedValue Whole(ValueReader reader, ParsedValue? item, string name)
    {
        if (item is not { } value)
        {
            return ParsedValue.Invalid($"not a value of {name}");
        }

        if (!value.IsValid)
        {
            return value;
        }

        return reader.AtEnd ? value : ParsedValue.Invalid($"{name} takes one value");
    }

    /// <summary>margin: &lt;length-percentage&gt; | auto. A negative margin is valid CSS and refused: the layout does not
    /// implement it (docs/architecture.md, known limits).</summary>
    private static ParsedValue ParseMargin(ValueReader reader, string name)
    {
        if (reader.Count == 1 && reader.Keyword("auto") is not null)
        {
            return ParsedValue.Of(new CssKeyword("auto"));
        }

        var value = Whole(reader, reader.Length(percentages: true, nonNegative: false, name), name);
        return value.Value switch
        {
            CssLength { Points: < 0 } or CssDimension { Value: < 0 } or CssPercentage { Value: < 0 } => ParsedValue.Refused($"a negative {name} is not supported"),
            _ => value,
        };
    }

    /// <summary>width, height, min-width, max-width: a keyword, or a non-negative length-percentage; the sizing keywords
    /// of CSS Sizing 3 (min-content …) are refused.</summary>
    private static ParsedValue ParseSize(ValueReader reader, string name, string keyword)
    {
        if (reader.Count == 1 && reader.Keyword(keyword) is not null)
        {
            return ParsedValue.Of(new CssKeyword(keyword));
        }

        if (reader.Count == 1 && reader.Keyword("min-content", "max-content", "fit-content", "stretch", "-webkit-fill-available", "-webkit-min-content", "-webkit-max-content", "-webkit-fit-content") is { } sizing)
        {
            return ParsedValue.Refused($"'{name}: {sizing}' is not supported");
        }

        if (reader.Peek is Syntax.FunctionValue function && function.IsNamed("fit-content"))
        {
            return ParsedValue.Refused($"'{name}: fit-content()' is not supported");
        }

        return Whole(reader, reader.Length(percentages: true, nonNegative: true, name), name);
    }

    /// <summary>&lt;line-width&gt; = &lt;length [0,∞]&gt; | thin | medium | thick (CSS Backgrounds 3 §3.3).</summary>
    private static ParsedValue ParseBorderWidth(ValueReader reader, string name)
    {
        if (reader.Count == 1 && reader.Keyword("thin", "medium", "thick") is { } keyword)
        {
            return ParsedValue.Of(new CssKeyword(keyword));
        }

        return Whole(reader, reader.Length(percentages: false, nonNegative: true, name), name);
    }

    /// <summary>
    /// The computed border width (CSS Backgrounds 3 §3.3): thin, medium and thick are 1px, 3px and 5px; zero when the
    /// side's style is none (or hidden); else the absolute length snapped as Chromium snaps border widths before layout:
    /// a width above 0 and below one device pixel is one pixel, anything else is floored to whole pixels (1px = 0.75pt at
    /// the scale of a printed page).
    /// </summary>
    private static ComputedResult ComputeBorderWidth(CssValue value, ComputeContext context, string side)
    {
        var style = context.Own(Find($"border-{side}-style")!) as CssKeyword;
        if (style is { Name: "none" or "hidden" })
        {
            return new CssLength(Length.Zero);
        }

        return SnappedLineWidth(value, context);
    }

    /// <summary>A &lt;line-width&gt; computed and snapped as Chromium snaps border widths (thin, medium and thick are
    /// 1px, 3px and 5px; above 0 and below 1px is 1px; else floored to whole pixels). outline-width computes the same,
    /// without the style rule (Chromium 159 keeps 3px for outline-style none, measured).</summary>
    private static ComputedResult SnappedLineWidth(CssValue value, ComputeContext context)
    {
        var computed = value switch
        {
            CssKeyword { Name: "thin" } => new CssLength(Length.FromPoints(0.75)),
            CssKeyword { Name: "medium" } => new CssLength(Length.FromPoints(2.25)),
            CssKeyword { Name: "thick" } => new CssLength(Length.FromPoints(3.75)),
            _ => context.LengthPercentage(value).Value,
        };
        if (computed is not CssLength length)
        {
            return context.LengthPercentage(value);
        }

        var pixels = length.Points / 0.75;
        var snapped = pixels is > 0 and < 1 ? 1 : Math.Floor(pixels);
        return new CssLength(Length.FromPoints(snapped * 0.75));
    }

    /// <summary>The computed display (CSS Display 3 §2.7): the root element is blockified (inline and the internal table
    /// values become block; list-item stays).</summary>
    private static ComputedResult ComputeDisplay(CssValue value, ComputeContext context)
    {
        if (!context.IsRoot || value is not CssKeyword keyword)
        {
            return value;
        }

        return keyword.Name switch
        {
            "inline" or "table-row-group" or "table-header-group" or "table-footer-group" or "table-row" or "table-cell"
                or "table-column-group" or "table-column" or "table-caption" => new CssKeyword("block"),
            _ => value,
        };
    }
}
