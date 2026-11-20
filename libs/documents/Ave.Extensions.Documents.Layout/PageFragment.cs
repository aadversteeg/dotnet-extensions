using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Layout;

/// <summary>One page of the fragment tree: the page size, the root box's fragment and the page-area fragments.</summary>
/// <param name="PageSize">The page size in points.</param>
/// <param name="Root">The root fragment, positioned relative to the page's top-left corner.</param>
/// <param name="Header">The header area's fragment, or null when the page has no header.</param>
/// <param name="Footer">The footer area's fragment, or null when the page has no footer.</param>
/// <param name="BodyClip">The rectangle the body is clipped to (<see cref="DocumentModel.BodyClip.BodyArea"/>): the body's area
/// less <see cref="DocumentModel.PageStyle.BodyClipInset"/> at its right and bottom; or null when it is not clipped.</param>
public sealed record PageFragment(Size PageSize, PlacedFragment Root, PlacedFragment? Header = null, PlacedFragment? Footer = null, Rect? BodyClip = null);
