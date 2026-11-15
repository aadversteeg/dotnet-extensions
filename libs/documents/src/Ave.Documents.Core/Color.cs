namespace Ave.Documents.Core;

/// <summary>
/// An RGB colour with 8 bits per channel, as used by CSS (<c>#1f4e79</c>).
/// </summary>
/// <remarks>
/// Stage 0 writes colours to PDF as DeviceRGB (ISO 32000-1 §8.6.4.3). Colour management (ICC-based colour
/// spaces, the OutputIntent that PDF/A requires) arrives with the conformance stage; the page model will then
/// carry a colour space next to the components, without changing this type's meaning.
/// </remarks>
/// <param name="Red">Red channel, 0–255.</param>
/// <param name="Green">Green channel, 0–255.</param>
/// <param name="Blue">Blue channel, 0–255.</param>
public readonly record struct Color(byte Red, byte Green, byte Blue)
{
    /// <summary>Black, the CSS initial value of <c>color</c> on paper.</summary>
    public static Color Black => new(0, 0, 0);

    /// <summary>White.</summary>
    public static Color White => new(255, 255, 255);

    /// <summary>Formats as a CSS hex colour, for example <c>#1f4e79</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"#{Red:x2}{Green:x2}{Blue:x2}";
}
