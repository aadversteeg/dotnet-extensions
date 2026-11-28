using System.Threading;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images;

/// <summary>
/// The pixels one document may decode to samples, all its images together (<see cref="ImageLimits.MaxDocumentPixels"/>).
/// <see cref="ImageLimits.MaxPixels"/> bounds one image; a document of many distinct images, each within that bound,
/// would still hold all their samples at once. A decode that is handed a budget charges the image's pixels from its
/// header, before any sample is made, and is refused (<c>Documents.Image.TooLarge</c>) when they do not fit. Use one
/// budget per render, and decode each distinct image once: a budget counts every decode it is charged for.
/// </summary>
public sealed class ImagePixelBudget
{
    private long _used;

    /// <summary>A budget of <see cref="ImageLimits.MaxDocumentPixels"/>.</summary>
    public ImagePixelBudget()
        : this(ImageLimits.MaxDocumentPixels)
    {
    }

    /// <summary>A budget of the given pixels, at most <see cref="ImageLimits.MaxDocumentPixels"/>.</summary>
    /// <param name="maxPixels">The pixels the budget allows.</param>
    public ImagePixelBudget(long maxPixels)
    {
        MaxPixels = System.Math.Clamp(maxPixels, 0, ImageLimits.MaxDocumentPixels);
    }

    /// <summary>The pixels the budget allows.</summary>
    public long MaxPixels { get; }

    /// <summary>The pixels charged so far.</summary>
    public long Used => Interlocked.Read(ref _used);

    /// <summary>Charges an image of the given size, or refuses it when it does not fit; then nothing is charged.</summary>
    internal Result<Unit, Error> Charge(long width, long height)
    {
        var pixels = width * height;
        while (true)
        {
            var used = Interlocked.Read(ref _used);
            if (pixels > MaxPixels - used)
            {
                return Result<Unit, Error>.Failure(new Error(ImageErrorCodes.TooLarge,
                        $"The image is {width}x{height} pixels; the images of one document decode to at most {MaxPixels} pixels together, and {used} are taken.")
                    .With(ErrorMetadataKeys.Value, $"{width}x{height}"));
            }

            if (Interlocked.CompareExchange(ref _used, used + pixels, used) == used)
            {
                return Result<Unit, Error>.Success(Unit.Value);
            }
        }
    }
}
