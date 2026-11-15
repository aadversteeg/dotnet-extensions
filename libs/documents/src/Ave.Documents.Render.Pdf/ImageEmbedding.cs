using System;
using System.Collections.Immutable;
using Ave.Documents.Images;
using Ave.Documents.Pdf;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// Embeds an image as an image XObject (ISO 32000-1 §8.9.5, Table 89): a stream of samples with its width, height,
/// colour space and bits per component. A page paints it with <c>Do</c> into the unit square that the current matrix
/// maps onto the image's rectangle (§8.9.4).
/// </summary>
internal static class ImageEmbedding
{
    /// <summary>
    /// The objects of one image:
    /// <list type="bullet">
    /// <item>a decoded image: its 8-bit samples in DeviceGray or DeviceRGB, Flate-compressed; with an alpha channel, a
    /// second DeviceGray image of the alpha values referenced as its <c>/SMask</c> (§11.6.5.3; Table 89: "a soft-mask
    /// image … that shall be used as a source of mask shape or mask opacity values"), which needs PDF 1.4 or later — this writer writes PDF 1.7;</item>
    /// <item>a JPEG: the file unchanged with <c>/Filter /DCTDecode</c> (§7.4.8); DCTDecode's default colour transform
    /// turns a three-component file's YCbCr into RGB.</item>
    /// </list>
    /// </summary>
    /// <param name="used">The image.</param>
    /// <param name="numbers">Its object numbers.</param>
    /// <returns>The indirect objects.</returns>
    public static ImmutableArray<IndirectObject> Build(UsedImage used, ImageObjectNumbers numbers) => used.Image switch
    {
        RasterImage raster => raster.Alpha is { } alpha && numbers.SoftMask is { } mask
            ?
            [
                new IndirectStream(numbers.Image, new PdfStream(Dictionary(raster, ColorSpaceName(raster.ColorSpace), mask), raster.Samples, StreamEncoding.Flate)),
                new IndirectStream(mask, new PdfStream(Dictionary(raster, "DeviceGray", null), alpha, StreamEncoding.Flate)),
            ]
            : [new IndirectStream(numbers.Image, new PdfStream(Dictionary(raster, ColorSpaceName(raster.ColorSpace), null), raster.Samples, StreamEncoding.Flate))],
        JpegImage jpeg => [new IndirectStream(numbers.Image, new PdfStream(Dictionary(jpeg, ColorSpaceName(jpeg.ColorSpace), null), jpeg.File, StreamEncoding.Dct))],
        _ => throw new InvalidOperationException($"No PDF embedding for {used.Image.GetType().Name}."),
    };

    /// <summary>The image dictionary (Table 89): Type, Subtype, Width, Height, ColorSpace, BitsPerComponent, SMask.</summary>
    private static PdfDictionary Dictionary(ImageData image, string colorSpace, int? softMask) => PdfDictionary.Of(
        ("Type", new PdfName("XObject")),
        ("Subtype", new PdfName("Image")),
        ("Width", new PdfInteger(image.PixelWidth)),
        ("Height", new PdfInteger(image.PixelHeight)),
        ("ColorSpace", new PdfName(colorSpace)),
        ("BitsPerComponent", new PdfInteger(8)),
        ("SMask", softMask is { } mask ? new PdfReference(mask) : null));

    private static string ColorSpaceName(ImageColorSpace colorSpace) => colorSpace == ImageColorSpace.Gray ? "DeviceGray" : "DeviceRGB";
}
