using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// Which of the OpenType GSUB features a text may switch are applied when it is shaped (CSS Fonts 4 §6, the font
/// variant properties, and §7.1, <c>font-feature-settings</c>); <c>ccmp</c> is always applied. <see cref="Default"/> is
/// what a text without settings gets.
/// </summary>
/// <param name="Ligatures">Whether the standard and contextual ligatures (<c>liga</c>, <c>clig</c>) are formed. Chromium
/// turns them off in text with a letter spacing other than zero, as CSS Text 3 §7.2 advises for optional ligatures where
/// the spacing between characters is not zero (measured, case <c>ls-letters</c>: "fix" and "office" print without the fi
/// and ffi ligatures).</param>
public sealed record ShapingFeatures(bool Ligatures = true)
{
    /// <summary>The features of a text without settings: composition and both kinds of ligature.</summary>
    public static ShapingFeatures Default { get; } = new();

    /// <summary>The optional features a text may switch on (<see cref="Enabled"/>): <see cref="OptionalFontFeatures.Tags"/>,
    /// each applied as the font's lookups for it say, by the lookup types the shaper applies (see <see cref="Shaper"/>).</summary>
    private static readonly FrozenSet<string> Optional = OptionalFontFeatures.Tags.ToFrozenSet(System.StringComparer.Ordinal);

    /// <summary>The optional features switched on, each of <see cref="OptionalFontFeatures.Tags"/>; empty unless set.</summary>
    public ValueList<string> Enabled { get; init; } = [];

    private static readonly FrozenSet<string> WithLigatures = new[] { "ccmp", "liga", "clig" }.ToFrozenSet();
    private static readonly FrozenSet<string> WithoutLigatures = new[] { "ccmp" }.ToFrozenSet();

    /// <summary>The GSUB feature tags applied; an enabled tag that is not optional is not.</summary>
    internal IReadOnlySet<string> Substitutions
    {
        get
        {
            var fixedSet = Ligatures ? WithLigatures : WithoutLigatures;
            if (Enabled.Count == 0)
            {
                return fixedSet;
            }

            var tags = new HashSet<string>(fixedSet, System.StringComparer.Ordinal);
            foreach (var tag in Enabled)
            {
                if (Optional.Contains(tag))
                {
                    tags.Add(tag);
                }
            }

            return tags;
        }
    }
}
