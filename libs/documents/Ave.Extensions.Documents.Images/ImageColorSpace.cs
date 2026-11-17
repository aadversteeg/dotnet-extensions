namespace Ave.Documents.Images;

/// <summary>
/// The colour space of an image's colour samples. Both map directly onto a PDF device colour space (ISO 32000-1
/// §8.6.4: DeviceGray, DeviceRGB).
/// </summary>
public enum ImageColorSpace
{
    /// <summary>One sample per pixel: black (0) to white (255).</summary>
    Gray,

    /// <summary>Three samples per pixel: red, green, blue.</summary>
    Rgb,
}
