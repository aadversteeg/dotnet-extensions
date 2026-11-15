# Architecture of Ave.Documents (milestone M1)

For a reviewer who has never seen the code. It says what each part is for, where the contracts are, where every
policy and whole algorithm lives, what is known not to work, and how each claim is checked. Paths are relative to
`src/` unless they start with another top-level folder.

## What it is

A document engine in pure managed .NET 10: a document written in C# goes in, a PDF comes out. No native library,
no P/Invoke, no external program at run time; reference tools (veraPDF, qpdf, poppler, MuPDF, HarfBuzz, Chromium)
are used only by tests and scripts. Everything that can fail returns `Result<T, Error>` (Ave.Extensions.Functional /
Ave.Extensions.ErrorPaths) with a path-shaped error code and the author's source file and line; exceptions mean a
bug in the engine.

## Layers

Each layer is one project and may use only the layers below it in this list. `UnitTests.Ave.Documents.Architecture`
checks this on the compiled assemblies, together with "no unsafe code, no P/Invoke, no native assets" and "no
test-only package in a product assembly".

| Project | Role |
| --- | --- |
| `Ave.Documents.Fluent` | code front-end: a document as an immutable value; translates it to the document model (cascade + validation) |
| `Ave.Documents.Render.Pdf` | page model → PDF: fonts, images, content streams, and the conformance profiles (tagging, XMP, output intent, attachments, refusals) |
| `Ave.Documents.Layout` | layout algorithms, pagination driver, painter (fragments → page model) |
| `Ave.Documents.PageModel` | page model: per page a display list plus structure and artifact markers |
| `Ave.Documents.DocumentModel` | document model: a styled box tree with computed values |
| `Ave.Documents.Typesetting` | white-space collapsing, UAX #14 line breaking, OpenType shaping, justification |
| `Ave.Documents.Pdf` | PDF objects and writer, content operators, XMP writer, sRGB ICC profile, `PdfProfile`; knows nothing about layout |
| `Ave.Documents.Fonts` | TrueType reader (with GSUB, GPOS, GDEF, kern) and subsetter |
| `Ave.Documents.Images` | PNG decoder and JPEG header reader |
| `Ave.Documents.Core` | shared vocabulary: lengths, geometry, colour, `ValueList<T>`, source locations, roles, metadata, attachments, the error-code tree |

The pipeline is four pure functions in a row (`Fluent/DocumentRendering.cs`):

```
Document ──DocumentTranslator.ToModel──▶ StyledDocument ──DocumentLayout.Run──▶ PageDocument ──PdfRenderer.Render──▶ bytes
 (front-end value)                       (document model)   (Paginator + PagePainter)  (page model)
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
is left, and repeats the whole document until the page count settles (at most 5 passes).

**The seam for later front-ends.** The layout contract (a pure function from box, space and break token to fragment
and token) is what a later front-end — markup, a Typst-like language, a report definition — plugs into. Today the
engine ships four algorithms (flow, inline, tracks, image) and registering another is internal
(`LayoutEnvironment.WithAlgorithm`); `BreakToken` can only be derived inside the layout assembly, and the parts of a
table are a display type (`TableDisplay`), not algorithms. When a front-end needs an algorithm of its own, these are
the three places to open, with that front-end as their first user.

## Where the policies and whole algorithms are

| What | Where | Rule it implements |
| --- | --- | --- |
| cascade and inheritance, user-agent styles | `Fluent/DocumentTranslator.cs`, `Cascade.cs`, `UserAgentStyles.cs` | CSS Cascade 4 §4, §7.1; HTML rendering (heading sizes) |
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
| PDF syntax and file structure | `Pdf/PdfSerializer.cs`, `PdfWriter.cs` | ISO 32000-1 §7.3, §7.5 |
| fonts and images in PDF | `Render.Pdf/FontEmbedding.cs`, `ToUnicodeCMap.cs`, `ImageEmbedding.cs` | ISO 32000-1 §9.7–§9.10, §8.9 |
| conformance profiles | `Pdf/PdfProfile.cs` (what each standard adds), `Render.Pdf/ConformanceRules.cs` and `Tagging/TableRegularity.cs` (what it refuses) | ISO 19005-3, ISO 14289-1; rule ids from veraPDF |
| tagged PDF | `Render.Pdf/Tagging/` (`PageEvents` → `StructureTree` → `StructureObjects`, `ContentMark`, `StructureTypes`) | ISO 32000-1 §14.6–§14.8 |
| XMP metadata | `Pdf/Xmp/` (generic writer and extension-schema descriptions), `Render.Pdf/MetadataPacket.cs` (which properties) | ISO 16684-1; ISO 19005-3 §6.6 |
| output intent colour profile | `Pdf/SrgbIccProfile.cs` | ICC.1:2001-04; IEC 61966-2-1 |
| attachments | `Render.Pdf/AttachmentEmbedding.cs`, catalog in `PdfRenderer.cs` | ISO 32000-1 §7.11; ISO 19005-3 §6.8 |
| object numbering | `Render.Pdf/ObjectPlan.cs` | fixed in advance, so output is deterministic |

Strategies are plain functions; the closed sets (PDF objects, draw items, fragments, front-end elements, XMP values,
structure kids) are records with private constructors, each in one file so the whole set can be read at once.

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
compression comes from the .NET runtime, so bytes are identical on the same runtime version only. Tests:
`Same_input_gives_byte_identical_output…`, `A_tagged_file_is_deterministic`, `Two_runs_give_byte_identical_PDFs…`.

## Known limits (all stages)

Each is documented next to its code as well.

- **Layout:** negative margins (refused as unsupported); orphans and widows; the `line-height` property and the strut
  (a line is always `line-height: normal`, which is why the timesheet tunes cell padding); `vertical-align: baseline`
  in cells; column gaps and column styles; the separated border model; diagonal joins of differently coloured
  borders; a keep rule never moves a break into an earlier sibling's content; padding that does not fit at the top of
  an empty page overflows; a paragraph with a page counter that breaks across pages is re-broken with each page's
  number; every multi-page document is laid out at least twice (page-count fixpoint).
- **Deliberate deviations from CSS:** header and footer areas are taken from the page area, not from the margins
  (as in RDL and Typst); an image with both width and height fits inside that box keeping its ratio (CSS would
  stretch); a row span's missing height goes to its last row; a page that cannot hold a table's header plus some body
  continues without the header.
- **Text:** no Unicode normalization before shaping (HarfBuzz composes a base and a mark into a precomposed glyph,
  this engine does not); contextual and other OpenType lookup types are skipped; default language system only;
  complex scripts, bidi and hyphenation are not implemented.
- **Fonts and images:** TrueType outlines only (no CFF); fonts are always explicit input; interlaced and 16-bit PNG,
  CMYK and arithmetic-coded JPEG are refused as unsupported.
- **PDF:** classic cross-reference table, no object streams; flat page tree; name and number trees as one node; one
  text per glyph in a ToUnicode map (the first use wins).
- **Tagging and PDF/UA:** header cells come only from header rows (Scope Column; no row headers, no Headers/IDs for
  nested headers); a block and a row of blocks are Div, so a row of blocks nests three Divs per column; no per-element
  language, no Span, Link, List, Caption, Note, Formula, TOC or bookmarks; no ActualText; empty structure elements are
  written for empty cells; the XMP packet has no padding; everything in page headers and footers is an artifact.
- **Speed:** measured, not tuned. On LAMORAK, Release, fonts parsed beforehand, PDF/A-3a + PDF/UA-1: the normal
  timesheet takes about 1.0–1.4 s cold and 110–150 ms warm, the long one 2.5–2.9 s cold and about 410 ms warm. The
  reporting PoC's Typst took 19 ms and 46 ms warm.
- **Long tables:** a table's grid and columns are prepared once per layout pass and handed from page to page in the
  table's break token; a page looks up the cells of its own rows through per-row indexes. Layout time now grows with
  the number of rows. Measured on LAMORAK (Release, A4, 2 cm margins, a three-column table with a header row, 0.5 pt
  grid lines, median of three runs after a warm-up, layout only):

  | Rows | Pages | Before | After |
  | ---: | ---: | ---: | ---: |
  | 250 | 9 | 605 ms | 386 ms |
  | 500 | 17 | 1993 ms | 375 ms |
  | 1000 | 34 | 7734 ms | 529 ms |
  | 2000 | 67 | 31005 ms | 1088 ms |

  What still grows: the work per page is linear in the rows on that page, but every row is measured once more for
  each layout pass, so a document whose page count is used in its text (two passes or more) takes that many times as
  long. `TableScalingTests` fails when four times the rows take eight times as long.

## How each reference check is run

| Claim | Check | How to run |
| --- | --- | --- |
| layering, no native code, test packages stay out | `UnitTests.Ave.Documents.Architecture` (compiled assemblies); `tools/verify-samples.ps1` step 2 (publish folder, PE headers) | `dotnet test`; the script |
| line breaking | the official `LineBreakTest.txt`, Unicode 18.0.0, all 19,346 cases | `UnitTests.Ave.Documents.Typesetting` |
| shaping | HarfBuzz 15.0.0 as WebAssembly inside the test process, glyph for glyph | `UnitTests.Ave.Documents.Typesetting/Reference/` |
| table layout | numbers measured once in Chromium 157 and pinned | `UnitTests.Ave.Documents.Layout/ChromiumReferenceTests.cs` |
| text in the PDF | PdfPig reads every glyph back at the position the page model says | `TestSupport/PdfReadBack.cs` |
| structure tree | PdfPig reads the structure tree and marked content back (roles, order, attributes, text per element; every glyph marked) | `TestSupport/PdfStructureReadBack.cs`, `UnitTests.Ave.Documents.Samples/TaggedPdfTests.cs`, `UnitTests.Ave.Documents.Render.Pdf/TaggingTests.cs` |
| PDF/A-3b, PDF/A-3a, PDF/UA-1 | veraPDF 1.30.4 in Docker (pinned by digest) on every sample; a plain file must fail as negative control | `UnitTests.Ave.Documents.Samples/ReferenceValidationTests.cs` (trait `Category=Reference`, skipped without Docker); script step 5 |
| attachments | PdfPig in process, and qpdf in Docker, byte for byte | `TaggedPdfTests`, `ReferenceValidationTests`; script step 5 |
| PDF syntax, fonts, images | `qpdf --check`, `pdffonts`, `pdfimages`, fontTools on every extracted subset | script step 4 |
| how it looks | poppler and MuPDF renders, side by side with the PoC's Typst pages | script steps 4 and 6 (`out/samples/png`, `out/samples/sbs`) |

`dotnet test --filter Category!=Reference` leaves the Docker checks out. Golden files are updated with
`UPDATE_GOLDEN=1` and reviewed as a diff.

## Background

Three ideas come from other engines, studied through their public documents, not their code. The layout contract —
a pure function of a box, a constraint space (the space the box may use on the current page) and an incoming break
token, returning a fragment and an outgoing break token, plus a second function for the intrinsic widths
(`MinMax`) — follows Chromium's LayoutNG. Choosing among breakpoints by their appeal, and among the best the latest,
follows Chromium's `BreakAppeal`; CSS Fragmentation 3 §4.5 leaves that choice to the user agent. The page-count
fixpoint is capped at five passes, as Typst caps the same fixpoint.
