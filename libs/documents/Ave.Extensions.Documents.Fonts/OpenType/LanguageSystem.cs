using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fonts.OpenType;

/// <summary>A language system table (OpenType spec, "Language system table").</summary>
/// <param name="RequiredFeature">The index of the feature that is always applied, or null ("if no
/// required features, set to 0xFFFF").</param>
/// <param name="FeatureIndices">The indices of its features in the feature list.</param>
public sealed record LanguageSystem(int? RequiredFeature, ValueList<int> FeatureIndices);
