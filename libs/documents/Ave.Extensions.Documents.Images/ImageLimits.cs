namespace Ave.Extensions.Documents.Images;

/// <summary>
/// The largest image the engine decodes. A PNG header announces its size in 13 bytes, up to 2^31 − 1 by 2^31 − 1
/// pixels (PNG §11.2.2), while the file that follows may be a few kilobytes of compressed zeros. Without a limit such a
/// file asks for terabytes of memory; with one, every buffer the decoder allocates is bounded before it is allocated.
/// </summary>
/// <remarks>
/// The limit is 2^26 pixels (64 Mi, for example 8192 × 8192): an A4 page scanned at 600 dpi is 4961 × 7016 = 34.8 Mi
/// pixels, so every image a document page can show sharply fits. At the limit the decoded RGB samples and the alpha
/// channel take at most 256 MiB, the inflated scanlines at most 256 MiB plus one byte per row. A JPEG file is read
/// for its header only when it is embedded in a PDF, so the limit does not apply there; it applies when a JPEG is
/// decoded to samples (<see cref="ImageDecoder.DecodeSamples(ImageData)"/>, for the raster back-end), where the coefficients take
/// two bytes per sample of each component, plus the padding of the last blocks.
/// </remarks>
public static class ImageLimits
{
    /// <summary>The largest number of pixels (width × height) a decoded image may have: 2^26.</summary>
    public const long MaxPixels = 1L << 26;

    /// <summary>
    /// The largest number of pixels all images of one document decode to together: 2^27, two images at
    /// <see cref="MaxPixels"/>, or about 60 screenshots of 1920 × 1080. Each decoded pixel is held as its samples until
    /// the output is written (four bytes for RGB with alpha), so the limit bounds that memory at 512 MiB per document.
    /// Charged from each image's header before it is decoded (<see cref="ImagePixelBudget"/>); a JPEG that is only
    /// embedded is never decoded, so it is not charged.
    /// </summary>
    public const long MaxDocumentPixels = 1L << 27;
}
