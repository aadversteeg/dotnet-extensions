using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
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

    /// <summary>
    /// The most slices one piece is cut into: 100. The longest length the front-end accepts is 14 400 pt
    /// (<c>InputLimits.MaxLength</c>), so 100 slices show an image of that height whole on a page area of 144 pt (2 inches)
    /// or more. A taller piece (a line height may be a large multiple of the font size) or a smaller page area is refused
    /// with <c>Documents.Layout.TooManySlices</c> at its 101st slice. Without the limit the number of pages would follow
    /// from numbers in the input, not from its size (a 14 400 pt image on a page area of 0.01 pt is 1 440 000 pages); with
    /// it a document has at most 100 pages per piece more than it has pieces (docs/architecture.md, "Bounded work").
    /// </summary>
    public const int MaxSlices = 100;

    /// <summary>
    /// The most slices one document is cut into, all pieces together: 1 000, room for ten pieces at
    /// <see cref="MaxSlices"/>. The limit per piece bounds the pages per piece, not the pages per input byte: a paragraph
    /// of many tall lines takes up to 100 pages per line, and its lines are laid out again on every page it reaches. So
    /// the slices of a whole document are counted too (<see cref="Paginator"/>), and the page that would bring the count
    /// past 1 000 is refused with <c>Documents.Layout.TooManySlices</c>; the message names this limit. Only content taller
    /// than the page area is sliced, so a document without such a piece never meets the limit, however many pages it has
    /// (docs/architecture.md, "Bounded work").
    /// </summary>
    public const int MaxSlicesPerDocument = 1_000;

    /// <summary>The number of slices a fragment holds, at any depth (without recursion: fragments nest as deep as the
    /// input).</summary>
    /// <param name="fragment">The fragment, for instance the body of one page.</param>
    /// <returns>The number of <see cref="SliceFragment"/>s in it.</returns>
    public static int Count(Fragment fragment)
    {
        var count = 0;
        var pending = new Stack<Fragment>();
        pending.Push(fragment);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case SliceFragment:
                    count++;
                    break;
                case BoxFragment box:
                    foreach (var child in box.Children)
                    {
                        pending.Push(child.Fragment);
                    }

                    break;
                case LineFragment line:
                    foreach (var child in line.Children)
                    {
                        pending.Push(child.Fragment);
                    }

                    break;
            }
        }

        return count;
    }

    /// <summary>The slice of <paramref name="whole"/> this page shows, after the <paramref name="shown"/> height that
    /// <paramref name="slices"/> slices on earlier pages showed, in the <paramref name="height"/> the page has for it.</summary>
    /// <param name="box">The box the content belongs to, for the error.</param>
    /// <param name="whole">The whole content's fragment.</param>
    /// <param name="shown">The height shown on earlier pages; 0 for the first slice.</param>
    /// <param name="slices">The number of slices shown on earlier pages; 0 for the first slice.</param>
    /// <param name="height">The height available on this page.</param>
    /// <returns>The slice, and the height shown after it, or null when it is the last slice; None when the page has no
    /// height for a slice at all (a slice of nothing would make no progress); <c>Documents.Layout.TooManySlices</c> when
    /// the content would need more than <see cref="MaxSlices"/> slices.</returns>
    public static Result<Maybe<(SliceFragment Slice, double? ShownAfter)>, Error> Next(Box box, Fragment whole, double shown, int slices, double height)
    {
        if (!(height > Fitting.TolerancePoints))
        {
            return Result<Maybe<(SliceFragment, double?)>, Error>.Success(Maybe<(SliceFragment, double?)>.None);
        }

        if (slices >= MaxSlices)
        {
            return Result<Maybe<(SliceFragment, double?)>, Error>.Failure(LayoutErrors.TooManySlices(box, whole.Size.Height, shown));
        }

        var rest = whole.Size.Height - shown;
        var isLast = Fitting.Fits(rest, height);
        var sliceHeight = isLast ? rest : height;
        var slice = new SliceFragment(new Size(whole.Size.Width, sliceHeight), new PlacedFragment(new Point(0, -shown), whole), IsContinuation: shown > 0);
        return Result<Maybe<(SliceFragment, double?)>, Error>.Success(Maybe.From<(SliceFragment, double?)>((slice, isLast ? null : shown + sliceHeight)));
    }
}
