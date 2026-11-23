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
    /// <see cref="FontMetricRounding.Exact"/>, <see cref="FontSizeRounding.Exact"/>, <see cref="LengthRounding.Exact"/>,
    /// <see cref="SplitLineHalves.Exact"/>, <see cref="TableEndOverflow.EndsOnPage"/>, <see cref="BodyClip.None"/>,
    /// <see cref="CellsBesideSlices.GoOn"/>, <see cref="RepeatedHeaderRows.AboveEverySlice"/>,
    /// <see cref="SliceRestEnd.InsideCell"/> and <see cref="CutRowAlignment.RestAlignedInItsPart"/>.</summary>
    public static LayoutProfile Default { get; } = new();

    /// <summary>What happens to an image or a line box taller than the page area (<see cref="DocumentModel.MonolithicOverflow"/>).</summary>
    public MonolithicOverflow MonolithicOverflow { get; init; } = MonolithicOverflow.Refuse;

    /// <summary>Whether a table row may be cut where a part of it shows nothing (<see cref="DocumentModel.EmptyRowParts"/>).</summary>
    public EmptyRowParts EmptyRowParts { get; init; } = EmptyRowParts.Avoid;

    /// <summary>Whether a font's ascent, descent and line gap are used as they are or rounded to whole pixels as Chromium
    /// rounds them (<see cref="DocumentModel.FontMetricRounding"/>).</summary>
    public FontMetricRounding FontMetricRounding { get; init; } = FontMetricRounding.Exact;

    /// <summary>Whether text is shaped at its font size as it is given or at the two rounded sizes Chromium shapes with
    /// (<see cref="DocumentModel.FontSizeRounding"/>).</summary>
    public FontSizeRounding FontSizeRounding { get; init; } = FontSizeRounding.Exact;

    /// <summary>Whether lengths are kept as they are or put on Chromium's layout unit of 1/64 CSS pixel where Chromium
    /// puts them there (<see cref="DocumentModel.LengthRounding"/>).</summary>
    public LengthRounding LengthRounding { get; init; } = LengthRounding.Exact;

    /// <summary>How thick the half of a grid line is drawn where a page break between table rows splits the line
    /// (<see cref="DocumentModel.SplitLineHalves"/>).</summary>
    public SplitLineHalves SplitLineHalves { get; init; } = SplitLineHalves.Exact;

    /// <summary>Where the part of a table that passes the page end below a row placed whole goes: it ends on the page, or it
    /// continues on the next page and takes its height there (<see cref="DocumentModel.TableEndOverflow"/>).</summary>
    public TableEndOverflow TableEndOverflow { get; init; } = TableEndOverflow.EndsOnPage;

    /// <summary>Whether what a page's body draws past the body's area shows (<see cref="DocumentModel.BodyClip"/>).</summary>
    public BodyClip BodyClip { get; init; } = BodyClip.None;

    /// <summary>Where the other cells of a table row go on, on the pages that go on with the slices of a piece in one of
    /// its cells (<see cref="DocumentModel.CellsBesideSlices"/>).</summary>
    public CellsBesideSlices CellsBesideSlices { get; init; } = CellsBesideSlices.GoOn;

    /// <summary>Where the content of a cell aligned <c>middle</c> or <c>bottom</c> goes on the later pages of a row that a
    /// page break cuts (<see cref="DocumentModel.CutRowAlignment"/>).</summary>
    public CutRowAlignment CutRowAlignment { get; init; } = CutRowAlignment.RestAlignedInItsPart;

    /// <summary>On which later pages of a table its repeated header rows are drawn, where a piece taller than the room
    /// below them is sliced in one of its cells (<see cref="DocumentModel.RepeatedHeaderRows"/>).</summary>
    public RepeatedHeaderRows RepeatedHeaderRows { get; init; } = RepeatedHeaderRows.AboveEverySlice;

    /// <summary>Where the part of a table cell ends that holds nothing but the rest of a piece sliced on an earlier page
    /// (<see cref="DocumentModel.SliceRestEnd"/>).</summary>
    public SliceRestEnd SliceRestEnd { get; init; } = SliceRestEnd.InsideCell;
}

/// <summary>
/// Where the part of a table cell ends, in the collapsing border model, that continues from an earlier page and holds
/// nothing but the rest of a piece sliced there (an image or a line taller than the room left, <see cref="MonolithicOverflow.Slice"/>):
/// the rest's later slices, no other content, no padding below them. It decides where the grid line below the cell lies,
/// and so where the table ends and what follows starts. Every other cell, and such a cell under either value on the page
/// where the piece starts, holds its half of the line below it (CSS 2.1 §17.6.2).
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) the policy has no meaning: there is no grid line,
/// and a table of that model with content sliced in a cell is refused (<c>Documents.Layout.TableBreak</c>; case
/// <c>ts-break-image</c>).</para>
/// </summary>
public enum SliceRestEnd
{
    /// <summary>The part holds its half of the line below it, as every cell does: the line starts where the rest ends.</summary>
    InsideCell,

    /// <summary>
    /// The part ends where the rest ends, without its half of the line below it, so the line is centred on the rest's end,
    /// as Chromium prints it. Measured against Chromium's print (<c>tools/reference/chromium</c>): below the rest of a
    /// 228 pt image that ends at 76.125 pt the 0.75 pt line runs from 75.75 to 76.5 pt, where a whole cell's would run from
    /// 76.125 to 76.875 pt (case <c>a4-header-image-228</c>; Chromium paints the image's end at the whole pixel, 76.5 pt),
    /// and a 2.25 pt line from 75 to 77.25 pt (<c>a4-header-image-228-line3-below</c>); the text after the table starts
    /// that much higher (<c>a4-header-image-row</c>, <c>a4-header-image-174</c>, <c>-180</c>); the same in a cell with a
    /// background (<c>a4-header-image-228-background</c>), below the rest of a
    /// line 240 pt tall (<c>a4-header-tall-line</c>) and of an image in a table nested in the cell
    /// (<c>a4-header-nested-image-228</c>, the nested table's line). A part that lays content out on the page holds its
    /// half under this value too (<c>a4-header-cut-text-row</c>, <c>a4-header-line2-25-cut-text-row</c>), and so does a
    /// whole cell. So does the part of a cell of the same row that goes on beside the rest with nothing left, so there the
    /// row ends that half below the rest (<c>a4-header-image-228-beside-line</c>: the line from 76.5 to 77.25 pt). Where
    /// the rest ends on a whole pixel, the two values print alike (<c>a4-noheader-image-228</c>).
    /// </summary>
    AtLineCentre,
}

/// <summary>
/// On which later pages of a table its repeated header rows are drawn, where a piece in one of its body cells (an image,
/// a line) is taller than the room below them (<see cref="MonolithicOverflow.Slice"/>). Only a table whose header rows
/// repeat is concerned; nothing changes on its first page.
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) neither value is reached: a table of that model
/// with content sliced in a cell is refused (<c>Documents.Layout.TableBreak</c>; case <c>ts-break-image</c>), and its
/// header rows start every later fragment, one spacing above the first body row (case <c>ts-break-header</c>).</para>
/// </summary>
public enum RepeatedHeaderRows
{
    /// <summary>The header rows are drawn above every slice, and each slice is as tall as the room below them; a piece
    /// taller than that room but not than the page area goes on without them, whole.</summary>
    AboveEverySlice,

    /// <summary>
    /// The header rows are drawn on every page where the table still lays content out, and not on a page that only shows
    /// the rest of content laid out on an earlier page, as Chromium prints it. Chromium lays a piece out whole below the
    /// header rows of the page where it starts, also one no taller than the page area, and lets it pass the page end; on a
    /// later page where nothing of the table is left but the rest of such pieces, it draws no header rows, and the rest
    /// starts at the top of the page area. Measured against Chromium's print (<c>tools/reference/chromium</c>): a 228 pt
    /// image below one or two header rows (cases <c>a4-header-image-228</c>, <c>a4-header2-image-228</c>), as the second
    /// body row (<c>a4-header-row-image-228</c>), a 408 pt image (<c>a4-header-image-row</c>), a line 240 pt tall
    /// (<c>a4-header-tall-line</c>), images of 170, 174 and 180 pt, taller than the room below the header row and no
    /// taller than the page area (<c>a4-header-image-170</c>, <c>-174</c>, <c>-180</c>). Where the table has rows still to
    /// come or a cell of the piece's row beside it goes on, also an empty one, to the row's end
    /// (<c>a4-header-image-360-image</c>, <c>a4-header-image-beside-lines</c>, <c>a4-header-image-228-beside-line</c>),
    /// Chromium draws the header rows over the slice at the top of the page; the engine draws them above it, the slice
    /// below them, a listed difference (docs/architecture.md, "Known limits", "A repeated header row over a slice").
    /// </summary>
    WhereContentIsLaidOut,
}

/// <summary>
/// Where the content of the cells of a table row that a page break cuts goes on the pages after the cut. On the page where
/// the row starts, every cell of a cut row starts at the top of its part under both values, as Chromium prints it (case
/// <c>fr-cut-row-uncut-bottom-cell</c>: a bottom-aligned cell that fits beside a cell that is cut); a row that no break
/// cuts is aligned as its cells' <see cref="VerticalAlign"/> says under both values.
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) a cut row whose cells are all aligned at the top is
/// drawn as under both values (cases <c>ts-break-cut</c>, <c>ts-break-move</c>); one with a cell aligned at the middle
/// or bottom is refused (<c>Documents.Layout.TableBreak</c>), as no case measures it.</para>
/// </summary>
public enum CutRowAlignment
{
    /// <summary>The rest of a cell aligned <c>middle</c> or <c>bottom</c> is aligned in the rest of its row on the last page
    /// of the row: a shorter rest beside a longer one sits lower.</summary>
    RestAlignedInItsPart,

    /// <summary>
    /// The rest of every cell of a cut row starts at the top of its part on every page, whatever its
    /// <see cref="VerticalAlign"/>, as Chromium prints it: the rest continues where the cell's content broke off. Measured
    /// against Chromium's print (<c>tools/reference/chromium</c>): the rest of a bottom- and of a middle-aligned cell on the
    /// next page (cases <c>fr-old-cut-cell-bottom</c>, <c>fr-old-cut-cell-middle</c>), a row cut over three pages
    /// (<c>fr-cut-row-three-pages-bottom</c>), a cut row between two rows that are not cut, whose bottom-aligned cells stay
    /// aligned on both pages (<c>fr-cut-row-between-aligned-rows</c>).
    /// </summary>
    TopOnEveryPage,
}

/// <summary>
/// Where the other cells of a table row go on, on a page that goes on with the next slice of a piece taller than the page
/// area in one of its cells (<see cref="MonolithicOverflow.Slice"/>): beside the slice from the top of the page, or below
/// it. Only a cell that continues from the previous page is concerned; a cell whose content ended there stays empty either
/// way, and nothing changes on the page where the piece starts.
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) neither value is reached: a table of that model
/// with content sliced in a cell is refused (<c>Documents.Layout.TableBreak</c>; case <c>ts-break-image</c>).</para>
/// </summary>
public enum CellsBesideSlices
{
    /// <summary>The other cells go on from the top of the page, beside the slice, page by page: no page beside a slice is
    /// left empty when a cell beside it has content left.</summary>
    GoOn,

    /// <summary>
    /// The other cells go on below the slice the page shows, as Chromium prints it: Chromium lays the piece out whole on
    /// the page where it starts, and a cell beside it that goes on starts each later page below what is left of the piece
    /// there. A page wholly covered by a slice shows nothing of those cells; on the page of the last slice they go on right
    /// below it, and where their next line does not fit there, at the top of the next page. Measured against Chromium's
    /// print (<c>tools/reference/chromium</c>): a 408 pt image beside 30 lines (case <c>a4-image-beside-lines</c>), a
    /// 228 pt image beside 30 lines (<c>a4-slice2-beside-lines</c>), a 348 pt image whose last slice ends 12 pt above the
    /// page end beside 40 lines (<c>a4-slice-near-end-beside-lines</c>).
    /// </summary>
    Below,
}

/// <summary>
/// Whether each page clips its body to the body's area: the page area less the header and footer areas. (The HTML
/// front-end lays out the top and bottom page-margin boxes as the header and footer areas of a page without top and
/// bottom margins, so there the body's area is the CSS page area.) Layout places the body inside that area; what passes
/// its edge is drawn all the same, such as the grid line below a table row placed whole at the top of a page
/// (<see cref="TableEndOverflow"/>). The header and footer areas are never clipped.
/// </summary>
public enum BodyClip
{
    /// <summary>Nothing is clipped: what the body draws past its area shows on the page.</summary>
    None,

    /// <summary>
    /// The body is drawn under a clip to its area, as Chromium prints it: what passes the area does not show. Measured
    /// against Chromium's print (<c>tools/reference/chromium</c>, read back by <c>visible.py</c>): every page's body is drawn
    /// under one rectangle clip, the page area (cases <c>a2-row-end-after</c>, <c>a2-row-end-6pt-part-after</c>,
    /// <c>a4-image-in-cell</c>, <c>a4-header-tall-line</c>, <c>fr-positions</c>), and with page-margin boxes the page
    /// area too, while the margin boxes are drawn before it, outside any clip, and show where they pass into the page area
    /// (<c>pm-tall-over</c>). Fills, images and text are all under it. The pages, the text and the structure are those of
    /// <see cref="None"/>; only what shows of a drawing that passes the area changes.
    /// </summary>
    BodyArea,
}

/// <summary>
/// Where the part of a table goes that passes the page end below a row placed whole at the top of a page under
/// <see cref="EmptyRowParts.Allow"/> (its content fits, its line below does not): below the table's last row, or at a
/// break between rows after it. That part is never content: it is a grid line, or the half of a line that a cell holds.
/// The page that ends draws the line under either value; the value decides what the next page does.
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) neither value is reached: a page of a table of that
/// model that would pass the page end is refused (<c>Documents.Layout.TableBreak</c>), and a last row that fits only
/// without the spacing, padding and border below it moves to the next page (case <c>ts-break-end</c>).</para>
/// </summary>
public enum TableEndOverflow
{
    /// <summary>The table ends on the page: it takes no more than the page area, and what follows it starts at the top of
    /// the next page. A table that ends the document adds no page.</summary>
    EndsOnPage,

    /// <summary>
    /// What passes the page end continues on the next page and takes its height there, as Chromium prints it: the next
    /// page starts with a part of the table as tall as what passed, and what follows the table starts below it; where the
    /// table ends the document, that part is on a page of its own. It is painted only where part of a cell's border box
    /// passed: then the next page draws the grid lines that passed, as they lay below the page end. Where only the
    /// table's outer half of its bottom line passed, the part is empty, and as tall as that whole half also where only part
    /// of it passed (cases <c>a2-row-end-thick-less-after</c>, <c>a4-cell-image-179-5</c>). Measured against Chromium's print
    /// (<c>tools/reference/chromium</c>): a row whose line below passes by its outer half only takes that half again at
    /// the top of the next page, unpainted (cases <c>a2-row-end-first-row-half-*</c>, with a 6 pt line
    /// <c>a2-row-end-first-row-half-6pt-below</c>), and on a page of its own where nothing follows
    /// (<c>a2-row-end-first-row-half-alone</c>); a row whose content ends at the page end passes by the whole line, which
    /// the next page draws again and takes (cases <c>a2-row-end</c>, <c>a2-row-end-after</c>, with a 6 pt line
    /// <c>a2-row-end-6pt</c> and <c>a2-row-end-6pt-after</c>, under a cell background <c>a2-row-end-background</c>, and part
    /// of the cell's half in <c>a2-row-end-6pt-part-after</c>); at a break between rows the next page lays its rows out
    /// below what passed (<c>a2-row-end-span-after</c>). It also starts inside a cell's bottom padding: in a table that
    /// continues from the page before, a row at the top of the page (continued, or moved there whole) whose cells' content
    /// fits and whose bottom padding passes the page end keeps its lines there, and the padding and the line below
    /// continue on the next page, painted, however much taller than a line they are (cases <c>fr-continued-pad-past-33</c>,
    /// <c>-45</c>, <c>-60</c>: 3, 15 and 30 pt of padding past the page end; <c>-33-next-row</c>, where the line to the
    /// next row is drawn once, as the upper half that passed, also 6 and 2.25 pt wide: <c>-line-6pt</c>, <c>-line-2-25pt</c>;
    /// <c>fr-row-later-page-padding-overflows</c>; <c>a4-continued-cell-pad-bottom-30</c>). Where what passed is taller
    /// than the next page area, that page takes the whole area and the rest passes again (cases
    /// <c>fr-continued-pad-past-taller-than-page</c>, <c>-next-row</c>); every page takes the page area, so it ends.
    /// </summary>
    ContinuesOnNextPage,
}

/// <summary>
/// How thick each half of a horizontal grid line of a table is drawn where a page break between two rows splits the line
/// at its centre (<c>BoxDecorationBreak.Slice</c>): the upper half ends one page, the lower half starts the next. Only the
/// paint follows the policy: each half takes its exact width in layout under either value, so content is where it is
/// under <see cref="Exact"/>. A pixel here is the CSS pixel: 1/96 inch, 0.75 pt (CSS Values 4 §6.2).
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) the policy has no meaning: there is no grid line to
/// split; the table and each cut cell are sliced as block boxes are (cases <c>ts-break-rows</c>, <c>ts-break-cut</c>).</para>
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
/// The font size text is shaped at: the glyphs' advances, the kerning between them and the place of a mark on its base.
/// The font is drawn at its size under either value; where an advance differs from the font's own width at that size,
/// the back-ends place each glyph where layout put it (the PDF with a TJ adjustment, ISO 32000-1 §9.4.3, as it places
/// kerned glyphs). A pixel here is the CSS pixel: 1/96 inch, 0.75 pt (CSS Values 4 §6.2).
/// </summary>
public enum FontSizeRounding
{
    /// <summary>Everything at the font size as it is given.</summary>
    Exact,

    /// <summary>
    /// The font size in pixels floored to 1/100 px for kerning and every other positioning value, and that size truncated
    /// to 1/64 px for the glyphs' own advances, in single precision, as Chromium prints text: Blink rounds a font's size
    /// down to its font cache's precision (<c>platform/fonts/font_description.cc</c>,
    /// <c>FontDescription::EffectiveFontSize</c>), and the font library under Skia scales the advances with the pixels per
    /// em truncated to 1/64 px (skrifa <c>src/instance.rs</c>, <c>Size::fixed_linear_scale</c>). Nothing is rounded per glyph,
    /// word or space, so the text is narrower by a constant share along the line: 0.098 % at 8 pt (10.66 px and
    /// 10.65625 px), 0.039 % at 10 pt, 0.071 % at 11 pt, 0.030 % at 13 pt; none at 9, 10.5 and 12 pt. Measured against
    /// Chromium's print (<c>tools/reference/chromium</c>): every word within 0.01 pt in cases <c>ta-sizes</c> (seven sizes),
    /// <c>ta-px</c> (sizes given in px, 10.65625 px floored to 10.65), <c>ta-right</c> (right-aligned: the start of the line
    /// moves), <c>ta-kerning-scale</c> (kerning at the floored size) and <c>ta-marks</c> (combining marks at the floored
    /// size). The advances are scaled in 16.16 fixed point as the font library scales them (case <c>ta-right</c>: which
    /// layout unit a line's width rounds up to). What the policy does not cover: the font is drawn at its size, where
    /// Chromium draws it at the floored size (7.995 pt for 8 pt); and the width of each text item, which Chromium rounds up
    /// to its layout unit of 1/64 px: that is
    /// <see cref="LengthRounding.SixtyFourthPixels"/>, a policy of its own (case <c>sk-size</c> needs both: an underline's
    /// gap edge 0.011 pt early and one pixel more painted under this policy alone).
    /// </summary>
    FlooredToPixelFractions,
}

/// <summary>
/// Whether layout keeps its lengths as they are or puts some of them on Chromium's length step: Blink's layout unit,
/// 1/64 CSS pixel (<c>platform/geometry/layout_unit.h</c>, <c>LayoutUnit</c>, six fraction bits), 0.01171875 pt. Only the
/// places listed under <see cref="SixtyFourthPixels"/> follow the policy; every other length stays as it is under either
/// value. A pixel here is the CSS pixel: 1/96 inch, 0.75 pt (CSS Values 4 §6.2).
/// </summary>
public enum LengthRounding
{
    /// <summary>Every length as layout computes it.</summary>
    Exact,

    /// <summary>
    /// The lengths below are put on steps of 1/64 px as Chromium puts them there; nothing else is. Measured against
    /// Chromium's print (<c>tools/reference/chromium</c>).
    /// <list type="number">
    /// <item>The width of a text item as line layout sees it, rounded UP: the part of one run's text that lies on one line
    /// (<c>Typesetting.LineSegment</c>), its glyphs' advances summed and rounded up to the next 1/64 px, for placing the
    /// next item, for aligning the line and for whether the line fits. Blink's line breaker takes each text item's width
    /// as <c>ShapeResult::SnappedWidth</c>, <c>LayoutUnit::FromFloatCeil</c> of the shaped width
    /// (<c>core/layout/inline/line_breaker.cc</c>, <c>LineBreaker::HandleText</c>); a new item starts at every element
    /// boundary. The glyphs inside an item keep their exact advances; an image on the line is not a text item. Cases
    /// <c>ta-items</c> (30 inline elements of two one-letter words at 8 and 10.5 pt: every word within 0.0005 pt of the
    /// item widths rounded up, where unrounded widths put the last word up to 0.176 pt early and widths rounded per word
    /// 0.266 pt late), <c>ta-items-right</c> (right-aligned: the start of the line moves by the sum of the steps),
    /// <c>ta-items-fit</c> (in a block wider than the unrounded widths and narrower than the rounded ones the last word
    /// wraps) and <c>sk-size</c> (an underline gap's edge).</item>
    /// </list>
    /// </summary>
    SixtyFourthPixels,
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
/// <para>With separated borders (<see cref="TableBorderModel.Separate"/>) rows are cut and moved by the same rule; under <see
/// cref="Allow"/> as Chromium prints it (cases <c>ts-break-cut</c>: a cell that ended goes on, empty, beside the cut
/// cell; <c>ts-break-move</c>; <c>ts-break-first-row</c>: a table whose first row has no break that fits moves to the
/// next page whole), under <see cref="Avoid"/> not measured, as with collapsed borders a deliberate rule of the engine.</para>
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
    /// but whose bottom padding or border passes the page end (case <c>a2-row-end-bottom-padding-30</c>). A row is not cut
    /// where one of its cells that starts on the page has no room there for its top border and padding, or for its first
    /// line below them: that cell has no breakpoint on the page, and the row moves to the next page whole where a break
    /// before it is valid, also beside a cell whose first line fits (cases <c>fr-row-later-padding-*</c>,
    /// <c>fr-row-later-page-padding-overflows</c>, <c>a2-row-end-nested-under-header</c>). Below header
    /// rows that repeat, a page does not end with the header alone where the first body row's content (an image, a line)
    /// does not fit below the header: the row is cut before it, its cells empty down to the page end, as Chromium prints
    /// it (cases <c>a4-header-image-228</c>, <c>a4-header2-image-228</c>, <c>a4-header-row-image-row</c>); its other cells
    /// draw what they hold there, text or a background, and the content goes on in its cell's part on the next page
    /// (<c>a4-header-image-228-beside-line</c>, <c>a4-header-image-beside-lines</c>, <c>a4-header-image-228-background</c>).</summary>
    Allow,
}
