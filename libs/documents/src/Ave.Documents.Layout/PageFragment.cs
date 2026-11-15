using Ave.Documents.Core;

namespace Ave.Documents.Layout;

/// <summary>One page of the fragment tree: the page size, the root box's fragment and the page-area fragments.</summary>
/// <param name="PageSize">The page size in points.</param>
/// <param name="Root">The root fragment, positioned relative to the page's top-left corner.</param>
/// <param name="Header">The header area's fragment, or null when the page has no header.</param>
/// <param name="Footer">The footer area's fragment, or null when the page has no footer.</param>
public sealed record PageFragment(Size PageSize, PlacedFragment Root, PlacedFragment? Header = null, PlacedFragment? Footer = null);
