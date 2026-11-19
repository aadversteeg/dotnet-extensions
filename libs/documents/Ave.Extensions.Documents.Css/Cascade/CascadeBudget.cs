using System;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// The limits of one cascade and what it has spent of them (<see cref="CssInputLimits"/>): style sheets taken in, index
/// entries, units of work, remembered entries and substituted characters. One budget serves one document: the index
/// that is built for it and the computer that styles it. The limits are the constants; tests make a budget with
/// smaller ones to reach them with small inputs, and read what was spent.
/// </summary>
internal sealed class CascadeBudget
{
    /// <summary>A budget for the next index built on this thread, for tests: taken (and cleared) by
    /// <see cref="CascadeIndex.Build"/>, so a test can count the work of a whole resolution through the public API.</summary>
    [ThreadStatic]
    internal static CascadeBudget? ForNextIndex;

    /// <summary>The most style sheets (<see cref="CssInputLimits.MaxStyleSheets"/>).</summary>
    public int MaxStyleSheets { get; init; } = CssInputLimits.MaxStyleSheets;

    /// <summary>The most index entries (<see cref="CssInputLimits.MaxCascadeEntries"/>).</summary>
    public int MaxEntries { get; init; } = CssInputLimits.MaxCascadeEntries;

    /// <summary>The most work (<see cref="CssInputLimits.MaxCascadeWork"/>).</summary>
    public long MaxWork { get; init; } = CssInputLimits.MaxCascadeWork;

    /// <summary>The most remembered entries (<see cref="CssInputLimits.MaxCascadeMemo"/>).</summary>
    public long MaxMemo { get; init; } = CssInputLimits.MaxCascadeMemo;

    /// <summary>The most substituted characters (<see cref="CssInputLimits.MaxSubstitutionWork"/>).</summary>
    public long MaxSubstitution { get; init; } = CssInputLimits.MaxSubstitutionWork;

    /// <summary>Style sheets taken in so far.</summary>
    public int StyleSheets { get; private set; }

    /// <summary>Index entries so far.</summary>
    public long Entries { get; private set; }

    /// <summary>Units of work so far.</summary>
    public long Work { get; private set; }

    /// <summary>Entries remembered so far.</summary>
    public long Memo { get; private set; }

    /// <summary>Characters substituted so far.</summary>
    public long Substituted { get; private set; }

    /// <summary>Whether the work limit has been passed: the cascade stops.</summary>
    public bool Exhausted => Work > MaxWork;

    /// <summary>Takes in one more style sheet; false past the limit.</summary>
    public bool TakeStyleSheet() => ++StyleSheets <= MaxStyleSheets;

    /// <summary>Adds index entries; false past the limit.</summary>
    public bool AddEntries(long entries)
    {
        Entries += entries;
        return Entries <= MaxEntries;
    }

    /// <summary>Charges units of work; false once the limit is passed.</summary>
    public bool Charge(long units)
    {
        Work += units;
        return Work <= MaxWork;
    }

    /// <summary>Asks to remember entries: true, and counted, while the memo has room; false (nothing counted) when it is
    /// full, and the caller computes again later instead.</summary>
    public bool Remember(long entries)
    {
        if (Memo + entries > MaxMemo)
        {
            return false;
        }

        Memo += entries;
        return true;
    }

    /// <summary>Charges substituted characters; false once the limit is passed.</summary>
    public bool Substitute(long characters)
    {
        Substituted += characters;
        return Substituted <= MaxSubstitution;
    }

    /// <summary>The characters one unit of work compares.</summary>
    internal const int CharactersPerUnit = 64;

    /// <summary>The units a comparison of <paramref name="characters"/> characters costs beyond its first: one per
    /// <see cref="CharactersPerUnit"/>.</summary>
    public static long CharactersCost(long characters) => characters / CharactersPerUnit;
}
