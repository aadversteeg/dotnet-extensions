using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// The part GSUB and GPOS share (OpenType spec, "OpenType Layout Common Table Formats"): a script list that says,
/// per script and language system, which features exist; a feature list that says which lookups implement each
/// feature; and the lookup list itself.
/// </summary>
/// <param name="Scripts">The script list.</param>
/// <param name="Features">The feature list, indexed by the language systems.</param>
/// <param name="Lookups">The lookup list, indexed by the features.</param>
public sealed record LayoutTable(ValueList<ScriptTable> Scripts, ValueList<FeatureTable> Features, ValueList<Lookup> Lookups)
{
    /// <summary>The table of a font that has none: no scripts, features or lookups.</summary>
    public static LayoutTable Empty { get; } = new([], [], []);

    /// <summary>The default script tag, used when the font has no entry for the text's script.</summary>
    public const string DefaultScript = "DFLT";

    /// <summary>
    /// The lookups that implement <paramref name="features"/> for a script, in lookup-list order: "During text
    /// processing, a client applies a lookup to each glyph in the string before moving to the next lookup", and
    /// lookups are taken in the order of the lookup list (OpenType spec, "Lookup list table"). The script falls back
    /// to <see cref="DefaultScript"/> when the font does not list it ("Script list table"); the language system is
    /// the script's default one, and its required feature, if any, is always included ("Language system table").
    /// </summary>
    /// <param name="script">The OpenType script tag, for example <c>latn</c>.</param>
    /// <param name="features">The feature tags to apply, for example <c>liga</c>.</param>
    /// <returns>The lookups; empty when the font has neither the script nor a default script.</returns>
    public ImmutableArray<Lookup> LookupsFor(string script, IReadOnlySet<string> features)
    {
        var table = Scripts.FirstOrDefault(found => found.Tag == script) ?? Scripts.FirstOrDefault(found => found.Tag == DefaultScript);
        if (table?.DefaultLanguage is not { } language)
        {
            return [];
        }

        var featureIndices = language.FeatureIndices
            .Where(index => index < Features.Count && features.Contains(Features[index].Tag))
            .Concat(language.RequiredFeature is { } required && required < Features.Count ? [required] : []);
        return [.. featureIndices
            .SelectMany(index => Features[index].LookupIndices)
            .Where(index => index < Lookups.Count)
            .Distinct()
            .Order()
            .Select(index => Lookups[index])];
    }
}

/// <summary>A script table: its tag and its default language system (OpenType spec, "Script table").</summary>
/// <param name="Tag">The script tag, for example <c>latn</c> or <c>DFLT</c>.</param>
/// <param name="DefaultLanguage">The default language system, or null when the font has none for this script.</param>
public sealed record ScriptTable(string Tag, LanguageSystem? DefaultLanguage);

/// <summary>A language system table (OpenType spec, "Language system table").</summary>
/// <param name="RequiredFeature">The index of the feature that is always applied, or null ("0xFFFF if no
/// required features").</param>
/// <param name="FeatureIndices">The indices of its features in the feature list.</param>
public sealed record LanguageSystem(int? RequiredFeature, ValueList<int> FeatureIndices);

/// <summary>A feature (OpenType spec, "Feature table"): its tag and the lookups that implement it.</summary>
/// <param name="Tag">The feature tag, for example <c>kern</c>.</param>
/// <param name="LookupIndices">Indices into the lookup list.</param>
public sealed record FeatureTable(string Tag, ValueList<int> LookupIndices);

/// <summary>A lookup (OpenType spec, "Lookup table"): subtables of one lookup type, tried in order.</summary>
/// <param name="Index">Its index in the lookup list.</param>
/// <param name="Flags">The lookup flags: which glyphs the lookup skips.</param>
/// <param name="Subtables">The subtables, already resolved through extension subtables.</param>
public sealed record Lookup(int Index, LookupFlags Flags, ValueList<LookupSubtable> Subtables);
