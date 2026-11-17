using System;

namespace Ave.Extensions.Documents.Images;

/// <summary>
/// A decoded image, ready to be placed on a page: its size in pixels, its colour space and the data a back-end
/// needs. Images are explicit input (bytes from the caller), like fonts; create one with
/// <see cref="ImageDecoder.Decode"/>. Two images are equal when their files are byte-identical.
/// </summary>
/// <remarks>
/// A closed set of two cases, so a back-end can handle each: <see cref="RasterImage"/> (decoded samples, from PNG)
/// and <see cref="JpegImage"/> (the compressed file, embedded unchanged).
/// </remarks>
public abstract class ImageData : IEquatable<ImageData>
{
    private protected ImageData(ImageId id, int pixelWidth, int pixelHeight, ImageColorSpace colorSpace)
    {
        Id = id;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        ColorSpace = colorSpace;
    }

    /// <summary>The content digest of the image file.</summary>
    public ImageId Id { get; }

    /// <summary>The width in pixels, at least 1.</summary>
    public int PixelWidth { get; }

    /// <summary>The height in pixels, at least 1.</summary>
    public int PixelHeight { get; }

    /// <summary>The colour space of the colour samples (an alpha channel is separate).</summary>
    public ImageColorSpace ColorSpace { get; }

    /// <summary>A short description for text output and errors, for example <c>png 120x80 rgb+alpha</c>.</summary>
    /// <returns>The description.</returns>
    public abstract string Describe();

    /// <summary>Equal when the image files are byte-identical.</summary>
    /// <param name="other">The other image.</param>
    /// <returns>Whether both come from the same bytes.</returns>
    public bool Equals(ImageData? other) => other is not null && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ImageData other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>Returns <see cref="Describe"/>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Describe();

    /// <summary>The colour space as it appears in text output.</summary>
    private protected string ColorSpaceName => ColorSpace == ImageColorSpace.Gray ? "gray" : "rgb";
}
