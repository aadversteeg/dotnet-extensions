using System.Collections.Generic;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>Table, list and fragmentation properties.</summary>
public static partial class PropertyRegistry
{
    /// <summary>The list-style-type values the engine draws (CSS Counter Styles 3 §6 predefined styles).</summary>
    private static readonly string[] SupportedListStyles =
    [
        "disc", "circle", "square", "decimal", "decimal-leading-zero", "lower-roman", "upper-roman", "lower-alpha",
        "upper-alpha", "lower-latin", "upper-latin", "none",
    ];

    private static IEnumerable<PropertyDefinition> TableAndListProperties()
    {
        yield return Keywords("border-collapse", inherited: true, "separate", ["separate", "collapse"], [], "table and inline-table boxes", "CSS 2.1 §17.6");

        yield return new PropertyDefinition(
            "border-spacing", inherited: true, new CssList([Zero, Zero], CommaSeparated: false), "table and inline-table boxes",
            "<length [0,∞]>{1,2}", PropertySupport.Supported, "CSS 2.1 §17.6.1",
            ParseBorderSpacing,
            ComputeBorderSpacing);

        yield return Keywords("table-layout", inherited: false, "auto", ["auto", "fixed"], [], "table and inline-table boxes", "CSS 2.1 §17.5.2");

        yield return Keywords("caption-side", inherited: true, "top", ["top"], ["bottom"], "table-caption boxes", "CSS 2.1 §17.4.1");

        yield return Keywords("empty-cells", inherited: true, "show", ["show"], ["hide"], "table-cell boxes", "CSS 2.1 §17.6.1.1");

        yield return new PropertyDefinition(
            "list-style-type", inherited: true, new CssKeyword("disc"), "list items", string.Join(" | ", SupportedListStyles),
            PropertySupport.Supported, "CSS Lists 3 §3.3; CSS Counter Styles 3 §6",
            reader =>
            {
                if (reader.Count != 1)
                {
                    return ParsedValue.Invalid("list-style-type takes one value");
                }

                if (reader.Keyword(SupportedListStyles) is { } style)
                {
                    return ParsedValue.Of(new CssKeyword(style));
                }

                return reader.AnyKeyword() is { } other
                    ? ParsedValue.Refused($"the counter style '{other}' is not supported")
                    : reader.String() is not null
                        ? ParsedValue.Refused("a string as list marker is not supported")
                        : ParsedValue.Invalid("not a value of list-style-type");
            },
            AsSpecified);

        yield return Keywords("list-style-position", inherited: true, "outside", ["outside"], ["inside"], "list items", "CSS Lists 3 §3.5");

        yield return InitialKeyword("list-style-image", inherited: true, "none", [], otherValuesRefused: true, "list items", "CSS Lists 3 §3.4");
    }

    private static IEnumerable<PropertyDefinition> FragmentationProperties()
    {
        string[] refusedBreaks = ["left", "right", "recto", "verso", "column", "avoid-column", "region", "avoid-region", "all"];
        yield return Keywords("break-before", inherited: false, "auto", ["auto", "avoid", "avoid-page", "page", "always"], refusedBreaks,
            "block-level boxes, grid items, flex items, table row groups, table rows", "CSS Fragmentation 3 §3.1");
        yield return Keywords("break-after", inherited: false, "auto", ["auto", "avoid", "avoid-page", "page", "always"], refusedBreaks,
            "block-level boxes, grid items, flex items, table row groups, table rows", "CSS Fragmentation 3 §3.1");
        yield return Keywords("break-inside", inherited: false, "auto", ["auto", "avoid", "avoid-page"], ["avoid-column", "avoid-region"],
            "all elements except inline-level boxes, internal ruby boxes, table column boxes, table column group boxes, absolutely-positioned boxes",
            "CSS Fragmentation 3 §3.2");

        foreach (var name in new[] { "orphans", "widows" })
        {
            yield return new PropertyDefinition(
                name, inherited: true, new CssNumber(2, IsInteger: true), "block containers that establish an inline formatting context",
                "<integer [1,∞]>", PropertySupport.Supported, "CSS Fragmentation 3 §3.3",
                reader => reader.Count == 1 && reader.Integer() is { } count && count >= 1
                    ? ParsedValue.Of(new CssNumber(count, IsInteger: true))
                    : ParsedValue.Invalid($"{name} is an integer of at least 1"),
                AsSpecified);
        }

        yield return Keywords("box-decoration-break", inherited: false, "slice", ["slice", "clone"], [], "all elements", "CSS Fragmentation 3 §5.4");
    }

    /// <summary>border-spacing: one length for both directions, or horizontal then vertical.</summary>
    private static ParsedValue ParseBorderSpacing(ValueReader reader)
    {
        var lengths = new List<CssValue>();
        while (!reader.AtEnd && lengths.Count < 2)
        {
            if (reader.Length(percentages: false, nonNegative: true, "border-spacing") is not { } length)
            {
                return ParsedValue.Invalid("not a value of border-spacing");
            }

            if (!length.IsValid)
            {
                return length;
            }

            lengths.Add(length.Value!);
        }

        if (!reader.AtEnd || lengths.Count == 0)
        {
            return ParsedValue.Invalid("border-spacing takes one or two lengths");
        }

        return ParsedValue.Of(new CssList([lengths[0], lengths.Count == 2 ? lengths[1] : lengths[0]], CommaSeparated: false));
    }

    private static ComputedResult ComputeBorderSpacing(CssValue value, ComputeContext context)
    {
        var list = (CssList)value;
        var items = new List<CssValue>();
        foreach (var item in list.Items)
        {
            var computed = context.LengthPercentage(item);
            if (computed.Value is null)
            {
                return computed;
            }

            items.Add(computed.Value);
        }

        return new CssList([.. items], CommaSeparated: false);
    }
}
