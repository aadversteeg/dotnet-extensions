using System;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>
/// Comparing the engine with lengths measured in Chromium (the reference cases in <c>tools/reference/chromium</c>,
/// README there). Chromium stores every layout length as a whole number of layout units of 1/64 CSS px
/// (<c>LayoutUnit = FixedPoint&lt;6, int32_t&gt;</c>, <c>third_party/blink/renderer/platform/geometry/layout_unit.h:473</c>,
/// Chromium 159/160); the engine keeps exact values. So a check against a measured Chromium length is "equal within n
/// layout units", where n is the number of snapped lengths that add up to the measured value (a truncated width or
/// padding, a ceiled text item, a truncated share of a distribution), derived from the structure of the case and stated
/// next to the check. A sum gets the sum of its parts' n. There is no other tolerance.
/// </summary>
public static class ChromiumLayout
{
    /// <summary>One Chromium layout unit in points: 1/64 CSS px, and 1 CSS px = 0.75 pt.</summary>
    public const double LayoutUnit = 0.75 / 64;

    /// <summary>
    /// Room for the engine's own floating-point arithmetic in a comparison, in layout units: a value that is n layout
    /// units away in exact arithmetic may come out a few ulps above n.
    /// </summary>
    public const double FloatSlack = 1e-6;

    /// <summary>
    /// The measured length as Chromium holds it: a whole number of layout units. The measurement script writes lengths
    /// in points with three decimals; a layout unit (0.0117 pt) is more than twice the 0.0005 pt that rounding can move a
    /// value, so the nearest whole number of layout units is the value Chromium had.
    /// </summary>
    /// <param name="measured">The length as the measurement wrote it, in points.</param>
    /// <returns>The length in layout units.</returns>
    public static double InLayoutUnits(double measured) => Math.Round(measured / LayoutUnit);

    /// <summary>
    /// A position down or across the page as Chromium paints it, in whole CSS pixels (1 px = 0.75 pt): the nearest one,
    /// and half a pixel up, towards the larger value, for either sign. Measured against Chromium's print
    /// (<c>tools/reference/chromium</c>): every baseline, image edge and grid-line edge the engine lays out at a half pixel
    /// that Chromium paints a pixel apart from it prints at the larger one, but at the end of a sliced piece's rest, which
    /// is a rule of layout (<c>SliceRestEnd</c>); below zero too: a line 240 pt tall drawn again a page area higher has its
    /// top at -202.5 px and prints at -202 px (case <c>a4-header-tall-line</c>, "W" at 45.75 pt).
    /// </summary>
    /// <param name="points">The position in points.</param>
    /// <returns>The pixel, a whole number.</returns>
    public static int Pixel(double points) => (int)Math.Floor((points / 0.75) + 0.5);

    /// <summary>A position as Chromium paints it (<see cref="Pixel"/>), in points.</summary>
    /// <param name="points">The position in points.</param>
    /// <returns>The painted position in points.</returns>
    public static double WholePixel(double points) => Pixel(points) * 0.75;

    /// <summary>How far the engine's length is from Chromium's, in layout units.</summary>
    /// <param name="engine">The engine's length, in points.</param>
    /// <param name="measured">Chromium's length as the measurement wrote it, in points.</param>
    /// <returns>The absolute difference in layout units.</returns>
    public static double DifferenceInLayoutUnits(double engine, double measured) => Math.Abs(engine / LayoutUnit - InLayoutUnits(measured));

    /// <summary>
    /// An artefact of the reference, not of CSS: the factor by which Chromium on Linux scales a font's advances at a
    /// font size it rounds. Blink floors the font size to 1/100 px (<c>FontDescription::EffectiveFontSize</c>,
    /// <c>third_party/blink/renderer/platform/fonts/font_description.cc:263-274</c>), and the Linux font backend for web fonts
    /// (Skia's Fontations backend, skrifa) truncates the pixels per em to 1/64 px to match FreeType
    /// (<c>skrifa src/instance.rs:52-64</c>, <c>fixed_linear_scale</c>). At 10 pt (13.333 px) the advances are computed for
    /// 13.328125 px: a factor of 0.99960938. At 9 pt (12 px), 10.5 pt (14 px) and 12 pt (16 px) it is 1. The engine does
    /// not imitate this; one test applies the factor to the engine's text widths to keep the explanation checked.
    /// </summary>
    /// <param name="fontSizePoints">The font size in points.</param>
    /// <returns>Chromium's pixels per em divided by the exact pixels per em.</returns>
    public static double FontSizeFactor(double fontSizePoints)
    {
        var pixels = fontSizePoints / 0.75;
        var hundredths = Math.Floor(pixels * 100) / 100;
        var ppem = Math.Floor(hundredths * 64) / 64;
        return ppem / pixels;
    }
}
