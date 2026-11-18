using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Lays out a box that holds one image: a replaced element (CSS 2.1 §10.3.2), a leaf producer that only takes part
/// in layout through its size. It is monolithic: an image never breaks across pages (CSS
/// Fragmentation 3 §4.1 lists no breakpoint inside replaced content). One that does not fit moves to the next page;
/// one taller than an empty page is <c>Documents.Layout.DoesNotFit</c>, or sliced over the pages it needs, as the
/// document's profile says (<see cref="MonolithicSlices"/>).
/// </summary>
public static class ImageLayout
{
    /// <summary>The CSS reference pixel: 1px = 1/96 inch = 0.75pt (CSS Values 4 §6.2). An image's natural size is
    /// its pixel size in CSS pixels; resolution metadata in the file (PNG pHYs, JPEG JFIF density) is not used.</summary>
    public const double PointsPerPixel = 0.75;

    /// <summary>The Image algorithm; see <see cref="LayoutAlgorithm"/> for the contract.</summary>
    /// <param name="environment">For the document's profile (an image needs no fonts or other algorithms).</param>
    /// <param name="box">A box whose only child is an <see cref="ImageNode"/>.</param>
    /// <param name="space">The space on the current page.</param>
    /// <param name="incoming">Null, or an <see cref="ImageBreakToken"/>: when the box's top padding and border were placed
    /// on an earlier page by a last-resort break before the image (<see cref="SlicedBox"/>), or when earlier pages
    /// showed slices of the image (<see cref="ImageBreakToken.Shown"/>).</param>
    /// <returns>The fragment, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming) =>
        incoming is not (null or ImageBreakToken)
            ? throw new ArgumentException($"Image received a {incoming.GetType().Name} it did not produce.", nameof(incoming))
            : TheImage(box).OnSuccessBind(image => SlicedBox.Layout(box, space, incoming, (incoming as ImageBreakToken) ?? ImageBreakToken.Instance, (width, height, _) =>
        {
            var size = UsedSize(image, width);
            var shown = (incoming as ImageBreakToken)?.Shown ?? 0;
            if (shown == 0 && Fitting.Fits(size.Height, height))
            {
                return SlicedBox.Placed([new PlacedFragment(new Point(0, 0), new ImageFragment(image, size))], size.Height, null);
            }

            // Taller than the room it has: moved on (None), refused at the start of a page, or sliced (MonolithicSlices).
            // Once sliced, the image goes on slicing, wherever its next part lands.
            if (shown == 0 && !MonolithicSlices.Apply(environment, SlicedBox.ContentIsAtPageStart(box.Style, space, incoming)))
            {
                return SlicedBox.NothingFits;
            }

            var next = MonolithicSlices.Next(new ImageFragment(image, size), shown, height);
            return next.HasNoValue
                ? SlicedBox.NothingFits
                : SlicedBox.Placed(
                    [new PlacedFragment(new Point(0, 0), next.Value.Slice)],
                    next.Value.Slice.Size.Height,
                    next.Value.ShownAfter is { } after ? ImageBreakToken.After(after) : null);
        }));

    /// <summary>The intrinsic widths of an image box: min-content and max-content are both its used width when no
    /// width constrains it (CSS Sizing 3 §5.1 for replaced elements), plus border and padding.</summary>
    /// <param name="environment">Not used.</param>
    /// <param name="box">A box whose only child is an <see cref="ImageNode"/>.</param>
    /// <returns>The widths.</returns>
    public static Result<IntrinsicWidths, Error> Measure(LayoutEnvironment environment, Box box) =>
        TheImage(box).OnSuccessMap(image =>
        {
            var width = UsedSize(image, double.PositiveInfinity).Width + SlicedBox.HorizontalInsets(box.Style);
            return new IntrinsicWidths(width, width);
        });

    /// <summary>
    /// The used size of an image (CSS 2.1 §10.3.2 and §10.6.2, replaced elements with an intrinsic ratio):
    /// <list type="bullet">
    /// <item>width and height both set: the largest size with the image's aspect ratio that fits inside both. This
    /// DIFFERS from CSS, which would stretch the image to exactly that box; keeping the ratio is this engine's rule
    /// (like <c>object-fit: contain</c>, with the box shrunk to the image);</item>
    /// <item>only the width set: "the used value of 'height' is: (used width) / (intrinsic ratio)" (§10.6.2), and the other way
    /// round for only the height (§10.3.2);</item>
    /// <item>neither: the natural size, scaled down to the available width when it is wider (as with
    /// <c>max-width: 100%</c>), so a large image never runs off the page.</item>
    /// </list>
    /// </summary>
    /// <param name="image">The image node.</param>
    /// <param name="availableWidth">The content width of the box.</param>
    /// <returns>The used size, in points.</returns>
    public static Size UsedSize(ImageNode image, double availableWidth)
    {
        var natural = new Size(image.Image.PixelWidth * PointsPerPixel, image.Image.PixelHeight * PointsPerPixel);
        var ratio = natural.Width / natural.Height;
        return (image.Width, image.Height) switch
        {
            ({ } width, { } height) => FromWidth(Math.Min(width.Points, height.Points * ratio), ratio),
            ({ } width, null) => FromWidth(width.Points, ratio),
            (null, { } height) => FromWidth(height.Points * ratio, ratio),
            _ when natural.Width > availableWidth => FromWidth(availableWidth, ratio),
            _ => natural,
        };
    }

    /// <summary>A size of the given width with the image's aspect ratio (width / height).</summary>
    private static Size FromWidth(double width, double ratio) => new(width, width / ratio);

    private static Result<ImageNode, Error> TheImage(Box box) =>
        box.Children is [ImageNode image]
            ? Result<ImageNode, Error>.Success(image)
            : Result<ImageNode, Error>.Failure(LayoutErrors.InvalidContent(box, "an image box needs exactly one image child."));
}
