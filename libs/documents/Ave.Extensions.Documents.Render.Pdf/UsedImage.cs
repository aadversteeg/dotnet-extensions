using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;

namespace Ave.Extensions.Documents.Render.Pdf;

/// <summary>An image the document draws, with its name in the pages' /XObject resources.</summary>
/// <param name="Image">The image.</param>
/// <param name="ResourceName">Its resource name, for example <c>Im1</c>.</param>
internal sealed record UsedImage(ImageData Image, PdfName ResourceName)
{
    /// <summary>True when the image needs a soft mask: a decoded image with an alpha channel.</summary>
    public bool HasMask => Image is RasterImage { Alpha: not null };

    /// <summary>
    /// Collects the images of a document in order of first use, each once however often it is drawn (images are
    /// equal when their files are byte-identical), so names and object numbers depend only on the document.
    /// </summary>
    public static ImmutableArray<UsedImage> Collect(PageDocument document) =>
        [.. document.Pages
            .SelectMany(page => page.Items.OfType<DrawImage>())
            .Select(draw => draw.Image)
            .Distinct()
            .Select((image, index) => new UsedImage(image, new PdfName($"Im{index + 1}")))];
}
