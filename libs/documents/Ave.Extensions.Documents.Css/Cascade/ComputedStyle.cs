using System;
using System.Collections.Frozen;
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

    /// <summary>Makes a style from the computed value of every longhand, by <see cref="PropertyDefinition.Index"/>, the
    /// custom properties and where the font size came from.</summary>
    internal ComputedStyle(CssValue[] values, IReadOnlyDictionary<string, CssTokens> custom, FontSizeSource fontSizeSource)
    {
        _values = values;
        CustomProperties = custom;
        FontSizeSource = fontSizeSource;
    }

    /// <summary>The initial style: every property at its initial value, no custom properties.</summary>
    public static ComputedStyle Initial { get; } = new([.. PropertyRegistry.Longhands.Select(property => property.Initial)], FrozenDictionary<string, CssTokens>.Empty, Properties.FontSizeSource.Medium);

    /// <summary>Where the font size comes from (<see cref="Properties.FontSizeSource"/>), for the children's sizes.</summary>
    internal FontSizeSource FontSizeSource { get; }

    /// <summary>The computed custom properties, by name (with the two hyphens).</summary>
    public IReadOnlyDictionary<string, CssTokens> CustomProperties { get; }

    /// <summary>The computed value of a longhand.</summary>
    public CssValue this[PropertyDefinition property] => _values[property.Index];

    /// <summary>The computed value of a longhand by name (ASCII case-insensitive), or null for a name that is not a
    /// registered longhand: a shorthand, a property ignored by rule, a custom property (see
    /// <see cref="CustomProperties"/>).</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or null.</returns>
    public CssValue? Find(string name) => PropertyRegistry.Find(name) is { } property ? _values[property.Index] : null;

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
