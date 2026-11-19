# Architecture of Ave.Extensions.Documents (milestone M1, and milestone M2 as far as it is merged)

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

The four algorithms are public, and a caller can hand one any break token: one it built, one changed with `with`, or one
that another box's layout returned. Each algorithm checks the token it is given, and a token it cannot have returned for
the box — another algorithm's kind, a child, line, row or cell the box does not have, a height shown that is negative,
not finite, or the whole height of the sliced image or line or more — is the error `Documents.Layout.InvalidBreakToken`,
never an exception; a token inside a child or a cell is checked by that child's algorithm, and a table's token does not
carry its grid over to another table (`BreakTokenCheckTests`). The token types keep their public constructors: a check is needed in any case, because a
caller can pass one box's token to another box, so closing the constructors would not close the hole. Pagination never
meets the error: it hands each box the token that box's own layout returned.

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
| a font's line metrics (ascent, descent, line gap for `normal` and the strut): OS/2 typographic when the font sets USE_TYPO_METRICS, otherwise 'hhea' (where 'hhea' has none: typographic, then Windows), as Chromium reads them on Linux; for every front-end, not a policy | `Fonts/FontParser.cs` (`LineMetricsOf`), `FontMetrics.Line` | Skia `SkFontHost_FreeType.cpp`, Fontations `skrifa` `metrics.rs`; checked against Chromium's print (cases `d-typo-*`) |
| orphans and widows: where a paragraph breaks, the best breakpoint inside a complete paragraph; a cut of a table row is as good as the breaks it makes inside its cells | `Layout/InlineLayout.cs` (`BreakWhereFull`, `BestBreakInside`), `Tracks/TableBreaks.cs` (`Cut`) | CSS Fragmentation 3 §3.3, §4.4 rule 3; checked against Chromium's print |
| early breaks: a keep rule that reaches back into an earlier box's content, never into a box that continues from the page before; a box that breaks at its own early break keeps it and reports it as perfect, lowered only by the `break-inside: avoid` of the boxes the break lies inside (a table's row group kept together included, when the break lies between two of its rows), and its parent compares that with its own breakpoints (a tie keeps the break inside): a break against orphans, widows or a keep-with-next stays inside the box, a break inside a box or paragraph kept together loses to a better break before the box | `Layout/EarlyBreak.cs`, `FlowLayout.cs`, `InlineLayout.cs`, `BreakAvoidance.cs` (`Kept`), `Tracks/TablePage.cs`, `LayoutResult.cs` (`KeepsItsBreak`, `KeptAppeal`) | CSS Fragmentation 3 §4.4, §4.5; Chromium `MovePastBreakpoint`, `CalculateBreakAppealInside`, `kNeedsEarlierBreak`; checked against Chromium's print (cases `b-*`, `fr-keep-*`, `kb-*`) |
| bottom padding and border that do not fit below a box's complete content: the box breaks at its best inner breakpoint and keeps it, at the appeal of a kept break (above: a box or paragraph kept together then moves whole where a better break lies before it, cases `kb-n*`); without one it breaks before its bottom padding and border, which start the next page (a last resort, so a box lower on the page moves whole); not in table cells | `Layout/SlicedBox.cs`, `FlowLayout.cs` | CSS Fragmentation 3 §4.1, §5.4; Chromium `FinishFragmentation`; checked against Chromium's print in block flow (cases `fr-pad-*`, `fr-border-*`, `a3-pad-*`) |
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
| shaping at a break and where a line ends: Where a line ends and how wide it is follows Chromium 159 (Blink `ShapingLineBreaker::ShapeLine`, `LineBreaker`): the paragraph is shaped once and a line keeps the paragraph's glyphs; the start of a wrapped line and the end of a line are shaped again on their own from and to the nearest place that is safe to break, when that end is not safe (the shaper sets HarfBuzz's `HB_GLYPH_FLAG_UNSAFE_TO_BREAK` as HarfBuzz does: a pair whose values are all zero marks nothing, a lookup marks the clusters after its first glyph's own). The hyphen shown at a soft hyphen is shaped alone and never kerned with the letter before it. At a line end at a collapsible space, left-aligned text keeps the paragraph's glyphs (the last letter keeps its kerning with the space, and the line is measured with it); right-aligned, centred and justified text shape the end again before the space. Before a forced break and at the paragraph's end the end is shaped again in every alignment, as measured in Chromium 159's print, which is the reference for where lines end and wins over the reading of its source (that reading says start-aligned text keeps the end): `xx AV <br>To AV` fits "xx AV" on one line only from 252 px in all four alignments, and the right-aligned line shows the unkerned 5,037 units; `xx AV To AV ` at 254 px is `xx AV | To | AV` in all four. When a line, shaped as it is, does not fit, the break opportunity before it is taken. So a line can differ from shaping its text alone: a ligature cut by a break is shaped as its half alone, as measured: `ff` + U+200B + `fi` ends `f f` (1,402 units in Chromium's print), not the ff ligature HarfBuzz gives "ff" alone; each piece shaped again equals HarfBuzz for that piece. | `Typesetting/GreedyLineBreaker.cs`, `ShapedGlyph.UnsafeToBreak`, `Shaper.ShapeLinePiece`, `LineShapingBudget`, `Layout/InlineLayout.cs` (`LineEndAtSpace`) | Blink `ShapingLineBreaker::ShapeLine`, `LineBreaker`; HarfBuzz's unsafe-to-break flag (hb-buffer.h); checked against Chromium's print (`ChromiumLineEndTests`) |
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
together, at the author's line where there is one. One problem is one error with its own code; two or more come as one
error with the code `Aggregate` (Ave.Extensions.ErrorPaths), whose parts carry each problem's code.

| Layer | Checks | Error codes |
| --- | --- | --- |
| front-end (`Fluent/InputChecks.cs`, `InputLimits.cs`) | the author's input: lengths, borders, columns, spans, table shape, page breaks only where they can apply, metadata text (XML characters), language tags, media types, unique attachment names, nesting depth, a layout profile with defined policy values | `Documents.Input.*` |
| layout (`Layout/LayoutErrors.cs`) | only what cannot be laid out: content that does not fit, content that would need more than 100 slices (one piece) or 1 000 (one document), columns that do not fit, a page-count fixpoint that does not settle | `Documents.Layout.*` |
| back-end contract, one rule set for both back-ends (`PageModel/PageModelRules.cs`; each back-end reports a breach under its own code), plus the options (`Render.Pdf/PdfRenderer.cs`) | a page model from any producer: metadata and at least one page, each of positive finite size, nothing missing, finite positions and sizes (and edges) within the range of a PDF real, glyphs of their font with a text, enum values, spans within the table model's limits, table structure that nests, a table of rows or of row groups with at most one head, which comes first, one role per node, attachments with a name, a media type and a unique name; markers (structure, artifact, clip) that nest by the one nesting rule, with at most 27 clips one inside the other (`MarkerNesting`); render options with a profile and a file identifier of 1 to 32 767 bytes | `Documents.Pdf.Write.InvalidPageModel`, `InvalidOptions`, `InvalidFileIdentifier`; while the structure tree is built (`Render.Pdf/Tagging/StructureTree.cs`), a node under two parents or a decorative node in the structure: `Documents.Pdf.Write.InvalidStructure` |
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

Choices, each stated at its code: coverage is exact for straight edges, to 10^-9 within about 2^22 pixels of the
origin (see the limits below), in every pixel and for both fill rules, also where contours overlap or cross inside a pixel, a contour crosses itself, or a clip box's side
runs between them (`Graphics/AreaRasterizer.cs`). The integral of the winding number over a pixel gives its coverage
wherever the winding number takes at most two values there, 0 and one other or two of one sign; a pixel where it may take
more is flagged from the pieces of edges in it (`Graphics/CellFlags.cs`: one run of consecutive edges of one polygon,
at most two pieces or monotone, is clean; everything else is flagged), and a flagged pixel gets the exact area of the
arrangement of its pieces (`Graphics/CellArrangement.cs`), keeping the integral's value where the two differ by at most
10^-9. A flagged pixel whose pieces are two sides apart keeps the integral, which is exact there: the sides of a thin
stroke or of the gap between two, the pieces going down one run and those going up one run with a straight line
between them that crosses the pixel from its top to its bottom (or the same turned a quarter), so the winding number
takes two neighbouring values (`CellFlags.IsTwoSided`, the argument at the code). Measured: 41 to 71 % of the flagged
pixels of seven whole fonts at 12 pixels per em, 76 to 93 % on the sample pages at 96 dpi. `OverlapCoverageTests` holds every pixel, flagged or not, to exact rational areas computed another way (vertical
slabs instead of rows) on degenerate and random shapes, with and without a clip box. The fill rule of a pixel the clip
box cuts is taken over the part inside it, so a thin part does not blow up a value (`ClipBoxCoverageTests`); curves are
within 1/64 pixel; colours are
blended on the encoded 8-bit values, as PDFium, MuPDF and poppler blend them — blending in linear light would be more
correct in theory and visibly different from every PDF viewer, so it is not done; an image is a grid of uniformly
coloured squares (PDF's `/Interpolate false`), each device pixel showing the mean of the image over the part it covers;
a JPEG's three components are YCbCr unless an Adobe marker says otherwise, as DCTDecode reads them.

**What exact coverage costs.** Measured on the development host against the code before it, which drew every pixel
from the integral alone: the sample documents take 1.0 to 1.6 times as long at 96 dpi (the timesheets 1.2 to 1.6, the
other samples 1.0 to 1.3) and 1.0 to 1.24 times as long at 300 dpi; every glyph of a font filled on its own at 12
pixels per em takes 1.7 to 2.6 times as long. Small text costs the most; at a higher resolution fewer pixels hold more
than one edge.

**Arguments of the graphics library.** One rule for every public member of `Ave.Extensions.Documents.Graphics`
(`Canvas`, `Path`, `PathBuilder` and the types they take): a value it cannot draw with — not finite, out of range, a size
that does not fit — is answered with a `Result` error with a `GraphicsErrorCodes` code (`Documents.Graphics.InvalidValue`,
`Documents.Graphics.TooLarge`), never with an exception; only a null reference (`ArgumentNullException`) is the caller's
bug. A path cannot be built out of order: `PathBuilder` offers only `MoveTo`, which hands back a `SubpathBuilder` with
the other commands, so a segment before the first `MoveTo` does not compile. Inside
the ranges (table below) the size of a coordinate makes no work: a shape is clipped to the box in floating point before
any number becomes an integer, so a point far outside costs what a point on the box's side costs. The work of a fill
follows the shape inside the box: per row, the edges that cross it and the pixels from the shape's left to its right,
plus the charged arrangement of the pixels where contours may overlap (the work limit). No limit bounds the edges of a
path: a zigzag of 20,000 edges, each over all rows of a 4,096 × 4,096 canvas, is charged 818,051 units (it flags only
the 4,094 pixels of row 0, where it closes along the canvas's top line) and took 2.9 to 3.8 s, work in proportion to the
path, not to the charge. `ArgumentRuleTests` in
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
MuPDF on the nearest (an exact half down the page at 72 and 96 dpi, either way at 120 and 300 dpi) up to an em of 256
pixels, poppler on the one above (it floors) at every size, at 96 and at 300 dpi alike; MuPDF draws a larger glyph at
its exact place, and the engine draws every glyph where the page model puts it. So a line of text can lie up to half a pixel (MuPDF) or up to one
pixel (poppler) from the viewers' place; it never moves a line. Measured on lines of Lato 11 pt with baselines at
fractions 0.1 to 0.95 of a pixel, and on the sliced sample: page 5's line has its baseline at 110.476 px at 96 dpi,
both viewers draw it at 110 (ink centre row 105.33 in both, 105.82 in the engine, with equal ink;
`tools/reference/raster/README.md`). This holds for every sample; the comparison carries it by drawing the engine's
page again with the viewers' rows, for MuPDF only the glyph runs it puts on a row
(`RasterComparison.BaselinesOnPixelRows`).

**Known limits.** No hinting: small text is lighter than in a viewer that hints (PDFium). Two items that meet at a
boundary inside a pixel each paint their exact coverage, so some of what is beneath shows through: two rectangles of
colour 51,102,153 on white meeting at half a pixel give 102,140,178 at the boundary (a quarter of the white), MuPDF
95,135,175; poppler moves rectangle edges to whole pixels instead (51,102,153, but the boundary moves); the samples have
no such items (`tools/reference/raster/README.md`). An enlarged image shows its sample squares, as the PDF defines it
(ISO 32000-1 §8.9.5.3; PDF/A forbids asking for interpolation, ISO 19005-3 §6.2.8); some viewers smooth it. An image
whose grid reaches farther than 2^40 device pixels from the origin (`Canvas.MaxImageReach`; at 96 dpi about
8.25 × 10^11 pt) is refused: `Documents.Graphics.InvalidValue` from the canvas, `Documents.Raster.ImageOutOfReach` from
the raster back-end; the PDF back-end writes such an image as the page model says. The bound is the arithmetic of
placing the image's pixel edges: each is computed from terms as large as the reach, each product and sum rounds by
2^-53 of its size, so an edge lies at most 6 · 2^-53 · reach from its exact place, within the bound 2^-10.4 pixel (for
one edge, under a fifth of an 8-bit step at full contrast; a pixel that several edges cross takes that from each, and
the worst measured, an image squeezed nearly flat on the canvas, is 0.822 of a step from the exact value, every other
kind of transform at most 0.53); the same bound keeps the rounding of a pixel's preimage below 2^-12 of its size.
Measured beyond it, with numbers that are not round: an edge 0.08 pixel off at a reach of 4.9 × 10^15 pixels, 0.67 at
4.9 × 10^16, 5.3 at 4.9 × 10^17; a rotated 61 × 43 image enlarged 10^13 times (reach 8.6 × 10^14) up to 3 steps off.
Within it the tests draw images enlarged so far that a page shows a small part of one pixel, rotated, sheared or not,
and get the exact areas (`Graphics/Canvas.cs`, `ImageResamplingTests`, `PageRasterizerTests`). The reach is counted
from the page's origin, not from the page: (|x| + width) × resolution / 72 along x for an image's rectangle at x, and
the same along y. So an image centred on the origin reaches 1.5 times its width and is refused from two thirds of the
width an image that starts at the origin may have; and an image wholly off the page beyond the reach is refused
although it would show nothing, where a rectangle at the same place is drawn, and the PDF back-end writes both
(`RenderingTests`). A fill whose exact coverage takes more work than a limit is refused the same way, before any of it
is painted: the work of a pixel where contours may overlap grows with the cube of the pieces of edges in it, so
`Canvas.MaxFillWork` (2^27 units for one fill), `Canvas.MaxCanvasWork` (2^28 units for all fills of one canvas, so of
one page) and `Canvas.MaxRowPieces` (2^18 pieces in the flagged pixels of one row) bound it; `Documents.Graphics.TooMuchWork` from the canvas and from the raster
back-end, which reports a glyph's refused fill for its page. A two-sided pixel costs no units. Measured on the
development host: the largest glyph of eight fonts at 12 pixels per em takes 248,985 units (539 times under the fill
limit), the largest sample page 6.83 × 10^5 units at 96 dpi (393 times under the canvas limit); a fill just inside its
limit took 1.23 s, a canvas filled to its limit 2.38 s (about 9 ns a unit); a thousand thin bars through one pixel ask 7.4 × 10^9 units
and are refused in about 50 ms. The PDF back-end writes such a page as the page model says, and a viewer draws it with
its own rasteriser. Far from the origin coverage is less exact, and the fill still succeeds: it is exact to 10^-9
within about 2^22 pixels of the origin (2^23 along x alone), which covers every canvas up to 2^22 pixels on a side;
beyond that the error grows with the distance, measured at most 3.5 × 10^-9 at 2^24 pixels, 1.4 × 10^-8 at 2^26 (6.9 ×
10^-9 along x alone, as wide as a canvas may be) and 1.1 × 10^-7 near `Canvas.MaxPixelCoordinate` (2^30), where an 8-bit
value can be one step off (14 of 12.8 million). The row integral interpolates x where an edge crosses a row line, and
far out a double keeps few bits of a pixel. A known limit, to be repaired
(`CoverageTests.Known_limit_far_from_the_origin_a_pixel_can_be_one_8_bit_step_off`). A font whose licence forbids embedding (OS/2 fsType) is drawn by the raster back-end, since a PNG
holds pixels and no font, and refused by the PDF back-end (`Documents.Pdf.Write.FontEmbeddingNotPermitted`), so
`ToPng` draws a document that `ToPdf` refuses. JPEG: arithmetic coding, 12-bit, lossless and four components are refused, as when a
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
   the same way: every glyph a lookup looks at and every subtable it tries (`Shaping/ShapingBudget.cs`). Breaking a paragraph costs a fixed number of steps per break opportunity and, per line, at most `GreedyLineBreaker.MaxEarlierBreaksTried` + 2 shapings of the line's ends, each at most the line's own text. All pieces of one paragraph share one `LineShapingBudget`: at most 16 characters shaped again per character of the paragraph (at least 4,096), and together the shaping limits of the paragraph itself (max(65,536, 1,024 × n) operations); past either the paragraph is refused with `Documents.Typesetting.TooMuchWork`. Marking glyphs unsafe to break skips what a lookup's walk marked already (a run of n marks on one base costs n steps) and is charged to the shaping budget; the normalizer finds every cluster's end in one pass and does not sort a run of more than 32 marks. Counted in `TextCostTests` (two sizes per input).
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
| an HTML document | 2^24 UTF-16 code units (bytes are checked against the same number); nesting 512 elements, the depth at which Chromium stops nesting; 2^18 elements and 2^20 nodes made, the elements the parser opens again or clones included; selectedcontent clones 4 nodes and characters per input character, at least 65 536; 10 000 diagnostics kept | `Html.Parsing/HtmlInputLimits.cs`, `Html.Parsing/TreeBuilding/TreeBuilder.Select.cs` | `Documents.Html.TooLarge`, `TooDeep` (the parse fails); `Documents.TooManyDiagnostics` (the last diagnostic) |
| a style sheet | 2^24 UTF-16 code units; as bytes, 3 × 2^24 before decoding (UTF-8 takes at most 3 bytes per code unit), then 2^24 code units; nesting of blocks, functions and rules 64 | `Css/CssInputLimits.cs` | `Documents.Css.TooLarge`, `TooDeep` (the reading fails; a style resolution fails with it) |
| nesting of selectors (pseudo-class arguments and nested style rules together) | 32 levels below the top one (a style rule and 32 nested in it) | `Css/CssInputLimits.cs` | `Documents.Css.TooDeep` (the style resolution fails) |
| work of one cascade | 2^26 units: a matching fact computed, a simple selector tested, 64 characters compared, a sibling counted, a rule or declaration collected, a candidate looked at, a comparison of a sort, a var() reference followed | `Css/CssInputLimits.cs` (`MaxCascadeWork`) | `Documents.Css.TooLarge` (the style resolution fails) |
| entries one cascade remembers | 2^22: matching facts, parent-rule matches, sibling tables, var() substitutions, custom-property tables; past it they are computed again, inside the work limit | `Css/CssInputLimits.cs` (`MaxCascadeMemo`) | none |
| entries of one cascade's index | 2^21: a selector of a style rule or a declaration, counted each time a sheet is taken in | `Css/CssInputLimits.cs` (`MaxCascadeEntries`) | `Documents.Css.TooLarge` (the style resolution fails) |
| style sheets of one cascade | 1 024: the user-agent sheet, style elements, linked and imported sheets; a sheet taken in twice counts twice | `Css/CssInputLimits.cs` (`MaxStyleSheets`) | `Documents.Css.TooLarge` (the style resolution fails) |
| one var() substitution | 2 097 152 characters of text, Chromium 159's limit (measured); within 8 characters per var() of it the engine cannot tell what Chromium keeps (refused); the value it makes nests at most 64 levels | `Css/CssInputLimits.cs` (`MaxSubstitutionLength`, `MaxDepth`) | past the length: invalid at computed-value time, as in Chromium (`Documents.Css.Invalid.ComputedValue`); within 8 characters per substitution of it, where the engine's measure cannot tell: refused (`Documents.Css.Unsupported.Value`); deeper: `Documents.Css.TooDeep` (the style resolution fails) |
| all var() substitutions of one cascade | 2^23 characters | `Css/CssInputLimits.cs` (`MaxSubstitutionWork`) | `Documents.Css.TooLarge` (the style resolution fails) |
| a computed length or percentage | 2^24 px (or percent) either way; Chromium clamps near 3.36 × 10^7 px | `Css/CssInputLimits.cs` (`MaxLengthPixels`) | `Documents.Css.Unsupported.Value` (the declaration is refused) |
| diagnostics of one style sheet, one cascade, one resolution | 10 000 kept; each quotes at most 100 characters of one name or value from the input, with `…` where it is cut | `Css/CssInputLimits.cs`, `Html.Parsing/HtmlInputLimits.cs`, `Shared/InputQuote.cs` (compiled into Css, Html.Parsing and Html) | `Documents.TooManyDiagnostics`: how many were left out, and how many of those refuse the document |
| points of one glyph outline, components included | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| component references one glyph outline visits | 65 536 | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| nesting of composite glyphs | 64 levels | `Fonts/Tables/GlyphOutlineDecoder.cs` | `Documents.Font.Parse.InvalidValue` |
| clips open one inside the other on a page, for both back-ends | 27 (in PDF one `q` each, plus the drawing's own: the 28 levels of ISO 32000-1 Annex C and ISO 19005-3 §6.1.13; the raster back-end takes the same) | `PageModel/PageModelRules.cs` | `Documents.Pdf.Write.InvalidPageModel`, `Documents.Raster.InvalidPageModel` |
| slices of one piece of content taller than the page area (`MonolithicOverflow.Slice`) | 100, one per page | `Layout/MonolithicSlices.cs` | `Documents.Layout.TooManySlices` |
| slices of one document, all pieces together (`MonolithicOverflow.Slice`) | 1 000, counted page by page; a document without a piece taller than the page area has none | `Layout/MonolithicSlices.cs`, `Layout/Paginator.cs` | `Documents.Layout.TooManySlices` (the message names the limit for one document) |

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
- `svg` and `math` are refused as elements, and the selector matching rules of SVG and MathML content (their own
  rules for names and attributes) are not built.
- `StyleComputer.Compute`, public for a caller that styles its own tree, throws `InvalidOperationException` when an
  element's parent was not computed first: a wrong order of calls, not input. `HtmlStyleResolver.Resolve` always
  computes in tree order.
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
- The caller's resolver gives bytes, no content type, so what it gives for a style sheet is read as CSS; Chromium
  applies only a resource served as text/css. An empty URL and the document's own address are an empty style sheet
  without asking the resolver, as in Chromium; for any other address a resolver gives null for what is not a style
  sheet (pinned: `FaithfulSubsetTests.What_the_resolver_gives_for_another_address_is_read_as_a_style_sheet`).
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
  color-scheme `meta` other than normal or light; `autofocus`; a `charset` other than utf-8 on a link; `align` on html, head and body;
  an img with alt text and a size whose image is not there (Chromium shows the alt text inline, and the size computes
  to auto); `fencedframe` and `geolocation`. An inline img whose image is not there and that has no alt text gets
  Chromium's values: its width, height and aspect-ratio compute to auto, unless it has both sizes, or an aspect-ratio
  and one of them. Whether an image is there, stage 3 decides by its signature only: the bytes start as a PNG or a
  JPEG file. Whether they decode is checked where the image is read, in stage 4.
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
  depends on where the page ends, so a table offers no early break there); bottom padding and a bottom border taller
  than the page area overflow the page they start (as in Chromium) when the box ends the document, and are refused
  (`Documents.Layout.DoesNotFit`) when content follows them; a table cell whose content does not fit under
  the repeated header and, on a page without the header, does not fit with its last child's bottom margin and the
  cell's bottom padding is refused, where Chromium prints it (case `fr-header-cell-margin`); a table header taller than
  the page is refused (`Documents.Layout.DoesNotFit`; Chromium cuts it like a body row); a table row taller than the page
  only through its cells' padding is refused (Chromium prints it on a fresh page and lets the padding overflow); a table
  row without cells is refused as input (`Documents.Input.Invalid.Table`; Chromium lays it out with no height) — each
  pinned with Chromium's pages in `ListedDifferenceTests`; under `EmptyRowParts.Avoid` and `MonolithicOverflow.Refuse`,
  the defaults, a table row that continues from the page before, whose rest fills the page area with only the half of
  its grid line below past the page end, is refused (`Documents.Layout.DoesNotFit`) when another row follows it (under
  `Allow` the line passes the page end, as Chromium prints it; under `Slice` the rest is cut at the deepest place;
  `RowEndTests`, `A_continued_row_whose_lines_fill_the_next_page_is_refused_under_Avoid`); a paragraph with a page counter that breaks across pages is re-broken with each page's
  number; a document that shows its page count is laid out at least twice (page-count fixpoint); one without is
  laid out once.
- **Work in nested tables:** a box inside k nested tables is laid out up to 2k + 1 times on a page (each table
  measures every table inside it at unlimited height; a cut cell is laid out in the cut and once more for what is left
  of it), so t nested tables that break across a page cost 2 (t + 1)(t + 2) layout calls: quadratic in the depth, not
  exponential (`TableNestingCostTests`). At the nesting limit, measured on .NET 10 x64 on 2026-11-18: 63 nested one-cell
  tables that break across 2 pages take 8,320 calls (24,704 with repeated grid lines, which make it 4 pages), 3 to 5 s,
  1.5 to 1.8 GB allocated in all with about 111 MB of process memory at the peak, and 672 KB of stack; 64 nested blocks
  take 130 calls, a quarter of a second, 10 MB and 304 KB of stack. The cost per page is not constant: the same nesting
  over 20 pages takes 155,776 calls, 74 s and 68 GB allocated (peak about 150 MB), and over 200 pages it does not finish
  in 30 minutes, because a paragraph and the boxes around it are laid out again on every page. A layout result kept
  within the page and carried from page to page is the follow-up, in the next stage 1 pull request.
- **Against Chromium:** at most font sizes Chromium's text is narrower than the font's advances, by a share that
  depends on the size (0.039 % at 10 pt, 0.071 % at 11 pt, more at small sizes; none at 9, 10.5 and 12 pt), so a line
  that fits in Chromium by less than that share of its text width can break one word earlier in the engine. The
  engine keeps the font's exact advances; the reference cases that pin lengths use sizes Chromium keeps exact
  (`tools/reference/chromium/README.md`, "Where Chromium is not exact"). With `line-height: normal` Chromium's line is
  the font's ascent, descent and line gap each rounded to whole pixels (Lato at 12 pt: 19 px, 14.25 pt, where the
  font's values give 14.4 pt), and it floors the half-leading above a line to a whole pixel; with the default
  `FontMetricRounding.Exact` the engine keeps the font's exact values, so a line of `normal` height and a baseline's
  place in its line differ by less than a pixel, and with `WholePixels` it rounds as Chromium does (every line of the
  Lato cases `f-lato-*` and `f-mixed-lato` within 1/64 pixel; `FontMetricRoundingTests`). Under `Exact` and an explicit
  line height a baseline's place in its line differs more: the largest measured difference is 1.16 px (0.87 pt, Lato
  10 pt at `line-height: 1.5`, case `fr-lh-first-baseline`, `LineBoxReferenceTests`); and where a line mixes font sizes
  under a line height given as a length, the boxes' rounded metrics decide the line's height, so the engine's line can
  be up to about half a pixel taller or shorter per line: ten lines of 15 pt with a 20 pt word on two of them fill a
  150 pt page in Chromium and need a second page in the engine (case `fr-lh-mixed-sizes`). Chromium keeps every length
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
  The font's line metrics come from the table Chromium reads on Linux (Fontations): OS/2 typographic metrics when the
  font sets USE_TYPO_METRICS, whatever the table's version, otherwise 'hhea' (the same values for Lato, not for Theano
  Didot); this is the reference — on Windows and macOS Chromium takes the metrics from the platform, and which table
  that is, is not documented. These metrics move every inline box, not only lines of `normal` height. At 0.37
  and 0.38 pt (about half a pixel) Chromium's print shows no text at all and gives the lines no height (it floors the
  font size to 1/100 pixel first, so at 0.38 pt, 0.5067 px, Lato's ascent is 0.49 px and rounds to 0); the engine
  draws the text, and at 0.38 pt with `WholePixels` gives each line a pixel (cases `f-tiny`, `f-tiny-038`;
  `FontMetricRoundingTests`). A line height smaller than its text (12 pt text on 7.5 pt lines, or 0)
  overflows its line; Chromium paints such overflowing glyphs again at the next page's edge and adds a page for the last
  line's overflow, the engine paints them once. HarfBuzz gets up to 5 characters of context from Chromium when a piece is shaped again; the engine shapes the piece alone. None of the lookups the engine applies reads context, so no measured case differs. Chromium's 1/64 px positions: line extents differ by up to 0.056 pt at 75 pt.
  Page breaks, measured in Chromium's print and pinned with both results (`ListedDifferenceTests`, cases `fr-old-*`):
  a forced break after only empty boxes is not taken (Chromium leaves a blank page first); a forced break inside a box
  kept together moves the whole box to a fresh page first (Chromium breaks there at once: a break inside a box that
  holds a forced break is perfect before the box's keep applies, `CalculateBreakAppealInside`); an empty box whose
  margin carries it past the page end stays at the page end, so a keep before it holds (Chromium breaks before it); a
  box kept together does not hide a better breakpoint before it (in Chromium it does, in one shape: `n0 n1 o0 o1 | p0`
  where the engine prints `n0 | n1 o0 o1 p0`); an empty box past the end of the last page adds no blank page; a paragraph that moved to a new page whole counts only
  its orphans before a break on that page, where Chromium counts its widows there too (it differs only where widows are
  more than the page holds: 7 of 1,792 measured documents; `InlineLayout.BreakWhereFull`; cases `fr-moved-para-widows-4`, `fr-moved-para-widows-3-control`). The rest of a cut cell aligned `middle` or `bottom` is aligned
  in the rest of its row (Chromium puts it at the top); a margin dropped at a row cut does not stay in the row's height
  (Chromium keeps the uncut height). Where a cell's lines all fit and only its bottom padding or grid line does not, at
  the top of a page the engine cuts the row as a last resort, where Chromium leaves the first page blank and prints the
  row whole on the next (cases `fr-f4-padding-page-start`, `a2-cells-end-page-top`); a row that starts a later page,
  after a break between rows, and is taller than the page area only through its cells' padding is cut before its last
  line, where Chromium prints all its lines on that page and lets the padding overflow (cases
  `fr-row-later-page-padding-overflows`, `fr-row-later-page-padding-overflows-two-cells`).
  Under a repeated header row, a cell whose box's bottom padding does not fit on a page with the box's content: where
  the header is repeated, the engine leaves the header alone on the first page, where Chromium cuts the row below it
  because its line fits and only its padding does not (`fr-header-pad-box-72`). Where the header is more than a quarter
  of the page area, neither repeats it and both print the same pages (cases `fr-header-pad-box-20`,
  `fr-header-pad-box-36`, `fr-header-pad-grid-36`, `fr-header-pad-top-36`), as they do without the header row
  (`fr-header-pad-none-36`). Padding taller than the page area with content after it is refused (the limit above;
  `fr-header-pad-box-50`).
  A keep-with-next on the last paragraph inside boxes whose bottom padding goes to a page of its own holds across that
  padding: the engine then breaks the next paragraph against its orphans, widows or keep (`a0 | a1 | j0 | j1 j2 z`),
  where Chromium breaks before it and the page with the padding holds nothing else (`a0 | a1 | (blank) | j0 j1 j2 | z`).
  Where the keep-with-next is on the outer box, with `orphans` 1, without the padding or without the keep, both print
  the same pages (cases `fr-kwn-pad-*`).
- **Deliberate deviations from CSS:** header and footer areas are taken from the page area, not from the margins
  (as in RDL and Typst); an image with both width and height fits inside that box keeping its ratio (CSS would
  stretch); a row span's missing height goes to its last row; a page that cannot hold a table's header plus some body
  continues without the header.
- **Header rows are repeated only up to a quarter of the page area**, as Chromium prints them: a header taller than
  that is shown on the table's first page only (`TablePage.RepeatsHeader`; `HeaderRepeatTests`, cases
  `a6-header-quarter-*`: on a page area of 180 pt a header of 45.0 pt is repeated and one of 45.75 pt is not, on 360 pt
  90.0 and 90.75; cases `fr-old-header-quarter-*`: on 150, 180 and 240 pt a header with bottom padding exactly a quarter
  high is repeated and one 0.75 pt higher is not). The limit is the one CSS Tables 3 §6.2 states for repeated headers
  ("up to one quarter for header rows"), and Chromium's: in
  `third_party/blink/renderer/core/layout/table/table_layout_algorithm.cc`, `TableLayoutAlgorithm::GenerateFragment`
  (read in the source of Chromium 150; the measured browser is Chromium 159), a header group is repeated when two
  conditions hold: its block size is at most a quarter of the fragmentainer's block size, and its `break-inside` has an
  applicable `avoid` value. The engine checks the first condition only. Its header rows have no `break-inside` value
  of their own in this respect: they are repeated as the header groups of the measured cases, which carry no
  `break-inside` in their documents and which Chromium repeats. The decision is made once, where the table's first
  fragment is laid out, and the table's break token carries it to its later pages
  (`TracksBreakToken.RepeatsHeader`): a header not repeated on the first page is never repeated later, as in Chromium,
  which does not start repeating on a later fragment. The fragmentainer's block size is the height of the page area
  (`ConstraintSpace.FragmentainerBlockSize`): it does not depend on where the table starts on its first page
  (`HeaderDecisionTests`), and the measurement of a cut table cell with unlimited height carries the value of the page
  it measures for, so a table inside a cell decides there as on the page. Under an outer table's repeated header the
  page area is less by that header, as Chromium reserves its room for the rows below it
  (`ConstraintSpaceBuilder::ReserveSpaceInFragmentainer`): on a page area of 180 pt below an outer header of one line a
  nested header of 39.75 pt is repeated and one of 42.75 pt is not, in both (`a6-nested-under-header-h2400`,
  `a6-nested-under-header-h2700`, `a6-nested-under-header-h2700-n16`; 42.75 pt is less than a quarter of 180 pt and
  more than a quarter of 180 pt less the outer header). A nested header of 45.0 pt in a cell with 12 pt padding is a
  quarter of the page area and is repeated on every page (`a6-nested-header-pad12-h2925`); one of 60 pt is shown on page
  1 only, whatever the last page holds (`a6-nested-header-h4425-n14`, `-n16`, `-n20`, `-n26`: 7, 9, 2 and 8 rows on the
  last page, where 7 fit below the header).
- **Empty row parts are a policy, not a deviation** (`LayoutProfile.EmptyRowParts`). Chromium cuts a row wherever its
  cells can break, so a page may end with a part of a row that shows nothing (a row of empty padded blocks after other
  rows) or start with one (the rest of a row whose text ended on the page before; case `a3-mirror`). With
  `EmptyRowParts.Allow` the engine cuts where Chromium cuts in the measured cases, also in
  one more respect: a row whose cells all end on the
  page, with only the half of the grid line below it past the page end, is not cut but moves to the next page whole
  (cases `a2-cells-end`, `a2-cells-end-header`, `a2-cells-end-text`: the one step of 42, one pixel apart with and
  without a repeated header row, where this decides; under `Avoid` the row moves whole as well, and keeps its cells'
  line on the page and is cut only where it cannot move, at the top of a page). The same pages and words (cases `a2-padded-blocks`, `a2-line-breaks`, `a3-mirror` and
  their whole-pixel forms `a2-padded-blocks-px`, `a3-mirror-px`, with `FontMetricRounding.WholePixels`; `RowPartTests`,
  Fluent), and where every length is whole pixels the grid lines in the same places within 0.1 pt. A row whose lines all
  fit on the page but whose bottom padding or border does not moves to the next page whole under both values, as
  Chromium moves it (`a2-row-end-bottom-padding-30`, a cell of five lines with 30 pt bottom padding below six lines:
  `a0`..`a5` on page 1 and `b0`..`b4`, `after` on page 2; `PinnedDifferenceTests`). Chromium paints grid
  lines at whole pixels, the engine where layout puts them; where lengths are not whole pixels they differ by up to
  0.5 pt (`a2-rowspan-blocks`, a cell over two rows beside 10 pt blocks; 0.25 pt in `a2-padded-blocks` and
  `a3-mirror`). Whether the engine paints grid lines at whole pixels is left to the HTML front-end's stage. With `Avoid`, the code front-end's default, a
  cut where a part shows nothing is a last resort: the row moves to the next page whole, and is cut there only where it
  cannot move (at the top of a page), so no table is refused for it (`Tracks/TableBreaks.cs`, `CutShows`; one refusal
  of a row whose line below does not fit is listed under "Layout" above); where a row
  shows nothing over more than a page this needs a page more than Chromium. Reason for that default: an empty strip of
  a row reads as a layout fault in a report, and avoiding it costs nothing where the row can move.
- **A row that starts its page area, under `EmptyRowParts.Allow`, lets its grid line below pass the page end**, as
  Chromium prints it (`TablePage.PassesOnlyByItsLineBelow`; `RowEndTests`, cases `a2-row-end*`). In a table that
  continues from the page before, a unit that starts its page area (a row that continues from the page before, or the
  first on the page, with no header row above it), whose cells' content fits and whose line below does not, is placed
  whole; so is the first row of such a unit, and the page
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
  (iv) A table that itself starts at the top of a page with such a first row is refused under `Allow`
  (`Documents.Layout.DoesNotFit`). Chromium leaves that page empty and prints the row whole on the next one
  (`a2-row-end-first-row`, a cell of 11 lines below 14.25 pt top padding, then a row "after": a blank page,
  `x0`..`x10`, `after`), or cuts the row between its lines where they are a hair taller than the page area in its unit
  of 1/64 pixel (`a2-row-end-first-row-cut`, 12 lines of 14.9375 pt: `x0`..`x9`, then `x10 x11 after`). The engine
  prints neither; both belong with the slicing of borders at page breaks. Under `Avoid` the engine cuts such a row as
  a last resort at the top of a page (listed above, `fr-f4-padding-page-start`; `RowEndTests`, `A_table_that_starts_the_page_with_a_first_row_that_fits_but_for_its_line_below_is_refused_under_Allow`).
  The rule is not for a table that starts lower on the page: below a paragraph such a first row moves to the next
  page with its table, as in Chromium (`a2-row-end-below-paragraph`). Nor for a table that continues below an outer
  table's repeated header row, which does not start the page: under `Avoid` the row is cut below the header as Chromium
  cuts it (`a2-row-end-nested-under-header`). (v) Under `Allow` the engine does not take that cut; that page goes on
  without the outer header, where the row starts the page and is placed whole (`H a0`..`a9`, `x0`..`x9`, `H after`), and
  Chromium repeats the header and cuts the row (`H a0`..`a9`, `H x0`..`x7`, `H x8 x9 after`; `RowEndTests`). With
  13.875 pt of top padding the same holds under `Slice`, and under `Refuse` the document is refused
  (`Documents.Layout.DoesNotFit`; `a2-row-end-nested-under-header-13875`). With 9 lines below 12 pt of top padding the
  rest of the inner table fits below the repeated header but for the outer table's bottom line: Chromium repeats the
  header and lets that line pass the page end (and draws it again on a third page that shows nothing else), and the
  engine goes on without the header under every profile (`H a0`..`a9`, `x0`..`x8 after`;
  `a2-row-end-nested-under-header-end`).
  A cell background ends at the middle of the line below (192.375), where Chromium ends it at the line's outer edge
  (192.75; `a2-row-end-background`). That is the table model's own rule, not new here; the 0.375 pt between lies under
  the line, which hides it, as the engine has only solid, opaque lines (no dashed or translucent ones). Limits (i) to
  (iii) and the background go away when borders are sliced at page breaks, noted for stage 4.
- **Monolithic content taller than the page area is a policy** (`LayoutProfile.MonolithicOverflow`): refused by
  default, or sliced as Chromium slices it (`Layout/MonolithicSlices.cs`): the same pages and the same image
  rectangles where the piece stands alone on its pages, in a one-column table and in a table inside a cell, and the
  content after the last slice in the same place in the first three shapes (cases `a4-image-first`,
  `a4-image-after-text`, `a4-line-after-text`, `a4-image-single-cell`, `a4-image-nested-table`; `MonolithicSliceTests`,
  `SliceInTableTests`); in a table inside a cell the row after the slice stands lower than in Chromium (below the
  last slice), and the differences in other table shapes are listed below. A piece is cut into at most 100 slices, one per page (`MonolithicSlices.MaxSlices`): 100 slices show the longest image the front-end accepts, 14 400 pt, on a page area of 144 pt, and a piece that needs more is `Documents.Layout.TooManySlices` (`MonolithicSliceLimitTests`). A document is cut into at most 1 000 slices, all pieces together (`MonolithicSlices.MaxSlicesPerDocument`): the limit per piece bounds the pages per piece, not the pages per input byte, since one paragraph of many tall lines takes up to 100 pages per line. The page that would bring the count past 1 000 is `Documents.Layout.TooManySlices` too, and its message names the limit for one document. Each slice is clipped to
  its part of the page area across the whole page width (Chromium clips only in the block direction). In tagged PDF
  every slice of an image is content of its figure, as the pages of a paragraph are content of the paragraph: each page
  shows another part of the image, the alternative text is on the figure and is read once. The later slices of a line
  of text are artifacts (`ArtifactKind.SliceContinuation`, `/Artifact /Type /Pagination`), a stated choice: ISO 32000-1
  §14.8.2.2.1 says "The real content of a document comprises objects representing material originally introduced by
  the document's author. Artifacts are graphics objects that are not part of the author's original content but rather
  are generated by the conforming writer in the course of pagination, layout, or other strictly mechanical processes."
  The lower part of a glyph is the author's content, so by the first sentence a later slice is real content; but the
  first slice already delivers the line's text (it draws the whole line, clipped; ISO 32000-1 §14.8.2.2.3), and the
  later pages draw the same glyphs again, so as content the text would be read twice. The engine marks them as artifacts so that the text is read once (PDF/UA-1 checkpoint 01-002,
  `docs/pdf-profiles-and-accessibility.md`). veraPDF passes PDF/A-3a and PDF/UA-1 on such a file (sample C, with a
  sliced image, a sliced line and a sliced image in a table under a header row). A sliced figure is on several pages,
  so it has no `/BBox` (ISO 32000-1 Table 344 asks for one only for a figure "appearing in
  its entirety on a single page"). In a table, the row after a sliced piece starts below its last slice, in every
  shape, so no text is drawn over an image. **Deliberate deviation from Chromium:** Chromium does the same in some shapes
  (a one-column table, `a4-image-single-cell`) and draws the next row at the top of the page, over the last slice, in
  others (two columns, `a4-image-in-cell`; a table inside a cell, `a4-image-nested-table`). Where the content's cell
  does not start the page only because of the grid lines above it, the page is cut at the deepest place, inside every
  table around it, as Chromium cuts when no breakpoint is valid, so the slice starts at the top of the next page
  (`ConstraintSpace.IsBelowStartInsets`, under `Slice` only). So `Slice` can change the pages of a document that has no
  piece taller than a page: under `Refuse` that cut is not taken. **Listed difference:** `a4-nested-padded-image-160`
  (a 160 pt image in a table inside a cell, each cell with 15 pt top padding) has 2 pages in Chromium and under `Slice`,
  and 3 under `Refuse`, the default (`PinnedDifferenceTests`). In 600 random documents whose pieces each fit a page
  (each laid out under `Refuse` and under `Slice`, with both values of `EmptyRowParts`), 1 under `Avoid` and 1 under
  `Allow` had other pages under `Slice` than under `Refuse`, and 85 under `Avoid` (66 under `Allow`) that `Refuse`
  refuses were laid out under `Slice` (the shape above, a piece that fits the page area but not with its cell's insets;
  a continued cell whose rest fits the page area only without its bottom padding is another such shape, listed below).
  Taking the cut under `Refuse` too is left to later layout work. **Deliberate deviation from Chromium:** beside a sliced
  piece, the other cells of its row go on page by page beside the slices (cases `a4-image-beside-lines`,
  `a4-line-beside-lines`); Chromium holds their content back while the slices run and goes on with it below the end
  of the piece. In a cell that spans rows the pages follow from the rules for rows: the page breaks at the latest
  breakpoint of the span (inside its last row), the span's missing height goes to its last row, and the next row
  comes below the last slice (case `a4-image-rowspan`; Chromium breaks between the spanned rows and draws the next row
  over the slice).
  Under a repeated header row the content is sliced right below the header, on a page after one that showed no body row
  other than slices of a piece that was itself sliced; after a page that did show one, the page holds only the header
  and the row moves on once. That page with only the header is Chromium's: the first slice starts on Chromium's page and
  at Chromium's place in all eleven measured cases (`a4-header-*`, `a4-intro-header-image-row`; `SliceUnderHeaderTests`),
  each with a header of at most a quarter of the page area; a taller header is not repeated (above), and the piece is
  sliced as without a header.
  **Deliberate deviation from Chromium:** on the later pages the engine repeats the header above every slice and makes
  each slice as tall as the room below it, so the content is shown whole and once; Chromium moves the content up a whole
  page area on each later page and either leaves the header out (`a4-header-image-row`) or draws the header over 16.5 pt
  of the content that are never shown (`a4-header-row-image-row`).
  A piece that fits the page area alone, but not with the insets around it in its table cell (the cell's padding and
  grid lines), is sliced too: the test for slicing is "the piece with its insets does not fit a page", not "the piece is
  taller than the page area" (`Tracks/TableSegments.cs`, `SlicedAboveBottomInset`; `SliceAboveInsetTests`, cases
  `a4-cell-pad3-image-177`, `a4-cell-pad3-line-177`, `a4-cell-image-179-5`). Before, 83 of 600 random documents whose
  pieces each fit a page were refused under `Slice` for this shape. **Listed difference from Chromium:** Chromium does
  not slice such a piece; it moves it whole to the top of the next page and lets the cell's bottom padding and grid line
  pass the page end. The engine has the same pages and words, but the piece in two slices (176.625 and 0.375 pt for a
  177 pt image in a cell with 3 pt padding), and what follows starts lower on the next page. Under `Refuse` such a piece
  is refused as before. **Listed difference from Chromium:** the same holds for content that fits the page area only
  without its cell's bottom padding when the cell continues from the page before. Chromium places the rest of the
  cell on the page and lets the padding pass the page end; the engine never lets a cell's bottom padding pass the
  page end, and records no breakpoint inside content that continues from the page before (`FlowLayout`), so under
  `Refuse` the table is refused (`Documents.Layout.DoesNotFit`), and under `Slice` the content is laid out again above
  the bottom padding and breaks earlier: a cell with 30 pt bottom padding around a table whose rest on page 2 fits the
  page area gives `t34 t35 t36 t37 t39 t40 t41 t42` on page 2 in Chromium and `t34 t35 t36 t37`, then `t39 t40 t41 t42`
  on page 3 under `Slice` (`a4-continued-cell-pad-bottom-30`; `PinnedDifferenceTests`). Under an outer table's
  repeated header the page is printed without that header where the rest then fits, under every profile: an outer
  header "O" above a cell with 30 pt bottom padding around a table of 19 one-line rows gives `O r10`..`r18` on page 2
  in Chromium and `r10`..`r18` in the engine (`a4-continued-cell-pad-bottom-30-under-header`; `PinnedDifferenceTests`).
  A block outside a table whose bottom padding does not fit below such a piece at the top of a
  page keeps the piece whole and breaks before its bottom padding and border, which start the next page with what
  follows below them (`SlicedBox`), as Chromium does: a block with 3 pt padding above and below a 177 pt image puts
  "after" on page 2, 3 pt lower than at the top of a page, in both (`a4-image-177-block-padding-3`;
  `SliceAboveInsetTests`).
  Only images and line boxes are sliced; padding or borders taller than a page are refused under either value.
  Bottom padding of a block that does not fit below the last slice of a piece in it starts the next page, and what
  follows the block comes once, below it, as in Chromium (cases `a4-image-block-padding-60`,
  `a4-image-block-padding-133`; `MonolithicSliceTests`).
- **Text:** Unicode normalization before shaping follows HarfBuzz's default (compose a base and a mark the font has
  precomposed, decompose what it lacks), except: a base followed only by an enclosing mark (Me) is not decomposed
  (HarfBuzz counts Me as a mark); a cluster with a variation selector (U+FE00–U+FE0F) is normalized, where HarfBuzz
  leaves it alone (`handle_variation_selector_cluster`); the combining classes are the UCD's, not HarfBuzz's modified
  ones (`hb-unicode.hh`, `_hb_modified_combining_class`: Hebrew, Arabic shadda, Telugu, Thai, Tibetan), so marks of
  those scripts can come in another order; after a reorder that then composes, the clusters (which glyph carries which
  characters) differ while glyphs and positions agree; Unicode variation sequences (cmap format 14) are not read, the
  default glyph is drawn; the fallbacks for a missing space and for U+2011 are not made (the text is refused with
  `GlyphMissing`) (`Typesetting/Unicode/Normalization.cs`). The variation selector and the two fallbacks are pinned
  against HarfBuzz (`NormalizationReferenceTests`); the enclosing mark, the combining classes, the clusters after a
  reorder and the variation sequences need a font the family's tests do not have, and are not pinned. A zero width joiner between a base and a mark: the shaper
  skips it and attaches the mark; HarfBuzz applies `mark` with manual joiners and leaves the mark unattached. In the
  PDF text layer a soft hyphen outside a ligature is dropped and one inside a ligature kept; Chromium keeps every soft
  hyphen and adds U+2010 at a shown hyphen. Contextual and other OpenType lookup types are skipped, and so is mark-to-mark positioning
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
  have without the span, and the cell continues on the next page, filling this page down to its end, as Chromium lays
  a spanning cell out in the height of all its rows (`TableBreaks.BetweenRowsAppeal`, `Cut`; cases `fr-f7-*`). The
  author's keep rules still apply: a row group kept together, a spanning cell kept together, a row kept together (which
  keeps the rows its spanning cell reaches into, `fr-f8-*`), `KeepWithNext`. So the rows under a label fill their pages
  as rows without one do. A cell kept together is a keep rule at a break inside its row too: the row is not cut there
  while a better break exists (`TableBreaks.InsideAppeal`). A row group's keep is applied from outside the group, as
  Chromium applies `break-inside: avoid` to a box's own break: where a kept group taller than the page must break, it
  breaks where its rows break best (between two rows rather than inside a kept row or against orphans, `fr-f6-*`;
  `TableBreaks.Breakpoint.Overall`). A row is not cut where some cell's lines all fit and only its bottom padding or its
  share of the grid line below it does not: the row moves whole, as Chromium never breaks a cell before its bottom
  padding (`TableSegments.Slice`, cases `fr-f4-*`). A cell that breaks at a breakpoint it chose itself keeps it, and so
  does its row (`fr-f9-cell-*`, see "early breaks" above).
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
| line ends | Line ends against Chromium's print: 3,667 paragraphs in Lato 75 pt, four alignments, widths around every change of Chromium's lines (`ChromiumLineEndTests`, data in the Typesetting tests' `Reference/Chromium`): every line break equal, every line extent within 0.06 pt. Each piece shaped again against HarfBuzz for that piece alone, the hyphen alone: 19,350 pieces, 0 differences (`LineReshapingReferenceTests`). The unsafe-to-break flags against HarfBuzz's: 3,046 clusters, 0 differences (`UnsafeToBreakReferenceTests`). | `UnitTests.Extensions.Documents.Typesetting/Reference/` |
| preview images | every sample page against MuPDF 1.25.1 and poppler 25.03.0 renders of the engine's PDF, at 96 and 300 dpi, by the rule in `RasterComparison`; golden PNG files of the samples | `UnitTests.Extensions.Documents.Samples/RasterReferenceTests.cs`, `PreviewTests.cs`; `tools/reference/raster/README.md` |
| glyph outlines | fontTools 4.57.0 in the reference tools image, every glyph of the test fonts and hand-made composites, point for point and exactly | `UnitTests.Extensions.Documents.Fonts/GlyphShapeReferenceTests.cs` |
| JPEG decoding | the ITU-T T.83 compliance streams A1, C1, C2, G1, K1 (downloaded when the tests run, SHA-256 checked on every use, trait `Category=Network`: a failed download fails on CI and skips elsewhere): coefficients equal EREF, re-transformed samples equal DREF, exactly; libjpeg-turbo 2.1.5 `djpeg -dct int` on cjpeg-made files, within a stated difference per case | `UnitTests.Extensions.Documents.Images/JpegConformanceTests.cs`, `JpegDecoderReferenceTests.cs` |
| PNG encoding | the engine's decoder and Pillow read every pixel back; bytes pinned | `UnitTests.Extensions.Documents.Images/PngEncoderTests.cs` |
| table layout | numbers measured in Chromium 159 in the pinned Playwright container with font hinting off, and pinned; compared within n layout units (1/64 px), n counted from the structure of each case; the cases, the measuring script and the rule are in `tools/reference/chromium/` | `UnitTests.Extensions.Documents.Layout/ChromiumReferenceTests.cs` |
| page starts and row cuts across shapes | a generated sweep: 8 kinds of leading space × 16 kinds of first piece (4 of them with bottom padding or a bottom border in block flow) × 51 start offsets, a paragraph after each piece (6 528 documents, 13 964 pages, both asserted): no refusal, every word of the input document once (the expected words come from the document, not from a layout), nothing past the page area, no part of a row that shows nothing where it can be avoided | `UnitTests.Extensions.Documents.Fluent/PageStartSweepTests.cs` |
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

Some ideas come from other engines; where the engine follows their code expression for expression, `NOTICE.md` names the files, with their copyright and licence (Chromium's Blink layout, Skia and Fontations for font line metrics, HarfBuzz's normalizer). The layout contract —
a pure function of a box, a constraint space (the space the box may use on the current page) and an incoming break
token, returning a fragment and an outgoing break token, plus a second function for the intrinsic widths
(`MinMax`) — follows Chromium's LayoutNG. Choosing among breakpoints by their appeal, and among the best the latest,
follows Chromium's `BreakAppeal` (`third_party/blink/renderer/core/layout/break_appeal.h`, read for its order of
appeals and its last resort, "not a valid class A, B or C breakpoint"); CSS Fragmentation 3 §4.5 leaves that choice to
the user agent. Going back to a better breakpoint inside earlier content by laying it out once more follows Chromium's
`EarlyBreak` (`early_break.h`: "In cases where we run out of space at an unideal location, we may want to go back and
break here instead"), with the same bound: one more layout, and no early break while following one. The page-count
fixpoint is capped at five passes, as Typst caps the same fixpoint.
