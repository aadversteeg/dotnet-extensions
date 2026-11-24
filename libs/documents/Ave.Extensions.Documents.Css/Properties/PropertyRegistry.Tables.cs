using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>Table, list and fragmentation properties.</summary>
public static partial class PropertyRegistry
{
    /// <summary>The list-style-type values the engine draws (CSS Counter Styles 3 §6 predefined styles).</summary>
    internal static readonly string[] SupportedListStyles =
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

        yield return Keywords("caption-side", inherited: true, "top", ["top", "bottom"], ["block-start", "block-end", "inline-start", "inline-end", "left", "right"], "table-caption boxes", "CSS 2.1 §17.4.1");

        yield return Keywords("empty-cells", inherited: true, "show", ["show", "hide"], [], "table-cell boxes", "CSS 2.1 §17.6.1.1");

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
                    ? ParsedValue.Refused($"the counter style '{CssSerializer.Shortened(other)}' is not supported")
                    : reader.String() is not null
                        ? ParsedValue.Refused("a string as list marker is not supported")
                        : ParsedValue.Invalid("not a value of list-style-type");
            },
            AsSpecified);

        yield return Keywords("list-style-position", inherited: true, "outside", ["outside"], ["inside"], "list items", "CSS Lists 3 §3.5");

        yield return InitialKeyword("list-style-image", inherited: true, "none", [], otherValuesRefused: true, "list items", "CSS Lists 3 §3.4");

        yield return CounterProperty("counter-reset", 0, "CSS Lists 3 §4.2");
        yield return CounterProperty("counter-increment", 1, "CSS Lists 3 §4.3");
        yield return CounterProperty("counter-set", 0, "CSS Lists 3 §4.4");
    }

    /// <summary>
    /// <c>counter-reset</c>, <c>counter-increment</c> or <c>counter-set</c> (CSS Lists 3 §4.1): <c>none</c>, or counters
    /// by name, each with an integer or the default (0 for a reset and a set, 1 for an increment). A name may not be
    /// <c>none</c> or a CSS-wide keyword (invalid, CSS Values 4 §4.2). Refused at the declaration: the
    /// <c>list-item</c> counter (the numbers of a list follow the list's own rules here), an integer beyond 32 bits
    /// (Chromium 159 clamps it, measured: <c>counter-reset: n 2147483648</c> prints 2147483647), more than
    /// <see cref="CssInputLimits.MaxCounterNames"/> counters, and <c>reversed()</c>.
    /// </summary>
    private static PropertyDefinition CounterProperty(string name, int defaultValue, string specification) =>
        new(name, inherited: false, new CssKeyword("none"), "all elements, ::before, ::after",
            "none | [<custom-ident> <integer>?]+ (a name other than list-item, within 32 bits, at most 16 names)",
            PropertySupport.Supported, specification,
            reader =>
            {
                if (reader.Count == 1 && reader.Keyword("none") is not null)
                {
                    return ParsedValue.Of(new CssKeyword("none"));
                }

                var changes = new List<CssCounterChange>();
                while (!reader.AtEnd)
                {
                    if (reader.Peek is FunctionValue function)
                    {
                        return function.IsNamed("reversed")
                            ? ParsedValue.Refused($"'{CssSerializer.Shortened([function])}' is not drawn yet: a counter that counts down is not; reset it to its first number")
                            : ParsedValue.Invalid($"not a value of {name}");
                    }

                    if (reader.Peek is not CssToken { Kind: CssTokenKind.Ident } identifier)
                    {
                        return ParsedValue.Invalid($"not a value of {name}");
                    }

                    reader.Next();
                    var counter = identifier.Text;
                    if (InfraAscii.ToLower(counter) is "none" or "initial" or "inherit" or "unset" or "default" or "revert" or "revert-layer")
                    {
                        return ParsedValue.Invalid($"'{CssSerializer.Shortened(counter)}' is not a counter name");
                    }

                    var mark = reader.Mark;
                    var value = reader.Integer();
                    if (value is null && reader.Mark != mark)
                    {
                        return ParsedValue.Invalid($"not a value of {name}");
                    }

                    if (counter == "list-item")
                    {
                        return ParsedValue.Refused($"'{name}' of list-item is not drawn yet: the numbers of a list follow its own rules here (ol start, reversed, li value); write them there");
                    }

                    if (value is > int.MaxValue or < int.MinValue)
                    {
                        return ParsedValue.Refused($"the value of the counter '{CssSerializer.Shortened(counter)}' is beyond a 32-bit integer, where a browser clamps it; write a number within ±2147483647");
                    }

                    changes.Add(new CssCounterChange(counter, (int)(value ?? defaultValue)));
                    if (changes.Count > CssInputLimits.MaxCounterNames)
                    {
                        return ParsedValue.Refused($"it names more than {CssInputLimits.MaxCounterNames} counters; split the declaration over the elements");
                    }
                }

                return changes.Count == 0 ? ParsedValue.Invalid($"not a value of {name}") : ParsedValue.Of(new CssCounterChanges([.. changes]));
            },
            AsSpecified);

    private static IEnumerable<PropertyDefinition> FragmentationProperties()
    {
        // The values Chromium 159 takes (measured): always, all and the region values of CSS Fragmentation 4 are
        // invalid there, so they are invalid here too.
        string[] refusedBreaks = ["left", "right", "recto", "verso", "column", "avoid-column"];
        yield return Keywords("break-before", inherited: false, "auto", ["auto", "avoid", "avoid-page", "page"], refusedBreaks,
            "block-level boxes, grid items, flex items, table row groups, table rows", "CSS Fragmentation 3 §3.1");
        yield return Keywords("break-after", inherited: false, "auto", ["auto", "avoid", "avoid-page", "page"], refusedBreaks,
            "block-level boxes, grid items, flex items, table row groups, table rows", "CSS Fragmentation 3 §3.1");
        yield return Keywords("break-inside", inherited: false, "auto", ["auto", "avoid", "avoid-page"], ["avoid-column"],
            "all elements except inline-level boxes, internal ruby boxes, table column boxes, table column group boxes, absolutely-positioned boxes",
            "CSS Fragmentation 3 §3.2");

        foreach (var name in new[] { "orphans", "widows" })
        {
            yield return new PropertyDefinition(
                name, inherited: true, new CssNumber(2, IsInteger: true), "block containers that establish an inline formatting context",
                $"<integer [1,{MaxShortInteger}]>", PropertySupport.Supported, "CSS Fragmentation 3 §3.3",
                reader => reader.Count == 1 && reader.Integer() is { } count && count >= 1
                    ? count <= MaxShortInteger
                        ? ParsedValue.Of(new CssNumber(count, IsInteger: true))
                        : ParsedValue.Refused($"{name} above {MaxShortInteger}: Chromium 159 clamps it to {MaxShortInteger} (measured)")
                    : ParsedValue.Invalid($"{name} is an integer of at least 1"),
                AsSpecified);
        }

        yield return Keywords("box-decoration-break", inherited: false, "slice", ["slice", "clone"], [], "all elements", "CSS Fragmentation 3 §5.4");
    }

    /// <summary>The largest orphans and widows Chromium keeps: it stores them in 16 bits.</summary>
    private const int MaxShortInteger = 32767;

    /// <summary>border-spacing: one length for both directions, or horizontal then vertical (CSS 2 §17.6.1).</summary>
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

    /// <summary>The computed border-spacing: two absolute lengths, horizontal then vertical (CSS 2 §17.6.1).</summary>
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
