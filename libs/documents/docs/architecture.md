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
- **Fragment tree** (`Layout/Fragment.cs`). The output of layout per page: boxes cut at page breaks, lines, text,
  images, table rules, with positions relative to the parent. Internal to the layout layer, but serialised for tests.
- **Page model** (`PageModel/DrawItem.cs`, `PageDocument.cs`). What to draw, in absolute page coordinates: glyph runs
  (glyph ids, not text plus a font name), filled rectangles, images, and markers — `StructureStart`/`StructureEnd`
  (node, role, source location, cell span) for content, `ArtifactStart`/`ArtifactEnd` for decoration and page
  furniture. It does not depend on the document model, so other back-ends (raster, SVG) and a future PDF reader can
  share it. Rule: markers nest; artifacts do not nest and contain no structure markers; content outside every marker
  belongs to nothing (a tagged profile refuses it).

All three have a stable text form (`DocumentModelText`, `FragmentText`, `PageModelText`) used by golden tests.

## The layout contract

`Layout/LayoutAlgorithm.cs` states it: a layout algorithm is a pure function (environment, box, constraint space,
incoming break token) → `LayoutResult` (a fragment plus an outgoing break token and a break appeal, or "no room",
or an error). `ConstraintSpace.cs` is the available space and whether this is the top of a page; `BreakToken.cs`
says where to continue; `LayoutResult.cs` and `BreakAppeal.cs` carry the result and how good the break is.
`LayoutEnvironment.cs` holds the fonts, the algorithms by id, and the current page (for page counters). The
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

| What | Where | Rule it implements |
| --- | --- | --- |
| cascade and inheritance, user-agent styles | `Fluent/DocumentTranslator.cs`, `Cascade.cs`, `UserAgentStyles.cs` | CSS Cascade 4 §4, §6.1, §7.2; HTML rendering (heading sizes) |
| input validation | `Fluent/InputChecks.cs` | one function per rule, each cites its source |
| block flow, keep rules, break choice | `Layout/FlowLayout.cs`, `BreakAvoidance.cs`, `ForcedBreaks.cs` | CSS 2.1 §9.4.1, CSS Fragmentation 3 §3–§4 |
| margin collapsing | `Layout/MarginCollapsing.cs` | CSS 2.1 §8.3.1 |
| box slicing at page breaks | `Layout/SlicedBox.cs` | CSS Fragmentation 3 §5.2–§5.4 |
| inline layout, line boxes | `Layout/InlineLayout.cs`, `LineBoxes.cs` | CSS 2.1 §9.4.2, §10.8 |
| tables and rows of blocks | `Layout/Tracks/` (`TracksLayout.cs` lists the steps) | CSS 2.1 §17 (fixed and auto layout, collapsing borders), CSS Grid `fr` §7.2.4 / §11.7.1, CSS Fragmentation for row breaks |
| images | `Layout/ImageLayout.cs` | CSS 2.1 §10.3.2 (with a stated deviation for width + height) |
| page templates, page counters | `Layout/PageAreas.cs`, `Paginator.cs`, `PageContext.cs` | CSS Paged Media 3 §4.2.2, §5, §6.1 |
| what is content and what is an artifact | `Layout/PagePainter.cs` | ISO 32000-1 §14.8.2.2 |
| line breaking | `Typesetting/LineBreaking/UnicodeLineBreaking.cs`, `GreedyLineBreaker.cs` | UAX #14 rev. 57 |
| shaping | `Typesetting/Shaper.cs`, `Shaping/` | OpenType GSUB/GPOS/GDEF, legacy kern |
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
| front-end (`Fluent/InputChecks.cs`, `InputLimits.cs`) | the author's input: lengths, borders, columns, spans, table shape, page breaks only where they can apply, metadata text (XML characters), language tags, media types, unique attachment names, nesting depth | `Documents.Input.*` |
| layout (`Layout/LayoutErrors.cs`) | only what cannot be laid out: content that does not fit, columns that do not fit, a page-count fixpoint that does not settle | `Documents.Layout.*` |
| back-end contract, one rule set for both back-ends (`PageModel/PageModelRules.cs`; each back-end reports a breach under its own code), plus the options (`Render.Pdf/PdfRenderer.cs`) | a page model from any producer: metadata and at least one page, each of positive finite size, nothing missing, finite positions and sizes, glyphs of their font with a text, enum values, spans within the table model's limits, table structure that nests, a table of rows or of row groups with at most one head, which comes first, one role per node, attachments with a name, a media type and a unique name; render options with a profile and a file identifier of 1 to 32 767 bytes | `Documents.Pdf.Write.InvalidPageModel`, `InvalidOptions`, `InvalidFileIdentifier` |
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

Choices, each stated at its code: coverage is exact for straight edges where no two contours overlap or meet inside a
pixel (where two contours wound the same way overlap, a pixel crossed by an edge of each is up to a quarter of a pixel
too dark, measured 63.3 of 255 steps on a real glyph; where contours of opposite direction meet inside a pixel they
cancel, up to the whole pixel; `Graphics/AreaRasterizer.cs`), curves are within 1/64 pixel; colours are
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
images at 1:1 on the pixel grid against MuPDF (poppler resamples every image drawn below 4×, also at 1:1).

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
| every number of a page model (sizes, positions, font sizes, areas) | within ±3.403 × 10^38, the range of a PDF real (ISO 32000-1 Annex C, Table C.1) | `PageModel/PageModelRules.cs` | `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel` |
| pixels of one page drawn as an image | 2^26 | `Render.Raster/PageRasterizer.cs`, `Graphics/Canvas.cs` | `Documents.Raster.TooLarge`; `Documents.Graphics.TooLarge` |
| coordinates the graphics library draws with | ±10^150 after the transform; a clip box within ±2^30 pixels | `Graphics/Canvas.cs` | `Documents.Graphics.InvalidValue` |
| PDF/UA grid of one table | the first row's width; 4 194 304 slots | `Render.Pdf/Tagging/TableRegularity.cs` | `Documents.Pdf.Conformance.PdfUa1.TableTooLarge` |
| nesting of elements | 64 levels | `Fluent/InputLimits.cs` | `Documents.Input.Invalid.TooDeep` |
| nesting of arrays and dictionaries in one PDF object | 64 levels | `Pdf/PdfSerializer.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| nesting of arrays and structures in one XMP property | 64 levels | `Pdf/Xmp/XmpPacket.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| a length in the front-end | 14 400 pt | `Fluent/InputLimits.cs` | the property's `Documents.Input.Invalid.*` code |
| an HTML document | 2^24 UTF-16 code units (bytes are checked against the same number); nesting 512 elements, the depth at which Chromium stops nesting; 2^18 elements and 2^20 nodes made, the elements the parser opens again or clones included; selectedcontent clones 4 nodes and characters per input character, at least 65 536; 10 000 diagnostics kept | `Html.Parsing/HtmlInputLimits.cs`, `Html.Parsing/TreeBuilding/TreeBuilder.Select.cs` | `Documents.Html.TooLarge`, `TooDeep` (the parse fails); `Documents.TooManyDiagnostics` (the last diagnostic) |
| a style sheet | 2^24 UTF-16 code units; as bytes, 3 × 2^24 before decoding (UTF-8 takes at most 3 bytes per code unit), then 2^24 code units; nesting of blocks, functions and rules 64 | `Css/CssInputLimits.cs` | `Documents.Css.TooLarge`, `TooDeep` (the reading fails; a style resolution fails with it) |
| nesting of selectors (pseudo-class arguments and nested style rules together) | 32 levels | `Css/CssInputLimits.cs` | `Documents.Css.TooDeep` (the style resolution fails) |
| work of one cascade | 2^26 units: a matching fact computed, a simple selector tested, 64 characters compared, a sibling counted, a rule or declaration collected, a candidate looked at, a comparison of a sort, a var() reference followed | `Css/CssInputLimits.cs` (`MaxCascadeWork`) | `Documents.Css.TooLarge` (the style resolution fails) |
| entries one cascade remembers | 2^22: matching facts, parent-rule matches, sibling tables, var() substitutions, custom-property tables; past it they are computed again, inside the work limit | `Css/CssInputLimits.cs` (`MaxCascadeMemo`) | none |
| entries of one cascade's index | 2^21: a selector of a style rule or a declaration, counted each time a sheet is taken in | `Css/CssInputLimits.cs` (`MaxCascadeEntries`) | `Documents.Css.TooLarge` (the style resolution fails) |
| style sheets of one cascade | 1 024: the user-agent sheet, style elements, linked and imported sheets; a sheet taken in twice counts twice | `Css/CssInputLimits.cs` (`MaxStyleSheets`) | `Documents.Css.TooLarge` (the style resolution fails) |
| one var() substitution | 2 097 152 characters of text, Chromium 159's limit (measured); the value it makes nests at most 64 levels | `Css/CssInputLimits.cs` (`MaxSubstitutionLength`, `MaxDepth`) | past the length: invalid at computed-value time, as in Chromium (`Documents.Css.Invalid.ComputedValue`); within 8 characters per substitution of it, where the engine's measure cannot tell: refused (`Documents.Css.Unsupported.Value`); deeper: `Documents.Css.TooDeep` (the style resolution fails) |
| all var() substitutions of one cascade | 2^26 characters | `Css/CssInputLimits.cs` (`MaxSubstitutionWork`) | `Documents.Css.TooLarge` (the style resolution fails) |
| diagnostics of one style sheet, one cascade, one resolution | 10 000 kept | `Css/CssInputLimits.cs`, `Html.Parsing/HtmlInputLimits.cs` | `Documents.TooManyDiagnostics`: how many were left out, and how many of those refuse the document |
| points of one glyph outline, components included | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| component references one glyph outline visits | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| nesting of composite glyphs | 64 levels | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |

In stage 3 a limit that is reached ends the work where it is reached: the style resolution fails with the limit's
error, in both modes of `HtmlStyleOptions.Unsupported`, and returns no partly styled tree. Selector matching
(`Css/Selectors/SelectorMatcher.cs`) computes, per step of a selector and element, whether the steps up to it match
and whether some ancestor's or earlier sibling's do, with an explicit stack, each once per cascade while the memo has
room; a subject reached through `+` or `~` must have as many earlier siblings, which rejects long sibling chains at
once. A run of one compound through `+` (`p + p + … + p`) is matched by the number of consecutive siblings that match
it, so it is linear in the elements; a chain of different compounds through `+` costs its steps times the elements,
and is charged that. A nested rule refers to its parent's selector list instead of copying it. An element is matched
only against the rules bucketed under its ID, its classes, its type, and the rules that name none of these
(`Css/Cascade/CascadeIndex.cs`). A custom property is resolved when it is first needed, by an iterative walk; a cycle
is refused. Elements with the same parent table and the same winning custom properties share one table. The tests
count this work instead of timing it: at twice the size of each shape that once grew faster than its input, the work
is at most 2.2 times the work plus a constant, and the places that charge are pinned by value (`CascadeWorkTests`).
No reader recurses on the input deeper than the nesting limits above, and each runs on a 256 KB stack at the deepest
allowed input: parsing, the tree text, the style resolution, the import chain and the nesting that substitution makes
(`DeepInputTests`, `CssBoundedWorkTests`, `BoundedWorkTests`). Memory, measured on a Linux development machine
(.NET 10, Release): parse and style resolution of 2^18 elements, each with a style of its own and three more nodes
(2^20 nodes), take about 0.88 GB resident.

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
top-level `!` other than `!important`, a rule in the wrong place, another engine's prefix); the
cascade reports what it ignores by rule and what it refuses **when it applies** (a refused declaration that would
win, a rule with an unknown property that matches an element), once per declaration (per element for the user-agent
sheet, so the element is named). Refusal is decided by the diagnostic's code alone; `HtmlStyleOptions.Unsupported`
chooses whether refusals fail the document (the default) or are only reported.

The policy for each refusal: input above CSS Syntax 3 that the engine cannot read is either
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
  not drawn (only a form control that is not drawn is accepted, with the engine's values: Chromium gives a hidden
  `textarea` its user-agent border and background, pinned in `FaithfulSubsetTests`); `noscript` (the parser runs with scripting off,
  Chromium with it on, so their trees differ); a script a browser would run and every event handler attribute, since
  the engine runs no scripts (a data block is accepted).
- Template contents are not styled. The parser keeps `<template shadowrootmode>` a template; the style resolution
  refuses it, since Chromium 159 attaches a declarative shadow root there.
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
  fragment parsing in a `template`, `textarea`, `title` or `select` context, or in a foreign context (`math`, `mi`,
  svg `title` and `desc`; HD-8); an `<option>` opened inside an open `<selectedcontent>` (HD-10, behind the refused
  `select`; the clones the engine makes there are its own, neither the standard's nor Chromium's); white space after a
  row with an open formatting element in a template (HD-11). Each is a case of
  `tools/reference/chromium/html-tree/cases.tsv`, pinned as still different; the foreign contexts and the HD-10
  clones are pinned by the engine's own tree (`ChromiumTreeTests`).
- No pseudo-elements, no `:has()`, `:lang()`, `:dir()`; selectors Chromium 159 rejects are invalid here too (the `s`
  attribute modifier, `:heading`).
- A computed `calc()` that keeps a length and a percentage is refused (the document model's length holds one of
  them); typed arithmetic and the math functions other than calc, min, max and clamp are refused; ex and ch need the
  caller's font metrics.
- A value of a feature outside v0 is refused without its grammar being checked, so input Chromium drops as invalid
  can be refused (never accepted), as can any value the grammar does not read (the refusal policy above).
- Declarations in a subtree that is not drawn (inside display: none) are still cascaded, so their refusals count
  (pinned in `FaithfulSubsetTests`).
- Values the engine refuses because Chromium computes them by a rule it does not keep: a font size from a relative
  value below 6px (Chromium's minimum logical font size) and above 10 000px; a length past 2^24 px; orphans and
  widows past 32 767; a hue past 2^24 degrees; a `none` colour component; `overflow` visible beside a scrolling axis
  (it computes to auto); `zoom` other than 1, a named animation (also a value that may name one: a `var()` or
  `env()` in `animation`, a keyword named twice), `resize`, `scrollbar-gutter`, `-webkit-user-modify` other than
  read-only and `will-change` that makes a layer (each changes Chromium's printed rendering, measured); a font size
  above 0 and below 2e-7px (Chromium computes 1e-7px as 0px); a cycle of `var()` references and a `!` inside a
  `var()`; selectors Chromium matches by its own rule (an upper-case `OF`, An+B of 2^30 or more, an escaped `*`).
- HTML Chromium styles by a rule the engine does not keep (each measured): a Content-Security-Policy `meta` in head; a
  color-scheme `meta` other than normal or light; `autofocus`; the `charset` of a link; `align` on html, head and body;
  an img with alt text and a size whose image is not there (Chromium shows the alt text inline, and the size computes
  to auto); `fencedframe` and `geolocation`. Whether an image is there, stage 3 decides by its signature only: the
  bytes start as a PNG or a JPEG file. Whether they decode is checked where the image is read, in stage 4.
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

- **Layout:** negative margins (refused as unsupported); orphans and widows; the `line-height` property and the strut
  (a line is always `line-height: normal`, which is why the timesheet tunes cell padding); `vertical-align: baseline`
  in cells; column gaps and column styles; the separated border model; diagonal joins of differently coloured
  borders; a keep rule never moves a break into an earlier sibling's content; padding that does not fit at the top of
  an empty page overflows; a paragraph with a page counter that breaks across pages is re-broken with each page's
  number; a document that shows its page count is laid out at least twice (page-count fixpoint); one without is
  laid out once.
- **Against Chromium:** at most font sizes Chromium's text is narrower than the font's advances, by a share that
  depends on the size (0.039 % at 10 pt, 0.071 % at 11 pt, more at small sizes; none at 9, 10.5 and 12 pt), so a line
  that fits in Chromium by less than that share of its text width can break one word earlier in the engine. The
  engine keeps the font's exact advances; the reference cases that pin lengths use sizes Chromium keeps exact
  (`tools/reference/chromium/README.md`, "Where Chromium is not exact").
- **Leading space before the first unbreakable piece (NF-1):** leading space (margins, empty boxes) plus a first
  unbreakable piece that together exceed the page area, at the document start or after a forced page break, is
  refused (`Documents.Layout.DoesNotFit`; the message names the element and the room it had). The layout counts a box
  after only its own top margin, or after empty boxes with margins, as being at the start of the page, so it does not
  move it to the next page, where that space would be gone (`LayoutResult.NothingFits`). The same rule has two softer
  faces: a first table row kept together that does not fit there is cut inside instead of moved, and a table's header
  row may stand alone at the foot of that page. A paragraph of text before it makes the box move on as usual. Pinned by
  `LeadingSpaceLimitTests` (Fluent); the rule itself is next milestone's work.
- **A blank part of a table row as tall as the page area:** a part of a row that is as tall as the page area and
  draws nothing cannot be cut, and the document is refused (`Documents.Layout.DoesNotFit`). A row is cut only where
  some cell draws something below the row's top on that page (`TableBreaks.CutsInsideRow`); a page that would hold
  only blank lines, empty padded blocks or no-break spaces of the row offers no cut, and at the top of a page the row
  cannot move on. The row may have text elsewhere: "top", 30 blank lines and "end" in one cell is refused, because
  the page after "top" would hold blank lines only. This is not the leading-space limit above: there is no leading
  space, and the row could be broken; the message does not name the blank stretch as the cause. Pinned by
  `BlankRowPartLimitTests` (Fluent). Cutting anyway when the only other outcome is a refusal is next milestone's
  rule.
- **Three smaller table and page-start cases (review 36, notes):**
  - *The mirror of the strip.* A row may be cut where it draws text on this page while what is left of it for the
    next page draws nothing; that page then starts with an empty part of the row, 8 to 10 pt high. Seen for one kind
    of cell only: a cell with two empty blocks with padding (237 of 2 400 documents in the reviewer's sweep). The rule
    for a cut asks only that something of the row is left, not that it draws.
  - *A thin strip under a thick line.* With an 8 pt line under the first row of each group of rows under a label, a
    page may end with the 8 pt line and the 1 pt line that closes the table right under it, and the second row
    follows whole on the next page (1 page of 107 in the reviewer's probe). The label's text line ends 1 pt below the
    top of the second row, inside the thick line, so something is drawn below that row's top by the letter of the
    rule, although nothing of the row shows.
  - *Two older first-on-page cases.* A block with padding that is first on a page and holds a piece that cannot be
    broken, between "page area minus both paddings" and "page area minus the top padding" high, is refused; lower
    on the page the same block is placed and broken. A paragraph kept together that is first on a page inside a
    padded block is broken, although it would fit the next page. Both were so before fix round 5.
- **Deliberate deviations from CSS:** header and footer areas are taken from the page area, not from the margins
  (as in RDL and Typst); an image with both width and height fits inside that box keeping its ratio (CSS would
  stretch); a row span's missing height goes to its last row; a page that cannot hold a table's header plus some body
  continues without the header.
- **Text:** no Unicode normalization before shaping (HarfBuzz composes a base and a mark into a precomposed glyph,
  this engine does not); contextual and other OpenType lookup types are skipped, and so is mark-to-mark positioning
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
  lower half starting the next (as Chromium 159 draws it); `Clone`, the code front-end's default for tables, ends the
  page with the whole line and starts the next page with it again (the engine's own rule for collapsed borders,
  modelled on `clone` and on word processors). A row fits when it fits with what of the line its page draws. At a
  break inside a row, `Slice` draws no line at the cut (a box cut by a break gets no border there); `Clone` closes the
  table on both pages: the page ends with the whole line below the cut row (the table's bottom border for the last
  row), and the next page starts with the whole line above it; the cut content keeps clear of both. With `Clone` the
  table is closed across its full width on both pages, at every break: a cell that spans rows and continues over the
  break is closed there by its own borders: where the line crosses it, its bottom and top border compete as two
  cells' do, and the line is the same on both pages; where the line is its own bottom or top edge (a cut inside its
  last or first row), the line is resolved as anywhere else, with what lies beyond it. A row is cut inside itself only
  when, on this page and below the top of that row, some cell draws something (a visible glyph, an image, a rule, a
  filled or bordered box), and something of the row is left for the next page; otherwise it moves to the next page
  whole (`Tracks/TableBreaks.cs`, `CutsInsideRow`, decided from the placed result). So no page ends with an empty strip
  of a row: not beside a cell that spans rows and placed its text higher up, nor beside cells of invisible characters
  or empty padded blocks. With `Clone` such a cut needs one line width more room than with `Slice`, so on a very small
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
| shaping | HarfBuzz 15.0.0 as WebAssembly inside the test process, glyph for glyph | `UnitTests.Extensions.Documents.Typesetting/Reference/` |
| preview images | every sample page against MuPDF 1.25.1 and poppler 25.03.0 renders of the engine's PDF, at 96 and 300 dpi, by the rule in `RasterComparison`; golden PNG files of the samples | `UnitTests.Extensions.Documents.Samples/RasterReferenceTests.cs`, `PreviewTests.cs`; `tools/reference/raster/README.md` |
| glyph outlines | fontTools 4.57.0 in the reference tools image, every glyph of the test fonts and hand-made composites, point for point and exactly | `UnitTests.Extensions.Documents.Fonts/GlyphShapeReferenceTests.cs` |
| JPEG decoding | the ITU-T T.83 compliance streams A1, C1, C2, G1, K1 (downloaded when the tests run, SHA-256 checked on every use, trait `Category=Network`: a failed download fails on CI and skips elsewhere): coefficients equal EREF, re-transformed samples equal DREF, exactly; libjpeg-turbo 2.1.5 `djpeg -dct int` on cjpeg-made files, within a stated difference per case | `UnitTests.Extensions.Documents.Images/JpegConformanceTests.cs`, `JpegDecoderReferenceTests.cs` |
| PNG encoding | the engine's decoder and Pillow read every pixel back; bytes pinned | `UnitTests.Extensions.Documents.Images/PngEncoderTests.cs` |
| table layout | numbers measured in Chromium 159 in the pinned Playwright container with font hinting off, and pinned; compared within n layout units (1/64 px), n counted from the structure of each case; the cases, the measuring script and the rule are in `tools/reference/chromium/` | `UnitTests.Extensions.Documents.Layout/ChromiumReferenceTests.cs` |
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
follows Chromium's `BreakAppeal`; CSS Fragmentation 3 §4.5 leaves that choice to the user agent. The page-count
fixpoint is capped at five passes, as Typst caps the same fixpoint.
