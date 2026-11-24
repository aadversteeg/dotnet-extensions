using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Values;

/// <summary>One counter named in <c>counter-reset</c>, <c>counter-increment</c> or <c>counter-set</c> (CSS Lists 3 §4.1):
/// its name and the integer that goes with it, the default when none is written (0 for a reset or a set, 1 for an
/// increment).</summary>
/// <param name="Name">The counter's name, a custom identifier compared as written (CSS Values 4 §4.2).</param>
/// <param name="Value">The integer, within 32 bits.</param>
public sealed record CssCounterChange(string Name, int Value)
{
    /// <summary>Formats as CSS.</summary>
    public override string ToString() => $"{Name} {Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The computed value of <c>counter-reset</c>, <c>counter-increment</c> or <c>counter-set</c> other than <c>none</c>
/// (CSS Lists 3 §4.1): the counters in the order written, a name written twice twice (Chromium 159, measured: case
/// <c>gc-counter-order</c>, an increment of <c>n 3 n 4</c> adds 7, a reset of <c>q 1 q 5</c> leaves 5). The value keeps
/// the place of the declaration it came from, where a change that leaves 32 bits is refused.
/// </summary>
/// <param name="Changes">The counters, at most <see cref="CssInputLimits.MaxCounterNames"/>.</param>
public sealed record CssCounterChanges(ValueList<CssCounterChange> Changes) : CssValue
{
    /// <summary>The style sheet of the declaration; null until the cascade computes the value.</summary>
    public string? SourceName { get; init; }

    /// <summary>Where the declaration is written; the default until the cascade computes the value.</summary>
    public TextPosition Position { get; init; }

    /// <summary>Formats as CSS.</summary>
    public override string ToString() => string.Join(" ", System.Linq.Enumerable.Select(Changes, change => change.ToString()));
}
