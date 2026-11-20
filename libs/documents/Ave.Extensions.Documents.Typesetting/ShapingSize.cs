using System;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// The two font sizes a run of text is shaped at, in points: the size of the positions (kerning, the place of a mark on
/// its base, every other GPOS value) and the size of the font's own advances ('hmtx'). Equal for a font used at the size
/// it is given (<see cref="Exact"/>); <see cref="FlooredToPixelFractions"/> gives the two sizes Chromium shapes with.
/// </summary>
/// <param name="Positions">The size the font's positioning values are scaled at, in points.</param>
/// <param name="Advances">The size the glyphs' own advances are scaled at, in points.</param>
public readonly record struct ShapingSize(double Positions, double Advances)
{
    /// <summary>Points per CSS pixel: 0.75 (CSS Values 4 §6.2, 1 px = 1/96 in, 1 pt = 1/72 in).</summary>
    private const double PointsPerPixel = 0.75;

    /// <summary>Both sizes are the font size as it is given.</summary>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>The sizes.</returns>
    public static ShapingSize Exact(double fontSize) => new(fontSize, fontSize);

    /// <summary>
    /// The font size in CSS pixels floored to 1/100 px for the positions, and that size truncated to 1/64 px for the
    /// advances, both in single precision, as Chromium prints text. Blink rounds the size of every font it uses down to its
    /// font cache's precision of 1/100 px (<c>platform/fonts/font_description.cc</c>, <c>FontDescription::EffectiveFontSize</c>:
    /// <c>floorf</c> of the float size times <c>kFontSizePrecisionMultiplier</c>, 100, in <c>font_cache_key.h</c>), and
    /// HarfBuzz is scaled to that size (<c>shaping/harfbuzz_face.cc</c>, <c>HarfBuzzFace::GetScaledFont</c>), so kerning and
    /// mark positions are at it. The glyphs' advances come from Skia's Fontations back-end, whose font library skrifa
    /// truncates the pixels per em to 1/64 px as FreeType does (skrifa <c>src/instance.rs</c>, <c>Size::fixed_linear_scale</c>:
    /// <c>(ppem * 64.) as i32</c>). Nothing is rounded per glyph. Measured against Chromium's print
    /// (<c>tools/reference/chromium</c>): at 8 pt (10.667 px) the sizes are 10.66 px and 10.65625 px, at 10 pt 13.33 and
    /// 13.328125, at 11 pt 14.66 and 14.65625, at 13 pt 17.33 and 17.328125, and every word of a line is where Chromium
    /// prints it within 0.01 pt (cases <c>ta-sizes</c>, <c>ta-right</c>); a size given as 10.65625 px, already a multiple of
    /// 1/64 px, is floored to 10.65 and then truncated to 10.640625 (case <c>ta-px</c>); kerning is at the floored size
    /// (case <c>ta-kerning-scale</c>), and so is the place of a combining mark on its base (case <c>ta-marks</c>). Sizes of
    /// a whole number of 1/64 px that survive the floor are kept: 9, 10.5 and 12 pt (12, 14 and 16 px), 11 px. The single
    /// precision matters: 8.4 px is the float 8.3999996, which floors to 8.39 px and truncates to 8.375 px, where the same
    /// steps in double precision give 8.40 and 8.390625 (0.19 % wider).
    /// </summary>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>The sizes, in points.</returns>
    public static ShapingSize FlooredToPixelFractions(double fontSize)
    {
        var pixels = (float)(fontSize / PointsPerPixel);
        var floored = MathF.Floor(pixels * 100f) / 100f;
        var truncated = (int)(floored * 64f) / 64f;
        return new ShapingSize(floored * PointsPerPixel, truncated * PointsPerPixel);
    }
}
