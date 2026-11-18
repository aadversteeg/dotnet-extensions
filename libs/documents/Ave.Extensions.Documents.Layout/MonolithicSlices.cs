using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// THE RULE FOR MONOLITHIC CONTENT TALLER THAN THE PAGE AREA (<see cref="LayoutProfile.MonolithicOverflow"/>). Monolithic
/// content — an image, a line box — has no breakpoint inside it (CSS Fragmentation 3 §4.1). Where it does not fit below
/// other content it moves to the next page, as any piece does. Where it starts the page and still does not fit, moving
/// gains nothing, and the profile decides: <see cref="MonolithicOverflow.Refuse"/> fails with
/// <c>Documents.Layout.DoesNotFit</c>; <see cref="MonolithicOverflow.Slice"/> slices it (§4.4: "the UA may also fragment
/// the contents of monolithic elements by slicing the element's graphical representation"), as Chromium prints it
/// (<c>tools/reference/chromium</c>, cases <c>a4-*</c>): the content is placed whole at the top of the page and shows
/// there down to the end of the page area; each later page shows the next part of it, one page-area height (the height
/// it is given there) at a time; the last slice is as tall as what is left, and what follows starts right below it.
/// Each slice is a <see cref="SliceFragment"/>, clipped to its own top and bottom.
/// </summary>
internal static class MonolithicSlices
{
    /// <summary>Whether content that does not fit is sliced: the profile slices, and the content starts the page (a
    /// break before it would make no progress).</summary>
    /// <param name="environment">The environment, with the document's profile.</param>
    /// <param name="startsPage">True when nothing lies above the content on this page.</param>
    /// <returns>True to slice, false to refuse (or move the content on, where it does not start the page).</returns>
    public static bool Apply(LayoutEnvironment environment, bool startsPage) =>
        startsPage && environment.Profile.MonolithicOverflow == MonolithicOverflow.Slice;

    /// <summary>The slice of <paramref name="whole"/> this page shows, after the <paramref name="shown"/> height earlier
    /// pages showed, in the <paramref name="height"/> the page has for it.</summary>
    /// <param name="whole">The whole content's fragment.</param>
    /// <param name="shown">The height shown on earlier pages; 0 for the first slice.</param>
    /// <param name="height">The height available on this page.</param>
    /// <returns>The slice, and the height shown after it, or null when it is the last slice; None when the page has no
    /// height for a slice at all (a slice of nothing would make no progress).</returns>
    public static Maybe<(SliceFragment Slice, double? ShownAfter)> Next(Fragment whole, double shown, double height)
    {
        if (!(height > Fitting.TolerancePoints))
        {
            return Maybe<(SliceFragment, double?)>.None;
        }

        var rest = whole.Size.Height - shown;
        var isLast = Fitting.Fits(rest, height);
        var sliceHeight = isLast ? rest : height;
        var slice = new SliceFragment(new Size(whole.Size.Width, sliceHeight), new PlacedFragment(new Point(0, -shown), whole), IsContinuation: shown > 0);
        return Maybe.From<(SliceFragment, double?)>((slice, isLast ? null : shown + sliceHeight));
    }
}
