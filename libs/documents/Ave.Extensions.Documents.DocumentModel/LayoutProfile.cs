namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The layout policies of a document: where CSS leaves the layout a choice, or where the engine offers a choice beside
/// what Chromium prints, the front-end decides, and layout reads the decision here. The policies are bundled per
/// front-end family (docs/architecture.md, "Policies"): the code front-end keeps <see cref="Default"/> unless the author
/// asks otherwise; a front-end that follows the browser sets the values Chromium prints. A value, not a setting: two
/// documents with the same profile are laid out by the same rules.
/// </summary>
public sealed record LayoutProfile
{
    /// <summary>The code front-end's policies: <see cref="MonolithicOverflow.Refuse"/>, <see cref="EmptyRowParts.Avoid"/>,
    /// <see cref="FontMetricRounding.Exact"/> and <see cref="SplitLineHalves.Exact"/>.</summary>
    public static LayoutProfile Default { get; } = new();

    /// <summary>What happens to an image or a line box taller than the page area (<see cref="DocumentModel.MonolithicOverflow"/>).</summary>
    public MonolithicOverflow MonolithicOverflow { get; init; } = MonolithicOverflow.Refuse;

    /// <summary>Whether a table row may be cut where a part of it shows nothing (<see cref="DocumentModel.EmptyRowParts"/>).</summary>
    public EmptyRowParts EmptyRowParts { get; init; } = EmptyRowParts.Avoid;

    /// <summary>Whether a font's ascent, descent and line gap are used as they are or rounded to whole pixels as Chromium
    /// rounds them (<see cref="DocumentModel.FontMetricRounding"/>).</summary>
    public FontMetricRounding FontMetricRounding { get; init; } = FontMetricRounding.Exact;

    /// <summary>How thick the half of a grid line is drawn where a page break between table rows splits the line
    /// (<see cref="DocumentModel.SplitLineHalves"/>).</summary>
    public SplitLineHalves SplitLineHalves { get; init; } = SplitLineHalves.Exact;
}

/// <summary>
/// How thick each half of a horizontal grid line of a table is drawn where a page break between two rows splits the line
/// at its centre (<c>BoxDecorationBreak.Slice</c>): the upper half ends one page, the lower half starts the next. Only the
/// paint follows the policy: each half takes its exact width in layout under either value, so content is where it is
/// under <see cref="Exact"/>. A pixel here is the CSS pixel: 1/96 inch, 0.75 pt (CSS Values 4 §6.2).
/// </summary>
public enum SplitLineHalves
{
    /// <summary>Each half is drawn as thick as it is: half the line's width.</summary>
    Exact,

    /// <summary>
    /// A half thinner than one pixel is drawn one pixel thick, from its top edge down, as Chromium prints it; a thicker
    /// half is drawn as it is. Measured against Chromium's print (<c>tools/reference/chromium</c>): with a line of
    /// 0.75 pt (1 px) each half is half a pixel and prints as one whole pixel from the pixel nearest to its top
    /// (cases <c>a2-cells-end</c>, <c>b5-row-span-kept-cell</c>, <c>a2-row-end-first-row-half-second-row</c>); with
    /// 1.5 pt (2 px) each half is one pixel (<c>a2-row-break-line-1-5pt</c>); with 2.25 pt (3 px) the halves are 1.5 px,
    /// each edge on the nearest pixel, so 1 px at the end of the page and 2 px at the start of the next
    /// (<c>a2-row-break-line-2-25pt</c>); with 6 pt each half is 4 px (<c>a2-row-break-line-6pt</c>). A one-pixel half drawn
    /// from the half's top edge covers the same pixel as Chromium's once both edges are put on whole pixels.
    /// </summary>
    AtLeastOnePixel,
}

/// <summary>
/// How layout takes a font's ascent, descent and line gap at a font size, for <c>line-height: normal</c>, the strut and
/// the baseline's place in a line. A pixel here is the CSS pixel: 1/96 inch, 0.75 pt (CSS Values 4 §6.2).
/// </summary>
public enum FontMetricRounding
{
    /// <summary>The font's values scaled to the font size, unrounded, and the leading split in two equal halves (CSS 2.1
    /// §10.8.1).</summary>
    Exact,

    /// <summary>
    /// As Chromium takes them: the ascent, the descent and the line gap of each font at its size each rounded to a whole
    /// pixel, half away from zero (<c>platform/fonts/font_metrics.cc</c>, <c>FontMetrics::AscentDescentWithHacks</c>:
    /// "ascent = SkScalarRoundToScalar(-metrics.fAscent); descent = SkScalarRoundToScalar(metrics.fDescent);";
    /// <c>platform/fonts/simple_font_data.cc</c>: line spacing "lroundf(ascent) + lroundf(descent) + lroundf(line_gap)"),
    /// so a line of <c>normal</c> height is a whole number of pixels; and the half-leading above the text floored to a whole
    /// pixel, the rest of the leading below it (<c>core/layout/inline/line_utils.cc</c>, <c>CalculateLeadingSpace</c>: "ascent_leading_spacing{((line_height
    /// - current_height.LineHeight()) / 2).Floor()}"), for <c>normal</c> and for line heights given as numbers or lengths
    /// alike. Measured against Chromium's print (<c>tools/reference/chromium</c>, cases <c>f-lato-*</c> and
    /// <c>f-mixed-lato</c>: every line within 1/64 pixel). What the policy does not cover: a line height given as a
    /// length, a percentage or a number is taken as it is given, while Chromium keeps it in its layout unit of 1/64 pixel
    /// (a length or a percentage rounded to it, a number times the font size floored), so pages can differ; a front-end
    /// that follows Chromium gives the line height in that unit, and then the pages are Chromium's (cases
    /// <c>f-length-*</c>, <c>f-letter-*</c>, <c>f-number-1501</c>). Chromium also paints each text baseline at a whole
    /// pixel, the nearest one, half up, which moves ink by at most half a pixel and never a line (case
    /// <c>pm-snap-pitch</c>: lines 20.5 px apart from a top at 19.5 px have their baselines painted at 35 and 55 px); the
    /// engine does not (docs/architecture.md, "Against Chromium").
    /// </summary>
    WholePixels,
}

/// <summary>
/// What layout does with monolithic content that is taller than the page area, at the top of a page where moving it on
/// gains nothing: an image, or a line box. CSS Fragmentation 3 §4.4 lets the user agent choose: where there is no
/// breakpoint below the top of the page and not all the content fits, "the UA may also fragment the contents of
/// monolithic elements by slicing the element's graphical representation." Padding and borders taller than a page,
/// and a header or footer area that leaves no body, are not monolithic content: they are refused under either value.
/// </summary>
public enum MonolithicOverflow
{
    /// <summary>Layout fails with <c>Documents.Layout.DoesNotFit</c>, naming the content and the room it had: nothing is
    /// cut that the author did not see cut.</summary>
    Refuse,

    /// <summary>The content is sliced as Chromium prints it: placed whole at the top of the page, it shows there as far as
    /// the page area reaches, and each following page shows the next part of it, as tall as the room it has there, until
    /// its end; what follows it starts right below that end. Each slice is clipped
    /// to its part of the page area (a page-model clip). In tagged
    /// PDF every slice of an image is content of its figure, whose alternative text is read once; the later slices of a
    /// line of text are pagination artifacts, so its text is read once. One piece is cut into at most 100 slices, one per page:
    /// a piece that would need more is refused with <c>Documents.Layout.TooManySlices</c> (a slice is as tall as the room
    /// it has on its page, which under a repeated header or inside padding is less than the page area), so the number of
    /// pages follows from the size of the input, not from the numbers in it. A document is cut into at most 1 000 slices,
    /// all pieces together; one that would need more is refused with the same code. A piece in a table cell that fits the
    /// page area but not with the cell's padding and grid lines is sliced too: the test is that the piece with its insets
    /// does not fit a page; Chromium moves such a piece whole and lets the padding pass the page end
    /// (docs/architecture.md, "Known limits"). This value can also change the pages of a document that has no piece
    /// taller than a page: content below the top padding of boxes that start the page is cut at the deepest place only
    /// under it, so such a document can have a page less than under <see cref="Refuse"/> (Chromium's pages, case
    /// <c>a4-nested-padded-image-160</c>), and a document refused under <see cref="Refuse"/> for such a piece is laid
    /// out.</summary>
    Slice,
}

/// <summary>
/// Whether a table row may be cut at a page break where one part of it — the part on this page, or the rest on the
/// next — shows nothing: a strip of empty cells at the end or the start of a page. CSS leaves where a row breaks to the
/// user agent (CSS Fragmentation 3 §4.4 and CSS 2.1 §17 name no rule for it).
/// </summary>
public enum EmptyRowParts
{
    /// <summary>Such a cut is a last resort: the row moves to the next page whole where that avoids the strip, and is
    /// cut anyway only where nothing else is possible (a row that shows nothing over a page's height).</summary>
    Avoid,

    /// <summary>A row is cut wherever its cells can break, also where a part shows nothing, as Chromium prints it
    /// (<c>tools/reference/chromium</c>, cases <c>a2-padded-blocks</c>, <c>a2-line-breaks</c> and <c>a3-mirror</c>); and a
    /// row whose cells all end on the page, with only the half of the grid line below it past the page end, is not cut
    /// but moves to the next page whole, as Chromium does (cases <c>a2-cells-end*</c>); so does a row whose lines all fit
    /// but whose bottom padding or border passes the page end (case <c>a2-row-end-bottom-padding-30</c>).</summary>
    Allow,
}
