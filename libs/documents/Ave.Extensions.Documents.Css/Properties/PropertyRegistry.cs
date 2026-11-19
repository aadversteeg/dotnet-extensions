using Ave.Extensions.Documents.Core;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// The one property registry: every longhand the engine knows (supported, or supported at its initial value only),
/// every shorthand, and the properties ignored by rule. A property in none of these lists is unsupported. The registry
/// is the v0 boundary of the HTML front-end (docs/html-css-profile.md is generated from it), and stage 4 reads the
/// same per-property facts for the code front-end.
/// </summary>
/// <remarks>The definitions are grouped in partial files by subject (<c>PropertyRegistry.Box.cs</c>, <c>.Text.cs</c>,
/// <c>.Fonts.cs</c>, <c>.Tables.cs</c>, <c>.InitialOnly.cs</c>, <c>.Shorthands.cs</c>, <c>.Ignored.cs</c>); one static
/// constructor collects them, so their order does not depend on the order of field initialisers across files.</remarks>
public static partial class PropertyRegistry
{
    /// <summary>The initial font size, <c>medium</c>: 16px = 12pt (CSS Fonts 4 §2.5, HTML Standard §15.3.4).</summary>
    public const double InitialFontSizePoints = 12;

    private static readonly FrozenDictionary<string, PropertyDefinition> ByName;
    private static readonly FrozenDictionary<string, ShorthandDefinition> ShorthandsByName;
    private static readonly FrozenDictionary<string, IgnoredProperty> IgnoredByName;

    static PropertyRegistry()
    {
        var longhands = new List<PropertyDefinition>();
        longhands.AddRange(BoxProperties());
        longhands.AddRange(TextProperties());
        longhands.AddRange(FontProperties());
        longhands.AddRange(TableAndListProperties());
        longhands.AddRange(FragmentationProperties());
        longhands.AddRange(InitialOnlyProperties());
        for (var i = 0; i < longhands.Count; i++)
        {
            longhands[i].Index = i;
        }

        // Read-only views: a cast cannot change the registry for the whole process.
        Longhands = longhands.AsReadOnly();
        ByName = longhands.ToFrozenDictionary(property => property.Name, StringComparer.Ordinal);
        Shorthands = Array.AsReadOnly([.. ShorthandProperties()]);
        ShorthandsByName = Shorthands.ToFrozenDictionary(shorthand => shorthand.Name, StringComparer.Ordinal);
        Ignored = Array.AsReadOnly([.. IgnoredProperties()]);
        IgnoredByName = Ignored.ToFrozenDictionary(ignored => ignored.Name, StringComparer.Ordinal);

        FontSize = ByName["font-size"];
        FontFamily = ByName["font-family"];
        FontWeight = ByName["font-weight"];
        FontStyle = ByName["font-style"];
        LineHeight = ByName["line-height"];
        Color = ByName["color"];
        Display = ByName["display"];
        TextAlign = ByName["text-align"];
    }

    /// <summary>Every longhand, in registry order.</summary>
    public static IReadOnlyList<PropertyDefinition> Longhands { get; }

    /// <summary>Every shorthand.</summary>
    public static IReadOnlyList<ShorthandDefinition> Shorthands { get; }

    /// <summary>The properties ignored by rule.</summary>
    public static IReadOnlyList<IgnoredProperty> Ignored { get; }

    /// <summary>font-size, which every other property's <c>em</c> needs first.</summary>
    public static PropertyDefinition FontSize { get; }

    /// <summary>font-family.</summary>
    public static PropertyDefinition FontFamily { get; }

    /// <summary>font-weight.</summary>
    public static PropertyDefinition FontWeight { get; }

    /// <summary>font-style.</summary>
    public static PropertyDefinition FontStyle { get; }

    /// <summary>line-height.</summary>
    public static PropertyDefinition LineHeight { get; }

    /// <summary>color.</summary>
    public static PropertyDefinition Color { get; }

    /// <summary>display.</summary>
    public static PropertyDefinition Display { get; }

    /// <summary>text-align.</summary>
    public static PropertyDefinition TextAlign { get; }

    /// <summary>The longhand of this name (any case), or null.</summary>
    public static PropertyDefinition? Find(string name) => ByName.TryGetValue(InfraAscii.ToLower(name), out var property) ? property : null;

    /// <summary>The shorthand of this name (any case), or null.</summary>
    public static ShorthandDefinition? FindShorthand(string name) => ShorthandsByName.TryGetValue(InfraAscii.ToLower(name), out var shorthand) ? shorthand : null;

    /// <summary>The ignored property of this name (any case), or null.</summary>
    public static IgnoredProperty? FindIgnored(string name) => IgnoredByName.TryGetValue(InfraAscii.ToLower(name), out var ignored) ? ignored : null;

    // ---- helpers for the definitions ----

    private static readonly Func<CssValue, ComputeContext, ComputedResult> AsSpecified = (value, _) => value;

    /// <summary>A property whose values are keywords: the supported ones, and those CSS defines that are refused.</summary>
    private static PropertyDefinition Keywords(string name, bool inherited, string initial, string[] supported, string[] refused, string appliesTo, string specification) =>
        new(name, inherited, new CssKeyword(initial), appliesTo, string.Join(" | ", supported), PropertySupport.Supported, specification,
            reader => OneKeyword(reader, name, supported, refused),
            AsSpecified);

    /// <summary>Exactly one keyword: supported, refused, or not a value of the property.</summary>
    private static ParsedValue OneKeyword(ValueReader reader, string name, string[] supported, string[] refused)
    {
        if (reader.Count != 1)
        {
            return ParsedValue.Invalid($"{name} takes one keyword");
        }

        if (reader.Keyword(supported) is { } keyword)
        {
            return ParsedValue.Of(new CssKeyword(keyword));
        }

        return reader.Keyword(refused) is { } other
            ? ParsedValue.Refused($"'{name}: {CssSerializer.Shortened(other)}' is not supported")
            : ParsedValue.Invalid($"not a value of {name}");
    }

    /// <summary>A property the engine implements only at its initial value, a keyword. <paramref name="others"/> are the
    /// other keywords CSS defines (refused); any other value is refused too when <paramref name="otherValuesRefused"/>
    /// (the property has values beyond keywords, which this definition does not check), else invalid.</summary>
    private static PropertyDefinition InitialKeyword(string name, bool inherited, string initial, string[] others, bool otherValuesRefused, string appliesTo, string specification) =>
        new(name, inherited, new CssKeyword(initial), appliesTo, initial, PropertySupport.InitialOnly, specification,
            reader =>
            {
                if (reader.Count == 1 && reader.Keyword(initial) is not null)
                {
                    return ParsedValue.Of(new CssKeyword(initial));
                }

                if (reader.Count == 1 && reader.Keyword(others) is { } other)
                {
                    return ParsedValue.Refused($"'{name}: {CssSerializer.Shortened(other)}' is not supported (only {initial} is)");
                }

                return otherValuesRefused ? ParsedValue.Refused($"'{name}' is supported only at its initial value, {initial}") : ParsedValue.Invalid($"not a value of {name}");
            },
            AsSpecified);

    /// <summary>A property the engine implements only at its initial value, a length (zero) or a keyword that means
    /// zero; every other value is refused (it is not checked for validity).</summary>
    private static PropertyDefinition InitialZero(string name, bool inherited, string initialKeyword, string appliesTo, string specification) =>
        new(name, inherited, initialKeyword == "0" ? new CssLength(Ave.Extensions.Documents.Core.Length.Zero) : new CssKeyword(initialKeyword), appliesTo,
            initialKeyword, PropertySupport.InitialOnly, specification,
            reader =>
            {
                if (initialKeyword != "0" && reader.Keyword(initialKeyword) is not null && reader.AtEnd)
                {
                    return ParsedValue.Of(new CssKeyword(initialKeyword));
                }

                var mark = reader.Mark;
                if (reader.Length(percentages: true, nonNegative: false, name) is { IsValid: true, Value: CssLength { Points: 0 } } && reader.AtEnd)
                {
                    return ParsedValue.Of(initialKeyword == "0" ? new CssLength(Ave.Extensions.Documents.Core.Length.Zero) : new CssKeyword(initialKeyword));
                }

                reader.Reset(mark);
                return ParsedValue.Refused($"'{name}' is supported only at its initial value, {initialKeyword}");
            },
            AsSpecified);
}
