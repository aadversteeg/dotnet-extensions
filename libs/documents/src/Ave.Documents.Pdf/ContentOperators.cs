using System.Collections.Immutable;

namespace Ave.Documents.Pdf;

/// <summary>
/// The content-stream operators the engine uses, one function per operator, each named after what it does and
/// citing where ISO 32000-1 defines it. Positions are in PDF user space: points, origin at the bottom-left
/// corner of the page, y up (§8.3.2.3).
/// </summary>
public static class ContentOperators
{
    /// <summary><c>q</c>: save the graphics state (§8.4.4, Table 57).</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation SaveState() => new("q", []);

    /// <summary><c>Q</c>: restore the graphics state saved by the matching <c>q</c> (§8.4.4, Table 57).</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation RestoreState() => new("Q", []);

    /// <summary><c>rg</c>: set the non-stroking (fill) colour in DeviceRGB, components 0–1 (§8.6.8, Table 74).</summary>
    /// <param name="red">Red, 0–1.</param>
    /// <param name="green">Green, 0–1.</param>
    /// <param name="blue">Blue, 0–1.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation SetFillRgb(double red, double green, double blue) =>
        new("rg", [PdfNumber.From(red), PdfNumber.From(green), PdfNumber.From(blue)]);

    /// <summary><c>re</c>: add a rectangle to the current path; (x, y) is its lower-left corner (§8.5.2.1, Table 59).</summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Bottom edge.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation Rectangle(double x, double y, double width, double height) =>
        new("re", [PdfNumber.From(x), PdfNumber.From(y), PdfNumber.From(width), PdfNumber.From(height)]);

    /// <summary><c>f</c>: fill the current path with the non-zero winding rule (§8.5.3.1, Table 60).</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation Fill() => new("f", []);

    /// <summary><c>BT</c>: begin a text object (§9.4.1, Table 107).</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation BeginText() => new("BT", []);

    /// <summary><c>ET</c>: end a text object (§9.4.1, Table 107).</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation EndText() => new("ET", []);

    /// <summary><c>Tf</c>: set the font, by its name in the page's /Font resources, and the size (§9.3.1, Table 105).</summary>
    /// <param name="resourceName">The font's key in the page resources, for example <c>F1</c>.</param>
    /// <param name="size">The font size in points.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation SetFont(PdfName resourceName, double size) => new("Tf", [resourceName, PdfNumber.From(size)]);

    /// <summary>
    /// <c>Tm</c>: set the text matrix and text line matrix (§9.4.2, Table 108). With <c>1 0 0 1 x y</c> the
    /// next glyph's origin (on the baseline) is placed at (x, y).
    /// </summary>
    /// <param name="x">Horizontal position of the text origin.</param>
    /// <param name="y">Vertical position of the baseline.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation MoveTextTo(double x, double y) =>
        new("Tm", [new PdfInteger(1), new PdfInteger(0), new PdfInteger(0), new PdfInteger(1), PdfNumber.From(x), PdfNumber.From(y)]);

    /// <summary>
    /// <c>TJ</c>: show glyphs with individual position adjustments (§9.4.3, Table 109). The array holds strings
    /// of glyph codes and numbers; a number moves the next glyph LEFT by that many thousandths of the font size.
    /// </summary>
    /// <param name="items">Strings of glyph codes and adjustment numbers, in order.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation ShowGlyphsWithAdjustments(ImmutableArray<PdfObject> items) => new("TJ", [new PdfArray(items)]);
}
