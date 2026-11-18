namespace Ave.Extensions.Documents.Fonts;

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
/// <param name="IsItalic">True for an italic or oblique face ('OS/2' fsSelection bit 0).</param>
public sealed record FontMetrics(
    int UnitsPerEm,
    int Ascender,
    int Descender,
    int LineGap,
    int? CapHeight,
    FontBoundingBox BoundingBox,
    double ItalicAngle,
    bool IsFixedPitch,
    int WeightClass,
    bool IsItalic = false)
{
    private readonly LineMetrics? line;

    /// <summary>
    /// The metrics of the default line spacing (<c>line-height: normal</c>), from the table Chromium takes them from;
    /// the 'hhea' values (<see cref="Ascender"/>, <see cref="Descender"/>, <see cref="LineGap"/>) when not set.
    /// </summary>
    public LineMetrics Line
    {
        get => line ?? new LineMetrics(Ascender, Descender, LineGap, LineMetricsSource.Hhea);
        init => line = value;
    }

    /// <summary>Converts a value in design units to points at a font size.</summary>
    /// <param name="designUnits">The value in font design units.</param>
    /// <param name="fontSize">The font size in points (the size of one em).</param>
    /// <returns>The value in points.</returns>
    public double ToPoints(double designUnits, double fontSize) => designUnits * fontSize / UnitsPerEm;
}

/// <summary>
/// A font's line metrics: what <c>line-height: normal</c> and the strut are made of (CSS Inline 3 §5.3 leaves the
/// table to the user agent: "UAs should use … sTypoAscender and sTypoDescender from the font's OS/2 table"; CSS 2.1
/// §10.8.1 the same). The engine takes them where Chromium does on Linux, which reads them through Skia's FreeType port
/// (<c>src/ports/SkFontHost_FreeType.cpp</c>) and, for web fonts, Fontations (<c>skrifa/src/metrics.rs</c>): the OS/2
/// typographic metrics when the font sets USE_TYPO_METRICS (fsSelection bit 7), otherwise 'hhea'; and when 'hhea' gives
/// no ascender and no descender, the typographic metrics if they are not zero, else usWinAscent and usWinDescent
/// (<see cref="FontParser"/>).
/// </summary>
/// <param name="Ascender">Distance from the baseline to the top of the line, positive, in design units.</param>
/// <param name="Descender">Distance from the baseline to the bottom of the line, NEGATIVE, in design units.</param>
/// <param name="LineGap">The extra space between lines, in design units.</param>
/// <param name="Source">The table the values come from.</param>
public sealed record LineMetrics(int Ascender, int Descender, int LineGap, LineMetricsSource Source);

/// <summary>Where a font's line metrics come from.</summary>
public enum LineMetricsSource
{
    /// <summary>'hhea' ascender, descender and lineGap.</summary>
    Hhea,

    /// <summary>'OS/2' sTypoAscender, sTypoDescender and sTypoLineGap.</summary>
    Typo,

    /// <summary>'OS/2' usWinAscent and usWinDescent, no line gap.</summary>
    Win,
}
