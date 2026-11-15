using System.Collections.Immutable;

namespace Ave.Documents.Images;

/// <summary>
/// An image decoded to 8-bit samples, row by row from the top, pixels left to right, the channels of one pixel next
/// to each other — exactly the sample layout of a PDF image XObject with BitsPerComponent 8 (ISO 32000-1 §8.9.3).
/// </summary>
public sealed class RasterImage : ImageData
{
    internal RasterImage(ImageId id, int pixelWidth, int pixelHeight, ImageColorSpace colorSpace, ImmutableArray<byte> samples, ImmutableArray<byte>? alpha)
        : base(id, pixelWidth, pixelHeight, colorSpace)
    {
        Samples = samples;
        Alpha = alpha;
    }

    /// <summary>The colour samples: width × height × (1 for grey, 3 for RGB) bytes.</summary>
    public ImmutableArray<byte> Samples { get; }

    /// <summary>The opacity of each pixel, 0 (transparent) to 255 (opaque), width × height bytes; null when the
    /// image has no transparency information.</summary>
    public ImmutableArray<byte>? Alpha { get; }

    /// <inheritdoc/>
    public override string Describe() =>
        $"png {PixelWidth}x{PixelHeight} {ColorSpaceName}{(Alpha is null ? string.Empty : "+alpha")}";
}
