using System;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>What the engine does with a property.</summary>
public enum PropertySupport
{
    /// <summary>The values listed in <see cref="PropertyDefinition.Supported"/> are implemented.</summary>
    Supported,

    /// <summary>Only the initial value is implemented (it is what the engine always does): any other value is refused.
    /// Such a property is registered so that resets and user-agent rules that set it to its initial value are not
    /// refused.</summary>
    InitialOnly,
}

/// <summary>
/// One longhand property, defined once (the property registry): its name, whether it is inherited, its initial value,
/// what it applies to, the values the engine supports, how a value is parsed and how its computed value is made. Stage
/// 4 reads the same definitions for the code front-end's cascade, so the two front-ends cannot disagree on a property's
/// facts.
/// </summary>
public sealed class PropertyDefinition
{
    internal PropertyDefinition(
        string name,
        bool inherited,
        CssValue initial,
        string appliesTo,
        string supported,
        PropertySupport support,
        string specification,
        Func<ValueReader, ParsedValue> parse,
        Func<CssValue, ComputeContext, ComputedResult> compute)
    {
        Name = name;
        Inherited = inherited;
        Initial = initial;
        AppliesTo = appliesTo;
        Supported = supported;
        Support = support;
        Specification = specification;
        Parse = parse;
        Compute = compute;
    }

    /// <summary>The property name, lowercase.</summary>
    public string Name { get; }

    /// <summary>Whether the property is inherited (CSS Cascade 5 §7.2).</summary>
    public bool Inherited { get; }

    /// <summary>The initial value, as a computed value (CSS Cascade 5 §7.1).</summary>
    public CssValue Initial { get; }

    /// <summary>What the property applies to, as the defining specification says.</summary>
    public string AppliesTo { get; }

    /// <summary>The values the engine supports, in the value definition syntax of CSS Values 4 §2 (for the profile
    /// document); everything else valid for the property is refused.</summary>
    public string Supported { get; }

    /// <summary>Whether all supported values are implemented or only the initial value.</summary>
    public PropertySupport Support { get; }

    /// <summary>The specification and section that defines the property.</summary>
    public string Specification { get; }

    /// <summary>The position of the property in the registry (an index into a computed style).</summary>
    internal int Index { get; set; }

    /// <summary>Parses a specified value (white space removed, no var(), no CSS-wide keyword).</summary>
    internal Func<ValueReader, ParsedValue> Parse { get; }

    /// <summary>Makes the computed value from a specified value (CSS Cascade 5 §4.4).</summary>
    internal Func<CssValue, ComputeContext, ComputedResult> Compute { get; }

    /// <summary>The name.</summary>
    public override string ToString() => Name;
}

/// <summary>A shorthand property (CSS Cascade 5 §1.2): it sets several longhands at once.</summary>
public sealed class ShorthandDefinition
{
    internal ShorthandDefinition(string name, string[] longhands, string supported, string specification, Func<ValueReader, ShorthandParse> parse)
    {
        Name = name;
        Longhands = longhands;
        Supported = supported;
        Specification = specification;
        Parse = parse;
    }

    /// <summary>The shorthand's name, lowercase.</summary>
    public string Name { get; }

    /// <summary>The longhands it sets (every one of them, also those it resets to their initial value).</summary>
    public System.Collections.Generic.IReadOnlyList<string> Longhands { get; }

    /// <summary>The values the engine supports, for the profile document.</summary>
    public string Supported { get; }

    /// <summary>The specification and section that defines it.</summary>
    public string Specification { get; }

    /// <summary>Parses the shorthand into its longhands' specified values.</summary>
    internal Func<ValueReader, ShorthandParse> Parse { get; }

    /// <summary>The name.</summary>
    public override string ToString() => Name;
}

/// <summary>A property that has no effect in a static paged document: declarations of it are ignored by rule.</summary>
/// <param name="Name">The property name, lowercase.</param>
/// <param name="Reason">Why it has no effect.</param>
public sealed record IgnoredProperty(string Name, string Reason);

/// <summary>The result of parsing a value: a value, invalid CSS, or valid CSS the engine refuses.</summary>
/// <param name="Value">The specified value, when valid and supported.</param>
/// <param name="Problem">Why it is invalid or unsupported.</param>
/// <param name="Unsupported">Whether it is valid but unsupported (rather than invalid).</param>
internal readonly record struct ParsedValue(CssValue? Value, string? Problem, bool Unsupported)
{
    /// <summary>For a refused value, the diagnostic code when it is more precise than
    /// <see cref="CssErrorCodes.Unsupported.Value"/> (a math or colour function).</summary>
    public ErrorCode? Code { get; init; }

    public static ParsedValue Of(CssValue value) => new(value, null, false);

    public static ParsedValue Invalid(string problem) => new(null, problem, false);

    public static ParsedValue Refused(string problem) => new(null, problem, true);

    public bool IsValid => Value is not null;
}

/// <summary>The longhands a shorthand sets, or why it cannot.</summary>
/// <param name="Values">Each longhand's specified value, by name.</param>
/// <param name="Problem">Why it is invalid or unsupported.</param>
/// <param name="Unsupported">Whether it is valid but unsupported.</param>
internal readonly record struct ShorthandParse(System.Collections.Generic.IReadOnlyDictionary<string, CssValue>? Values, string? Problem, bool Unsupported)
{
    /// <summary>For a refused value, the more precise diagnostic code, as for <see cref="ParsedValue.Code"/>.</summary>
    public ErrorCode? Code { get; init; }

    /// <summary>The failure of a longhand value read inside the shorthand, kept as it is (invalid or refused, with its
    /// code).</summary>
    public static ShorthandParse Failed(ParsedValue value, string? problem = null) =>
        new(null, value.Problem ?? problem ?? "not a value", value.Unsupported) { Code = value.Code };

    public static ShorthandParse Invalid(string problem) => new(null, problem, false);

    public static ShorthandParse Refused(string problem) => new(null, problem, true);
}

/// <summary>A computed value, or why the specified value cannot be computed by the engine (a unit it cannot resolve
/// without font metrics, a calc() that keeps a length and a percentage).</summary>
/// <param name="Value">The computed value.</param>
/// <param name="Unsupported">Why it is refused, or null.</param>
internal readonly record struct ComputedResult(CssValue? Value, string? Unsupported)
{
    public static implicit operator ComputedResult(CssValue value) => new(value, null);

    public static ComputedResult Refused(string why) => new(null, why);
}
