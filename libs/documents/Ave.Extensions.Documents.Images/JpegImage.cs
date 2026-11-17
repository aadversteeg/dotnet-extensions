using System.Collections.Immutable;

namespace Ave.Extensions.Documents.Images;

/// <summary>
/// A baseline or progressive JPEG file, kept as it is: PDF viewers decode it themselves through the DCTDecode
/// filter (ISO 32000-1 §7.4.8), so the engine only reads its header.
/// </summary>
public sealed class JpegImage : ImageData
{
    internal JpegImage(ImageId id, int pixelWidth, int pixelHeight, ImageColorSpace colorSpace, ImmutableArray<byte> file)
        : base(id, pixelWidth, pixelHeight, colorSpace)
    {
        File = file;
    }

    /// <summary>The complete JPEG file.</summary>
    public ImmutableArray<byte> File { get; }

    /// <inheritdoc/>
    public override string Describe() => $"jpeg {PixelWidth}x{PixelHeight} {ColorSpaceName}";
}
