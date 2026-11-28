using System.Threading;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images;

/// <summary>
/// The pixels one document may decode to samples, all its images together (<see cref="ImageLimits.MaxDocumentPixels"/>).
/// <see cref="ImageLimits.MaxPixels"/> bounds one image; a document of many distinct images, each within that bound,
/// would still hold all their samples at once. A decode that is handed a budget charges the image's pixels from its
/// header, before any sample is made, and is refused (<c>Documents.Image.TooLarge</c>) when they do not fit. A decode
/// that then fails gives its pixels back: an image is charged only while it is decoded and while its samples are held,
/// so a header that announces more than its data holds cannot use up the budget. What the failed decodes produced is
/// counted apart and never given back: a PNG's pixels of the rows its data filled or began, a JPEG's whole frame (its
/// coefficients are made before its data is read). Once that count reaches
/// <see cref="ImageLimits.MaxFailedDocumentPixels"/>, every later image is refused without being decoded
/// (<c>Documents.Image.TooLarge</c>), so the work of images that do not decode is bounded too. Use one budget per
/// render, and decode each distinct image once: a budget counts every decode it is charged for.
/// </summary>
public sealed class ImagePixelBudget
{
    private long _used;
    private long _failed;

    /// <summary>A budget of <see cref="ImageLimits.MaxDocumentPixels"/>.</summary>
    public ImagePixelBudget()
        : this(ImageLimits.MaxDocumentPixels)
    {
    }

    /// <summary>A budget of the given pixels, at most <see cref="ImageLimits.MaxDocumentPixels"/>.</summary>
    /// <param name="maxPixels">The pixels the budget allows.</param>
    public ImagePixelBudget(long maxPixels)
        : this(maxPixels, ImageLimits.MaxFailedDocumentPixels)
    {
    }

    /// <summary>A budget of the given pixels, and of the given pixels that failed decodes may produce.</summary>
    internal ImagePixelBudget(long maxPixels, long maxFailedPixels)
    {
        MaxPixels = System.Math.Clamp(maxPixels, 0, ImageLimits.MaxDocumentPixels);
        MaxFailedPixels = System.Math.Clamp(maxFailedPixels, 0, ImageLimits.MaxFailedDocumentPixels);
    }

    /// <summary>The pixels the budget allows.</summary>
    public long MaxPixels { get; }

    /// <summary>The pixels charged so far.</summary>
    public long Used => Interlocked.Read(ref _used);

    /// <summary>The pixels failed decodes may produce before later images are refused.</summary>
    internal long MaxFailedPixels { get; }

    /// <summary>The pixels the failed decodes produced so far.</summary>
    internal long FailedPixels => Interlocked.Read(ref _failed);

    /// <summary>Charges an image of the given size, or refuses it when it does not fit, or when the failed decodes have
    /// produced <see cref="MaxFailedPixels"/>; then nothing is charged.</summary>
    internal Result<Unit, Error> Charge(long width, long height)
    {
        var pixels = width * height;
        if (FailedPixels is var failed && failed >= MaxFailedPixels)
        {
            return Result<Unit, Error>.Failure(new Error(ImageErrorCodes.TooLarge,
                    $"The image is not decoded: images of this document that did not decode have produced {failed} pixels, and those of one document may produce at most {MaxFailedPixels} together; after that no image of the document is decoded.")
                .With(ErrorMetadataKeys.Value, $"{failed}>={MaxFailedPixels}"));
        }

        while (true)
        {
            var used = Interlocked.Read(ref _used);
            if (pixels > MaxPixels - used)
            {
                var taken = used == 0 ? string.Empty : $", and other images of the document take {used} of them";
                return Result<Unit, Error>.Failure(new Error(ImageErrorCodes.TooLarge,
                        $"The image is {width}x{height} = {pixels} pixels; the images of one document decode to at most {MaxPixels} pixels together{taken}.")
                    .With(ErrorMetadataKeys.Value, $"{width}x{height}"));
            }

            if (Interlocked.CompareExchange(ref _used, used + pixels, used) == used)
            {
                return Result<Unit, Error>.Success(Unit.Value);
            }
        }
    }

    /// <summary>Gives back what <see cref="Charge"/> took for an image whose decode failed, and counts what that decode
    /// produced (at most the image's pixels) toward <see cref="MaxFailedPixels"/>, for good.</summary>
    internal void Failed(long width, long height, long produced)
    {
        Interlocked.Add(ref _used, -(width * height));
        Interlocked.Add(ref _failed, System.Math.Clamp(produced, 0, width * height));
    }

    /// <summary>The result of a decode charged for an image of the given size, with the charge given back when the
    /// decode failed and the whole image counted as what it produced.</summary>
    internal Result<T, Error> Kept<T>(long width, long height, Result<T, Error> decoded)
    {
        if (decoded.IsFailure)
        {
            Failed(width, height, width * height);
        }

        return decoded;
    }
}
