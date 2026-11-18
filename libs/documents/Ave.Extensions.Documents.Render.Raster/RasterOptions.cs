using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Render.Raster;

/// <summary>
/// How a page is turned into pixels. A value: nothing is read from the machine, so the same page and options always
/// give the same pixels.
/// </summary>
/// <param name="Resolution">Pixels per inch (a point is 1/72 inch): 96 is a screen's nominal resolution, 300 a print
/// preview's. A pixel is then <c>Resolution / 72</c> points' worth of the page; the image is ⌈page width × Resolution /
/// 72⌉ by ⌈page height × Resolution / 72⌉ pixels.</param>
public sealed record RasterOptions(double Resolution = 96)
{
    /// <summary>The opaque colour the page is painted with first, as paper; null for transparent pixels. White unless
    /// set.</summary>
    public Color? Background { get; init; } = Color.White;
}
