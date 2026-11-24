using System.Collections.Frozen;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// Which of the OpenType GSUB features a text may switch are applied when it is shaped (CSS Fonts 4 §6, the font
/// variant properties); <c>ccmp</c> is always applied. <see cref="Default"/> is what a text without settings gets.
/// </summary>
/// <param name="Ligatures">Whether the standard and contextual ligatures (<c>liga</c>, <c>clig</c>) are formed. Chromium
/// turns them off in text with a letter spacing other than zero (CSS Text 3 §7.2: "When the effective spacing between two
/// characters is not zero, user agents should not apply optional ligatures"; measured, case <c>ls-letters</c>: "fix" and
/// "office" print without the fi and ffi ligatures).</param>
public sealed record ShapingFeatures(bool Ligatures = true)
{
    /// <summary>The features of a text without settings: composition and both kinds of ligature.</summary>
    public static ShapingFeatures Default { get; } = new();

    private static readonly FrozenSet<string> WithLigatures = new[] { "ccmp", "liga", "clig" }.ToFrozenSet();
    private static readonly FrozenSet<string> WithoutLigatures = new[] { "ccmp" }.ToFrozenSet();

    /// <summary>The GSUB feature tags applied.</summary>
    internal FrozenSet<string> Substitutions => Ligatures ? WithLigatures : WithoutLigatures;
}
