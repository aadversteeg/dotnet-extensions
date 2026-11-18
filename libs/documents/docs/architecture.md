# Architecture of Ave.Extensions.Documents (milestone M1, and milestone M2 as far as it is merged)

For a reviewer who has never seen the code. It says what each part is for, where the contracts are, where every
policy and whole algorithm lives, what is known not to work, and how each claim is checked. Paths are relative to the
family folder (`libs/documents`).

## What it is

A document engine in pure managed .NET 10: a document written in C# goes in, a PDF comes out. No native library,
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
| `Ave.Extensions.Documents` | composition: `ToPdf` joins the front-end, layout and the PDF back-end; the one package an application needs |
| `Ave.Extensions.Documents.Fluent` | code front-end: a document as an immutable value; translates it to the document model (cascade + validation); knows nothing about layout or PDF |
| `Ave.Extensions.Documents.Render.Pdf` | page model → PDF: fonts, images, content streams, and the conformance profiles (tagging, XMP, output intent, attachments, refusals) |
| `Ave.Extensions.Documents.Layout` | layout algorithms, pagination driver, painter (fragments → page model) |
| `Ave.Extensions.Documents.PageModel` | page model: per page a display list plus structure and artifact markers |
| `Ave.Extensions.Documents.DocumentModel` | document model: a styled box tree with computed values |
| `Ave.Extensions.Documents.Typesetting` | white-space collapsing, UAX #14 line breaking, OpenType shaping, justification |
| `Ave.Extensions.Documents.Pdf` | PDF objects and writer, content operators, XMP writer, sRGB ICC profile, `PdfProfile`; knows nothing about layout |
| `Ave.Extensions.Documents.Fonts` | TrueType reader (with GSUB, GPOS, GDEF, kern) and subsetter |
| `Ave.Extensions.Documents.Images` | PNG decoder and JPEG header reader |
| `Ave.Extensions.Documents.Core` | shared vocabulary: lengths, geometry, colour, `ValueList<T>`, source locations, roles, metadata, attachments, the error-code tree |

The pipeline is four pure functions in a row (`Ave.Extensions.Documents/DocumentRendering.cs`). The composition lives in its
own package so that a front-end depends only on the vocabulary it translates into (Core, Fonts, Images,
DocumentModel), and a second front-end (markup) sits next to the first one instead of on top of it:

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
| line heights and the strut: `normal`, numbers, lengths and percentages; every line starts with its paragraph's strut | `DocumentModel/LineHeight.cs`, `Layout/LineBoxes.cs`, `Fluent/LineHeightValue.cs` (computed values) | CSS 2.1 §10.8, §10.8.1; CSS Inline 3 §5.1, §5.3; checked against Chromium's print |
| a font's line metrics (ascent, descent, line gap for `normal` and the strut): OS/2 typographic when the font sets USE_TYPO_METRICS, otherwise 'hhea' (where 'hhea' has none: typographic, then Windows), as Chromium reads them on Linux; for every front-end, not a policy | `Fonts/FontParser.cs` (`LineMetricsOf`), `FontMetrics.Line` | Skia `SkFontHost_FreeType.cpp`, Fontations `skrifa` `metrics.rs`; checked against Chromium's print (cases `d-typo-*`) |
| orphans and widows: where a paragraph breaks, the best breakpoint inside a complete paragraph; a cut of a table row is as good as the breaks it makes inside its cells | `Layout/InlineLayout.cs` (`BreakWhereFull`, `BestBreakInside`), `Tracks/TableBreaks.cs` (`Cut`) | CSS Fragmentation 3 §3.3, §4.4 rule 3; checked against Chromium's print |
| early breaks: a keep rule that reaches back into an earlier box's content, never into a box that continues from the page before; a box that breaks at its own early break keeps it (its parent does not compare it with its own breakpoints) | `Layout/EarlyBreak.cs`, `FlowLayout.cs`, `InlineLayout.cs`, `Tracks/TablePage.cs`, `LayoutResult.cs` (`KeepsItsBreak`) | CSS Fragmentation 3 §4.4, §4.5; Chromium `MovePastBreakpoint`, `kNeedsEarlierBreak`; checked against Chromium's print (cases `b-*`, `fr-keep-*`) |
| bottom padding and border that do not fit below a box's complete content: the box breaks at its best inner breakpoint and keeps it; without one it breaks before its bottom padding and border, which start the next page (a last resort, so a box lower on the page moves whole); not in table cells | `Layout/SlicedBox.cs`, `FlowLayout.cs` | CSS Fragmentation 3 §4.1, §5.4; Chromium `FinishFragmentation`; checked against Chromium's print in block flow (cases `fr-pad-*`, `fr-border-*`, `a3-pad-*`) |
| where a page starts: a box that does not fit below margins, empty boxes or padding moves on; only content that starts the page is refused; the last-resort break | `Layout/ConstraintSpace.cs` (`IsAtPageStart`), `FlowLayout.cs`, `SlicedBox.cs`, `BreakAppeal.cs` (`LastResort`) | CSS Fragmentation 3 §4.1, §4.4, §5.2; checked against Chromium's print |
| margin collapsing | `Layout/MarginCollapsing.cs` | CSS 2.1 §8.3.1 |
| box slicing at page breaks | `Layout/SlicedBox.cs` | CSS Fragmentation 3 §5.2–§5.4 |
| inline layout, line boxes | `Layout/InlineLayout.cs`, `LineBoxes.cs` | CSS 2.1 §9.4.2, §10.8 |
| tables and rows of blocks | `Layout/Tracks/` (`TracksLayout.cs` lists the steps) | CSS 2.1 §17 (fixed and auto layout, collapsing borders), CSS Grid `fr` §7.2.4 / §11.7.1, CSS Fragmentation for row breaks |
| images | `Layout/ImageLayout.cs` | CSS 2.1 §10.3.2 (with a stated deviation for width + height) |
| page templates, page counters | `Layout/PageAreas.cs`, `Paginator.cs`, `PageContext.cs` | CSS Paged Media 3 §4.2.2, §5, §6.1 |
| what is content and what is an artifact | `Layout/PagePainter.cs` | ISO 32000-1 §14.8.2.2 |
| line breaking | `Typesetting/LineBreaking/UnicodeLineBreaking.cs`, `GreedyLineBreaker.cs` | UAX #14 rev. 57 |
| shaping | `Typesetting/Shaper.cs`, `Shaping/` | OpenType GSUB/GPOS/GDEF, legacy kern |
| Unicode normalization before shaping: decompose, reorder marks, recompose, as far as the font allows | `Typesetting/Unicode/Normalization.cs`, data `UnicodeNormalizationData.g.cs` | UAX #15; HarfBuzz's normalizer (hb-ot-shape-normalize.cc) |
| shaping at a break and where a line ends: Where a line ends and how wide it is follows Chromium 159 (Blink `ShapingLineBreaker::ShapeLine`, `LineBreaker`): the paragraph is shaped once and a line keeps the paragraph's glyphs; the start of a wrapped line and the end of a line are shaped again on their own from and to the nearest place that is safe to break, when that end is not safe (the shaper sets HarfBuzz's `HB_GLYPH_FLAG_UNSAFE_TO_BREAK` as HarfBuzz does: a pair whose values are all zero marks nothing, a lookup marks the clusters after its first glyph's own). The hyphen shown at a soft hyphen is shaped alone and never kerned with the letter before it. At a line end at a collapsible space, left-aligned text keeps the paragraph's glyphs (the last letter keeps its kerning with the space, and the line is measured with it); right-aligned, centred and justified text shape the end again before the space. Before a forced break and at the paragraph's end the end is shaped again in every alignment, as measured in Chromium 159's print, which is the reference for where lines end and wins over the reading of its source (that reading says start-aligned text keeps the end): `xx AV <br>To AV` fits "xx AV" on one line only from 252 px in all four alignments, and the right-aligned line shows the unkerned 5,037 units; `xx AV To AV ` at 254 px is `xx AV | To | AV` in all four. When a line, shaped as it is, does not fit, the break opportunity before it is taken. So a line can differ from shaping its text alone: a ligature cut by a break is shaped as its half alone, as measured: `ff` + U+200B + `fi` ends `f f` (1,402 units in Chromium's print), not the ff ligature HarfBuzz gives "ff" alone; each piece shaped again equals HarfBuzz for that piece. | `Typesetting/GreedyLineBreaker.cs`, `ShapedGlyph.UnsafeToBreak`, `Shaper.ShapeLinePiece`, `LineShapingBudget`, `Layout/InlineLayout.cs` (`LineEndAtSpace`) | Blink `ShapingLineBreaker::ShapeLine`, `LineBreaker`; HarfBuzz's unsafe-to-break flag (hb-buffer.h); checked against Chromium's print (`ChromiumLineEndTests`) |
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

## Who checks what

Every rule is checked in one place, by the layer that owns the reason for it; each check reports all its problems
together, at the author's line where there is one.

| Layer | Checks | Error codes |
| --- | --- | --- |
| front-end (`Fluent/InputChecks.cs`, `InputLimits.cs`) | the author's input: lengths, borders, columns, spans, table shape, page breaks only where they can apply, metadata text (XML characters), language tags, media types, unique attachment names, nesting depth | `Documents.Input.*` |
| layout (`Layout/LayoutErrors.cs`) | only what cannot be laid out: content that does not fit, columns that do not fit, a page-count fixpoint that does not settle | `Documents.Layout.*` |
| PDF back-end contract (`Render.Pdf/PageModelRules.cs`, `PdfRenderer.cs` for the options) | a page model from any producer: metadata and at least one page, each of positive finite size, nothing missing, glyphs of their font with a text, enum values, spans within the table model's limits, table structure that nests, a table of rows or of row groups with at most one head, which comes first, one role per node, attachments with a name, a media type and a unique name; render options with a profile and a file identifier of 1 to 32 767 bytes | `Documents.Pdf.Write.InvalidPageModel`, `InvalidOptions`, `InvalidFileIdentifier` |
| profile rules (`Render.Pdf/ConformanceRules.cs`, `ProfileLimits.cs`, `Tagging/TableRegularity.cs`) | what a profile forbids: no title or language, a figure without (or with blank) alternative text, heading order, attachments without relationship, irregular tables (and tables too large to check), the PDF/A limits (page size, string and name length, media type form, object count), glyph and text rules; every veraPDF rule and how it is met is in `docs/conformance-matrix.md` | `Documents.Pdf.Conformance.*` |

The slot-filling rule of a table (which slot each cell takes, and when a span runs past its group or overlaps
another) is `Core/TableSlots.cs`. The front-end's table check, the layout's grid and the PDF/UA regularity check all
use it, so the three cannot disagree; the span rule (`TableSlots.IsValidSpan`), the language form
(`Core/LanguageTags.cs`) and the characters text may hold (`Core/TextCharacters.cs`) are single rules in the same way.
Some rules are checked twice, on purpose, by calling the same rule: the front-end reports them at the author's line,
and the back-end refuses them as well, because it accepts page models that did not come from the front-end. These are
the span rule, unique and non-blank attachment names (a duplicate would silently drop a file from the embedded-files
name tree), and the language form (for the tagged profiles).

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
   PNG header's size against `ImageLimits` before anything is inflated; a JPEG is embedded without being decoded).
3. **Name a limit where a number decides the size** — decoded pixels, glyphs made by substitutions, lookup operations,
   spans, the PDF/UA grid, nesting, lengths — check it before the work, and give it an error code (table below). A
   limit holds for the whole input it protects (one text, not each piece of it).
4. **Compute with input numbers in 64 bits**, so a sum or product cannot wrap around into a valid-looking value.

| Limit | Value | Where | Error |
| --- | --- | --- | --- |
| work of a layout table parse | 8 units per byte, at least 16 384 | `Fonts/OpenType/TableReader.cs` | `Documents.Font.Parse.InvalidValue` |
| decoded image | 2^26 pixels | `Images/ImageLimits.cs` | `Documents.Image.TooLarge` |
| glyphs of one text | 64 per input glyph, at least 16 384 | `Typesetting/Shaping/GlyphSubstitution.cs` | `Documents.Typesetting.TooManyGlyphs` |
| shaping operations of one text | 1 024 per input glyph, at least 65 536 | `Typesetting/Shaping/ShapingBudget.cs` | `Documents.Typesetting.TooMuchWork` |
| cell span | 1 000 columns, 65 534 rows | `Core/TableSlots.cs` | `Documents.Input.Invalid.Table`, `Documents.Pdf.Write.InvalidPageModel` |
| PDF/UA grid of one table | the first row's width; 4 194 304 slots | `Render.Pdf/Tagging/TableRegularity.cs` | `Documents.Pdf.Conformance.PdfUa1.TableTooLarge` |
| nesting of elements | 64 levels | `Fluent/InputLimits.cs` | `Documents.Input.Invalid.TooDeep` |
| nesting of arrays and dictionaries in one PDF object | 64 levels | `Pdf/PdfSerializer.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| nesting of arrays and structures in one XMP property | 64 levels | `Pdf/Xmp/XmpPacket.cs` | `Documents.Pdf.Write.NestedTooDeep` |
| a length in the front-end | 14 400 pt | `Fluent/InputLimits.cs` | the property's `Documents.Input.Invalid.*` code |

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
compression comes from the .NET runtime, so bytes are identical on the same runtime version only. Tests:
`Same_input_gives_byte_identical_output…`, `A_tagged_file_is_deterministic`, `Two_runs_give_byte_identical_PDFs…`.

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
  pinned with Chromium's pages in `ListedDifferenceTests`; a paragraph with a page counter that breaks across pages is re-broken with each page's
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
  (`tools/reference/chromium/README.md`, "Where Chromium is not exact"). Chromium rounds the font metrics of every
  inline box to whole pixels: its ascent and its descent (and for `line-height: normal` its line gap) each rounded, and
  the half-leading above it floored to a whole pixel; the engine keeps the font's exact values. So a line of `normal`
  height differs by up to about a pixel per line (Lato at 12 pt: Chromium 19 px, 14.25 pt, the font's values 14.4 pt;
  three roundings can reach 1.5 px for a font with a line gap), and under an explicit line height a baseline's place
  in its line differs too: the largest measured difference is 1.16 px (0.87 pt, Lato 10 pt at `line-height: 1.5`, case
  `fr-lh-first-baseline`, `LineBoxReferenceTests`). Where a line mixes font sizes under a line height given as a length,
  the boxes' rounded metrics decide the line's height, and the engine's line can be up to about half a pixel taller or
  shorter per line: ten lines of 15 pt with a 20 pt word on two of them fill a 150 pt page in Chromium and need a second
  page in the engine (case `fr-lh-mixed-sizes`). The policy `FontMetricRounding.WholePixels` follows Chromium's rounding
  (pull request 8). Chromium also snaps every used line height to 1/64 px (a number times the font size floored, a length
  or a percentage rounded); the engine does not, so where a page end falls within lines/64 px of an exact fit the page
  can break one line earlier or later (the front-end that speaks CSS will hand over lengths in that unit). The font's
  line metrics come from the table Chromium reads on Linux (Fontations): OS/2 typographic metrics when the font sets
  USE_TYPO_METRICS, whatever the table's version, otherwise 'hhea'; this is the reference — on Windows and macOS
  Chromium takes the metrics from the platform, and which table that is, is not documented. These metrics move every
  inline box, not only lines of `normal` height. A line height smaller than its text (12 pt text on 7.5 pt lines, or 0)
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
  more than the page holds: 7 of 1,792 measured documents; `InlineLayout.BreakWhereFull`). A table header is repeated on every page whatever its height; Chromium repeats it only
  while the header and its grid line are at most a quarter of the page area high (the fragmentainer block size divided by
  4, the page area rounded up to whole pixels first; a header exactly a quarter high repeats: measured at 150, 180 and
  240 pt, limits 37.5, 45 and 60 pt; it also repeats only when the header group is `break-inside: avoid`, which the
  browser's own style sheet sets for print), and above that limit it is an ordinary row group. Whether the engine gets
  that rule as a layout policy is a question for stage 4. The rest of a cut cell aligned `middle` or `bottom` is aligned
  in the rest of its row (Chromium puts it at the top); a margin dropped at a row cut does not stay in the row's height
  (Chromium keeps the uncut height). Where a cell's lines all fit and only its bottom padding or grid line does not, at
  the top of a page the engine cuts the row as a last resort, where Chromium leaves the first page blank and prints the
  row whole on the next (case `fr-f4-padding-page-start`).
- **Deliberate deviations from CSS:** header and footer areas are taken from the page area, not from the margins
  (as in RDL and Typst); an image with both width and height fits inside that box keeping its ratio (CSS would
  stretch); a row span's missing height goes to its last row; a page that cannot hold a table's header plus some body
  continues without the header.
- **Deliberate deviation from Chromium: no part of a table row that shows nothing where moving the row avoids it.**
  Chromium cuts a row wherever its cells can break, so a page may end with a part of a row that shows nothing (a row of
  empty padded blocks after other rows) or start with one (the rest of a row whose text ended on the page before; case
  `a3-mirror`). The engine cuts a row only where both parts show something, and otherwise moves the row to the next
  page whole; a cut where a part shows nothing is a last resort, taken only where the row cannot move (at the top of a
  page), so no table is refused for it (`Tracks/TableBreaks.cs`, `CutShows`). Where a row shows nothing over more than
  a page, the engine therefore needs a page more than Chromium (cases `a2-padded-blocks`, `a2-line-breaks`: the engine's
  page 2 is the blank part of the row, Chromium's page 1 ends with it). Reason: an empty strip of a row reads as a
  layout fault in a report, and the rule that avoids it costs nothing where the row can move. Pinned by `RowPartTests`
  (Fluent).
- **Text:** Unicode normalization before shaping follows HarfBuzz's default (compose a base and a mark the font has
  precomposed, decompose what it lacks), except: a base followed only by an enclosing mark (Me) is not decomposed
  (HarfBuzz counts Me as a mark); a cluster with a variation selector (U+FE00–U+FE0F) is normalized, where HarfBuzz
  leaves it alone (`handle_variation_selector_cluster`); the combining classes are the UCD's, not HarfBuzz's modified
  ones (`hb-unicode.hh`, `_hb_modified_combining_class`: Hebrew, Arabic shadda, Telugu, Thai, Tibetan), so marks of
  those scripts can come in another order; after a reorder that then composes, the clusters (which glyph carries which
  characters) differ while glyphs and positions agree; Unicode variation sequences (cmap format 14) are not read, the
  default glyph is drawn; the fallbacks for a missing space and for U+2011 are not made (the text is refused with
  `GlyphMissing`) (`Typesetting/Unicode/Normalization.cs`). A zero width joiner between a base and a mark: the shaper
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
| line ends | Line ends against Chromium's print: 3,667 paragraphs in Lato 75 pt, four alignments, widths around every change of Chromium's lines (`ChromiumLineEndTests`, data in the Typesetting tests' `Reference/Chromium`): every line break equal, every line extent within 0.06 pt. Each piece shaped again against HarfBuzz for that piece alone, the hyphen alone: 19,350 pieces, 0 differences (`LineReshapingReferenceTests`). The unsafe-to-break flags against HarfBuzz's: 3,004 clusters, 0 differences (`UnsafeToBreakReferenceTests`). | `UnitTests.Extensions.Documents.Typesetting/Reference/` |
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
