using System.Collections.Immutable;

namespace Ave.Extensions.Documents.Pdf;

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

    /// <summary>
    /// <c>W</c>: intersect the current clipping path with the current path, by the non-zero winding rule (§8.5.4,
    /// Table 61). It comes between the path and the painting operator that ends it, and takes effect once that operator
    /// has run (§8.5.4); <c>n</c> ends the path without painting anything.
    /// </summary>
    /// <returns>The operation.</returns>
    public static ContentOperation Clip() => new("W", []);

    /// <summary><c>n</c>: end the current path without filling or stroking it (§8.5.3.1, Table 60): the painting operator
    /// that ends a clipping path (<see cref="Clip"/>) without drawing it.</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation EndPath() => new("n", []);

    /// <summary>
    /// <c>cm</c>: concatenate a matrix to the current transformation matrix (§8.4.4, Table 57; §8.3.4). With
    /// <c>w 0 0 h x y</c> an image, which fills the unit square of user space (§8.9.4), lands on the rectangle
    /// (x, y, w, h).
    /// </summary>
    /// <param name="a">Horizontal scale.</param>
    /// <param name="b">Shear (rotation) component.</param>
    /// <param name="c">Shear (rotation) component.</param>
    /// <param name="d">Vertical scale.</param>
    /// <param name="e">Horizontal translation.</param>
    /// <param name="f">Vertical translation.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation ConcatMatrix(double a, double b, double c, double d, double e, double f) =>
        new("cm", [PdfNumber.From(a), PdfNumber.From(b), PdfNumber.From(c), PdfNumber.From(d), PdfNumber.From(e), PdfNumber.From(f)]);

    /// <summary><c>Do</c>: paint an external object, by its name in the page's /XObject resources (§8.8, Table 87).</summary>
    /// <param name="resourceName">The XObject's key in the page resources, for example <c>Im1</c>.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation PaintXObject(PdfName resourceName) => new("Do", [resourceName]);

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

    /// <summary>
    /// <c>BDC</c>: begin a marked-content sequence with a tag and a property list (§14.6, Table 320). Tagged PDF marks
    /// content with its structure type and a marked-content identifier, <c>/P &lt;&lt;/MCID 0&gt;&gt; BDC</c>
    /// (§14.7.4.2), and decoration with <c>/Artifact</c> (§14.8.2.2). The property list is written inline (§14.6.2).
    /// </summary>
    /// <param name="tag">The tag, for example <c>P</c> or <c>Artifact</c>.</param>
    /// <param name="properties">The property list.</param>
    /// <returns>The operation.</returns>
    public static ContentOperation BeginMarkedContent(PdfName tag, PdfDictionary properties) => new("BDC", [tag, properties]);

    /// <summary><c>EMC</c>: end the marked-content sequence begun by the matching <c>BDC</c> (§14.6, Table 320).</summary>
    /// <returns>The operation.</returns>
    public static ContentOperation EndMarkedContent() => new("EMC", []);
}
