# Architecture of Ave.Extensions.Documents (milestone M1)

For a reviewer who has never seen the code. It says what each part is for, where the contracts are, where every
policy and whole algorithm lives, what is known not to work, and how each claim is checked. Paths are relative to the
family folder (`libs/documents`).

## What it is

A document engine in pure managed .NET 10: a document written in C# goes in, a PDF (or preview images) comes out. No native library,
no P/Invoke, no external program at run time; reference tools (veraPDF, qpdf, poppler, MuPDF, HarfBuzz, Chromium)
are used only by tests and scripts. Everything that can fail returns `Result<T, Error>` (Ave.Extensions.Functional /
Ave.Extensions.ErrorPaths) with a path-shaped error code and the author's source file and line; exceptions mean a
bug in the engine.

## Layers

Each layer is one project and may use only the layers below it in this list. `UnitTests.Extensions.Documents.Architecture`
checks this on the compiled assemblies, together with "no unsafe code, no P/Invoke, no native assets" and "no
test-only package in a product assembly".

| Project | Role |
| --- | --- |
| `Ave.Extensions.Documents` | composition: `ToPages` joins the front-end and the layout, `ToPdf` and `ToPng`/`ToPngPages`/`ToImages` add a back-end; the one package an application needs |
| `Ave.Extensions.Documents.Fluent` | code front-end: a document as an immutable value; translates it to the document model (cascade + validation); knows nothing about layout or PDF |
| `Ave.Extensions.Documents.Render.Pdf` | page model → PDF: fonts, images, content streams, and the conformance profiles (tagging, XMP, output intent, attachments, refusals) |
| `Ave.Extensions.Documents.Render.Raster` | page model → pixels: glyph outlines, rectangles and images drawn with Graphics (beside Render.Pdf, not above it) |
| `Ave.Extensions.Documents.Layout` | layout algorithms, pagination driver, painter (fragments → page model) |
| `Ave.Extensions.Documents.PageModel` | page model: per page a display list plus structure and artifact markers |
| `Ave.Extensions.Documents.DocumentModel` | document model: a styled box tree with computed values |
| `Ave.Extensions.Documents.Typesetting` | white-space collapsing, UAX #14 line breaking, OpenType shaping, justification |
| `Ave.Extensions.Documents.Pdf` | PDF objects and writer, content operators, XMP writer, sRGB ICC profile, `PdfProfile`; knows nothing about layout |
| `Ave.Extensions.Documents.Fonts` | TrueType reader (with GSUB, GPOS, GDEF, kern) and subsetter |
| `Ave.Extensions.Documents.Graphics` | 2D graphics: paths, affine transforms, filling with exact-area anti-aliasing, an 8-bit RGBA canvas with source-over compositing, image drawing with an exact box filter |
| `Ave.Extensions.Documents.Images` | PNG decoder and encoder, JPEG header reader and decoder |
| `Ave.Extensions.Documents.Html` | HTML front-end, first part: the user-agent style sheet, presentational hints, the document's style sheets through a caller's resolver, the cascade to a styled tree with the computed value of every registered property (no layout yet) |
| `Ave.Extensions.Documents.Css` | CSS: Syntax 3 parser, Selectors 4, media and supports conditions, the cascade (origins, layers, nesting, `var()`) and the one property registry with its computed values; knows nothing about HTML |
| `Ave.Extensions.Documents.Html.Parsing` | the HTML Standard's parser (§13.2, scripting off): tokenizer, tree construction, an immutable tree with line and column on every node, parse errors as diagnostics |
| `Ave.Extensions.Documents.Core` | shared vocabulary: lengths, geometry, colour, `ValueList<T>`, source locations, roles, metadata, attachments, the error-code tree |

The pipeline is four pure functions in a row (`Ave.Extensions.Documents/DocumentRendering.cs`). The composition lives in its
own package so that a front-end depends only on the vocabulary it translates into (Core, Fonts, Images,
DocumentModel), and a second front-end (markup) sits next to the first one instead of on top of it:

```
Document ──DocumentTranslator.ToModel──▶ StyledDocument ──DocumentLayout.Run──▶ PageDocument ──PdfRenderer.Render──▶ bytes
 (front-end value)                       (document model)   (Paginator + PagePainter)  (page model)
                                                                                        └──PageRasterizer.Render──▶ pixels
```

## The two models (and the fragment tree between them)

- **Document model** (`DocumentModel/Node.cs`, `BoxStyle.cs`, `TextStyle.cs`, `StyledDocument.cs`). What the author
  meant, with CSS semantics and only computed values: a tree of `Box` (with a `SemanticRole` and a
  `LayoutAlgorithmId`), `TextRun`/`CounterRun` and `ImageNode`. Every node has an id (pre-order) and the source
  location of the call that made it. Roles (`Core/SemanticRole.cs`) are separate from layout: a box laid out as a
  table is not necessarily a table for a screen reader. Any front-end (code now, markup later) produces this model.
  The document also carries its layout profile (`DocumentModel/LayoutProfile.cs`): the policies its front-end chose
  where layout has a choice (see the policy table below).
- **Fragment tree** (`Layout/Fragment.cs`). The output of layout per page: boxes cut at page breaks, lines, text,
  images, table rules, and slices of content taller than a page, with positions relative to the parent. Internal to
  the layout layer, but serialised for tests.
- **Page model** (`PageModel/DrawItem.cs`, `PageDocument.cs`). What to draw, in absolute page coordinates: glyph runs
  (glyph ids, not text plus a font name), filled rectangles, images, and markers — `StructureStart`/`StructureEnd`
  (node, role, source location, cell span) for content, `ArtifactStart`/`ArtifactEnd` for decoration and page
  furniture, `ClipStart`/`ClipEnd` (a rectangle) around what is drawn clipped. It does not depend on the document
  model, so other back-ends (raster, SVG) and a future PDF reader can share it. One nesting rule for all three kinds
  of marker, stated once on `DrawItem` and checked once for both back-ends with the other page-model rules
  (`PageModel/PageModelRules.cs`, `MarkerNesting`): the markers of a page nest as one sequence of pairs, every end
  closing the innermost open marker of its own kind; artifacts do not nest and contain no structure markers; a clip
  may open anywhere, at most 27 one inside the other. Content outside
  every structure and artifact marker belongs to nothing (a tagged profile refuses it).

All three have a stable text form (`DocumentModelText`, `FragmentText`, `PageModelText`) used by golden tests.

## The layout contract

`Layout/LayoutAlgorithm.cs` states it: a layout algorithm is a pure function (environment, box, constraint space,
incoming break token) → `LayoutResult` (a fragment plus an outgoing break token and a break appeal, or "no room",
or an error). `ConstraintSpace.cs` is the available space and whether this is the top of a page; `BreakToken.cs`
says where to continue; `LayoutResult.cs` and `BreakAppeal.cs` carry the result and how good the break is.
`LayoutEnvironment.cs` holds the fonts, the algorithms by id, the current page (for page counters) and the document's
layout profile, which the paginator takes from the document (`DocumentLayout.Run` needs nothing else). The
paginator (`Layout/Paginator.cs`) calls the root algorithm once per page with the previous page's token until no token
is left. When the document shows its page count, it repeats the whole document until that count settles (at most 5
passes); otherwise one pass is all.

**The seam for later front-ends.** The layout contract (a pure function from box, space and break token to fragment
and token) is what a later front-end — markup, a Typst-like language, a report definition — plugs into. Today the
engine ships four algorithms (flow, inline, tracks, image) and registering another is internal
(`LayoutEnvironment.WithAlgorithm`); `BreakToken` can only be derived inside the layout assembly, and the parts of a
table are a display type (`TableDisplay`), not algorithms. When a front-end needs an algorithm of its own, these are
the three places to open, with that front-end as their first user.

## Where the policies and whole algorithms are

Where CSS lets the layout choose, or where the engine offers a choice beside what Chromium prints, the choice is a
policy of the document's layout profile (`DocumentModel/LayoutProfile.cs`). The policies are bundled per front-end
family: the profile is a value in the document model, the front-end sets it (the code front-end keeps the defaults
unless the author asks otherwise, a browser-like front-end would set Chromium's values), and layout reads it. The
policies today:

| Policy | Values (default first) | Where | Rule |
| --- | --- | --- | --- |
| monolithic overflow: an image or a line box taller than the page area, at the top of a page | `Refuse` (`Documents.Layout.DoesNotFit`); `Slice` (Chromium: sliced over the pages it needs, at most 100, each slice clipped; every slice of an image is content of its figure, later slices of a line are pagination artifacts) | `Layout/MonolithicSlices.cs`, `ImageLayout.cs`, `InlineLayout.cs`, `PagePainter.cs` | CSS Fragmentation 3 §4.4 ("the UA may also fragment the contents of monolithic elements by slicing the element's graphical representation"); checked against Chromium's print (cases `a4-*`) |
| font metric rounding: a font's ascent, descent and line gap at its size, for `line-height: normal`, the strut and the baseline's place in a line | `Exact`; `WholePixels` (Chromium: each rounded to a whole CSS pixel, half away from zero, the half-leading above floored to a whole pixel) | `Layout/LineBoxes.cs` (`InlineExtent`, `CssPixels`) | CSS 2.1 §10.8.1 leaves the metrics to the user agent; Chromium `font_metrics.cc`, `simple_font_data.cc`, `line_utils.cc`; checked against Chromium's print (cases `f-lato-*`, `f-mixed-lato`: every line within 1/64 pixel; the Theano Didot cases and `f-mixed` are measured and wait for the typographic metrics, USE_TYPO_METRICS) |
| empty row parts: a cut of a table row where one part shows nothing | `Avoid` (such a cut is a last resort: the row moves whole where it can); `Allow` (Chromium: a row is cut wherever its cells can break) | `Tracks/TableBreaks.cs` (`Cut`, `CutShows`) | CSS leaves where a row breaks to the user agent; checked against Chromium's print (cases `a2-*`, `a3-mirror`) |

The whole algorithms and the other rules:

| What | Where | Rule it implements |
| --- | --- | --- |
| cascade and inheritance, user-agent styles | `Fluent/DocumentTranslator.cs`, `Cascade.cs`, `UserAgentStyles.cs` | CSS Cascade 4 §4, §6.1, §7.2; HTML rendering (heading sizes) |
| input validation | `Fluent/InputChecks.cs` | one function per rule, each cites its source |
| block flow, keep rules, break choice | `Layout/FlowLayout.cs`, `BreakAvoidance.cs`, `ForcedBreaks.cs` | CSS 2.1 §9.4.1, CSS Fragmentation 3 §3–§4 |
| line heights and the strut: `normal`, numbers, lengths and percentages; every line starts with its paragraph's strut | `DocumentModel/LineHeight.cs`, `Layout/LineBoxes.cs`, `Fluent/LineHeightValue.cs` (computed values) | CSS 2.1 §10.8, §10.8.1; CSS Inline 3 §5.1, §5.3; checked against Chromium's print |
| orphans and widows: where a paragraph breaks, the best breakpoint inside a complete paragraph; a cut of a table row is as good as the breaks it makes inside its cells | `Layout/InlineLayout.cs` (`BreakWhereFull`, `BestBreakInside`), `Tracks/TableBreaks.cs` (`Cut`) | CSS Fragmentation 3 §3.3, §4.4 rule 3; checked against Chromium's print |
| early breaks: a keep rule that reaches back into an earlier box's content; a box whose bottom padding does not fit breaks at its best inner breakpoint | `Layout/EarlyBreak.cs`, `FlowLayout.cs`, `InlineLayout.cs`, `Tracks/TablePage.cs`, `SlicedBox.cs` | CSS Fragmentation 3 §4.4, §4.5; checked against Chromium's print |
| where a page starts: a box that does not fit below margins, empty boxes or padding moves on; only content that starts the page is refused; the last-resort break | `Layout/ConstraintSpace.cs` (`IsAtPageStart`), `FlowLayout.cs`, `SlicedBox.cs`, `BreakAppeal.cs` (`LastResort`) | CSS Fragmentation 3 §4.1, §4.4, §5.2; checked against Chromium's print |
| margin collapsing | `Layout/MarginCollapsing.cs` | CSS 2.1 §8.3.1 |
| box slicing at page breaks | `Layout/SlicedBox.cs` | CSS Fragmentation 3 §5.2–§5.4 |
| inline layout, line boxes | `Layout/InlineLayout.cs`, `LineBoxes.cs` | CSS 2.1 §9.4.2, §10.8 |
| tables and rows of blocks | `Layout/Tracks/` (`TracksLayout.cs` lists the steps) | CSS 2.1 §17 (fixed and auto layout, collapsing borders), CSS Grid `fr` §7.2.4 / §11.7.1, CSS Fragmentation for row breaks |
| images | `Layout/ImageLayout.cs` | CSS 2.1 §10.3.2 (with a stated deviation for width + height) |
| page templates, page counters | `Layout/PageAreas.cs`, `Paginator.cs`, `PageContext.cs` | CSS Paged Media 3 §4.2.2, §5, §6.1 |
| what is content and what is an artifact (a repeated header row and every slice of a line of text after the first are pagination artifacts; every slice of an image is content of its figure) | `Layout/PagePainter.cs` | ISO 32000-1 §14.8.2.2 |
| clips in PDF: `q x y w h re W n` … `Q` between marked-content sequences, never across one | `Render.Pdf/PageContentStream.cs` (`Clips`) | ISO 32000-1 §8.4.2, §8.5.4 |
| line breaking | `Typesetting/LineBreaking/UnicodeLineBreaking.cs`, `GreedyLineBreaker.cs` | UAX #14 rev. 57 |
| shaping | `Typesetting/Shaper.cs`, `Shaping/` | OpenType GSUB/GPOS/GDEF, legacy kern |
| Unicode normalization before shaping: decompose, reorder marks, recompose, as far as the font allows | `Typesetting/Unicode/Normalization.cs`, data `UnicodeNormalizationData.g.cs` | UAX #15; HarfBuzz's normalizer (hb-ot-shape-normalize.cc) |
| shaping at a break: each line shaped as the line it is, its ends shaped again where the paragraph's shaping marked them unsafe to break | `Typesetting/GreedyLineBreaker.cs`, `ShapedGlyph.UnsafeToBreak`, `Shaper.ShapeLinePiece` | HarfBuzz's unsafe-to-break flag (hb-buffer.h) |
| alignment and justification | `Typesetting/LineJustification.cs` | CSS Text 3 §6.1, §6.4 (inter-word) |
| font parsing and subsetting | `Fonts/FontParser.cs`, `Tables/`, `OpenType/`, `FontSubsetter.cs` | OpenType specification per table |
| glyph outlines (simple and composite glyphs, no hinting) | `Fonts/Tables/GlyphOutlineDecoder.cs`, `GlyphShape.cs` | OpenType "glyf — Glyph data" |
| PNG encoding (filter choice, chunks) | `Images/Png/PngEncoder.cs` | PNG §9, §11.2, §11.3.3.5, §12.8 |
| JPEG decoding (sequential and progressive Huffman, inverse DCT, up-sampling, colour) | `Images/Jpeg/JpegDecoder.cs`, `InverseDct.cs`, `JpegColor.cs` | ITU-T T.81 Annex A, B, F.2, G.1.2; T.871 §7; ISO 32000-1 §7.4.8 (ColorTransform) |
| PDF syntax and file structure | `Pdf/PdfSerializer.cs`, `PdfWriter.cs` | ISO 32000-1 §7.3, §7.5 |
| fonts and images in PDF | `Render.Pdf/FontEmbedding.cs`, `ToUnicodeCMap.cs`, `ImageEmbedding.cs` | ISO 32000-1 §9.7–§9.10, §8.9 |
| conformance profiles | `Pdf/PdfProfile.cs` (what each standard adds), `Render.Pdf/ConformanceRules.cs` and `Tagging/TableRegularity.cs` (what it refuses) | ISO 19005-3, ISO 14289-1; rule ids from veraPDF |
| tagged PDF | `Render.Pdf/Tagging/` (`PageEvents` → `StructureTree` → `StructureObjects`, `ContentMark`, `StructureTypes`) | ISO 32000-1 §14.6–§14.8 |
| XMP metadata | `Pdf/Xmp/` (generic writer and extension-schema descriptions), `Render.Pdf/MetadataPacket.cs` (which properties) | ISO 16684-1; ISO 19005-3 §6.6 |
| output intent colour profile | `Pdf/SrgbIccProfile.cs` | ICC.1:2001-04; IEC 61966-2-1 |
| attachments | `Render.Pdf/AttachmentEmbedding.cs`, catalog in `PdfRenderer.cs` | ISO 32000-1 §7.11; ISO 19005-3 §6.8 |
| object numbering | `Render.Pdf/ObjectPlan.cs` | fixed in advance, so output is deterministic |
| page model → pixels (glyph placement, image mapping, clip box) | `Render.Raster/PagePainter.cs`, `PageRasterizer.cs` | ISO 32000-1 §8.9.4 (image space), OpenType "glyf" (non-zero fill) |
| curve flattening (error bound 1/64 pixel) | `Graphics/Flattening.cs` | interpolation error h²/8·max\|B″\| |
| clipping to a box | `Graphics/Clipping.cs` | Sutherland–Hodgman (winding kept inside the box) |
| exact-area coverage, fill rules | `Graphics/AreaRasterizer.cs` | signed-area accumulation; ISO 32000-1 §8.5.3.3 |
| compositing, image resampling | `Graphics/Canvas.cs` | source over on encoded 8-bit values (Porter–Duff); exact box filter (ISO 32000-1 §8.9.5.3, Interpolate false) |

Strategies are plain functions; the closed sets (PDF objects, draw items, fragments, front-end elements, XMP values,
structure kids) are records with private constructors, each in one file so the whole set can be read at once.

## Who checks what

Every rule is checked in one place, by the layer that owns the reason for it; each check reports all its problems
together, at the author's line where there is one.

| Layer | Checks | Error codes |
| --- | --- | --- |
| front-end (`Fluent/InputChecks.cs`, `InputLimits.cs`) | the author's input: lengths, borders, columns, spans, table shape, page breaks only where they can apply, metadata text (XML characters), language tags, media types, unique attachment names, nesting depth, a layout profile with defined policy values | `Documents.Input.*` |
| layout (`Layout/LayoutErrors.cs`) | only what cannot be laid out: content that does not fit, content that would need more than 100 slices (one piece) or 1 000 (one document), columns that do not fit, a page-count fixpoint that does not settle | `Documents.Layout.*` |
| back-end contract, one rule set for both back-ends (`PageModel/PageModelRules.cs`; each back-end reports a breach under its own code), plus the options (`Render.Pdf/PdfRenderer.cs`) | a page model from any producer: metadata and at least one page, each of positive finite size, nothing missing, finite positions and sizes, glyphs of their font with a text, enum values, spans within the table model's limits, table structure that nests, a table of rows or of row groups with at most one head, which comes first, one role per node, attachments with a name, a media type and a unique name; markers (structure, artifact, clip) that nest by the one nesting rule, with at most 27 clips one inside the other (`MarkerNesting`); render options with a profile and a file identifier of 1 to 32 767 bytes | `Documents.Pdf.Write.InvalidPageModel`, `InvalidOptions`, `InvalidFileIdentifier` |
| raster back-end (`Render.Raster/PageRasterizer.cs`) | the same page-model contract (`PageModel/PageModelRules.cs`); options with a positive finite resolution; a page of at most 2^26 pixels at that resolution; a page number of the document | `Documents.Raster.InvalidPageModel`, `InvalidOptions`, `TooLarge`, `PageNotFound` |
| profile rules (`Render.Pdf/ConformanceRules.cs`, `ProfileLimits.cs`, `Tagging/TableRegularity.cs`) | what a profile forbids: no title or language, a figure without (or with blank) alternative text, heading order, attachments without relationship, irregular tables (and tables too large to check), the PDF/A limits (page size, string and name length, media type form, object count), glyph and text rules; every veraPDF rule and how it is met is in `docs/conformance-matrix.md` | `Documents.Pdf.Conformance.*` |

The slot-filling rule of a table (which slot each cell takes, and when a span runs past its group or overlaps
another) is `Core/TableSlots.cs`. The front-end's table check, the layout's grid and the PDF/UA regularity check all
use it, so the three cannot disagree; the span rule (`TableSlots.IsValidSpan`), the language form
(`Core/LanguageTags.cs`) and the characters text may hold (`Core/TextCharacters.cs`) are single rules in the same way.
Some rules are checked twice, on purpose, by calling the same rule: the front-end reports them at the author's line,
and the back-end refuses them as well, because it accepts page models that did not come from the front-end. These are
the span rule, unique and non-blank attachment names (a duplicate would silently drop a file from the embedded-files
name tree), and the language form (for the tagged profiles).

## The raster back-end

`PageRasterizer` draws a page of the page model as 8-bit RGBA pixels (sRGB values, alpha not premultiplied) at a
resolution: page coordinates times resolution / 72, pixel (i, j) covering the square from (i, j) to (i + 1, j + 1).
It checks the page model by the same contract as the PDF back-end, then `PagePainter` walks the display list in
painting order; markers draw nothing.

| Layer | Its part |
| --- | --- |
| Fonts | `TrueTypeFont.Shape`: a glyph's contours from 'glyf', composites resolved, no hinting, quadratic curves |
| Images | `ImageDecoder.DecodeSamples`: PNG samples, or a JPEG decoded (baseline and progressive); `ImageEncoder.EncodePng` |
| Graphics | flattening (1/64 pixel), clipping to a box, exact-area coverage, source-over compositing, the exact box filter for images |
| Render.Raster | glyphs as outlines filled with the non-zero rule at their exact positions (no hinting, no snapping, no cache), rectangles, images mapped onto their rectangle as PDF maps an image's unit square |
| composition | `ToPng`, `ToPngPages`, `ToImages` on a document or on its pages (`ToPages`) |

Choices, each stated at its code: coverage is exact for straight edges where no two contours overlap or meet inside
the part of a pixel that the clip box lets through (where two contours wound the same way overlap, a pixel crossed by
an edge of each is too dark by the doubly covered part: up to half the pixel with two contours whose edges run together
through it, more with three, under even-odd up to the whole pixel, a known defect repaired in the raster follow-up;
where contours of opposite direction meet inside a pixel they cancel, up to the whole pixel;
`Graphics/AreaRasterizer.cs`). The fill
rule is taken over that part of the pixel, A · g(|a| / A) with A its area and a the integral of the winding number
over it, so a clip edge through the inside of overlapping contours (winding 2 over part of a pixel) is exact
(`ClipBoxCoverageTests`); curves are within 1/64 pixel; colours are
blended on the encoded 8-bit values, as PDFium, MuPDF and poppler blend them — blending in linear light would be more
correct in theory and visibly different from every PDF viewer, so it is not done; an image is a grid of uniformly
coloured squares (PDF's `/Interpolate false`), each device pixel showing the mean of the image over the part it covers;
a JPEG's three components are YCbCr unless an Adobe marker says otherwise, as DCTDecode reads them.

**Arguments of the graphics library.** One rule for every public member of `Ave.Extensions.Documents.Graphics`
(`Canvas`, `Path`, `PathBuilder` and the types they take): a value it cannot draw with — not finite, out of range, a size
that does not fit — is answered with a `Result` error with a `GraphicsErrorCodes` code (`Documents.Graphics.InvalidValue`,
`Documents.Graphics.TooLarge`), never with an exception; only a null reference (`ArgumentNullException`) is the caller's
bug. A path cannot be built out of order: `PathBuilder` offers only `MoveTo`, which hands back a `SubpathBuilder` with
the other commands, so a segment before the first `MoveTo` does not compile. Inside
the ranges (table below) the work of a fill is bounded by the pixels it can touch, never by the size of the shape: a
shape is clipped to the box in floating point before any number becomes an integer. `ArgumentRuleTests` in
`UnitTests.Extensions.Documents.Graphics` holds it to that, hostile values included.

**Determinism.** The pixel path uses only correctly rounded double arithmetic and square roots, in a fixed order, and
no platform maths function, so the pixels do not depend on the platform; checked on x64 Linux, x64 Windows and arm64
Linux for .NET 10 (the documents legs of CI, arm64 on `ubuntu-24.04-arm`). The PNG bytes depend on the runtime's
deflate as well (see Determinism below), so the golden tests compare decoded pixels and one test pins the bytes of one
PNG.

**Where correctness is shown.** By analytic tests: the Graphics tests prove coverage, compositing and the image rule
against exact areas (any scale, offset and affine map), and the page-level tests
(`UnitTests.Extensions.Documents.Render.Raster/PageGeometryTests.cs`) check whole pages pixel by pixel against the
geometry: a glyph whose contours overlap, a glyph's offsets and size, an image in a box of another shape (the painter's
mapping of an image onto its rectangle), a rule thinner than a pixel. The golden PNG files catch change over time, not
error: they are the engine's own output.

**Compared with viewers: a sanity check.** `tools/reference/raster/README.md`: every sample page against MuPDF and
poppler renders of the engine's PDF, by the rule in `UnitTests.Extensions.Documents.Samples/RasterComparison.cs` (for
the largest difference away from edges, the mean squared error and the pixels over 128 steps, no farther from either
viewer than the viewers are from each other; for the pixels over 32 and 64 steps, no farther from the nearer one). It
catches gross errors, not small ones. Measured by the review of 2026-11-18 with planted errors, it did not see text 1 %
larger, curves drawn as coarse polygons, every rectangle one pixel to the right, an image snapped to whole pixels, or an
image's transparency ignored; at 96 dpi body text has no pixel away from edges, so the strictest metric does not see
text at all, which is why every sample page is also compared at 300 dpi. At 300 dpi the rule holds on 22 of 23 pages;
the exception is page 1 of timesheet-edge.nl, where 12,753 pixels differ by more than 128 steps from poppler against
12,569 between the viewers, while the same page has 552 such pixels against MuPDF. The engine is closest to MuPDF;
poppler draws glyph edges heavier and snaps thin rectangles to whole pixels. The viewers check images only for decoding and colour, on
images at 1:1 on the pixel grid against MuPDF (poppler resamples every image drawn below 4×, also at 1:1). The sliced
sample has nine more named exceptions (pages that hold little but an image or a few lines, where the viewers' distance
to each other is small); each is pinned with its numbers and holds the rule once its named causes are removed, image
interiors or baselines on whole pixels (`RasterReferenceTests`, `NamedExceptions`).

**Different from the viewers: baselines on whole pixels.** Both viewers draw a glyph's baseline on a whole pixel row,
MuPDF on the nearest (a half goes down the page), poppler on the one above (it floors), at 96 and at 300 dpi alike; the
engine draws a glyph where the page model puts it. So a line of text can lie up to half a pixel (MuPDF) or up to one
pixel (poppler) from the viewers' place; it never moves a line. Measured on lines of Lato 11 pt with baselines at
fractions 0.1 to 0.95 of a pixel, and on the sliced sample: page 5's line has its baseline at 110.476 px at 96 dpi,
both viewers draw it at 110 (ink centre row 105.33 in both, 105.82 in the engine, with equal ink;
`tools/reference/raster/README.md`). This holds for every sample; the comparison carries it by drawing the engine's
page again with the viewers' rows (`RasterComparison.BaselinesOnPixelRows`).

**Known limits.** No hinting: small text is lighter than in a viewer that hints (PDFium). Two items that meet at a
boundary inside a pixel each paint their exact coverage, so some of what is beneath shows through: two rectangles of
colour 51,102,153 on white meeting at half a pixel give 102,140,178 at the boundary (a quarter of the white), MuPDF
95,135,175; poppler moves rectangle edges to whole pixels instead (51,102,153, but the boundary moves); the samples have
no such items (`tools/reference/raster/README.md`). An enlarged image shows its sample squares, as the PDF defines it
(ISO 32000-1 §8.9.5.3; PDF/A forbids asking for interpolation, ISO 19005-3 §6.2.8); some viewers smooth it. An image
whose grid reaches farther than 2^40 device pixels from the origin (`Canvas.MaxImageReach`; at 96 dpi about
7.9 × 10^11 pt) is refused: `Documents.Graphics.InvalidValue` from the canvas, `Documents.Raster.ImageOutOfReach` from
the raster back-end; the PDF back-end writes such an image as the page model says. The bound is the arithmetic of
placing the image's pixel edges: each is computed from terms as large as the reach, each product and sum rounds by
2^-53 of its size, so an edge lies at most 6 · 2^-53 · reach from its exact place, within the bound 2^-10.4 pixel (under
a fifth of an 8-bit step at full contrast); the same bound keeps the rounding of a pixel's preimage below 2^-12 of its
size. Measured beyond it, with numbers that are not round: an edge 0.08 pixel off at a reach of 4.9 × 10^15 pixels,
0.67 at 4.9 × 10^16, 5.3 at 4.9 × 10^17; a rotated 61 × 43 image enlarged 10^13 times (reach 8.6 × 10^14) up to 3 steps
off. Within it the tests draw images enlarged so far that a page shows a small part of one pixel, rotated, sheared or
not, and get the exact areas (`Graphics/Canvas.cs`, `ImageResamplingTests`, `PageRasterizerTests`). JPEG: arithmetic coding, 12-bit, lossless and four components are refused, as when a
PDF embeds them. Encoding a PNG costs more than drawing the page (about 50 ms at 96 dpi, 500 ms at 300 dpi for an A4
page); speed is stage 5's work. Memory and time not yet bounded beyond the limits below: `ToPngPages` draws and keeps
every page before it returns (about 35 MB per A4 page at 300 dpi); a damaged JPEG can cost the decoder hundreds of MB and
seconds before it reports its error (inside the limit of 2^26 pixels, so a cost, not a breach); and a page model has no
work limit, so a hostile one (thousands of glyphs that each cover the page) can take many seconds. Each distinct image
is decoded once per render call, however often it is drawn.

## Bounded work: the one rule every reader of input follows

A reader of input is any code that takes bytes or values it did not make: the font parser, the image decoders, the
shaper (a font's lookups are input), the back-end's checks of a page model. **Its work and its memory are bounded by
the size of its input, or by a fixed and named limit; never by a number the input states.** Four habits make it so:

1. **Charge for what is done.** A parse has a work budget proportional to the bytes (`TableReader.WorkPerByte`), and
   charges it for every value read AND every record created (`TableReader.Records`, which `Count` calls): a record
   that reads nothing still costs memory. A structure that many offsets may point at is parsed once and shared
   (`TableReader.Shared`); walking parsed data again is charged too (`TableReader.Spend`). Shaping spends operations
   the same way: every glyph a lookup looks at and every subtable it tries (`Shaping/ShapingBudget.cs`); every search
   in the shaper is linear in the text, so the budget, a number per glyph, refuses only a font's work.
2. **Check a count against the bytes that must hold it** before allocating for it (`TableReader.Count` and `Fits`; a
   PNG header's size against `ImageLimits` before anything is inflated; a JPEG frame's size before its coefficients are
   allocated — a JPEG that is only embedded is never decoded).
3. **Name a limit where a number decides the size** — decoded pixels, glyphs made by substitutions, lookup operations,
   spans, the PDF/UA grid, nesting, lengths — check it before the work, and give it an error code (table below). A
   limit holds for the whole input it protects (one text, not each piece of it).
4. **Compute with input numbers in 64 bits**, so a sum or product cannot wrap around into a valid-looking value.

| Limit | Value | Where | Error |
| --- | --- | --- | --- |
| work of a layout table parse | 8 units per byte, at least 16 384 | `Fonts/OpenType/TableReader.cs` | `Documents.Font.Parse.InvalidValue` |
| decoded image (PNG, and JPEG decoded to samples) | 2^26 pixels | `Images/ImageLimits.cs` | `Documents.Image.TooLarge` |
| blocks the scans of one JPEG visit together | 32 times the blocks of its frame | `Images/Jpeg/JpegDecoder.cs` | `Documents.Image.TooMuchWork` |
| glyphs of one text | 64 per input glyph, at least 16 384 | `Typesetting/Shaping/GlyphSubstitution.cs` | `Documents.Typesetting.TooManyGlyphs` |
| shaping operations of one text | 1 024 per input glyph, at least 65 536 | `Typesetting/Shaping/ShapingBudget.cs` | `Documents.Typesetting.TooMuchWork` |
| cell span | 1 000 columns, 65 534 rows | `Core/TableSlots.cs` | `Documents.Input.Invalid.Table`; in a page model the back-end's code, `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel` |
| every number of a page model (sizes, positions, font sizes, areas), and the edges computed from them (x + width, y + height, the place of a glyph, the height above the foot of the page) | within ±3.403 × 10^38, the range of a PDF real (ISO 32000-1 Annex C, Table C.1) | `PageModel/PageModelRules.cs` | `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel` |
| pixels of one page drawn as an image | 2^26 | `Render.Raster/PageRasterizer.cs`, `Graphics/Canvas.cs` | `Documents.Raster.TooLarge`; `Documents.Graphics.TooLarge` |
| coordinates the graphics library draws with | ±10^150 after the transform; a clip box within ±2^30 pixels | `Graphics/Canvas.cs` | `Documents.Graphics.InvalidValue` |
| PDF/UA grid of one table | the first row's width; 4 194 304 slots | `Render.Pdf/Tagging/TableRegularity.cs` | `Documents.Pdf.Conformance.PdfUa1.TableTooLarge` |
| nesting of elements | 64 levels | `Fluent/InputLimits.cs` | `Documents.Input.Invalid.TooDeep` |
| nesting of arrays and dictionaries in one PDF object | 64 levels | `Pdf/PdfSerializer.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| nesting of arrays and structures in one XMP property | 64 levels | `Pdf/Xmp/XmpPacket.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| a length in the front-end | 14 400 pt | `Fluent/InputLimits.cs` | the property's `Documents.Input.Invalid.*` code |
| an HTML document | 2^24 UTF-16 code units (bytes are checked against the same number); nesting 512 elements, the depth at which Chromium stops nesting; selectedcontent clones 4 nodes and characters per input character; 10 000 diagnostics kept | `Html.Parsing/HtmlInputLimits.cs` | `Documents.Html.TooLarge`, `TooDeep` (the parse fails); `Documents.TooManyDiagnostics` (the last diagnostic; the style resolution refuses) |
| a style sheet | 2^24 UTF-16 code units; nesting of blocks, functions and rules 64 (the readers recurse once per level: 252 nested `@media` overflowed a 256 KB thread at the old limit of 256, 64 fit with room) | `Css/CssInputLimits.cs` | `Documents.Css.TooLarge`, `TooDeep` (a refusal; the sheet is empty) |
| nesting of selectors (pseudo-class arguments and nested style rules together) | 32 levels: the only recursion of selector matching and specificity, kept well inside the stack of a 256 KB thread | `Css/CssInputLimits.cs` | `Documents.Css.TooDeep` (the rule is refused) |
| selector matching and collecting of one cascade | 2^26 units (a simple selector tested, a fact remembered, a matched rule or declaration collected): about 3 s on the development machine for 1 000 rules matching each of 10 000 elements; 2^22 facts remembered (a bound on the memory) | `Css/CssInputLimits.cs` | `Documents.Css.TooLarge` |
| imported style sheets of one cascade | 1 024, counted over every import followed | `Css/CssInputLimits.cs` | `Documents.Css.TooLarge` |
| one var() substitution | 2 097 152 characters of text, Chromium 159's limit (measured) | `Css/CssInputLimits.cs` | past it: invalid at computed-value time, as in Chromium (`Documents.Css.Invalid.ComputedValue`); where the engine's measure cannot tell (within 8 characters per substitution of the limit): `Documents.Css.TooLarge` |
| all var() substitutions of one cascade | 2^26 characters | `Css/CssInputLimits.cs` | `Documents.Css.TooLarge` |
| diagnostics of one style sheet, one cascade, one resolution | 10 000 kept | `Css/CssInputLimits.cs`, `Html.Parsing/HtmlInputLimits.cs` | `Documents.TooManyDiagnostics` (a refusal) |
| points of one glyph outline, components included | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| component references one glyph outline visits | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| nesting of composite glyphs | 64 levels | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| clips open one inside the other on a page, for both back-ends | 27 (in PDF one `q` each, plus the drawing's own: the 28 levels of ISO 32000-1 Annex C and ISO 19005-3 §6.1.13; the raster back-end takes the same) | `PageModel/PageModelRules.cs` | `Documents.Pdf.Write.InvalidPageModel`, `Documents.Raster.InvalidPageModel` |
| slices of one piece of content taller than the page area (`MonolithicOverflow.Slice`) | 100, one per page | `Layout/MonolithicSlices.cs` | `Documents.Layout.TooManySlices` |
| slices of one document, all pieces together (`MonolithicOverflow.Slice`) | 1 000, counted page by page; a document without a piece taller than the page area has none | `Layout/MonolithicSlices.cs`, `Layout/Paginator.cs` | `Documents.Layout.TooManySlices` (the message names the limit for one document) |

Selector matching is linear in the selector's steps times the elements (`Css/Selectors/SelectorMatcher.cs`): per step
and element it computes whether the steps up to it match, whether some ancestor's or earlier sibling's does, each
once per cascade, with an explicit stack; a subject reached through `+` or `~` must have as many earlier siblings,
which rejects long sibling chains at once. A nested rule refers to its parent's selector list instead of copying it,
so selectors and specificity do not grow with the nesting. Class, ID and attribute-word tests use sets built once per
element. An element is matched only against the rules bucketed under its ID, its classes, its type, and the rules
that name none of these (`Css/Cascade/CascadeIndex.cs`); the rest of the matching work counts against the limit
above. Custom properties are resolved in dependency order by an iterative walk, so a chain of any length costs its
length; every substitution is measured as Chromium measures it and stops at its limit; results are shared by the
elements that inherit the same custom properties. No reader recurses on the input deeper than the nesting limits
above, and each of them runs on a 256 KB stack at the deepest allowed input (`CssBoundedWorkTests`,
`BoundedWorkTests`).

One reader is bounded by a constant rather than by its input: the `cmap` formats 4 and 12 are expanded into one entry
per mapped code point, so a few groups of 12 bytes can fill all 1 114 112 code points of Unicode (tens of MiB). The
bound is Unicode's size; a reader written to the rule above would keep the groups and search them.

## Conformance in one paragraph

`PdfProfile` is a value: `Plain`, `PdfA3b`, `PdfA3a`, `PdfUa1`, and combinations with `AndPdfUa1()`. The renderer
first applies the refusals (missing title or language, a figure without alternative text, heading order, an
attachment without relationship — `Documents.Pdf.Conformance.*`, all reported together, at the author's line), then
reads each page's markers into events, builds the structure tree from them (tagged profiles), refuses an irregular
table (PDF/UA), and writes: XMP metadata in sync with the document information, the sRGB output intent, the
structure tree with marked content (one MCID per content run between markers), artifacts with their kind, the
attachments with `/AF`. A plain file is written exactly as before profiles existed (same object numbers, no marked
content).

## Determinism

Same input, same output: dates and file identifier are inputs (the identifier defaults to a hash of the content), no
clock, no machine state, dictionaries keep insertion order, object numbers come from `ObjectPlan`, and the raster's
pixel path uses only correctly rounded arithmetic in a fixed order. Tests: repeated runs give byte-identical PDFs
(`Same_input_gives_byte_identical_output…`, `A_tagged_file_is_deterministic`, `Two_runs_give_byte_identical_PDFs…`),
and the pixels of a Graphics scene and of the samples' golden images are pinned.

Compressed bytes are the exception. PDF streams (FlateDecode) and PNG files are compressed by the .NET runtime's
deflate (`ZLibStream` at `CompressionLevel.Optimal`; zlib-ng in .NET 10). Microsoft's documentation of `DeflateStream`
says the compressed bytes "can vary between .NET releases, platforms, and underlying compression engines", and a Linux
distribution may build .NET against its system zlib. What is shown: the bytes of one pinned PNG (`PngEncoderTests`) are
equal on x64 Linux, x64 Windows and arm64 Linux in CI for .NET 10; a manual check found Flate output equal on Linux x64
(runtime 10.0.12) and Windows x64 (10.0.15) at all three compression levels (2026-11-18). So: the same pixels on the platforms checked; the PDF content does not depend on the machine by
construction (no clock, no machine state), but no test compares it across platforms; and with another runtime or
another zlib build the same content can come out as other compressed bytes.

## HTML/CSS front-end (stage 3: text to computed styles)

```
HTML text ──HtmlParser.Parse──▶ HtmlParseResult ─┐
CSS text  ──CssParser.ParseStyleSheet──▶ StyleSheet ┴─ HtmlStyleResolver.Resolve ──▶ StyledHtmlDocument (+ diagnostics)
                                     (CascadeIndex.Build + StyleComputer.Compute)
```

Three libraries, each below the next: `Html.Parsing` (Core only) is the HTML Standard's parser; `Css` (Core only)
knows nothing about HTML, and matches selectors on `IStyleElement`, which the HTML front-end implements; `Html`
(Core, Html.Parsing, Css) does style resolution. Stage 4 adds box generation to `Html`.

Where each rule lives:

| Rule | Place |
| --- | --- |
| tokenizer, tree construction, encoding sniffing, parse errors | `Html.Parsing/Tokenizing`, `TreeBuilding`, `Decoding` (HTML Standard §13.2) |
| CSS tokens, rules, declarations, error recovery, byte decoding | `Css/Syntax` (CSS Syntax 3) |
| the typed rules, @import order, @layer names, refused and ignored at-rules | `Css/Rules/RuleBuilder.cs` |
| selectors, their support class, specificity, matching | `Css/Selectors` (`PseudoClasses.cs` is the catalog, with the rules Chromium 159 applies after pseudo-elements) |
| media queries and @supports | `Css/Media`, `Css/Conditions`, answered in `Css/Cascade/CascadeIndex.cs` |
| every property: name, initial value, inheritance, supported values, computed value | `Css/Properties/PropertyRegistry*.cs` (the one registry; `docs/html-css-profile.md` is generated from it) |
| origins, importance, layers, nesting, specificity and order; revert, revert-layer, var(), defaulting | `Css/Cascade` (`DeclarationPreparer`, `CascadeIndex`, `StyleComputer`) |
| the user-agent style sheet | `Html/UserAgent/html.css` (HTML Standard §15 as Chromium 159 applies it) |
| presentational hints | `Html/PresentationalHints.cs` (with `AttributeValues.cs` for the HTML microsyntaxes) |
| which diagnostic refuses a document | `Css/CssDiagnostics.ClassOf`, `Html/HtmlStyleErrorCodes.cs` (`HtmlStyleDiagnostics.ClassOf`) |

Who checks what: the HTML parser reports parse errors (warnings: it recovers as the standard says, as a browser
does) and fails only past its limits or on bytes it would decode differently from a browser. The CSS reader reports
where it is written what CSS Syntax 3 recovers from and the few things it knows Chromium drops as well (warnings: a
top-level `!` other than `!important`, a rule in the wrong place, another engine's prefix, a `var()` cycle); the
cascade reports what it ignores by rule and what it refuses **when it applies** (a refused declaration that would
win, a rule with an unknown property that matches an element), once per declaration (per element for the user-agent
sheet, so the element is named). Refusal is decided by the diagnostic's code alone; `HtmlStyleOptions.Unsupported`
chooses whether refusals fail the document (the default) or are only reported.

The policy for each refusal (review 39, ruling D-b): input above CSS Syntax 3 that the engine cannot read is either
invalid, which a browser drops, or valid CSS beyond this profile, which a browser uses, and the engine cannot tell
which. Its one answer to both is a refusal, "not valid or not supported in this profile": a value that does not match
its grammar of a known property (`vertical-align: 5px`, `color: 12px`), also after `var()` substitution; a selector or
media query that does not parse; an unknown at-rule; a media feature it does not evaluate; an `@supports` condition it
cannot answer as Chromium. Valid CSS the engine does not implement is refused as before. A refused declaration that
would win is skipped, so the next one applies, as in a browser that does not know the value. So the engine refuses
what it cannot read and never takes, without a diagnostic, a value Chromium drops.

The reference is Chromium 159 (`tools/reference/chromium/computed-style`): its verdicts on the web-platform-tests
cases and `getComputedStyle` of every registered property on every element of a corpus, measured unrendered. The
comparison writes the engine's computed values as getComputedStyle does (`UnitTests.Extensions.Documents.Html/
ComputedText.cs`): lengths in px, currentcolor as the element's colour, min-width and min-height auto as 0px, and a
number line-height as number × font size, the used value Chromium returns even unrendered. The engine keeps
Chromium's own values where Chromium computes one (`-webkit-center`, `-webkit-left`, `-webkit-right`), so no value is
mapped in the comparison.

Known limits of stage 3 (also in the profile document):

- Refused, not implemented: quirks-mode documents; form controls that are drawn; the elements Chromium gives rules or
  a layout of their own (details, summary, marquee, option, optgroup, audio, canvas, embed, iframe, object, svg, math)
  and every element whose user-agent style is outside the profile (sub, sup, del, s, ruby, dialog …), also when it is
  not drawn (only a form control that is not drawn is accepted); `noscript` (the parser runs with scripting off,
  Chromium with it on, so their trees differ); a script a browser would run and every event handler attribute, since
  the engine runs no scripts (a data block is accepted).
- Template contents are not styled; `<template shadowrootmode>` stays a template (no declarative shadow DOM).
- Encodings: the HTML parser decodes UTF-8 and UTF-16. It refuses a `meta` element or an XML declaration that names
  another encoding, and bytes that are not UTF-8 with nothing declaring an encoding, which a browser would decode by a
  detected legacy encoding. Valid UTF-8 with no declaration is read as UTF-8, as Chromium 159 reads a local file; over
  HTTP Chromium would detect windows-1252 for it (E-1). Of two `charset` attributes on one `meta`, the last counts in
  the first 1024 bytes, as in Chromium; past them tree construction sees only the first (the tokenizer drops a
  repeated attribute). The CSS reader decodes UTF-8 and UTF-16; a style sheet in another encoding is read only when
  its bytes are ASCII, else refused.
- The parser follows Chromium 159 where it differs from the HTML Standard and the difference is cheap to take (a NUL
  in the data state, after `<` and in CDATA; U+FFFD and frameset-ok; `search` not special; formatting elements with
  `is`; a NUL in a processing instruction; the selectedcontent clone of script, object, iframe and embed; a UTF-16
  tail). It keeps the standard, a listed limit, where the input is rare and the tree is not styled: head elements
  inside `template` (HD-6); an HTML element named like a camel-case SVG element closed from inside SVG (HD-7);
  fragment parsing in a `template`, `textarea`, `title` or `select` context (HD-8); an `<option>` opened inside an open
  `<selectedcontent>` (HD-10, behind the refused `select`); white space after a row with an open formatting element
  in a template (HD-11). Each is a case of `tools/reference/chromium/html-tree/cases.tsv`, pinned as still
  different.
- No pseudo-elements, no `:has()`, `:lang()`, `:dir()`; selectors Chromium 159 rejects are invalid here too (the `s`
  attribute modifier, `:heading`).
- A computed `calc()` that keeps a length and a percentage is refused (the document model's length holds one of
  them); typed arithmetic and the math functions other than calc, min, max and clamp are refused; ex and ch need the
  caller's font metrics.
- A value of a feature outside v0 is refused without its grammar being checked, so input Chromium drops as invalid
  can be refused (never accepted), as can any value the grammar does not read (ruling D-b above).
- Declarations in a subtree that is not drawn (inside display: none) are still cascaded, so their refusals count.
- Values the engine refuses because Chromium computes them by a rule it does not keep: a font size from a relative
  value below 6px (Chromium's minimum logical font size) and above 10 000px; a length past 2^24 px; orphans and
  widows past 32 767; a hue past 2^24 degrees; a `none` colour component; `overflow` visible beside a scrolling axis
  (it computes to auto); `zoom` other than 1, a named animation, `resize`, `scrollbar-gutter` and `will-change` that
  makes a layer (each changes Chromium's printed rendering, measured).
- `-webkit-left`, `-webkit-right`, `-webkit-center` (from align attributes and the caption) must align descendant
  blocks as well as text; `border-style: inset` and `outset` (hr, table border) are drawn in two shades by Chromium;
  `overflow: hidden | clip` (hr, img) clips; `aspect-ratio: auto` with a ratio (the width and height attributes of
  img) gives a ratio to an element that is not replaced; `unicode-bidi: isolate` is accepted as it changes nothing in
  left-to-right text. Stage 4 implements these or refuses them.
- Until stage 4 the code front-end keeps its own cascade (`Fluent/Cascade.cs`: own style, shared style, table cell
  defaults, inherited) with its own initial values and its own list of what inherits, beside the property registry
  the HTML front-end reads. The facts both hold, the initial font size (12pt) and the heading sizes (2, 1.5, 1.17, 1,
  0.83 and 0.67 em), are tied by `UnitTests.Extensions.Documents.Architecture/FrontEndFactsTests.cs`; the rest is not
  tied until stage 4 reads the registry for both.

## Known limits (all stages)

Each is documented next to its code as well.

- **Layout:** negative margins (refused as unsupported); `vertical-align: baseline`
  in cells; column gaps and column styles; the separated border model; diagonal joins of differently coloured
  borders; a keep rule moves a break into an earlier table only between its rows, not to a cut inside a row (a cut
  depends on where the page ends, so a table offers no early break there); padding that does not fit at the top of
  an empty page overflows; a paragraph with a page counter that breaks across pages is re-broken with each page's
  number; a document that shows its page count is laid out at least twice (page-count fixpoint); one without is
  laid out once.
- **Against Chromium:** at most font sizes Chromium's text is narrower than the font's advances, by a share that
  depends on the size (0.039 % at 10 pt, 0.071 % at 11 pt, more at small sizes; none at 9, 10.5 and 12 pt), so a line
  that fits in Chromium by less than that share of its text width can break one word earlier in the engine. The
  engine keeps the font's exact advances; the reference cases that pin lengths use sizes Chromium keeps exact
  (`tools/reference/chromium/README.md`, "Where Chromium is not exact"). With `line-height: normal` Chromium's line is
  the font's ascent, descent and line gap each rounded to whole pixels (Lato at 12 pt: 19 px, 14.25 pt, where the
  font's values give 14.4 pt), and it floors the half-leading above a line to a whole pixel; with the default
  `FontMetricRounding.Exact` the engine keeps the font's exact values, so a line of `normal` height and a baseline's
  place in its line differ by less than a pixel, and with `WholePixels` it rounds as Chromium does (every line of the
  Lato cases `f-lato-*` and `f-mixed-lato` within 1/64 pixel; `FontMetricRoundingTests`). Chromium keeps every length
  of its layout in its unit of 1/64 pixel: a line height given as a length or a percentage is rounded to it, a number
  times the font size is floored to it (Lato 11 pt × 1.3 = 19.0625 px, not 19.067). The engine takes a line height as
  it is given, under either policy, so pages can differ: three lines of 20 pt on a 60 pt page area are 2 + 1 lines in
  Chromium and 3 on one page in the engine, and US Letter with 12 pt text and a line height of 14.4 pt or 120 % has 49
  lines a page in Chromium and 50 in the engine (cases `f-length-three`, `f-length-nine`, `f-letter-length`,
  `f-letter-percent`, `f-length-15004`, `f-number-1501`; `ChromiumLineHeightTests`, which pins both page counts). A
  line height given in Chromium's unit gives Chromium's pages: in all eight measured cases the same pages and words, and
  every baseline Chromium's once rounded to a whole pixel (`ChromiumLineHeightTests`,
  `A_line_height_given_in_Chromium_s_unit_gives_Chromium_s_pages_and_baselines`); a front-end that follows Chromium
  hands the engine its line heights in that unit. The difference that remains: Chromium paints a text baseline at the
  nearest whole pixel while the line keeps its place, so a baseline's ink can lie up to half a pixel (0.375 pt) from
  the engine's; it never moves a line. Chromium also paints grid lines at whole pixels, up to 0.5 pt from where layout puts them (`a2-rowspan-blocks`, `RowPartTests`).
  For `normal` Chromium also takes a font's OS/2 typographic metrics when the font sets USE_TYPO_METRICS, and the
  'hhea' metrics otherwise; the engine always takes 'hhea' (the same values for Lato, not for Theano Didot). At 0.37
  and 0.38 pt (about half a pixel) Chromium's print shows no text at all and gives the lines no height (it floors the
  font size to 1/100 pixel first, so at 0.38 pt, 0.5067 px, Lato's ascent is 0.49 px and rounds to 0); the engine
  draws the text, and at 0.38 pt with `WholePixels` gives each line a pixel (cases `f-tiny`, `f-tiny-038`;
  `FontMetricRoundingTests`).
- **Deliberate deviations from CSS:** header and footer areas are taken from the page area, not from the margins
  (as in RDL and Typst); an image with both width and height fits inside that box keeping its ratio (CSS would
  stretch); a row span's missing height goes to its last row; a page that cannot hold a table's header plus some body
  continues without the header.
- **Empty row parts are a policy, not a deviation** (`LayoutProfile.EmptyRowParts`). Chromium cuts a row wherever its
  cells can break, so a page may end with a part of a row that shows nothing (a row of empty padded blocks after other
  rows) or start with one (the rest of a row whose text ended on the page before; case `a3-mirror`). With
  `EmptyRowParts.Allow` the engine cuts as Chromium does, also in one more respect: a row whose cells all end on the
  page, with only the half of the grid line below it past the page end, is not cut but moves to the next page whole
  (cases `a2-cells-end`, `a2-cells-end-header`, `a2-cells-end-text`: the one step of 42, one pixel apart with and
  without a repeated header row, where this decides; under `Avoid` the cells keep their half line on the page and the
  row is cut before its last block). The same pages and words (cases `a2-padded-blocks`, `a2-line-breaks`, `a3-mirror` and
  their whole-pixel forms `a2-padded-blocks-px`, `a3-mirror-px`, with `FontMetricRounding.WholePixels`; `RowPartTests`,
  Fluent), and where every length is whole pixels the grid lines in the same places within 0.1 pt. Chromium paints grid
  lines at whole pixels, the engine where layout puts them; where lengths are not whole pixels they differ by up to
  0.5 pt (`a2-rowspan-blocks`, a cell over two rows beside 10 pt blocks; 0.25 pt in `a2-padded-blocks` and
  `a3-mirror`). Whether the engine paints grid lines at whole pixels is left to the HTML front-end's stage. With `Avoid`, the code front-end's default, a
  cut where a part shows nothing is a last resort: the row moves to the next page whole, and is cut there only where it
  cannot move (at the top of a page), so no table is refused for it (`Tracks/TableBreaks.cs`, `CutShows`); where a row
  shows nothing over more than a page this needs a page more than Chromium. Reason for that default: an empty strip of
  a row reads as a layout fault in a report, and avoiding it costs nothing where the row can move.
- **A row that starts its page area, under `EmptyRowParts.Allow`, lets its grid line below pass the page end**, as
  Chromium prints it (`TablePage.PassesOnlyByItsLineBelow`; `RowEndTests`, cases `a2-row-end*`). A unit that starts its
  page area (a row that continues from the page before, or the first on the page, with no header row above it), whose
  cells' content fits and whose line below does not, is placed whole; so is the first row of such a unit, and the page
  then breaks before the next row (`TableBreaks.InsideUnit`). Without this, 95 of 2,100 small tables were refused under
  `Allow` that `Avoid` lays out; now none is, and the 95 have Chromium's words on Chromium's pages. The whole line
  passes the page area, both halves, as in Chromium: 192 to 192.75 pt on a page area that ends at 192 (with a 6 pt line,
  192 to 198). Only grid lines and cell backgrounds pass, by at most the width of the line below the row; never a glyph
  or an image. The table's fragment is as tall as the page area; the line is drawn past it, so what follows the table
  starts on the next page. The limits, against Chromium:
  (i) Chromium draws the overflowing line again at the top of the next page; when the table ends the document, that is
  a page that holds only the line (`a2-row-end`: 3 pages in Chromium, 2 in the engine).
  (ii) Content after the table starts higher than in Chromium, by the width of that line: at the top of the next page
  in the engine, below the line drawn again in Chromium (a paragraph's baseline at 24.00 pt against 24.75 with a 0.75 pt
  line, against 30.00 with a 6 pt line; `a2-row-end-after`, `a2-row-end-6pt-after`). The engine reserves no offset
  without the line: a gap with no line would be a third behaviour.
  (iii) At the break between the first row and the next, the engine draws half of the line on each page (192 to 192.375
  and 12 to 12.375), where Chromium draws the whole line on both; content after it is 0.375 pt higher (1.125 pt below
  the top of the page against 1.5; `a2-row-end-span`, `a2-row-end-span-after`).
  A cell background ends at the middle of the line below (192.375), where Chromium ends it at the line's outer edge
  (192.75; `a2-row-end-background`). That is the table model's own rule, not new here; the 0.375 pt between lies under
  the line, which hides it, as the engine has only solid, opaque lines (no dashed or translucent ones). Limits (i) to
  (iii) and the background go away when borders are sliced at page breaks, noted for stage 4.
- **Monolithic content taller than the page area is a policy** (`LayoutProfile.MonolithicOverflow`): refused by
  default, or sliced as Chromium slices it (`Layout/MonolithicSlices.cs`): the same pages, the same image rectangles
  and the content after the last slice in the same place where the piece stands alone on its pages, in a one-column
  table and in a table inside a cell (cases `a4-image-first`, `a4-image-after-text`, `a4-line-after-text`,
  `a4-image-single-cell`, `a4-image-nested-table`; `MonolithicSliceTests`, `SliceInTableTests`); the differences in
  other table shapes are listed below. A piece is cut into at most 100 slices, one per page (`MonolithicSlices.MaxSlices`): 100 slices show the longest image the front-end accepts, 14 400 pt, on a page area of 144 pt, and a piece that needs more is `Documents.Layout.TooManySlices` (`MonolithicSliceLimitTests`). A document is cut into at most 1 000 slices, all pieces together (`MonolithicSlices.MaxSlicesPerDocument`): the limit per piece bounds the pages per piece, not the pages per input byte, since one paragraph of many tall lines takes up to 100 pages per line. The page that would bring the count past 1 000 is `Documents.Layout.TooManySlices` too, and its message names the limit for one document. Each slice is clipped to
  its part of the page area across the whole page width (Chromium clips only in the block direction). In tagged PDF
  every slice of an image is content of its figure, as the pages of a paragraph are content of the paragraph: each page
  shows another part of the image, the alternative text is on the figure and is read once. The later slices of a line
  of text are artifacts (`ArtifactKind.SliceContinuation`, `/Artifact /Type /Pagination`), a stated choice: ISO 32000-1
  §14.8.2.2.1 says "The real content of a document comprises objects representing material originally introduced by
  the document's author. Artifacts are graphics objects that are not part of the author's original content but rather
  are generated by the conforming writer in the course of pagination, layout, or other strictly mechanical processes."
  The lower part of a glyph is the author's content, so by the first sentence a later slice is real content; but the
  first slice already delivers the line's text, and the later pages draw the same glyphs again, so as content the text
  would be read twice. The engine marks them as artifacts so that the text is read once (PDF/UA-1 checkpoint 01-002,
  `docs/pdf-profiles-and-accessibility.md`). veraPDF passes PDF/A-3a and PDF/UA-1 on such a file (sample C, with a
  sliced image, a sliced line and a sliced image in a table under a header row). A sliced figure is on several pages,
  so it has no `/BBox` (ISO 32000-1 Table 344 asks for one only for a figure "appearing in
  its entirety on a single page"). In a table, the row after a sliced piece starts below its last slice, in every
  shape, so no text is drawn over an image. **Deliberate deviation from Chromium:** Chromium does the same in some shapes
  (a one-column table, `a4-image-single-cell`) and draws the next row at the top of the page, over the last slice, in
  others (two columns, `a4-image-in-cell`; a table inside a cell, `a4-image-nested-table`). Where the content's cell
  does not start the page only because of the grid lines above it, the page is cut at the deepest place, inside every
  table around it, as Chromium cuts when no breakpoint is valid, so the slice starts at the top of the next page
  (`ConstraintSpace.IsBelowStartInsets`, under `Slice` only). **Deliberate deviation from Chromium:** beside a sliced
  piece, the other cells of its row go on page by page beside the slices (cases `a4-image-beside-lines`,
  `a4-line-beside-lines`); Chromium holds their content back while the slices run and goes on with it below the end
  of the piece. In a cell that spans rows the pages follow from the rules for rows: the page breaks at the latest
  breakpoint of the span (inside its last row), the span's missing height goes to its last row, and the next row
  comes below the last slice (case `a4-image-rowspan`; Chromium breaks between the spanned rows and draws the next row
  over the slice).
  Under a repeated header row the content is sliced right below the header, on a page after one that showed no body row
  other than slices of a piece that was itself sliced; after a page that did show one, the page holds only the header
  and the row moves on once. That page with only the header is Chromium's: the first slice starts on Chromium's page and
  at Chromium's place in all eleven measured cases (`a4-header-*`, `a4-intro-header-image-row`; `SliceUnderHeaderTests`).
  **Deliberate deviation from Chromium:** on the later pages the engine repeats the header above every slice and makes
  each slice as tall as the room below it, so the content is shown whole and once; Chromium moves the content up a whole
  page area on each later page and either leaves the header out (`a4-header-image-row`) or draws the header over 16.5 pt
  of the content that are never shown (`a4-header-row-image-row`).
  Only images and line boxes are sliced; padding or borders taller than a page are refused under either value.
- **Text:** Unicode normalization before shaping follows HarfBuzz's default (compose a base and a mark the font has
  precomposed, decompose what it lacks), except that enclosing marks (Me) do not count as marks and HarfBuzz's fallbacks
  for a missing space or non-breaking hyphen are not made (`Typesetting/Unicode/Normalization.cs`); contextual and other OpenType lookup types are skipped, and so is mark-to-mark positioning
  (GPOS type 6); default language system only; complex scripts, bidi and hyphenation are not implemented. Only
  `white-space: normal` exists: a tab collapses to a space like every other white space, so there are no tab stops.
  A forced line break inside a paragraph is `LineBreak()` (U+2028; a line feed or carriage return in the text
  collapses to a space). U+000B, U+000C, U+0085 and U+2029 in the text are mandatory breaks too (UAX #14 classes BK
  and NL) and are not collapsible, so they also end the line: CSS Text 3 §5.5 says any character of classes BK and NL
  "must be treated as forced line breaks". (Its §4 asks for control characters other than tab, line feed and carriage
  return to be shown as visible glyphs; for U+000B, U+000C and U+0085 the two rules conflict, and the engine follows
  §5.5.) A
  soft hyphen (U+00AD) shows a hyphen only where the line breaks. Default-ignorable characters, such as U+2060 WORD
  JOINER and the zero-width space, take no space and need no glyph. A character the font has no glyph for is the
  error `GlyphMissing`; there is no fallback font and no substitute character: U+2011 NON-BREAKING HYPHEN, which Lato
  lacks, is refused where HarfBuzz would draw the ordinary hyphen.
- **Preview images:** see "The raster back-end" above (no hinting, seams between items that meet inside a pixel,
  images not fitted to pixels).
- **Fonts and images:** TrueType outlines only (no CFF); fonts are always explicit input; interlaced and 16-bit PNG,
  CMYK and arithmetic-coded JPEG are refused as unsupported. An image may have at most 2^26 pixels (about 67
  million, `Images/ImageLimits.cs`; `Documents.Image.TooLarge`), so a small file cannot claim a huge decoded size.
- **Input size:** elements nest at most 64 deep (`Fluent/InputLimits.cs`; `Documents.Input.Invalid.TooDeep`), which
  keeps every recursive step of the engine far from the stack limit; no length is longer than 14 400 pt
  (`InputLimits.MaxLength`). Every other limit is in the table under "Bounded work".
- **Table breaks:** at a break between two rows, the grid line there is resolved once, from both rows, and drawn as
  the table's `BoxStyle.DecorationBreak` says: `Slice` splits it in two halves, the upper half ending the page and the
  lower half starting the next (as Chromium 159 draws a line of several pixels, cases `mf4-line-*`; a one-pixel line
  Chromium cannot split: it draws it whole on both pages, and the next page's first row starts 0.375 pt lower than in
  the engine, case `a2-split-line-1px`; a difference, left to the HTML front-end's stage); `Clone`, the code front-end's default for tables, ends the
  page with the whole line and starts the next page with it again (the engine's own rule for collapsed borders,
  modelled on `clone` and on word processors). A row fits when it fits with what of the line its page draws. At a
  break inside a row, `Slice` draws no line at the cut (a box cut by a break gets no border there); `Clone` closes the
  table on both pages: the page ends with the whole line below the cut row (the table's bottom border for the last
  row), and the next page starts with the whole line above it; the cut content keeps clear of both. With `Clone` the
  table is closed across its full width on both pages, at every break: a cell that spans rows and continues over the
  break is closed there by its own borders: where the line crosses it, its bottom and top border compete as two
  cells' do, and the line is the same on both pages; where the line is its own bottom or top edge (a cut inside its
  last or first row), the line is resolved as anywhere else, with what lies beyond it. A row is cut inside itself only
  when something of it is left for the next page, and both parts show something: on this page some cell draws
  something (a visible glyph, an image, a rule, a filled or bordered box) below the grid line above the row, and on the
  next page the rest of some cell does (`Tracks/TableBreaks.cs`, `LeavesSomething` and `CutShows`, decided from the
  placed result); otherwise it moves to the next page whole, and only where it cannot move is the cut taken as a last
  resort (see the deviation above). So no page ends or starts with an empty strip of a row where the row could move:
  not beside a cell that spans rows and placed its text higher up, nor beside cells of invisible characters or empty
  padded blocks, nor under a thick line that hides the text reaching into the row. With `Clone` such a cut needs one line width more room than with `Slice`, so on a very small
  page area a table may not fit with `Clone` that fits with `Slice`. A row span does not keep its rows together: a
  break between two rows that a spanning cell crosses is an ordinary break between rows, with the appeal it would
  have without the span, and the cell continues on the next page (`TableBreaks.BetweenRowsAppeal`). The author's keep
  rules still apply: a row group kept together, a spanning cell kept together, `KeepWithNext`. So the rows under a
  label fill their pages as rows without one do. A cell kept together is a keep rule at a break inside its row too:
  the row is not cut there while a better break exists (`TableBreaks.InsideAppeal`).
- **PDF:** classic cross-reference table, no object streams; flat page tree; name and number trees as one node; one
  text per glyph in a ToUnicode map (the text the glyph shows most often), every other text the glyph shows written as
  replacement text (`/ActualText`) where it is drawn.
- **Tagging and PDF/UA:** header cells come only from header rows (Scope Column; no row headers, no Headers/IDs for
  nested headers); a block and a row of blocks are Div, so a row of blocks nests three Divs per column; no per-element
  language, no Span, Link, List, Caption, Note, Formula or TOC structure elements, no bookmarks; ActualText only on the
  marked-content spans of glyphs whose text differs from their ToUnicode entry (see PDF above); empty structure elements are
  written for empty cells; the XMP packet has no padding; everything in page headers and footers is an artifact.
  Figures and tables wholly on one page carry their `/BBox` (ISO 32000-1 Table 344); a table that continues on
  another page has none, and no other layout attributes are written.
- **Speed:** measured, not tuned. On a Windows workstation (16 logical processors, 64 GB of memory, .NET 10), Release, fonts parsed beforehand, PDF/A-3a +
  PDF/UA-1: the normal timesheet takes about 1.0–1.4 s cold and 110–150 ms warm, the long one 2.5–2.9 s cold and about
  410 ms warm. The Typst version of the same report (the proof of concept the sample follows) took 19 ms and 46 ms
  warm on the same machine.
- **Long tables:** a table's grid and columns are prepared once per layout pass and handed from page to page in the
  table's break token; a page looks up the cells of its own rows through per-row indexes. Layout time now grows with
  the number of rows. Measured on the same workstation (Release, A4, 2 cm margins, a three-column table with a header row, 0.5 pt
  grid lines, median of three runs after a warm-up, layout only):

  | Rows | Pages | Before | After |
  | ---: | ---: | ---: | ---: |
  | 250 | 9 | 605 ms | 386 ms |
  | 500 | 17 | 1993 ms | 375 ms |
  | 1000 | 34 | 7734 ms | 529 ms |
  | 2000 | 67 | 31005 ms | 1088 ms |

  What still grows: the work per page is linear in the rows on that page, but every row is measured once more for
  each layout pass, so a document whose page count is used in its text (two passes or more) takes that many times as
  long. `TableScalingTests` fails when four times the rows cost five times the layout calls (it counts work, not
  time). Because the columns are computed once, on the page where the table starts, a page number in an auto column
  keeps the width measured there: "p10" on page 10 overflows a column sized for "p1" (`TableLimitTests`). Give such a
  column a fixed width.

## How each reference check is run

| Claim | Check | How to run |
| --- | --- | --- |
| layering, no native code, test packages stay out | `UnitTests.Extensions.Documents.Architecture` (compiled assemblies); `tools/verify-samples.ps1` step 2 (publish folder, PE headers) | `dotnet test`; the script |
| line breaking | the official `LineBreakTest.txt`, Unicode 18.0.0, all 19,346 cases | `UnitTests.Extensions.Documents.Typesetting` |
| shaping | HarfBuzz 15.0.0 as WebAssembly inside the test process, glyph for glyph; broken paragraphs line by line (every line against HarfBuzz's shaping of that line alone) | `UnitTests.Extensions.Documents.Typesetting/Reference/` |
| preview images | every sample page against MuPDF 1.25.1 and poppler 25.03.0 renders of the engine's PDF, at 96 and 300 dpi, by the rule in `RasterComparison`; golden PNG files of the samples | `UnitTests.Extensions.Documents.Samples/RasterReferenceTests.cs`, `PreviewTests.cs`; `tools/reference/raster/README.md` |
| glyph outlines | fontTools 4.57.0 in the reference tools image, every glyph of the test fonts and hand-made composites, point for point and exactly | `UnitTests.Extensions.Documents.Fonts/GlyphShapeReferenceTests.cs` |
| JPEG decoding | the ITU-T T.83 compliance streams A1, C1, C2, G1, K1 (downloaded when the tests run, SHA-256 checked on every use, trait `Category=Network`: a failed download fails on CI and skips elsewhere): coefficients equal EREF, re-transformed samples equal DREF, exactly; libjpeg-turbo 2.1.5 `djpeg -dct int` on cjpeg-made files, within a stated difference per case | `UnitTests.Extensions.Documents.Images/JpegConformanceTests.cs`, `JpegDecoderReferenceTests.cs` |
| PNG encoding | the engine's decoder and Pillow read every pixel back; bytes pinned | `UnitTests.Extensions.Documents.Images/PngEncoderTests.cs` |
| table layout | numbers measured in Chromium 159 in the pinned Playwright container with font hinting off, and pinned; compared within n layout units (1/64 px), n counted from the structure of each case; the cases, the measuring script and the rule are in `tools/reference/chromium/` | `UnitTests.Extensions.Documents.Layout/ChromiumReferenceTests.cs` |
| page starts and row cuts across shapes | a generated sweep: 8 kinds of leading space × 12 kinds of first piece × 51 start offsets (4 896 documents): no refusal, every word once, nothing past the page area, no part of a row that shows nothing where it can be avoided | `UnitTests.Extensions.Documents.Fluent/PageStartSweepTests.cs` |
| page starts and breaks | Chromium's print of the same cases read back from its PDF, page by page (`tools/reference/chromium`, "Print positions": `positions.py` → `measured/<case>.print.txt`), compared with the engine's page model in the same form (`TestSupport/PrintedPages.cs`): the words of each page exactly, positions within n layout units | `UnitTests.Extensions.Documents.Fluent/PageStartTests.cs`, `ChromiumReferenceTests.cs` |
| text in the PDF | PdfPig reads every glyph back at the position the page model says | `TestSupport/PdfReadBack.cs` |
| structure tree | PdfPig reads the structure tree and marked content back (roles, order, attributes, text per element; every glyph marked) | `TestSupport/PdfStructureReadBack.cs`, `UnitTests.Extensions.Documents.Samples/TaggedPdfTests.cs`, `UnitTests.Extensions.Documents.Render.Pdf/TaggingTests.cs` |
| PDF/A-3b, PDF/A-3a, PDF/UA-1 | veraPDF 1.30.4 in Docker (pinned by digest) on every sample; a plain file must fail as negative control | `UnitTests.Extensions.Documents.Samples/ReferenceValidationTests.cs` (trait `Category=Reference`, skipped without Docker); script step 5 |
| attachments | PdfPig in process, and qpdf in Docker, byte for byte | `TaggedPdfTests`, `ReferenceValidationTests`; script step 5 |
| PDF syntax, fonts, images | `qpdf --check`, `pdffonts`, `pdfimages`, fontTools on every extracted subset | script step 4 |
| how it looks | poppler and MuPDF renders, side by side with the Typst version's pages (optional) | script steps 4 and 6 (`out/samples/png`, `out/samples/sbs`) |

`dotnet test --filter Category!=Reference` leaves the Docker checks out. Golden files are updated with
`UPDATE_GOLDEN=1` and reviewed as a diff.

## Background

Three ideas come from other engines, studied through their public documents, not their code. The layout contract —
a pure function of a box, a constraint space (the space the box may use on the current page) and an incoming break
token, returning a fragment and an outgoing break token, plus a second function for the intrinsic widths
(`MinMax`) — follows Chromium's LayoutNG. Choosing among breakpoints by their appeal, and among the best the latest,
follows Chromium's `BreakAppeal` (`third_party/blink/renderer/core/layout/break_appeal.h`, read for its order of
appeals and its last resort, "not a valid class A, B or C breakpoint"); CSS Fragmentation 3 §4.5 leaves that choice to
the user agent. Going back to a better breakpoint inside earlier content by laying it out once more follows Chromium's
`EarlyBreak` (`early_break.h`: "In cases where we run out of space at an unideal location, we may want to go back and
break here instead"), with the same bound: one more layout, and no early break while following one. The page-count
fixpoint is capped at five passes, as Typst caps the same fixpoint.
