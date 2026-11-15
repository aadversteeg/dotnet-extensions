using Ave.Documents.Core;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>A language system table (OpenType spec, "Language system table").</summary>
/// <param name="RequiredFeature">The index of the feature that is always applied, or null ("0xFFFF if no
/// required features").</param>
/// <param name="FeatureIndices">The indices of its features in the feature list.</param>
internal sealed record LanguageSystem(int? RequiredFeature, ValueList<int> FeatureIndices);
