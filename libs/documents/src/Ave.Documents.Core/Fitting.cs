namespace Ave.Documents.Core;

/// <summary>
/// The one rule for "does this fit": used by line breaking (does the next word fit the line) and by page
/// layout (does the next line or block fit the page).
/// </summary>
public static class Fitting
{
    /// <summary>
    /// How much a size may exceed the available space and still count as fitting, in points.
    /// </summary>
    /// <remarks>
    /// Widths and heights are sums of many scaled font values in floating point, so a run of text that fits
    /// exactly on paper can come out a few units in the 15th digit too wide. One millionth of a point is far
    /// below anything visible: one device pixel at 2400 dpi is 0.03 pt.
    /// </remarks>
    public const double TolerancePoints = 1e-6;

    /// <summary>True when <paramref name="required"/> fits in <paramref name="available"/>.</summary>
    /// <param name="required">The size needed, in points.</param>
    /// <param name="available">The space available, in points.</param>
    /// <returns>Whether it fits.</returns>
    public static bool Fits(double required, double available) => required <= available + TolerancePoints;
}
