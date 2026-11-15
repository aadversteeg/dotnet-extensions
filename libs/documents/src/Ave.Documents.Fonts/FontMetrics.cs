namespace Ave.Documents.Fonts;

/// <summary>
/// The font-wide measurements, in font design units (see <see cref="UnitsPerEm"/>). Divide by UnitsPerEm and
/// multiply by the font size to get points.
/// </summary>
/// <param name="UnitsPerEm">Design units per em ('head').</param>
/// <param name="Ascender">Distance from the baseline to the top of the line, positive ('hhea').</param>
/// <param name="Descender">Distance from the baseline to the bottom of the line, NEGATIVE ('hhea').</param>
/// <param name="LineGap">Extra space the font designer wants between lines ('hhea').</param>
/// <param name="CapHeight">Height of capital letters ('OS/2' version 2 and later); null when absent.</param>
/// <param name="BoundingBox">The box enclosing every glyph ('head').</param>
/// <param name="ItalicAngle">Slant in degrees counter-clockwise from vertical, 0 for upright fonts ('post').</param>
/// <param name="IsFixedPitch">True for monospaced fonts ('post').</param>
/// <param name="WeightClass">The weight on the CSS 1–1000 scale ('OS/2' usWeightClass).</param>
public sealed record FontMetrics(
    int UnitsPerEm,
    int Ascender,
    int Descender,
    int LineGap,
    int? CapHeight,
    FontBoundingBox BoundingBox,
    double ItalicAngle,
    bool IsFixedPitch,
    int WeightClass)
{
    /// <summary>Converts a value in design units to points at a font size.</summary>
    /// <param name="designUnits">The value in font design units.</param>
    /// <param name="fontSize">The font size in points (the size of one em).</param>
    /// <returns>The value in points.</returns>
    public double ToPoints(double designUnits, double fontSize) => designUnits * fontSize / UnitsPerEm;
}
