using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Css.Values;

/// <summary>
/// The length units (CSS Values 4 §6): the absolute ones as points, the font- and viewport-relative ones the engine
/// resolves, and the others, which are known and refused.
/// </summary>
public static class Units
{
    /// <summary>The absolute units in points (CSS Values 4 §6.2: 1in = 2.54cm = 25.4mm = 101.6Q = 72pt = 6pc = 96px).</summary>
    private static readonly FrozenDictionary<string, double> Absolute = new Dictionary<string, double>
    {
        ["px"] = 0.75,
        ["pt"] = 1,
        ["pc"] = 12,
        ["in"] = 72,
        ["cm"] = 72 / 2.54,
        ["mm"] = 72 / 25.4,
        ["q"] = 72 / 101.6,
    }.ToFrozenDictionary();

    /// <summary>The relative units the engine resolves: font-relative (CSS Values 4 §6.1.1) and viewport-relative
    /// (§6.1.2), through the values' context.</summary>
    public static FrozenSet<string> Relative { get; } = FrozenSet.Create("em", "rem", "ex", "ch", "vw", "vh");

    /// <summary>The relative units CSS defines and the engine does not resolve (refused).</summary>
    public static FrozenSet<string> UnsupportedRelative { get; } = FrozenSet.Create(
        "cap", "ic", "lh", "rlh", "rcap", "rch", "rex", "ric", "vmin", "vmax", "vi", "vb",
        "svw", "svh", "svi", "svb", "svmin", "svmax", "lvw", "lvh", "lvi", "lvb", "lvmin", "lvmax",
        "dvw", "dvh", "dvi", "dvb", "dvmin", "dvmax", "cqw", "cqh", "cqi", "cqb", "cqmin", "cqmax");

    /// <summary>The number of points in one of an absolute unit, or null when the unit is not absolute.</summary>
    /// <param name="unit">The unit, any case.</param>
    /// <returns>Points per unit, or null.</returns>
    public static double? PointsPer(string unit) => Absolute.TryGetValue(Ascii.ToLower(unit), out var points) ? points : null;

    /// <summary>Whether a unit is one of the relative units the engine resolves.</summary>
    public static bool IsResolvedRelative(string unit) => Relative.Contains(Ascii.ToLower(unit));

    /// <summary>Whether a unit is a length unit CSS defines that the engine refuses.</summary>
    public static bool IsUnsupportedLength(string unit) => UnsupportedRelative.Contains(Ascii.ToLower(unit));
}
