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

Choices, each stated at its code: coverage is exact for straight edges, curves are within 1/64 pixel; colours are
blended on the encoded 8-bit values, as PDFium, MuPDF and poppler blend them — blending in linear light would be more
correct in theory and visibly different from every PDF viewer, so it is not done; an image is a grid of uniformly
coloured squares (PDF's `/Interpolate false`), each device pixel showing the mean of the image over the part it covers;
a JPEG's three components are YCbCr unless an Adobe marker says otherwise, as DCTDecode reads them.

**Determinism.** The pixel path uses only correctly rounded double arithmetic and square roots, in a fixed order, and
no platform maths function; the PNG compression is the runtime's zlib-ng. The golden PNG files of the samples, the
pinned pixels of a Graphics scene and the pinned bytes of a PNG are verified on x64 Linux and Windows; arm64 by CI
(the documents leg on `ubuntu-24.04-arm`).

**Compared with viewers.** `tools/reference/raster/README.md`: every sample page against MuPDF and poppler renders of
the engine's PDF, by the rule in `UnitTests.Extensions.Documents.Samples/RasterComparison.cs` (no farther from either
viewer than the viewers are from each other; for pixels over 32 and 64, no farther from the nearer one). The engine is
closest to MuPDF; poppler draws glyph edges heavier and snaps thin rectangles to whole pixels. Images follow the PDF's
definition, not a viewer: the image rule is proven exactly against analytic areas (`Graphics` tests, any scale, offset
and affine map), and the viewers check only decoding and colour, on images at 1:1 on the pixel grid against MuPDF
(poppler resamples every image drawn below 4×, also at 1:1).

**Known limits.** No hinting: small text is lighter than in a viewer that hints (PDFium). Two items that meet at a
boundary inside a pixel each paint their exact coverage, so some of what is beneath shows through: two rectangles of
colour 51,102,153 on white meeting at half a pixel give 102,140,178 at the boundary (a quarter of the white), MuPDF
95,135,175; poppler moves rectangle edges to whole pixels instead (51,102,153, but the boundary moves); the samples have
no such items (`tools/reference/raster/README.md`). An enlarged image shows its sample squares, as the PDF defines it
(ISO 32000-1 §8.9.5.3; PDF/A forbids asking for interpolation, ISO 19005-3 §6.2.8); some viewers smooth it. JPEG: arithmetic coding, 12-bit, lossless and four components are refused, as when a
PDF embeds them. Encoding a PNG costs more than drawing the page (about 50 ms at 96 dpi, 500 ms at 300 dpi for an A4
page); speed is stage 5's work.

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
| blocks the scans of one JPEG visit together | 32 times the blocks of its frame | `Images/Jpeg/JpegDecoder.cs` | `Documents.Image.TooLarge` |
| glyphs of one text | 64 per input glyph, at least 16 384 | `Typesetting/Shaping/GlyphSubstitution.cs` | `Documents.Typesetting.TooManyGlyphs` |
| shaping operations of one text | 1 024 per input glyph, at least 65 536 | `Typesetting/Shaping/ShapingBudget.cs` | `Documents.Typesetting.TooMuchWork` |
| cell span | 1 000 columns, 65 534 rows | `Core/TableSlots.cs` | `Documents.Input.Invalid.Table`, `Documents.Pdf.Write.InvalidPageModel` |
| PDF/UA grid of one table | the first row's width; 4 194 304 slots | `Render.Pdf/Tagging/TableRegularity.cs` | `Documents.Pdf.Conformance.PdfUa1.TableTooLarge` |
| nesting of elements | 64 levels | `Fluent/InputLimits.cs` | `Documents.Input.Invalid.TooDeep` |
| nesting of arrays and dictionaries in one PDF object | 64 levels | `Pdf/PdfSerializer.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| nesting of arrays and structures in one XMP property | 64 levels | `Pdf/Xmp/XmpPacket.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| a length in the front-end | 14 400 pt | `Fluent/InputLimits.cs` | the property's `Documents.Input.Invalid.*` code |
| points of one glyph outline, components included | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| component references one glyph outline visits | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| nesting of composite glyphs | 64 levels | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |

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

Same input, same bytes: dates and file identifier are inputs (the identifier defaults to a hash of the content), no
clock, no machine state, dictionaries keep insertion order, object numbers come from `ObjectPlan`. Flate
compression (PDF streams, PNG files) comes from the .NET runtime's zlib-ng; its output was checked equal on Linux x64
(runtime 10.0.12) and Windows x64 (10.0.15) at all three compression levels (2026-11-18); arm64 by CI. Tests:
`Same_input_gives_byte_identical_output…`, `A_tagged_file_is_deterministic`, `Two_runs_give_byte_identical_PDFs…`.

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
