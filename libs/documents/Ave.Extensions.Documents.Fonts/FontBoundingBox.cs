namespace Ave.Extensions.Documents.Fonts;

/// <summary>A rectangle in font design units, y up from the baseline (the font's own coordinate system).</summary>
/// <param name="XMin">Left.</param>
/// <param name="YMin">Bottom.</param>
/// <param name="XMax">Right.</param>
/// <param name="YMax">Top.</param>
public sealed record FontBoundingBox(int XMin, int YMin, int XMax, int YMax);
