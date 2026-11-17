using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fonts.OpenType;

/// <summary>A feature (OpenType spec, "Feature table"): its tag and the lookups that implement it.</summary>
/// <param name="Tag">The feature tag, for example <c>kern</c>.</param>
/// <param name="LookupIndices">Indices into the lookup list.</param>
public sealed record FeatureTable(string Tag, ValueList<int> LookupIndices);
