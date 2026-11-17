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
    /// lookups are taken in the order of the lookup list (OpenType spec, "LookupList table"). When the font does not
    /// list the script, it falls back as HarfBuzz does (hb-ot-layout.cc, <c>hb_ot_layout_table_select_script</c>):
    /// to <see cref="DefaultScript"/> (OpenType spec, "ScriptList table": the DFLT script table is the one to use when the
    /// text's script has none), then to <c>dflt</c>, which many fonts use by mistake, then to <c>latn</c>, where
    /// some old fonts put all their features. The language system is the script's default one, and its required
    /// feature, if any, is always included ("Language system table").
    /// </summary>
    /// <param name="script">The OpenType script tag, for example <c>latn</c>.</param>
    /// <param name="features">The feature tags to apply, for example <c>liga</c>.</param>
    /// <returns>The lookups; empty when the font has none of the scripts tried.</returns>
    public ImmutableArray<Lookup> LookupsFor(string script, IReadOnlySet<string> features)
    {
        return [.. SelectedFeatures(script, features)
            .SelectMany(index => Features[index].LookupIndices)
            .Where(index => index < Lookups.Count)
            .Distinct()
            .Order()
            .Select(index => Lookups[index])];
    }

    /// <summary>
    /// The features of <see cref="LookupsFor"/>: the script's default language system's features whose tags are asked
    /// for, and its required feature, each feature index once. A language system may list one feature index many
    /// times, and walking its lookup list once per listing would multiply the work by numbers the file chooses.
    /// </summary>
    /// <param name="script">The OpenType script tag.</param>
    /// <param name="features">The feature tags to apply.</param>
    /// <returns>The feature indices, each once, in the order the language system lists them.</returns>
    public IEnumerable<int> SelectedFeatures(string script, IReadOnlySet<string> features)
    {
        var table = FallbackOrder(script).Select(tag => Scripts.FirstOrDefault(found => found.Tag == tag)).FirstOrDefault(found => found is not null);
        if (table?.DefaultLanguage is not { } language)
        {
            return [];
        }

        return language.FeatureIndices
            .Where(index => index < Features.Count && features.Contains(Features[index].Tag))
            .Concat(language.RequiredFeature is { } required && required < Features.Count ? [required] : [])
            .Distinct();
    }

    /// <summary>The script tags tried in turn for <paramref name="script"/> (see <see cref="LookupsFor"/>).</summary>
    private static string[] FallbackOrder(string script) => [script, DefaultScript, "dflt", "latn"];
}
