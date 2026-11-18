using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// The computed value of every registered longhand for one element (CSS Cascade 5 §4.4), and its custom properties
/// (CSS Variables 1 §2.2). Immutable; two styles with equal values are equal.
/// </summary>
public sealed class ComputedStyle : IEquatable<ComputedStyle>
{
    private readonly CssValue[] _values;

    internal ComputedStyle(CssValue[] values, IReadOnlyDictionary<string, CssTokens> custom)
    {
        _values = values;
        CustomProperties = custom;
    }

    /// <summary>The initial style: every property at its initial value, no custom properties.</summary>
    public static ComputedStyle Initial { get; } = new([.. PropertyRegistry.Longhands.Select(property => property.Initial)], new Dictionary<string, CssTokens>());

    /// <summary>The computed custom properties, by name (with the two hyphens).</summary>
    public IReadOnlyDictionary<string, CssTokens> CustomProperties { get; }

    /// <summary>The computed value of a longhand.</summary>
    public CssValue this[PropertyDefinition property] => _values[property.Index];

    /// <summary>The computed value of a longhand by name; throws for a name that is not a registered longhand.</summary>
    public CssValue this[string name] => PropertyRegistry.Find(name) is { } property
        ? _values[property.Index]
        : throw new ArgumentException($"'{name}' is not a registered longhand.", nameof(name));

    /// <inheritdoc/>
    public bool Equals(ComputedStyle? other) =>
        other is not null && _values.SequenceEqual(other._values)
        && CustomProperties.Count == other.CustomProperties.Count
        && CustomProperties.All(pair => other.CustomProperties.TryGetValue(pair.Key, out var value) && value.Text == pair.Value.Text);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ComputedStyle);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
