using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The one check on list labels that needs the place on the sheet (<see cref="ListItemLayout"/> places a label relative to
/// its item): a label that would start left of the sheet's left edge is refused, with the item's source
/// (<see cref="LayoutErrorCodes.MarkerOutsideSheet"/>). Chromium 159 draws such a label past the sheet and cuts it, or
/// leaves it out (cases <c>li-types</c>, <c>li-marker-sheet</c>); neither is what the author meant.
/// </summary>
internal static class ListMarkers
{
    /// <summary>The first label left of the sheet, or null. One walk over every page's body and areas with an explicit
    /// stack, linear in the number of fragments.</summary>
    /// <param name="pages">The fragment tree.</param>
    /// <returns>The error, or null.</returns>
    public static Error? OutsideSheet(ValueList<PageFragment> pages)
    {
        var pending = new Stack<(Fragment Fragment, double X)>();
        foreach (var page in pages)
        {
            foreach (var area in new[] { page.Header, page.Root, page.Footer })
            {
                if (area is not null)
                {
                    pending.Push((area.Fragment, area.Offset.X));
                }
            }
        }

        while (pending.Count > 0)
        {
            var (fragment, x) = pending.Pop();
            if (fragment is BoxFragment { Box.Role: SemanticRole.ListLabelRole } label)
            {
                // A hair below zero only because points do not hold every pixel value exactly is on the edge.
                if (x < -1e-9)
                {
                    return LayoutErrors.MarkerOutsideSheet(label.Box, x);
                }

                continue;
            }

            var children = fragment switch
            {
                BoxFragment box => box.Children,
                LineFragment line => line.Children,
                SliceFragment slice => [slice.Content],
                _ => ValueList<PlacedFragment>.Empty,
            };
            foreach (var child in children)
            {
                pending.Push((child.Fragment, x + child.Offset.X));
            }
        }

        return null;
    }
}
