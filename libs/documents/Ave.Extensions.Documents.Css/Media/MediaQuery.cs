using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Media;

/// <summary>A media query list (Media Queries 4 §3): it matches when one of its queries does; an empty list matches.</summary>
/// <param name="Queries">The queries; one that did not parse is <see cref="MediaQuery.NotAll"/> (§3.2).</param>
public sealed record MediaQueryList(ValueList<MediaQuery> Queries)
{
    /// <summary>The empty list, which matches every medium.</summary>
    public static MediaQueryList All { get; } = new([]);
}

/// <summary>A media query (Media Queries 4 §3): <c>[not | only]? type [and condition]?</c>, or a condition alone.</summary>
/// <param name="Negated">Whether it starts with <c>not</c>.</param>
/// <param name="Type">The media type, lowercase, or null when the query is a condition alone.</param>
/// <param name="Condition">The condition, or null.</param>
/// <param name="Position">Where it starts.</param>
public sealed record MediaQuery(bool Negated, string? Type, MediaCondition? Condition, TextPosition Position)
{
    /// <summary><c>not all</c>, what a query that does not parse becomes (§3.2).</summary>
    /// <param name="position">Where the query was.</param>
    /// <returns>The query.</returns>
    public static MediaQuery NotAll(TextPosition position) => new(true, "all", null, position);
}

/// <summary>A media condition (Media Queries 4 §3), evaluated in three-valued logic.</summary>
public abstract record MediaCondition;

/// <summary><c>not (…)</c>.</summary>
/// <param name="Operand">The negated condition.</param>
public sealed record MediaNot(MediaCondition Operand) : MediaCondition;

/// <summary><c>(…) and (…)</c>.</summary>
/// <param name="Operands">The conditions, all of which must hold.</param>
public sealed record MediaAnd(ValueList<MediaCondition> Operands) : MediaCondition;

/// <summary><c>(…) or (…)</c>.</summary>
/// <param name="Operands">The conditions, one of which must hold.</param>
public sealed record MediaOr(ValueList<MediaCondition> Operands) : MediaCondition;

/// <summary>A media feature test on width or height (§4.1, §4.2): plain (<c>width: 10cm</c>, <c>min-width</c>,
/// <c>max-width</c>), boolean (<c>(width)</c>) or range (<c>10cm &lt;= width &lt; 20cm</c>).</summary>
/// <param name="Feature">"width" or "height".</param>
/// <param name="Lower">The lower bound and whether it is inclusive, or null.</param>
/// <param name="Upper">The upper bound and whether it is inclusive, or null.</param>
/// <param name="Boolean">Whether it is the boolean form (true when the feature is not zero).</param>
public sealed record MediaFeatureTest(string Feature, (MediaLength Length, bool Inclusive)? Lower, (MediaLength Length, bool Inclusive)? Upper, bool Boolean) : MediaCondition;

/// <summary>A length in a media feature, resolved when the query is evaluated (Media Queries 4 §1.3: <c>em</c> and
/// <c>rem</c> are the initial font size, 16px; <c>vw</c> and <c>vh</c> the media context).</summary>
/// <param name="Value">The number.</param>
/// <param name="Unit">The unit, lowercase; empty for a unitless zero.</param>
public readonly record struct MediaLength(double Value, string Unit);

/// <summary>A condition whose value is unknown (§3.1): <c>&lt;general-enclosed&gt;</c>, or a feature the engine does
/// not evaluate (reported as unsupported when CSS defines it).</summary>
/// <param name="Text">What it was, for diagnostics.</param>
public sealed record MediaUnknown(string Text) : MediaCondition;

/// <summary>What media queries are evaluated against (Media Queries 4 §2.3, §4).</summary>
/// <param name="Type">The media type: <c>print</c> for a paged document.</param>
/// <param name="Width">The width the width feature has: for print in Chromium, the page area (the page box less its
/// margins).</param>
/// <param name="Height">The height, the same way.</param>
public sealed record MediaContext(string Type, Length Width, Length Height)
{
    /// <summary>Print on A4 with no margins.</summary>
    public static MediaContext PrintA4 { get; } = new("print", Length.FromMillimetres(210), Length.FromMillimetres(297));
}
