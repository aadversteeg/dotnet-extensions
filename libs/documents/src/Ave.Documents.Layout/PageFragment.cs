using Ave.Documents.Core;

namespace Ave.Documents.Layout;

/// <summary>One page of the fragment tree: the page size and the root box's fragment on it.</summary>
/// <param name="PageSize">The page size in points.</param>
/// <param name="Root">The root fragment, positioned relative to the page's top-left corner.</param>
public sealed record PageFragment(Size PageSize, PlacedFragment Root);
