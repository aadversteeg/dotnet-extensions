namespace Ave.Documents.Images;

/// <summary>
/// The largest image the engine decodes. A PNG header announces its size in 13 bytes, up to 2^31 − 1 by 2^31 − 1
/// pixels (PNG §11.2.2), while the file that follows may be a few kilobytes of compressed zeros. Without a limit such a
/// file asks for terabytes of memory; with one, every buffer the decoder allocates is bounded before it is allocated.
/// </summary>
/// <remarks>
/// The limit is 2^26 pixels (64 Mi, for example 8192 × 8192): an A4 page scanned at 600 dpi is 4961 × 7016 = 34.8 Mi
/// pixels, so every image a document page can show sharply fits. At the limit the decoded RGB samples and the alpha
/// channel take at most 256 MiB, the inflated scanlines at most 256 MiB plus one byte per row. JPEG files are not
/// decoded (they are embedded as they are), so the limit does not apply to them.
/// </remarks>
public static class ImageLimits
{
    /// <summary>The largest number of pixels (width × height) a decoded image may have: 2^26.</summary>
    public const long MaxPixels = 1L << 26;
}
