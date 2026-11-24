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
  `LayoutAlgorithmId`), `TextRun`/`CounterRun`, `InlineGroup` and `ImageNode`. `InlineGroup` is THE inline grouping:
  a part of an inline box's content (runs, images in the line, other groups) with a role, a link
  (`SemanticRole.Link(LinkTarget, description)`) or a span (`SemanticRole.Span`), its text style and a language
  (held, not yet written). Without a box it is transparent for layout (no margin, padding, border or line height of
  its own); with a box (`InlineGroup.Box`, an `InlineBoxStyle`: left and right margin, padding, border, background) it
  is an inline box that takes room on the line before its first character and after its last and paints its
  background and border (THE INLINE BOX STYLE); such a group may have no role, and then opens no structure element.
  Groups nest, one nesting level per group, a link never inside a link; a link may have a box, lie inside one and hold
  one, and in this version an image inside a group with a box is not inside a link as well. A link's target is a closed set (`Core/LinkTarget.cs`), today only an absolute URI of an
  accepted scheme (`Core/LinkUris.cs`, THE RULE FOR A LINK'S URI). Every node has an id (pre-order) and the source
  location of the call that made it. Roles (`Core/SemanticRole.cs`) are separate from layout: a box laid out as a
  table is not necessarily a table for a screen reader. Any front-end (code now, markup later) produces this model.
  The document also carries its layout profile (`DocumentModel/LayoutProfile.cs`): the policies its front-end chose
  where layout has a choice (see the policy table below).
- **Fragment tree** (`Layout/Fragment.cs`). The output of layout per page: boxes cut at page breaks, lines, text,
  images, table rules, and slices of content taller than a page, with positions relative to the parent. Internal to
  the layout layer, but serialised for tests.
- **Page model** (`PageModel/DrawItem.cs`, `PageDocument.cs`). What to draw, in absolute page coordinates: glyph runs
  (glyph ids, not text plus a font name), filled rectangles, images, link areas (`LinkArea`: the rectangle a reader
  clicks, one per line part of a link and one per image in it, which draws nothing and stands right inside its link's
  structure marker, outside every artifact), and markers — `StructureStart`/`StructureEnd`
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

The five algorithms are public, and a caller can hand one any break token: one it built, one changed with `with`, or one
that another box's layout returned. Each algorithm checks the token it is given, and a token it cannot have returned for
the box — another algorithm's kind, a child, line, row or cell the box does not have, a height shown that is negative,
not finite, or the whole height of the sliced image or line or more — is the error `Documents.Layout.InvalidBreakToken`,
never an exception; a token inside a child or a cell is checked by that child's algorithm, and a table's token does not
carry its grid over to another table (`BreakTokenCheckTests`). The token types keep their public constructors: a check is needed in any case, because a
caller can pass one box's token to another box, so closing the constructors would not close the hole. Pagination never
meets the error: it hands each box the token that box's own layout returned.

**The row of page-margin boxes** (`Layout/MarginBoxesLayout.cs`, id `margin-boxes`) lays out the generated boxes of one
page edge side by side, CSS Paged Media 3 §5.3.2: each child carries its place in the row (`BoxStyle.MarginBoxPlace`:
start, center, end), and a box that is not generated is no child. Without a centre box the start and end boxes share the
row by their max-content widths, or by max minus min when those do not fit; with one, the centre box shares the row with
an imaginary box twice as wide as the wider side box, is centred, and each side box gets half of the rest, also when the
other side is empty; a box alone takes the whole row; where all widths are zero, the row is shared by the number of boxes
each stands for (Chromium 159: two empty side boxes half each, an empty centre box beside an empty side box a third). The
row is only the content of a header or footer area with a fixed height, and each box is as tall as that height less its
own margins, its content placed by `vertical-align` (`ModelCheck` refuses a row anywhere else, a child without a place and
two at one place: `Documents.Input.Invalid.MarginBoxes`). Content taller than its box is drawn as Chromium draws it:
the box keeps its height and the body does not move, the content starts at the top of the box's content box under every
vertical alignment and runs on unclipped over the box's border, the body or past the sheet; the header area is painted
before the body, so the body lies over it, as in Chromium (cases `pm-tall-*`). Content wider than its box is refused at
the box (`Documents.Layout.MarginBoxOverflow`), also when not even the min-content widths fit the row, and so is a box
whose own margins, padding and border are taller than the row; Chromium draws wider content unclipped over the
neighbouring box or past the sheet — a known limit, pinned by `MarginBoxesLayoutTests` (at 12 pt "Left" beside a word
wider than the row: Chromium shrinks "Left" to 15.0 pt and draws both words over their boxes; case `pm-overflow`). Checked against the measured edges of Chromium's
rectangles at whole pixels: one edge is not reproduced ("Eeeeeee Eeeeeee" beside "Dd": 306.75 pt in Chromium, 307.13 pt
here, which paints at 307.5); the widths computed in 1/64 px would give Chromium's pixel there, which is not built.

**The seam for later front-ends.** The layout contract (a pure function from box, space and break token to fragment
and token) is what a later front-end — markup, a Typst-like language, a report definition — plugs into. Today the
engine ships five algorithms (flow, inline, tracks, image, margin-boxes) and registering another is internal
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
| font size rounding: the font size text is shaped at (the glyphs' advances, kerning, the place of a mark on its base) | `Exact`; `FlooredToPixelFractions` (Chromium: the size in CSS px floored to 1/100 px for kerning and every other positioning value, and that size truncated to 1/64 px for the glyphs' own advances, in single precision, the advances scaled in 16.16 fixed point as skrifa scales them; nothing rounded per glyph, so a line is narrower by a constant share: 0.098 % at 8 pt, 0.039 % at 10 pt, 0.071 % at 11 pt, none at 9, 10.5 and 12 pt). The font is drawn at its size under both; the back-ends place each glyph where layout put it (in PDF a TJ adjustment, as for kerning) | `Typesetting/ShapingSize.cs`, `Shaper.cs` (`InPoints`), `Layout/InlineLayout.cs` (`ShapingSizeOf`) | Blink `FontDescription::EffectiveFontSize` (`font_description.cc`, precision 100 in `font_cache_key.h`), `HarfBuzzFace::GetScaledFont`; skrifa `Size::fixed_linear_scale` (`instance.rs`); checked against Chromium's print (cases `ta-sizes`, `ta-px`, `ta-right`, `ta-marks`, every word within 0.01 pt; `ta-kerning-scale`: kerning at the floored size; `TextAdvanceTests`, `ShapingSizeTests`). The HTML front-end sets `FlooredToPixelFractions` |
| length rounding: which lengths are put on Chromium's layout unit of 1/64 CSS px | `Exact`; `SixtyFourthPixels` (Chromium: the width of each text item, the part of one run on one line, rounded up to 1/64 px, for placing what follows it, aligning the line and whether the line fits; the glyphs inside keep their advances, an image on the line is not a text item). The enum's documentation lists every place that follows the policy; today that is this one | `Typesetting/TextLine.cs` (`LineSegment.Width`), `GreedyLineBreaker.cs` (THE RULE FOR THE TEXT ITEM WIDTH), `Layout/InlineLayout.cs` (`BreakLines`) | Blink `ShapeResult::SnappedWidth` (`LayoutUnit::FromFloatCeil`) in `LineBreaker::HandleText` (`line_breaker.cc`); checked against Chromium's print (cases `ta-items`: every word within 0.0005 pt, where unrounded widths put the last word 0.176 pt early; `ta-items-right`; `ta-items-fit`: the last word wraps; `sk-size`; `TextAdvanceTests`, `TextItemWidthTests`). The HTML front-end sets `SixtyFourthPixels` |
| empty row parts: a cut of a table row where one part shows nothing | `Avoid` (such a cut is a last resort: the row moves whole where it can); `Allow` (Chromium: a row is cut wherever its cells can break) | `Tracks/TableBreaks.cs` (`Cut`, `CutShows`) | CSS leaves where a row breaks to the user agent; checked against Chromium's print (cases `a2-*`, `a3-mirror`) |
| body clip: what a page's body draws past its area (the page area less the header and footer areas) | `None`; `BodyArea` (Chromium: the body is drawn under a clip to its area, the page area's own size from its rounded start, `PageStyle.BodyClipInset`; the header and footer areas are not clipped) | `Layout/Paginator.cs` (the clip), `Layout/PagePainter.cs` (`PaintPage`) | Chromium's print (`tools/reference/chromium`, `visible.py`: one rectangle clip per page; `mf4-line-0`, `b2-split-row`, `pm-tall-over`); ISO 32000-1 §8.5.4 |
| cells beside slices: where the other cells of a table row go on, on a page that goes on with the slices of a piece in one of its cells | `GoOn` (from the top of the page, beside the slice); `Below` (Chromium: below the slice the page shows, so a page wholly covered by a slice shows nothing of them) | `Tracks/TableSegments.cs` (`BelowSlices`, `Measure`, `Slice`) | Chromium's print (cases `a4-image-beside-lines`, `a4-slice2-beside-lines`, `a4-slice-near-end-beside-lines`); CSS Fragmentation 3 §4.4 leaves sliced monolithic content to the user agent |

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
| bottom padding and border that do not fit below a box's complete content: the box breaks at its best inner breakpoint and keeps it, at the appeal of a kept break (above: a box or paragraph kept together then moves whole where a better break lies before it, cases `kb-n*`); without one it breaks before its bottom padding and border, which start the next page (a last resort, so a box lower on the page moves whole); that box ends right below its content, and so does every box around it whose page ends only because of that break, where a box whose page ends at any other break fills the page (cases `fr-pad-ancestor-*`, `fr-pad-then-sibling`, `fr-header-pad-*-36`); not in table cells | `Layout/SlicedBox.cs`, `FlowLayout.cs` | CSS Fragmentation 3 §4.1, §5.4; Chromium `FinishFragmentation`; checked against Chromium's print in block flow (cases `fr-pad-*`, `fr-border-*`, `a3-pad-*`) |
| where a page starts: a box that does not fit below margins, empty boxes or padding moves on; only content that starts the page is refused; the last-resort break | `Layout/ConstraintSpace.cs` (`IsAtPageStart`), `FlowLayout.cs`, `SlicedBox.cs`, `BreakAppeal.cs` (`LastResort`) | CSS Fragmentation 3 §4.1, §4.4, §5.2; checked against Chromium's print |
| margin collapsing | `Layout/MarginCollapsing.cs` | CSS 2.1 §8.3.1 |
| box slicing at page breaks | `Layout/SlicedBox.cs` | CSS Fragmentation 3 §5.2–§5.4 |
| inline layout, line boxes | `Layout/InlineLayout.cs`, `LineBoxes.cs` | CSS 2.1 §9.4.2, §10.8 |
| tables and rows of blocks | `Layout/Tracks/` (`TracksLayout.cs` lists the steps) | CSS 2.1 §17 (fixed and auto layout, collapsing borders), CSS Grid `fr` §7.2.4 / §11.7.1, CSS Fragmentation for row breaks |
| images | `Layout/ImageLayout.cs` | CSS 2.1 §10.3.2 (with a stated deviation for width + height) |
| page templates, page counters | `Layout/PageAreas.cs`, `Paginator.cs`, `PageContext.cs` | CSS Paged Media 3 §4.2.2, §5, §6.1 |
| links: an inline group with the link role is read through by inline layout (`Leaves`, an explicit stack); each line records its groups' parts as ranges of its children with their area (`LineFragment.Groups`, `LineGroupPart`); the painter opens the group's structure marker around each part and puts the link's areas first inside it; inside an artifact a link is only drawn. A link's area runs from the start of its first piece on the line to the end of its last (a space removed at the line end is outside it) and from its own font's ascent above the baseline to its descent below, whatever the runs inside it, and over the border box of every inline box inside it on the line (one area per line part, where Chromium writes one before the box, one for it and one after it: a listed difference); a link with a box of its own has that box's border box on the line as its area; under the body's clip an area is the part the clip shows (Chromium's rectangle of a box above the page area starts at its top); the edges are exact (not printed, so not rounded to Chromium's pixels). The PDF writes one link annotation per area (`/Rect`, `/F 4`, `/Border [0 0 0]`, `/Contents` the description, `/A` a URI action, no appearance stream, which veraPDF rule 6.3.3-1 exempts a link from), listed in the page's `/Annots`; tagged, a `Link` element holds the link's content and an object reference (OBJR) to each annotation, and each annotation's `/StructParent` key, after the pages' keys, names that element in the parent tree. The raster back-end draws nothing for an area. A link inside the document (`LinkTarget.Node`) leads to a box or an inline group of the body outside every artifact (THE RULE FOR A LINK INSIDE THE DOCUMENT, `ModelCheck`); the painter writes one `Anchor` for each such node at its first fragment (a box's border box, a group's first line part: THE RULE FOR A LINK TARGET'S PLACE), the page-model rules check one anchor per node and one for every link to a node, and the PDF writes the annotation's `/Dest [page /XYZ null top null]`, the anchor's page and top, left and zoom unchanged (THE RULE FOR A LINK'S DESTINATION); veraPDF has no rule on destinations | `DocumentModel/Node.cs` (`InlineGroup`), `ModelCheck.cs` (`Targets`), `LinkDescription.cs`, `Core/LinkTarget.cs`, `LinkUris.cs`, `Layout/InlineLayout.cs` (`Leaves`), `LineBoxes.cs` (THE RULE FOR A LINK'S AREA), `PagePainter.cs` (`Line`), `PageModel/DrawItem.cs` (`LinkArea`), `Render.Pdf/LinkAnnotations.cs`, `Tagging/StructureTree.cs`, `StructureObjects.cs` | ISO 32000-1 §12.5.2 (Table 164), §12.5.3 (Table 165), §12.5.6.5 (Table 173), §12.6.4.7, §14.7.4.3 (Table 325), §14.7.4.4, §14.8.4.4.2; ISO 19005-3 §6.3 (veraPDF rules 6.3.2-1, 6.3.2-2, 6.3.3-1), §6.5.1; ISO 14289-1 §7.18.1, §7.18.3, §7.18.5 (veraPDF rules 7.18.1-2, 7.18.3-1, 7.18.5-1, 7.18.5-2); Chromium 159's print (one rectangle per line part, from the link's own font); checked by `LinkAreaTests`, `LinkOutputTests` (read back with PdfPig, veraPDF PDF/A-3a and PDF/UA-1) |
| what is content and what is an artifact (a repeated header row and every slice of a line of text after the first are pagination artifacts; every slice of an image is content of its figure) | `Layout/PagePainter.cs` | ISO 32000-1 §14.8.2.2 |
| clips in PDF: `q x y w h re W n` … `Q` between marked-content sequences, never across one | `Render.Pdf/PageContentStream.cs` (`Clips`) | ISO 32000-1 §8.4.2, §8.5.4 |
| line breaking | `Typesetting/LineBreaking/UnicodeLineBreaking.cs`, `GreedyLineBreaker.cs` | UAX #14 rev. 57 |
| text that does not wrap (`text-wrap-mode: nowrap`, CSS Text 4 §5.1): no soft wrap opportunity is taken after a character of such a run, so its minimum width is its whole line; a forced break still breaks; an image in a line breaks by the mode of the element it lies in, carried by its box's own text style (`BoxStyle.Text`), else by its paragraph's; a line wider than its box overflows it start-aligned (nothing is scaled) | `Typesetting/LineBreaking/BreakableSegments.cs` (`Split`, THE RULE FOR NOWRAP), `Layout/InlineLayout.cs` (`WhiteSpaceOf`) | CSS Text 4 §5.1; CSS Text 3 §5.5, §6.1; Blink `LineBreaker::ShouldAutoWrap`, `HandleOpenTag`, `HandleCloseTag`; checked against Chromium's print (cases `ws-nowrap`, `ws-pre`, `ws-border`, `ws-overflow`, `ws-image`, `ws-soft`, `WhiteSpaceTests`) |
| white space per run (`white-space-collapse`, CSS Text 4): collapsed, preserved (`preserve`, `break-spaces`), or only its line feeds preserved, in one pass over the paragraph; a preserved line feed becomes the forced break U+2028; a preserved space does not collapse with a collapsible space beside it. At a line's ends a collapsible space is removed; a preserved space stays at the start and hangs at the end: not measured for fit, of no width at a soft wrap, and keeping the room that is left before a forced break and at the paragraph's end; inside a justified line every preserved space widens. Under `break-spaces` a space never hangs: it takes its room at a line's end, a line may break after every space (also between two), and a space that ends a justified line gets no share. A paragraph of preserved spaces, or of a preserved line feed, has a line | `Typesetting/WhiteSpaceCollapsing.cs`, `GreedyLineBreaker.cs` (`Hang`), `LineBreaking/BreakableSegments.cs` (`LineEndRules`), `LineJustification.cs`, `Layout/InlineLayout.cs` (`HasLineBoxes`) | CSS Text 3 §4.1.1, §4.1.2, §4.1.3, §8.2 (conditional hanging); CSS Text 4 §4.1 (`white-space-collapse`); Blink `InlineItemsBuilder::AppendPreserveWhitespace`, `AppendPreserveNewline`, `LineInfo::ComputeTrailingSpaceWidth`; checked against Chromium's print (cases `ws-*`, `WhiteSpaceTests`) |
| shaping | `Typesetting/Shaper.cs`, `Shaping/` | OpenType GSUB/GPOS/GDEF, legacy kern |
| Unicode normalization before shaping: decompose, reorder marks, recompose, as far as the font allows | `Typesetting/Unicode/Normalization.cs`, data `UnicodeNormalizationData.g.cs` | UAX #15; HarfBuzz's normalizer (hb-ot-shape-normalize.cc) |
| shaping at a break and where a line ends: Where a line ends and how wide it is follows Chromium 159 (Blink `ShapingLineBreaker::ShapeLine`, `LineBreaker`): the paragraph is shaped once and a line keeps the paragraph's glyphs; the start of a wrapped line and the end of a line are shaped again on their own from and to the nearest place that is safe to break, when that end is not safe (the shaper sets HarfBuzz's `HB_GLYPH_FLAG_UNSAFE_TO_BREAK` as HarfBuzz does: a pair whose values are all zero marks nothing, a lookup marks the clusters after its first glyph's own). The hyphen shown at a soft hyphen is shaped alone and never kerned with the letter before it. At a line end at a collapsible space, left-aligned text keeps the paragraph's glyphs (the last letter keeps its kerning with the space, and the line is measured with it); right-aligned, centred and justified text, and a line whose space at the end lies in a run with a text decoration, shape the end again before the space (case `dl-end`: with the space outside the underlined element the kerning stays). Before a forced break and at the paragraph's end the end is shaped again in every alignment, as measured in Chromium 159's print, which is the reference for where lines end and wins over the reading of its source (that reading says start-aligned text keeps the end): `xx AV <br>To AV` fits "xx AV" on one line only from 252 px in all four alignments, and the right-aligned line shows the unkerned 5,037 units; `xx AV To AV ` at 254 px is `xx AV | To | AV` in all four. When a line, shaped as it is, does not fit, the break opportunity before it is taken. So a line can differ from shaping its text alone: a ligature cut by a break is shaped as its half alone, as measured: `ff` + U+200B + `fi` ends `f f` (1,402 units in Chromium's print), not the ff ligature HarfBuzz gives "ff" alone; each piece shaped again equals HarfBuzz for that piece. | `Typesetting/GreedyLineBreaker.cs`, `ShapedGlyph.UnsafeToBreak`, `Shaper.ShapeLinePiece`, `LineShapingBudget`, `Layout/InlineLayout.cs` (`LineEndAtSpace`) | Blink `ShapingLineBreaker::ShapeLine`, `LineBreaker`; HarfBuzz's unsafe-to-break flag (hb-buffer.h); checked against Chromium's print (`ChromiumLineEndTests`) |
| an image in a line of text (an atomic inline, CSS Display 3 Appendix A): a box laid out by the image algorithm among the runs of an inline box. It is the object replacement character U+FFFC in the paragraph's text: white-space collapsing keeps a space on each side of it, it ends the shaping group before it, and it is one glyph of its margin box's width that is never shaped. A line may break before and after it whatever is beside it, also beside a no-break space or a joiner, between letters and before closing punctuation (Chromium's rule, which UAX #14 alone does not give for U+FFFC; beside GL, WJ and ZWJ other than U+00A0 a deliberate deviation from CSS Text 3 §5.5, under "Deliberate deviations from CSS"). Its margin box sits with its bottom edge on the baseline and makes the line taller when it reaches above the strut; it is no expansion opportunity of a justified line; its outer min-content and max-content widths count in the paragraph's intrinsic widths. It is laid out at its shrink-to-fit width, so an image wider than the line overflows it (unlike a block image, which is scaled down to the available width). Only the image algorithm is accepted there, and only with the initial page break properties (`Documents.Input.Unsupported.AtomicInline`) | `Layout/InlineLayout.cs` (`ShapeRuns`, `LaidOut`, `Measured`), `Layout/LineBoxes.cs` (`Build`, `AtomicInline`), `Typesetting/LineBreaking/BreakableSegments.cs` (`WithAtomicInlines`), `DocumentModel/ModelCheck.cs` (`AtomicInlines`), `Fluent/Elements.cs` (`InlineImage`) | CSS 2.1 §10.8 (the margin box), §10.8.1 (`vertical-align: baseline`), §10.3.9; CSS Text 3 §5.5; CSS Display 3 Appendix A (atomic inline); Blink `InlineItemsBuilder::AppendAtomicInline`, `LineBreaker::CanBreakAfterAtomicInline`, `LineBreaker::CanBreakAfter`, `LogicalBoxFragment::BaselineMetrics`; checked against Chromium's print (cases `ii-*`, `InlineImageTests`) |
| an inline box (CSS 2.1 §8, §10.8; THE INLINE BOX STYLE, THE RULE FOR AN INLINE BOX'S EDGES IN A LINE, THE RULE FOR PAINTING AN INLINE BOX): an inline group with a box (`InlineGroup.Box`). Its start edge (left margin, border and padding) and end edge are items of the line without text: the start edge stays with the group's first character and the end edge with its last, an edge is no break opportunity, no space, no expansion opportunity of a justified line, white-space collapsing does not see it, and an edge that takes room ends the shaping of the text before it (no kerning pair across it; one of a background alone keeps the pair). Vertically the box is its font's content area (ascent and descent of the group's own font) with its padding and border outside it; it does not make the line taller. Its background and then its border are painted where its part of the line opens, after the line's content before it, as a layout artifact. Under `InlineBoxSnapping.WholePixels` (the HTML profile) each painted edge lies on the nearest whole pixel of the page and the text inside is drawn from the box's top rounded to a whole pixel of the page, after pagination, where each line's place on its page is known (THE RULE FOR AN INLINE BOX ON WHOLE PIXELS). An empty box alone in a paragraph without text makes a line of the paragraph's line height when an edge takes room (CSS 2.1 §9.4.2), none with a background alone; one with only a vertical padding or border is refused. A box over two or more lines is sliced (`box-decoration-break: slice`, THE RULE FOR AN INLINE BOX OVER LINES): one part per line, each painted from its first to its last piece on the line, with the top and bottom sides on every part, the left side and the start edge's room on the first part only and the right side and the end edge's room on the last only; a box whose first character is a space that ends a line keeps its start edge on that line (its padding painted alone), a space removed at a line end carries no background; a box broken between two of its lines over a page break is a part on each page (THE RULE FOR AN INLINE BOX OVER A PAGE BREAK). Refused at layout, at the group: a box over two lines of a justified paragraph, one that breaks at a soft hyphen, and one on a line that starts a later page and reaches above its page area (Chromium paints that one on both pages). A link inside a box, a box inside a link and a link with a box of its own are drawn (a link's text is underlined, its edges are not: Chromium's print); an image inside a box is drawn on the baseline as any inline image, the box keeps its font's content area (cases `ib-image-*`); an image inside both a box and a link is refused | `Layout/InlineLayout.cs` (`Entries`, `ShapeRuns`), `Layout/LineBoxes.cs` (`Edge`, `LineGroups`), `Layout/InlineBoxPixels.cs`, `Layout/PagePainter.cs` (`Line`, `InlineBox`), `Typesetting/LineBreaking/BreakableSegments.cs` (`GluedEdges`), `DocumentModel/Node.cs` (`InlineBoxStyle`), `DocumentModel/ModelCheck.cs` (`Group`, `InlineBox`), `Html/Boxes/HtmlBoxGenerator.cs` (`InlineBoxOf`) | CSS 2.1 §8, §10.6.1, §10.8; CSS Text 3 §7.3; Blink `ShouldBreakShapingBeforeBox`; checked against Chromium's print (cases `ib-*`, `InlineBoxTests`, `InlineBoxEdgeBreakTests`, `InlineBoxMappingTests`) |
| text decoration lines (`TextStyle.Decorations`, the decorations applied to a run, outermost decorating box first): each is one filled rectangle per piece of the run on a line, a layout artifact, never content. Thickness a tenth of the decorating box's font size, at least one pixel; an underline's top a gap below the baseline (half the thickness rounded up, at least one pixel), one straight line across text of other sizes; a line-through's top 2A/3 − T/2 below the piece's text top and an overline's T above it (A the decorating font's ascent, T the thickness), so these two follow each piece's text; under whole-pixel metrics the thickness is floored and each top rounded to a whole pixel. A line runs across the piece's advances, widened justified spaces and hanging preserved spaces included; a collapsible space trimmed at a line end is not decorated; an image gets no line. Underlines and overlines are painted before the piece's glyphs, line-throughs after them. The lines change no line height and no break; a line end at a space of a decorated run is shaped again before the space, as in right-aligned text (case `dl-end`) | `Layout/TextDecorations.cs` (THE RULE FOR TEXT DECORATION LINES), `Layout/InkGaps.cs` (THE RULE FOR THE GAPS AROUND A GLYPH'S INK), `Layout/LineBoxes.cs` (`Build`), `Layout/InlineLayout.cs` (`WithDecorationFonts`, `LineEndAtSpace`), `Typesetting/ShapedGlyph.cs` (`Overhang`), `DocumentModel/ModelCheck.cs` (`Decorations`), `Fluent/TextModifiers.cs` (`Underline`, `LineThrough`, `Overline`) | CSS Text Decoration 3 §2, §2.5, §5.1, §5.2; CSS 2.1 Appendix E.2 step 7.2.1.4.1.1; Blink `TextDecorationInfo::ResolveDecorationAt`, `ComputeUnderlineLineData`, `ComputeLineThroughLineData`, `ComputeOverlineLineData`, `ComputeDecorationThickness`, `TextDecorationOffset::ComputeUnderlineOffsetAuto`, `DecorationLinePainter::Paint` (`SnapYAxis`), `TextFragmentPainter::Paint`, `NeedsAccurateEndPosition`; checked against Chromium's print (cases `dl-*`, `TextDecorationTests`) |
| the gaps an underline or overline leaves around a glyph's ink (`TextStyle.DecorationSkipInk`, `text-decoration-skip-ink`, inherited, initial `Auto`; the value of the text decides, not that of the decorating box): under `Auto` each underline and overline of a piece is the pieces of its rectangle left between the gaps, each its own rectangle in the line's place in the paint order; a line-through is never cut. One glyph's gap: the band is the line's computed rectangle (its painted top, its unrounded thickness T) inset by half a pixel above and below; the gap reaches from the leftmost to the rightmost x of the glyph's outline inside the band: where a segment crosses the band's top or bottom edge, and every point of a segment strictly inside it, a curve's control point included (as Skia counts it, so the gap can be wider than the ink); then widened by min(T, 13 px) on each side. The glyphs are the piece's own, in its own font and size; the band is the decorating box's line (case `sk-size`). A line is cut by its own gaps over its whole height, and by the gaps of the underlines and overlines painted before it on the same piece over the rows those lines' clips reach (each clip half a pixel past its line's computed rectangle; cases `sk-nest`, `sk-part`), written as rectangles: the line split along x at every gap end, in each column its rows minus the rows of the clips whose gaps cover it, neighbouring columns of equal rows one rectangle; a glyph never cuts the line of another piece (case `sk-touch`); spaces, an image, a soft hyphen's hyphen outside the band leave no gap | `Layout/InkGaps.cs` (THE RULE FOR THE GAPS AROUND A GLYPH'S INK; `InkOutlines`), `Layout/TextDecorations.cs` (`Of`), `Layout/LineBoxes.cs` (`Build`), `Layout/InlineLayout.cs` (`StackLines`), `Core/TextDecorationSkipInk.cs`, `DocumentModel/TextStyle.cs` (`DecorationSkipInk`), `Fluent/TextModifiers.cs` (`TextDecorationSkipInk`) | CSS Text Decoration 4 §2.10.4; Blink `TextPainter::ClipDecorationLine`, `TextDecorationPainter::PaintUnderOrOverLineDecorations`, `Font::GetTextIntercepts`; Skia `SkTextBlob::getIntercepts`, `SkGlyph::ensureIntercepts`, `calculate_path_gap`, `SkBezierQuad::IntersectWithHorizontalLine`; checked against Chromium's print (cases `sk-*`, `dl-skip`, `SkipInkTests`) |
| shaping across runs: the runs of a paragraph that are set in the same font (family, size, weight and style; the colour and the line height do not count) are shaped as one text, so a kerning pair or a ligature across their border is kept; a run without text joins the text it lies in, and a run that starts with U+200C starts a new one. Each run gets back the glyphs whose cluster starts in it: a ligature across a border belongs to the run with its first character and is drawn in that run's colour, as Chromium splits the shared result. Chromium's print maps such a ligature to the first run's characters only (`f<span>i</span>x` reads "fx"); the engine's glyph keeps every character it shows, for copying, search and tagged PDF. The start and end of a wrapped line are shaped again within their run (Chromium shapes them again within one item), so a wrapped line's first word loses a kerning pair across its run border, as in Chromium | `Layout/InlineLayout.cs` (`ShapingGroups`, `ShapeGroup`), `Typesetting/LineBreaking/BreakableSegments.cs` (`IsSafe`, `NextSafe`, `PreviousSafe`) | CSS Text 3 §7.3; Blink `InlineNode::ShapeText`, `ShouldBreakShapingBeforeText`, `ShapingLineBreaker::ShapeLine`; checked against Chromium's print (cases `sr-*`, `ShapingAcrossRunsTests`) |
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
| front-end (`Fluent/InputChecks.cs`, `InputLimits.cs`) | the author's input: lengths, borders, columns, spans, table shape, page breaks only where they can apply, metadata text (XML characters), language tags, media types, unique attachment names, nesting depth, a layout profile with defined policy values, a link's URI (THE RULE FOR A LINK'S URI, `Core/LinkUris.cs`) | `Documents.Input.*` |
| layout (`Layout/LayoutErrors.cs`) | only what cannot be laid out: content that does not fit, content that would need more than 100 slices (one piece) or 1 000 (one document), columns that do not fit, a page-count fixpoint that does not settle | `Documents.Layout.*` |
| back-end contract, one rule set for both back-ends (`PageModel/PageModelRules.cs`; each back-end reports a breach under its own code), plus the options (`Render.Pdf/PdfRenderer.cs`) | a page model from any producer: metadata and at least one page, each of positive finite size, nothing missing, finite positions and sizes (and edges) within the range of a PDF real, glyphs of their font with a text, enum values, spans within the table model's limits, table structure that nests, a table of rows or of row groups with at most one head, which comes first, one role per node, attachments with a name, a media type and a unique name; a link role with a target THE RULE FOR A LINK'S URI accepts and a description that is not blank; link areas right inside their own link's structure marker, outside every artifact, at most 8 191 per page; markers (structure, artifact, clip) that nest by the one nesting rule, with at most 27 clips one inside the other (`MarkerNesting`); render options with a profile and a file identifier of 1 to 32 767 bytes | `Documents.Pdf.Write.InvalidPageModel`, `InvalidOptions`, `InvalidFileIdentifier`; while the structure tree is built (`Render.Pdf/Tagging/StructureTree.cs`), a node under two parents or a decorative node in the structure: `Documents.Pdf.Write.InvalidStructure` |
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
   the same way: every glyph a lookup looks at and every subtable it tries (`Shaping/ShapingBudget.cs`). Breaking a paragraph costs a fixed number of steps per break opportunity and, per line, at most `GreedyLineBreaker.MaxEarlierBreaksTried` + 2 shapings of the line's ends, each at most the line's own text. All pieces of one paragraph share one `LineShapingBudget`: at most 16 characters shaped again per character of the paragraph (at least 4,096), and together the shaping limits of the paragraph itself (max(65,536, 1,024 × n) operations); past either the paragraph is refused with `Documents.Typesetting.TooMuchWork`. Marking glyphs unsafe to break skips what a lookup's walk marked already (a run of n marks on one base costs n steps) and is charged to the shaping budget; the normalizer finds every cluster's end in one pass and does not sort a run of more than 32 marks. Counted in `TextCostTests` (two sizes per input). An image in a line of text (an atomic inline) is measured and laid out each time its paragraph's lines are broken at a width: at most twice per layout of the paragraph (a second time when the paragraph takes the earlier break it reported), and so again on each page the paragraph continues on, at no limit to its height, and is one item of the line breaker with a fixed advance that is never shaped; how deep such boxes go is bounded by the nesting limit, and a box among the runs is one level. White space is collapsed in one pass over the paragraph whatever each run's mode; finding each character's run for the wrap modes is one more pass. Each item's wrap mode is read once, from its run, or for an image from its own box or else the paragraph's style, never by looking for a text run beside it (a paragraph of many images and no text is counted in `ImageParagraphCostTests`, two sizes). A line of text that does not wrap (`nowrap`) costs no more than any line: its soft wrap opportunities are dropped before the breaker sees them, so it adds no candidates, it is measured once, and its two ends are shaped again at most once each, from the same shared budget; hanging the preserved spaces at a line's end is one pass over that line's glyphs. Text decorations cost one pass over the glyphs of each decorated piece of a line and one rectangle per decoration per piece; a run carries at most `ModelLimits.MaxDecorations` (16) decorations, so the rectangles are bounded by the text (counted in `TextDecorationCostTests`, two sizes). Skipping ink costs, per underline or overline of a piece, one pass over the outline of each of the piece's glyphs (its segments), and the gaps joined in one pass after one sort; a line is also cut by the gaps of the at most 15 lines before it on the piece, over the rows their clips reach: one pass over the gap ends of the at most 16 lines after one sort, so a line has at most twice their gaps plus one columns, and a column at most 16 intervals of rows (one cut per clip), so a line's rectangles are at most 16 × (2 × gaps + 1) (counted in `SkipInkCostTests`: rectangles and cuts of a line cut over part of its height, two sizes). Each outline is decoded once per font and glyph within one paragraph's layout (`InkOutlines`), however many lines and decorations use it (counted in `SkipInkCostTests`: segments visited and outlines decoded, two sizes).
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
| a link's URI | 32 767 characters of printable ASCII (the longest string a PDF/A file may hold, veraPDF rule 6.1.13-3) | `Core/LinkUris.cs` | `Documents.Input.Invalid.Link`, from the front-end and the model check; in a page model the back-end's code, `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel` |
| link areas on one page | 8 191, a fixed bound on the annotations one page writes (one object and one `/Annots` entry each), far more than a page of text can show | `PageModel/PageModelRules.cs` | `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel` |
| nodes the links inside one document lead to | 65 536 in the model, and as many anchors in a page model | `DocumentModel/ModelCheck.cs`, `PageModel/PageModelRules.cs` | `Documents.Input.Invalid.Link`; in a page model the back-end's code, `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel` |
| an anchor's name in the code front-end | 1 024 characters | `Fluent/InternalLinks.cs` | `Documents.Input.Invalid.Link` |
| an author or a description from an HTML `meta` | 4 096 UTF-16 code units each, after stripping white space at both ends | `Html/HtmlMetadata.cs` | `Documents.Html.Unsupported.Metadata` (at the `meta`; the document has no author or description) |
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
attachments with `/AF`, and a link annotation per link area, with its description in every profile and, tagged, its
object reference in the Link element. A plain file is written exactly as before profiles existed (same object numbers, no marked
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
(Core, Html.Parsing, Css) does style resolution, and box generation with DocumentModel and Fonts (next section).

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
  left-to-right text. Box generation (next section) aligns text with the `-webkit-*` values (a descendant block is as
  wide as its container while sizes are refused, so aligning it changes nothing), refuses `inset` and `outset` borders
  that have a width, `overflow: hidden | clip` and `aspect-ratio` with a ratio on a block box, and draws `isolate` as
  `normal`.
- The code front-end keeps its own cascade (`Fluent/Cascade.cs`: own style, shared style, table cell
  defaults, inherited) with its own initial values and its own list of what inherits, beside the property registry
  the HTML front-end reads. The facts both hold, the initial font size (12pt) and the heading sizes (2, 1.5, 1.17, 1,
  0.83 and 0.67 em), are tied by `UnitTests.Extensions.Documents.Architecture/FrontEndFactsTests.cs`; the rest is not
  tied: box generation reads the registry for the HTML front-end only, and a later package ties the code front-end to
  it.

## HTML front-end (stage 4: box generation)

```
StyledHtmlDocument ──HtmlBoxGenerator.Generate──▶ HtmlBoxes (StyledDocument + FontSet) ──DocumentLayout.Run──▶ PageDocument
HTML text ──HtmlRendering.ToPages / ToPdf / ToPng (composition package)──▶ the same, in one call
```

Every `HtmlRendering` entry returns its output beside the diagnostics of parsing, style resolution and box generation
(`HtmlRendered<T>`), so under `UnsupportedPolicy.Warn` a caller sees each refusal and what was left out or changed for
it.

Box generation produces the same `StyledDocument` the code front-end produces; layout knows no HTML. It sets
Chromium's policies (`HtmlBoxGenerator.ChromiumProfile`: `MonolithicOverflow.Slice`, `EmptyRowParts.Allow`,
`FontMetricRounding.WholePixels`, `SplitLineHalves.AtLeastOnePixel`, `TableEndOverflow.ContinuesOnNextPage`,
`BodyClip.BodyArea`, `CellsBesideSlices.Below`, `CutRowAlignment.TopOnEveryPage`, `RepeatedHeaderRows.WhereContentIsLaidOut`, `SliceRestEnd.AtLineCentre`, `InlineBoxSnapping.WholePixels`). It draws `display: block`, `inline` and `none`, tables with collapsed borders, text,
the box properties of block boxes and the page size and margins; every other computed value is refused by name.

Where each rule lives:

| Rule | Place |
| --- | --- |
| block, inline and none; anonymous block boxes (CSS 2.1 §9.2.1.1); white space between blocks makes no box (§9.2.2.1); roles (h1–h6, p); the walk with an explicit stack; ids in pre-order; the nesting limit | `Html/Boxes/HtmlBoxGenerator.cs` |
| white-space collapsing across inline element boundaries (CSS Text 3 §4.1.1) | inline layout (`Typesetting/WhiteSpaceCollapsing.cs`), for both front-ends: box generation hands the text on as written, and each inline element starts with an empty run in its style, which carries its line height (CSS 2.1 §10.8.1) |
| which computed values are drawn, and every refusal | `Html/Boxes/BoxProperties.cs`, one row per registry longhand |
| font matching: `@font-face` (TrueType, through the resolver, loaded when first needed), then the caller's fonts; first family with a face; exact weight and style | `Html/Boxes/FontMatcher.cs` |
| `@page`: size and margins of rules without a selector, Chromium's rounding of the page | `Html/Boxes/PageRules.cs` |
| line height in Chromium's layout unit (a length rounded to 1/64 px, a number times the font size floored to it) | `HtmlBoxGenerator.LineHeightOf` |
| margins and padding in Chromium's layout unit (cut toward zero to 1/64 px) | `HtmlBoxGenerator.LayoutUnits` |
| tables: the parts, their structure, column sizes, spans; what the model's table cannot show | `Html/Boxes/TableBoxes.cs`; the values in `Html/Boxes/TableProperties.cs` |
| links inside the document: an `a href="#…"` leads to the element the HTML Standard's "indicated part of the document" names (an empty fragment and `top` in any case: the top of the document, the root's box; else the first element with that `id`, else the first `a` with that `name`, then both again with the fragment percent-decoded), also under a `base`; its node is the element's box, its link group, or for another inline element a span group made only for it; the links get their nodes when the tree is built (`LinkTargets`), so a link may come before its target; a name without a target is refused at the link | `Html/Boxes/HtmlBoxGenerator.Targets.cs` (THE RULE FOR A LINK INSIDE AN HTML DOCUMENT), `Html/LinkUrls.cs` (`Fragment`, `PercentDecoded`), `DocumentModel/LinkTargets.cs` |
| languages: an element's language (HTML Standard §3.2.6.2: `xml:lang` in the XML namespace, else `lang`, else the parent's) where it differs from its parent's, without regard to case, goes on its box, or for an inline element on a span group made for it (an `a href`: its link group); written as given when THE RULE FOR A LANGUAGE TAG accepts it (BCP 47 well-formed, at most 64 characters), else, and for the empty tag, refused at the start tag (`Documents.Html.Unsupported.Language`) and not written: no language is guessed | `Html/Boxes/HtmlBoxGenerator.Targets.cs` (THE RULE FOR AN ELEMENT'S LANGUAGE IN HTML), `Core/LanguageTags.cs`, `Render.Pdf/Tagging/StructureObjects.cs` (`Languages`) |
| links: an `a` with `href` inside a line is an inline group with the link role around its content, its empty run first (so the line height is as without the link); the URI by THE RULE FOR A LINK'S URL IN HTML (white space removed as the URL Standard's basic URL parser removes it, a relative URL resolved by RFC 3986 §5.2 only against an `http` or `https` base, the scheme in lower case, the C0 controls, space, `"`, `<`, `>`, U+007F and every character beyond ASCII percent-encoded as UTF-8, the shared part of the URL Standard's percent-encode sets, §1.3), then THE RULE FOR A LINK'S URI of the model, the only rule on schemes; the description by THE RULE FOR A LINK'S DESCRIPTION (`title`, text, an image's `alt`, the URI); refusals at the start tag (`Documents.Html.Unsupported.Link`) | `Html/LinkUrls.cs`, `Html/Boxes/HtmlBoxGenerator.Links.cs`; `Core/LinkUris.cs`, `DocumentModel/LinkDescription.cs` |
| table header cells: a `th`'s scope by THE RULE FOR A HEADER CELL'S SCOPE IN HTML (`scope` row or rowgroup heads the row, col or colgroup the column, in any ASCII case; else a `th` in a `thead` or in a row of `th` only heads its column, any other its row); the `headers` attribute is not read | `Html/Boxes/HtmlBoxGenerator.HeaderScopes.cs`; `Core/TableHeaderScope.cs` |
| document metadata: the author and the description from the first `meta` with that `name` (any ASCII case) and a content not empty after stripping, by THE RULE FOR A DOCUMENT'S METADATA IN HTML; a content past 4 096 characters or with a control character or one XML does not allow refused at the `meta` (`Documents.Html.Unsupported.Metadata`); no other `meta` name is read | `Html/HtmlMetadata.cs` |

**The refusal table.** `BoxProperties.Rows` holds one row per longhand of the property registry: what is drawn, and
where a value has no effect, the condition. A value is refused (`Documents.Html.Unsupported.Value`, at the element's
start tag, with the property and the value) only where it would change the print. In short:

| Properties | Drawn | Refused |
| --- | --- | --- |
| `display` | `block`, `inline`, `none`, `table` and its parts | `table-caption`; every other type (lists, …: the next packages) |
| `border-collapse` | `collapse` on a table | `separate` (the initial value, and HTML's) |
| table width, column widths, `table-layout` | the table at `auto` and `100%`; lengths of column elements; `table-layout: fixed` at width `100%` (at width `auto` the table is laid out automatically, as Chromium does) | percentages on column elements, any width on a cell or a column group, other table widths: see **Tables** |
| margins | lengths of 0 or more; `auto` as 0 (width is auto); on an inline box the vertical ones have no effect | negative, percentage, calculated |
| padding, border widths | lengths on a block box and on an inline box (border widths in whole pixels, as the style sheet snaps them) | percentages |
| border styles and colours | `none`, `hidden`, `solid`; colours of alpha 1; anything where the width is 0 | `inset`, `outset`; other alphas |
| `background-color` | alpha 1 on a block box and on an inline box; transparent anywhere | on the root or body element (`CanvasBackground`: Chromium paints it on the page, the model has no page background); other alphas |
| `width`, `height`, `min-width`, `max-width`, `aspect-ratio`, `overflow-*` | their initial value; any on an inline box (they do not apply); on an image a positive length for `width` and `height`, `auto` with a ratio (the natural one) and any `overflow` | other values on a block box |
| `box-sizing`, `border-spacing`, `empty-cells`, `caption-side`, list properties, outline properties, `color-scheme`, `print-color-adjust`, `unicode-bidi`, font synthesis | every value: no effect here (sizes auto, collapsed borders, no caption, no list item, outline-style none, light print, backgrounds printed, left-to-right, no synthesis) | — |
| `color` | alpha 1; any on an element without text of its own | other alphas where text is drawn |
| `white-space` and its longhands `white-space-collapse`, `text-wrap-mode` | `collapse`, `preserve`, `preserve-breaks`, `break-spaces`, `wrap`, `nowrap`, as the text style of the element's text (`TextStyle.WhiteSpaceCollapse`, `TextStyle.TextWrapMode`; the text goes to the model as written, line feeds and all) | `preserve-spaces`, `discard` (by the registry); a tab where spaces are preserved (`preserve`, `break-spaces`: tab stops are not built), at its text with its offset; white space between block boxes that its mode preserves |
| `text-decoration-line`, `text-decoration-color`, `text-decoration-style`, `text-decoration-skip-ink` | every line and combination, applied to the element's text and the text of every element inside it, through block boxes, not into an image: the parent's lines, then the element's own in the order underline, overline, line-through, each in the element's font and its decoration colour (`currentcolor` its `color`; `TextStyle.Decorations`); `solid`; `auto`, `none` from the element the text lies in (`TextStyle.DecorationSkipInk`) | a decoration colour of another alpha than 1; a text with more than 16 lines (at the start tag of the element it lies in); `blink`, `spelling-error`, `grammar-error`, other styles, a thickness, an underline offset or position other than `auto` and `skip-ink: all` (refused by the registry) |
| `vertical-align` | `baseline`; any on a block box | other values on an inline box |
| `box-decoration-break` | `slice`; `clone` on a box without border, padding or background (on an inline box, without horizontal margins either) | `clone` where it would draw |
| an inline box (an inline element with a background, border, padding or horizontal margin) | a group with a box (THE RULE FOR AN INLINE BOX IN HTML), on one line or sliced over lines and pages, also an empty one (alone in a paragraph too), nested ones, `mark` (its yellow background), `code` and `kbd` with a font family that resolves, links with a box of their own, inside a box or around one, images inside a box | a box over two lines of a justified paragraph or at a soft hyphen, and one on a line that starts a later page and reaches above its page area (`Documents.Input.Unsupported.InlineBox`, at layout, at the element); an image inside both a box and a link (`Documents.Html.Unsupported.InlineBox`, at the image) and an empty link with a box of its own (`Documents.Html.Unsupported.Link`, at the link); an empty box alone in a paragraph with a vertical padding or border and no left or right edge; rounded corners (by the registry) |
| `font-kerning` | `auto`, `normal` (Chromium kerns with both: cases `h-kerning-*`) | `none` (refused by the registry) |
| break properties, `orphans`, `widows`, `text-align`, font family, size, weight and style, `line-height` | every value (`avoid-page` as `avoid`; `break-after: page` as a forced break before the next box, none after the last box, as Chromium prints it) but a font size of 0 | a font size of 0; a family without a face (`Font`) |
| the properties supported only at their initial value | the initial value | any other (the registry already refuses it) |

Elements whose rendering no property says are refused by their own rule (`Documents.Html.Unsupported.Rendering`):
`wbr`, `q`. A `br` is the model's forced line break, the line separator U+2028, in its own text style (a block-level
`br` is refused); Chromium adds no line for a `br` that ends a block's content and an empty line for one that starts it
(cases `hb-*`). A block-level `img` is a box of the image algorithm: its file from a `data:` URL or through the resource
resolver against the document's base URL, its computed width and height (the attributes are presentational hints), its
alt text as the figure's description and an empty alt as decorative (case `hi-attributes`); an image that does not come
or does not decode is refused (`Rendering`), and so is a width and a height in another ratio than the image's own, which
Chromium stretches the image to and the image algorithm does not (`Value`, case `hi-stretch`). An `img` inside a line of text (`display: inline`) is the same image box
among the runs, at its place, with its margins, padding, border and background, and the text style of the element it lies
in, so it wraps as that element's text (cases `hi-inline`, `ii-baseline`); `vertical-align` other than `baseline` and a
break property other than `auto` on it are refused at its start tag, and `display: inline-block` by its value. The others are refused through their values: lists by `display`, a table without collapsed borders by
`border-collapse`, `hr` by its `inset` border, `abbr` with a title by its dotted underline, `sub` and `sup` by `vertical-align`
(in style resolution). A block box inside an inline box is refused (`BlockInInline`), and so is a tree deeper than
`ModelLimits.MaxNestingDepth` (`TooDeep`, under both policies). A character in the text that is not drawn is refused
at its text, once per text with the first such character, its offset and their count (`Character`): a control character other than tab, line feed and carriage return (Chromium draws form feed
and U+0085 as a missing glyph), the line and paragraph separators (Chromium prints a space that does not collapse), a
noncharacter, half of a surrogate pair (cases `h-char-*`). Under `UnsupportedPolicy.Warn` a refused value is
replaced by the model's initial value of what it sets, a refused display type is laid out as a block box, and an
element refused by its own rule is left out (`HtmlBoxGenerator`, remarks).

**Tables.** A table element with collapsed borders (CSS 2.1 §17.6.2) becomes the model's table: a tracks box, its row
groups (`thead` the header group, repeated as the model repeats it; `tbody` a row group), their rows and the cells, at
one level of the model, with the structure roles of the elements (header rows without body rows are the table's
body, for PDF/UA). Borders of the table, groups, rows and cells go to their boxes, and layout resolves the conflicts;
cells keep padding, background and `vertical-align` `top`, `middle` or `bottom`; internal parts have no margins and
rows and groups no padding (§17.5), the table no padding (§17.6.2); `colspan` and `rowspan` are read by the rules for
non-negative integers, a row span ends with its group (HTML Standard §4.9.12.1); at a page break between rows the
engine splits the grid line there at its centre, its upper half ending one page and its lower half starting the next
(`BoxDecorationBreak.Slice`), each half a whole pixel at least, as Chromium prints it (`SplitLineHalves.AtLeastOnePixel`;
"Table breaks" under Known limits). A footer group (`tfoot`) is drawn after the other row groups wherever it stands in the source; only
the first is the table's footer, a later one is a row group in its place (CSS 2.1 §17.2; Chromium's
`TableGroupedChildren`; cases `tf-after`, `tf-before`, `tf-head`, `tf-two`, `tf-top-border`, equal). The model's table
has no footer group, so its rows are the last body rows: the tagged PDF has them in a `TBody`, not a `TFoot`, and
Chromium's repetition of the footer on every page is not drawn (Known limits). Column sizes, as Chromium lays them out: in the automatic layout a
column element's length is the column's preferred width (`TrackSize.Preferred`) and a column without one is sized by
its content (`TrackSize.Auto`); in the fixed layout, which Chromium takes only for a table whose width is not `auto`
(`ComputedStyle::IsFixedTableLayout`; case `tl-fixed-width-auto`, where a 30 pt column element under
`table-layout: fixed` and width `auto` gets its content's width), a column element's length is an exact width
(`TrackSize.Fixed`) and the
columns without one share what is left (fill columns of weight 1). A table of width `auto` is as wide as its columns
need (`BoxWidth.Auto`), one of width `100%` takes its container's width (`BoxWidth.Stretch`). A limit of the fixed
layout: a fill column is never narrower than its content's min-content width, where Chromium lets content overflow
it; the two agree while the content fits its column (cases `tl-fixed-fill-wide-word` and `a4-line-beside-lines`, a
200 pt word in the first of two columns of 137.625 pt in Chromium, listed; `HtmlListedDifferenceTests`: the columns'
widths are the whole difference). Percent columns (a `col` of `width: <percentage>`, also over its `span`) are drawn in
the fixed layout of a table of width `100%`, as Chromium sizes them (`SynchronizeAssignableTableInlineSizeAndColumnsFixed`):
a percentage is of the width the columns share, the table's width less its outer half borders in the collapsing model
(case `tp-outer-border`); percent columns alone grow or shrink in proportion to fill that width, whatever their sum
(`tp-sum-100`, `tp-sum-below`, `tp-sum-above`, `tp-thirds`); beside columns without a width each keeps its percentage
and those columns share the rest equally (`tp-beside-auto`); a span gives each of its columns the percentage
(`tp-col-span`). Each is a fill column whose weight is its share, so layout divides the width exactly where Chromium cuts
each share to 1/64 px and gives the last column the remainder (less than 0.012 pt per column). Refused at the column
element (`Value`): a percent column in the automatic layout, beside a column of a length (Chromium widens the length
columns to what the percentages leave, `tp-beside-length`), and percentages of 100 or more beside a column without a
width (Chromium gives that column no width); a percent column, like a fill column, is never narrower than its
content's min-content width (the limit above). Refused (`Documents.Html.Unsupported.Table`,
or `Value` for a computed value): a table part outside its parent part, which CSS would wrap in anonymous table boxes
(§17.2.1); a table part on another element than its HTML element; captions, a header group after a row
group; a group or table without rows or cells; overlapping cells; `rowspan=0`; more column elements than columns;
a column in which no cell starts; the percent columns named above, a width on a cell or a column group (`tp-colgroup`), a table width other than
`auto` and `100%`, a horizontal margin on a table (but `auto` at width `100%`, where it is 0); backgrounds of the table, groups, rows and columns; column borders. Under
`Warn` the refused part is left out, refused columns share the width equally, overlapping spans become single cells,
and a table without cells is an empty block.

**The page.** Without an `@page` rule the page is the caller's, as Chromium prints on the paper of the print call:
`HtmlStyleOptions.Page`, by default A4 with no margins; media queries and viewport units see its page area, `size:
auto` takes its size and a bare `portrait` or `landscape` turns it (the case test passes US Letter, 612 × 792 pt, no
margins, which the reference tool prints on: cases `h-default-page`, `h-page-size-auto`, `h-page-landscape`,
`h-page-portrait`, `h-page-default-media`). Media queries see that page also where an `@page` rule sets another
size, as Chromium evaluates them (cases `h-media-page-size-narrow`, `h-media-page-size-wide`). A root element with `display: none` generates no box: one empty page of
the caller's page, whatever the `@page` rules say (case `h-root-display-none`). An `@page` rule without a selector gives `size` and the margins;
the declarations that reach the page-margin boxes and the margin rules are the page-context cascade's
(`PageContextCascade`, `Css.Unsupported.PageRule`; the page context inherits from the root element, `font-family`
included, cases `pm-inherit` and `pm-inherit-family`); any other declaration, an invalid value and the size and margins of
`@page :first` are refused (`PageRule`). A generated margin box gives every page a header area as high as the top margin
and a footer area as high as the bottom one, each a row of the margin-boxes algorithm that starts where Chromium starts
it, at the smaller of the left margin cut to 1/64 px and the page area's start (`PageArea.StartInMargin`; cases `pm-*`,
`pm-start-25mm`, `pm-start-12pt`); the model's top and bottom margins are then 0. An image in a margin box's `content`
(`url()`, or `image-set()` of one URL and its resolution) is an image box among the runs of its line, as an `img` in a
line of text is, at its natural size divided by the resolution, its URL resolved against the style sheet that holds the
`content` declaration (CSS Values 4 §4.5.1; cases `pm-image-*`). A margin box's margins and padding are laid out in
Chromium's unit of 1/64 px, as an element's are, and none is put on a whole pixel; Chromium paints the top of each of its
lines on the nearest whole pixel, as in the body (cases `pm-len-*`). The top row is listed from left to right and the
bottom row from right to left, the order Chromium paints them in whatever the order of their rules (case
`pm-paint-order`); the row keeps that order and places each box by its place. Box generation makes
the page as Chromium lays it out and prints it (`PageRules`, which cites the Chromium functions): the page area starts
at the left and top margins rounded to a whole CSS pixel, half up, and its width and height are what the margins
leave, rounded up to a whole pixel; the sheet is the page size rounded to whole points, then to whole 1/300 inch (A4
prints 594.96 × 841.92 pt). The right and bottom margins are what the sheet leaves beside the page area, 0 where the
area reaches past the sheet (cases `h-page-size-*`, `h-page-area-*`, `h-page-margin-*`).

**The status file.** `tools/reference/chromium/html-status.txt` has one line per reference case, written by
`UnitTests.Extensions.Documents.Html/Boxes/CaseStatusTests.cs` (with `DOCUMENTS_UPDATE_HTML_STATUS=1`): the case runs
through the whole HTML path unchanged and its pages are compared with every file measured for the case
(`HtmlCases`, where each comparison names its file and bound; `HtmlCases.Bound` is 0.1 pt):

- `measured/<case>.print.txt`, the print read back from the PDF: the same pages of the same size, the same words on
  each page in the same order, each word's start, end and font size within 0.1 pt, its baseline within 0.1 pt, and
  its colour and face (the PostScript name of the font, which names the font file and with it the weight and the
  style); the same area painted in each colour by the rectangles, filled or stroked (each pixel takes the colour of
  the last rectangle over it; a stroked rectangle is read back as the four bands it paints), and the same image areas
  in drawing order, each edge within 0.1 pt;
- `<case>.lines.txt`, the filled rectangles read back by a second script, with the bands of the print's strokes: the
  same painted area, colour not compared (the file has none);
- `<case>.words.txt`, the words read back by poppler: the same words inside the page, each start and end within
  0.1 pt, and the line box poppler gives it (the baseline less the face's ascender, plus its descender) within 0.1 pt;
- `<case>.boxes.txt`, the boxes measured in the page: the height of each table cell and of each box named by an element
  id within 0.1 pt, and its width within 0.1 pt. The tool measures the boxes in a browser window as wide as the print's
  page area (the width of the clip around each page's body, rounded up to a whole pixel as Chromium lays a page out), so
  a height that depends on the width is the print's. The window is one layout without page breaks, so the file holds
  each box whole: a box the engine prints in one part is compared on its page (and each copy in a repeated header row),
  a box it cuts over pages is compared in the engine's layout of the same boxes on one page as tall as the model allows,
  which must stay one page; the parts of a cut box are what `print.txt`, `lines.txt` and `words.txt` compare. A box
  named by its id is found by its place among the boxes of the role its tag gives on its source line.
- `<case>.links.txt`, only for a print that has link annotations, the links read back by `links.py` (each annotation's
  page, `/Rect` and target, in the order of the page's `/Annots`): the engine's link areas (`LinkArea`, each with the
  target of its link) the same number in the same order, each on the same page, every edge within one CSS pixel
  (`HtmlCases.LinkBound`, 0.75 pt: Chromium writes each edge at a whole pixel, the engine at its exact place), and the
  same target: a URI compared whole; a link inside the document on the same page, the top of the engine's anchor within
  one CSS pixel (`HtmlCases.DestinationBound`, 0.75 pt) of Chromium's top plus the page's top margin (Chromium writes a
  destination in page-area coordinates, one margin above the target, at a whole pixel). A case without the file has no
  link, and the engine must have none either.

Both sides are compared as they show. A PDF paints each fill, stroke and image only inside the clips around it
(ISO 32000-1 §8.5.4), and Chromium draws each page's body under one clip, the page area (its start rounded to a whole
pixel, its own size, not rounded up as layout takes it), while the page-margin boxes are drawn before it, outside any
clip. It still draws what passes that clip: a grid line past the page end, a cut cell's side lines or an image slice
drawn again offset up by the page area's height. So the read-backs (`visible.py`, used by `positions.py`,
`rectangles.py` and `ink.py`) write each rectangle and image area cut to the clips it is drawn under, and leave out what
lies wholly outside them; the engine's pages are read through their page-model clips by the same rule
(`PrintedPages.Engine`, `PrintedPages.Visible`). Words are compared whole: no word of a measured print lies wholly
outside its clip. The engine clips the body to its area under `LayoutProfile.BodyClip` (`BodyArea`, the HTML front-end's
value; `None`, the code front-end's default, clips nothing); the header and footer areas, where the HTML front-end puts
the page-margin boxes, are not clipped, as Chromium draws tall margin-box content over the body (`pm-tall-over`).

Baselines and the edges of rectangles and images are compared at the whole pixel where Chromium paints them (every
rectangle and image edge in the committed prints is a whole pixel), so a difference smaller than half a pixel there
can stay hidden; x positions, font sizes and box heights are compared as they are. A face in a print that is not a
font file the case loads fails `MeasuredFaceTests`: Chromium prints with a fallback font, without a word, when a font
file does not load.

A line reads `<case> equal`; `<case> refused <code> <property: value or element> at <line>:<column>` (the first
refusal, counted as one by its exact code: the next packages' work list; for content layout cannot place on any page,
`Documents.Layout.DoesNotFit`, the value is the room it had in points and the place only the line, the one a box
keeps; for an inline box layout does not draw where it stands, `Documents.Input.Unsupported.InlineBox`, the line and
column of the element that made it); `<case> listed <test>: <difference>` for a
difference of layout already listed and pinned by that test of the code front-end, which must stay the same
difference; or `<case> open <difference>` for a known difference, engine against Chromium, that is not fixed yet (no
pull request while one is open). The status is the line's second field. A case that lays out and differs otherwise
fails the test, and so does a status that changes without the file; writing the file anew makes no case `open` that
the committed file does not already name. To read it: `cut -d' ' -f2 html-status.txt | sort | uniq -c` gives the
picture; grep a code or a property for the cases it holds back.

The whole documents (`tools/reference/chromium/documents`: the timesheet on one page, a normal month and a long
month, and the invoice) are compared by the same code, against their own files: `documents/measured/` and the status
file `documents/html-status.txt`, one line per document, pinned by the same test and written by the same variable.

## Known limits (all stages)

Each is documented next to its code as well.

- **Content wider than the page area (HTML):** Chromium shrinks the whole print by the widest page's overflow, up to a
  factor of 1.5, and clips past it (`CalculateOverflowShrinkForPrinting`, pagination_utils.cc); the engine has no page
  scale and lets the content stick out (cases `h-wide-word-little`, `h-wide-word-much` and `hw-nowrap`, a line that does
  not wrap, listed; `HtmlListedDifferenceTests`: below the limit Chromium's print taken back through its scale is the
  engine's).
- **The page area past the sheet (HTML):** Chromium rounds the page area up to whole pixels even where it then reaches
  past the printed sheet, as on A4 without margins, the default page (794 × 1123 px, 595.5 × 842.25 pt, on a sheet of
  594.96 × 841.92 pt); the model holds no page area larger than its sheet, so the engine's ends at the sheet: 0.54 pt
  narrower and 0.33 pt lower (cases `h-page-a4-zero-*`, listed; `HtmlListedDifferenceTests`). A right margin of 0.53 pt
  and a bottom margin of 0.39 pt or more remove the difference (case `h-page-a4-margin-px`).
- **A ligature across the border of an inline element:** the engine's text keeps every character the glyph
  shows, Chromium's print maps it to the first run's characters only (`f<span>i</span>x` reads "fx"; the rule for shaping
  across runs above; case `sr-same-font`, listed; `HtmlListedDifferenceTests`).
- **A repeated table footer (HTML):** Chromium repeats a footer group at the end of every page its table is on when the
  group is at most a quarter of the page area and does not break inside (`break-inside: avoid`, which the user-agent
  style sheet gives `tfoot`), as it repeats a header group (`TableLayoutAlgorithm::Layout`); the model's table has no
  footer group, so the engine draws the footer once, after the last body row (case `tf-pages`, listed;
  `HtmlListedDifferenceTests`). With `break-inside: auto` on the footer group Chromium does not repeat it either
  (case `tf-pages-auto`), and a table on one page prints the same in both (case `tf-after`).
- **A link in a repeated table header row (HTML):** the engine writes the link's annotation from the header row's first
  copy only; the copies on later pages are artifacts, which carry no annotation (ISO 14289-1 §7.18.1: an annotation is
  tagged). Chromium 159 writes the link twice on every page that shows the header, at the same rectangle (case
  `lk-header-row`, listed; `HtmlListedDifferenceTests`). The print is the same.
- **A link around an inline box (HTML):** the engine writes one link area per line part of the link, over its text and
  the boxes inside it: the link is one thing to click, described once, and a link annotation's `/Rect` may be any
  rectangle (ISO 32000-1 §12.5.6.5). Chromium 159 writes one rectangle for the link's text before the box, one at the
  box's border box and one for the text after it (case `ib-link-span-inside`: 51.75..81.75, 81.75..109.5 and
  109.5..140.25 pt on the first line, where the engine's area is 51.504..140.18 pt; listed; `HtmlListedDifferenceTests`).
  Each of the engine's areas is the rectangle around Chromium's of its line. The print is the same.
- **What follows the row of an image sliced in a table (HTML):** Chromium lays an image taller than the page area out
  whole on the page where it starts and draws it in slices on the following pages. It ends the image's row on the next
  page, as tall as the rest of the image, also past the page end. In a row of two or more cells the next row starts
  below that end, or at the top of the following page, drawn over the slices still to come (a 408 pt image: the next
  row at 24.75 pt on page 4, over the last slice, which ends at 60 pt; a 588 pt image: on page 4, over the third of four
  slices). In a one-column table the next row starts below the last slice, as in the engine (case
  `a4-image-single-cell`, equal). After any table, what follows does not wait for the slices: it starts at the top of the
  following page, over them (24.75 pt). The engine starts what follows below the last slice in every shape (72.75 pt), so
  no text is drawn over an image; that place is the whole difference (cases `a4-image-in-cell`,
  `a4-slice4-cell-row-after`, `a4-row-image-row`, `a4-two-image-rows`, listed; `HtmlListedDifferenceTests`). In a table
  inside a cell (`a4-image-nested-table`, listed with them) the engine also draws the inner table's bottom line and the
  outer row's line one below the other (60 to 61.5 pt) where Chromium draws them as one (60 to 60.75 pt): Chromium ends
  the outer row as tall as the rest of the image as well, which comes with drawing over the slices; the test states
  the 0.75 pt and finds no other difference. The other cells of the
  image's row go on below the slices, as Chromium prints them (`CellsBesideSlices.Below`).
- **A repeated header row over a slice (HTML):** on a later page where a table repeats its header rows and goes on with
  the slices of a piece taller than the page area (a row still to come), Chromium still moves the piece up a whole page
  area, so the slice starts at the top of the page area (12 pt), and draws the header over it: 16.5 pt of the piece are
  never shown. The engine draws the header, then the slice below it (28.125 pt), so the piece shows whole and once, and
  what follows the slices in the table starts that much lower; where Chromium starts the next row in an empty part below
  the header on the page of the last slice, that row goes on 0.375 pt higher on the next page than the engine's, which
  starts it there. That is the whole difference (cases `a4-header-image-360-image`, `a4-header-row-image-row`,
  `a4-header-image-row-image`, `a4-header-two-image-rows`, listed; `HtmlListedDifferenceTests`). A writer does not want
  a header over an image, so the engine does not copy it.
- **Percent columns (HTML):** drawn only in the fixed layout of a table of width `100%`, alone or beside columns
  without a width (cases `tp-*`); in the automatic layout, beside a column of a length, and at 100 % or more beside a
  column without a width they are refused, as are widths on cells and column groups. A percent column is never
  narrower than its content's min-content width, where Chromium lets the content overflow (as for a fill column,
  `tl-fixed-fill-wide-word`). The percentages are of the table's width less its outer half borders; the engine and
  Chromium both take that half border from the widest line at the table's edge in any row, on every page (cases
  `te-left-first`, `te-left-later`, `te-right-first`, `te-right-later`, `te-table-border`, `te-break`), and each cell
  is inset by half of its own line. Percent columns in a table whose rows have outer borders of different widths are
  not measured (the `tp-*` cases have one border width).
- **A face that only a page-margin box uses (HTML):** Chromium has not loaded an `@font-face` face that no body text
  uses when it prints the margin boxes, and prints that text in its fallback serif face; the engine draws the face
  (case `pm-family-unused`, the same in three prints, listed; `HtmlListedDifferenceTests`). Used in the body too, the
  face prints in both (case `pm-inherit-family`).
- **A zero width non-joiner in the text:** Chromium's print keeps U+200C in the text of its PDF, in the word it
  lies in; the engine's PDF does not (case `sr-zwnj`, listed; `HtmlListedDifferenceTests`).

- **Layout:** negative margins (refused as unsupported); `vertical-align: baseline`
  in cells; column gaps and column styles; the separated border model; diagonal joins of differently coloured
  borders; a keep rule moves a break into an earlier table only between its rows, not to a cut inside a row (a cut
  depends on where the page ends, so a table offers no early break there); bottom padding and a bottom border taller
  than the page area overflow the page they start (as in Chromium) when the box ends the document, and are refused
  (`Documents.Layout.DoesNotFit`) when content follows them; a table cell whose content does not fit under
  the repeated header and, on a page without the header, does not fit with its last child's bottom margin and the
  cell's bottom padding is refused, where Chromium prints it (case `fr-header-cell-margin`); a table header taller than
  the page is refused (`Documents.Layout.DoesNotFit`; Chromium cuts it like a body row); a table row taller than the page
  only through its cells' padding is refused (Chromium prints it on a fresh page and lets the padding overflow; case
  `fr-old-row-taller-by-padding`; through the HTML front-end the engine cuts that row on the first page as a last
  resort, which then shows only the table's top line and the cell's sides, and prints "w" on the second with its padding
  passing to the third, as Chromium does: Chromium's blank first page, with the 0.75 pt top line at the top of the
  second, is the whole difference, `HtmlListedDifferenceTests`); a table
  row without cells is refused as input (`Documents.Input.Invalid.Table`; Chromium lays it out with no height) — each
  pinned with Chromium's pages in `ListedDifferenceTests`; under `EmptyRowParts.Avoid` and `MonolithicOverflow.Refuse`,
  the defaults, a table row that continues from the page before, whose rest fills the page area with only the half of
  its grid line below past the page end, is refused (`Documents.Layout.DoesNotFit`) when another row follows it (under
  `Allow` the line passes the page end, as Chromium prints it; under `Slice` the rest is cut at the deepest place;
  `RowEndTests`, `A_continued_row_whose_lines_fill_the_next_page_is_refused_under_Avoid`); through the HTML path, a
  table part that does not fit the page and cannot be broken is refused (`Documents.Layout.DoesNotFit`, with the room
  it had and the line of its element) where Chromium prints it (cases `a2-cells-end-page-top`, where Chromium leaves
  the first page blank and prints the row whole on the next, `fr-header-cell-margin` and `fr-header-pad-box-50`; the
  status file pins each as refused); a paragraph with a page counter that breaks across pages is re-broken with each page's
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
- **Text decoration:** only solid lines of the automatic thickness and position exist: `text-decoration-style` other than `solid`, `text-decoration-thickness` and `text-underline-offset` other than `auto`, `text-underline-position` other than `auto`, `text-emphasis` and `text-shadow` are not in the model. `text-decoration-skip-ink: all` is not in the model (`auto` and `none` are). Under whole-pixel metrics a line's top is rounded to a whole pixel counted from its line box's top, so it lies a whole number of pixels from its text's baseline; Chromium paints each baseline at a whole pixel of the page and its lines from there. Where line tops lie on whole pixels the two are equal (every case but one); where they do not, each line lies the same distance from its baseline in both and moves with the engine's unrounded baseline, by at most half a pixel (case `dl-fraction`, 17 of 18 rows; pinned by `TextDecorationTests.Where_a_line_top_is_off_the_whole_pixel_each_line_lies_from_its_baseline_as_in_Chromium_and_moves_with_the_unrounded_baseline`).
- **Against Chromium:** at most font sizes Chromium's text is narrower than the font's advances, by a share that
  depends on the size (0.039 % at 10 pt, 0.071 % at 11 pt, more at small sizes; none at 9, 10.5 and 12 pt), so a line
  that fits in Chromium by less than that share of its text width can break one word earlier in the engine. By
  default (`FontSizeRounding.Exact`) the engine keeps the font's exact advances; `FontSizeRounding.FlooredToPixelFractions`
  shapes at Chromium's two sizes, and `LengthRounding.SixtyFourthPixels` rounds the width of each text item up to
  1/64 px as Chromium's line breaker does. The HTML front-end sets both, and every word of the `ta-*` cases is where
  Chromium prints it within 0.01 pt. The code front-end keeps both exact. The reference cases that pin lengths use sizes Chromium keeps exact
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
  more than the page holds: 7 of 1,792 measured documents; `InlineLayout.BreakWhereFull`; cases `fr-moved-para-widows-4`, `fr-moved-para-widows-3-control`). Under `CutRowAlignment.RestAlignedInItsPart`, the code front-end's
  default, the rest of a cut cell aligned `middle` or `bottom` is aligned in the rest of its row (Chromium puts the content of
  every cell of a cut row at the top of its part on every page, which `TopOnEveryPage`, the HTML front-end's value, follows:
  cases `fr-old-cut-cell-*`, `fr-cut-row-*`, `CutRowAlignmentTests`); a margin dropped at a row cut does not stay in the row's height
  (Chromium keeps the uncut height: the rest of the row is 12 pt taller; case `fr-old-margin-at-row-cut`, listed
  through the HTML front-end too, `HtmlListedDifferenceTests`, where that height is the whole difference). Where a
  cell's lines all fit and only its bottom padding or grid line does not, at the top of a page the engine cuts the row
  as a last resort, where Chromium leaves the first page blank and prints the row whole on the next (cases
  `fr-f4-padding-page-start`, listed through the HTML front-end too, where the engine's first page is Chromium's
  second without its last line, and `a2-cells-end-page-top`, refused by the HTML path); a row that starts a later page,
  after a break between rows, and is taller than the page area only through its cells' padding is cut before its last
  line, where Chromium prints all its lines on that page and lets the padding overflow (cases
  `fr-row-later-page-padding-overflows`, `fr-row-later-page-padding-overflows-two-cells`). Under
  `TableEndOverflow.ContinuesOnNextPage`, the HTML front-end's value, such a row and a row that continues from the page
  before keep their lines and their padding continues on the next page, as Chromium prints it, where every cell of the
  row passes the page end with content on the page (cases `fr-continued-pad-past-*`,
  `fr-row-later-page-padding-overflows`, `a4-continued-cell-pad-bottom-30`; `TableEndOverflowTests`). Still cut so: a row
  with a cell that ends above the page end, whose whole bottom padding Chromium gives the next page (13.5 pt in
  `-two-cells`), and a row below a repeated header row (`a4-continued-cell-pad-bottom-30-under-header`, below).
  Padding that passes by more than the page area takes the whole next page and the rest the page after, as in Chromium;
  without a row after the table Chromium then starts what follows 0.75 pt below the page's top, over the padding, where
  the engine starts it below the padding (case `fr-continued-pad-past-taller-than-page`, open).
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
  stretch); a row span's missing height goes to its last row (Chromium spreads it over the spanned rows: with an image
  sliced in a cell that spans two rows, case `a4-image-rowspan`, the engine keeps the second row on the first page and
  starts the next row below the last slice, where Chromium puts the second row beside the first slice and draws the
  next one over the second slice; listed, `HtmlListedDifferenceTests`); a page that cannot hold a table's header plus some body
  continues without the header. A line breaks before and after an image in it also next to a character of the Unicode
  classes GL, WJ or ZWJ (a word joiner, a zero width joiner, a narrow no-break space), where CSS Text 3 §5.5 asks for
  no break ("with the exception of U+00A0 NO-BREAK SPACE, there must be no soft wrap opportunity between atomic inlines
  and adjacent characters belonging to the Unicode GL, WJ, or ZWJ line breaking classes"): the engine breaks as the
  reference does, Chromium 159 (`LineBreaker::CanBreakAfterAtomicInline` and `CanBreakAfter` break there; case
  `ii-joiners`).
  Where nested decorating boxes each draw an underline and an overline, the underline and the overline of one box are
  painted together, outermost box first, where CSS 2.1 Appendix E (step 7.2.1.4.1.1) paints every underline, in tree order,
  before every overline: the engine paints as the reference does, Chromium 159 (`TextDecorationPainter::PaintUnderOrOverLineDecorations`
  loops over the applied decorations and paints each one's underline, then its overline; `TextDecorations`).
- **A column with a given width in the automatic table layout follows Chromium, not CSS Tables 3,** for its minimum
  (`TrackSize.Preferred`). CSS Tables 3 §3.8.2 gives a table-column the outer min-content width "max(min-width,
  width)", so a `col` length would be a minimum and a table too narrow for its lengths would overflow. Chromium
  (`table_layout_algorithm_types.cc`, `TableTypes::CreateColumn`: the column's minimum is its `min-width`, its width
  only its maximum) shrinks such a column towards its cells' min-content instead (case `tw-pref`, `squeeze`: a 120pt
  column printed 96.492pt wide in a 150pt table). The engine follows the measured browser: the faithful-subset rule
  takes Chromium as the reference. The maximum agrees in both: the larger of the width and the cells' min-content
  (§3.8.2, "max(min-width, width, min-content width, min(max-width, width))").
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
  page area is less by the room that header takes, as Chromium reserves it for the rows below it
  (`ConstraintSpaceBuilder::ReserveSpaceInFragmentainer`): on the outer table's first page the header, on its later
  pages the header and the outer half of the outer table's top grid line, which the repeated header draws whole there
  (`TableLayoutAlgorithm::GenerateFragment`; `TablePage.Reserved`). On a page area of 180 pt below an outer header of
  one line a nested header of 39.75 pt is repeated and one of 42.75 pt is not, in both (`a6-nested-under-header-h2400`,
  `a6-nested-under-header-h2700`, `a6-nested-under-header-h2700-n16`; 42.75 pt is less than a quarter of 180 pt and
  more than a quarter of 180 pt less the outer header). The limit is 41.0625 pt for a nested table that starts on the
  outer table's first page (`a6-nested-first-h2525`, `-h253125`: 41.0 and 41.0625 pt, both repeated) and 40.96875 pt
  for one that starts on its second page (`a6-nested-late-h2521875`, `-h2525`, `-h253125`, `-h2700`: 40.96875 pt
  repeated, 41.0, 41.0625 and 42.75 pt not), also where the outer table clones its lines at a break
  (`a6-nested-late-clone-h2525`); with 6 pt grid lines the later-page limit is 39.0 pt (`a6-nested-late6-h1800`,
  `-h1850`), and below two outer headers each takes its share, 36.9375 pt (`a6-nested-late-d3-h2100`, `-h2125`; pages
  and words equal to Chromium's under every profile, `HeaderRepeatTests`). A nested header of 45.0 pt in a cell with 12 pt padding is a
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
  starts on the next page. Where that row ends the table, what passed is a policy (`LayoutProfile.TableEndOverflow`):
  under `ContinuesOnNextPage`, the HTML front-end's value, it continues on the next page and takes its height there
  (and so does the part of a split line's upper half that passed, at a break between rows, limit (iii) below),
  as Chromium prints it: the next page starts with a part of the table as tall as what passed, painted with the grid
  lines that passed where part of a cell's border box passed, empty where only the table's outer half of its bottom
  line passed, and on a page of its own when the table ends the document (`a2-row-end`, `a2-row-end-after`,
  `a2-row-end-6pt`, `a2-row-end-6pt-after`, `a2-row-end-6pt-part-after`, `a2-row-end-first-row-half-*`;
  `TableEndOverflowTests`). Chromium draws those lines again offset up by the page area's height and clips the page to
  its area, so its print does not show what it draws in the top margin; the engine clips the body the same way
  (`LayoutProfile.BodyClip`, below), and the comparison reads both as they show. Under `EndsOnPage`, the code front-end's default, the table ends on the page. The limits of that default,
  against Chromium:
  (i) Chromium draws the overflowing line again at the top of the next page; when the table ends the document, that is
  a page that holds only the line (`a2-row-end`: 3 pages in Chromium, 2 in the engine). Where only the outer half of
  the line passes, in a table that starts the page, that last page in Chromium shows nothing at all
  (`a2-row-end-first-row-half-alone`: 2 pages in Chromium, 1 in the engine; `RowEndTests`).
  (ii) Content after the table starts higher than in Chromium, by the width of that line: at the top of the next page
  in the engine, below the line drawn again in Chromium (a paragraph's baseline at 24.00 pt against 24.75 with a 0.75 pt
  line, against 30.00 with a 6 pt line; `a2-row-end-after`, `a2-row-end-6pt-after`).
  (iii) At the break between the first row and the next, inside a cell that spans both, the engine draws the upper half
  of the line at the end of the page (192 to 192.375) and nothing at the top of the next, keeping the half's room, where
  Chromium lets the whole line pass the page end and draws it again at the top of the next page; content after it is
  0.375 pt higher (1.125 pt below the top of the page against 1.5; `a2-row-end-span`, `a2-row-end-span-after`). Under
  `ContinuesOnNextPage` the part of the upper half that passed the page end continues too: the next page lays its rows
  out below it and draws it, and content after it is where Chromium prints it.
  (iv) A table that itself starts at the top of a page with such a first row: Chromium prints it in three ways. Where
  the content ends within half the grid line of the page end, so that only the outer half of the line below passes,
  it places the row whole; the engine does the same under `Allow` (`TablePage.PassesOnlyByItsLineBelow`: the row up to
  the middle of its line below fits), with Chromium's pages and words in the six measured places, a table at the top of
  the page, in a block, after an empty block, after a forced break, on page 2 after text and beside a second column,
  and with a second row after the first, where the page breaks between the rows and the line is split there (limit
  (iii)) (`a2-row-end-first-row-half-*`, a cell of 11 lines below 13.875 pt top padding: `x0`..`x10`, then `below` or
  `after`; `RowEndTests`), and for a table at the top of a table cell, below only the cell's top inset
  (`a2-row-end-first-row-half-in-cell`). Under `EndsOnPage` content after such a table starts higher than in Chromium,
  as in limit (ii). Where the content ends at the page end, Chromium
  leaves that page empty and prints the row whole on the next one (`a2-row-end-first-row`, 11 lines below 14.25 pt top
  padding, then a row "after": a blank page, `x0`..`x10`, `after`), or cuts the row between its lines where they are a
  hair taller than the page area in its unit of 1/64 pixel (`a2-row-end-first-row-cut`, 12 lines of 14.9375 pt:
  `x0`..`x9`, then `x10 x11 after`). The engine prints neither of these two and refuses the document under `Allow`
  (`Documents.Layout.DoesNotFit`); both belong with the slicing of borders at page breaks. Under `Avoid` the engine
  cuts such a row as a last resort at the top of a page, in all three shapes (listed above, `fr-f4-padding-page-start`;
  `RowEndTests`, `A_table_that_starts_the_page_with_a_first_row_that_fits_but_for_its_line_below_is_refused_under_Allow`).
  With nothing after the table, Chromium ends the document with a second page that shows nothing, and so does the
  engine under `ContinuesOnNextPage`; under `EndsOnPage` it has one page (`a2-row-end-first-row-half-alone`,
  `a2-row-end-first-row-half-6pt-alone`; limit (i)).
  The rule is not for a table that starts lower on the page: below a paragraph such a first row moves to the next
  page with its table, as in Chromium (`a2-row-end-below-paragraph`). Nor for a table that continues below an outer
  table's repeated header row, which does not start the page: under `Avoid` the row is cut below the header as Chromium
  cuts it (`a2-row-end-nested-under-header`; under `Allow` too, as the row, whose 14.25 pt of top padding do not fit
  page 1, moves to page 2 whole). (v) With 13.875 pt of top padding, under `Allow` that page goes on without the outer
  header, where the row starts the page and is placed whole (`H a0`..`a9`, `x0`..`x9`, `H after`), and Chromium repeats
  the header and cuts the row (`H a0`..`a9`, `H x0`..`x7`, `H x8 x9 after`; `RowEndTests`,
  `a2-row-end-nested-under-header-13875`): below the header the engine starts the inner table 0.375 pt higher than
  Chromium (its top line at 28.125, Chromium's at 28.5), so the row's ten lines fit there with only its line below past
  the page end, and such a row is not cut. With 9 lines below 12 pt of top padding the
  rest of the inner table fits below the repeated header but for the outer table's bottom line: Chromium repeats the
  header and lets that line pass the page end (and draws it again on a third page that shows nothing else), and the
  engine goes on without the header under every profile (`H a0`..`a9`, `x0`..`x8 after`;
  `a2-row-end-nested-under-header-end`).
  A cell background ends at the middle of the line below (192.375), where Chromium ends it at the line's outer edge
  (192.75; `a2-row-end-background`). That is the table model's own rule, not new here; the 0.375 pt between lies under
  the line, which hides it, as the engine has only solid, opaque lines (no dashed or translucent ones). The
  background stays under either value of `TableEndOverflow`. What passes is not carried after a cut inside a row, nor in
  a table that repeats its header rows (not measured): there the table takes no more than the page area.
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
  Taking the cut under `Refuse` too is left to later layout work. Beside a sliced piece the other cells of its row go on page by page beside the slices
  under `CellsBesideSlices.GoOn`, the code front-end's default, a deliberate deviation from Chromium (cases
  `a4-image-beside-lines`, `a4-line-beside-lines`); under `Below`, the HTML front-end's value, they go on below the slice
  each page shows, as Chromium prints it: Chromium lays the piece out whole on the page where it starts and goes on with
  the rest of the row below what is left of it (cases `a4-image-beside-lines`, `a4-slice2-beside-lines`,
  `a4-slice-near-end-beside-lines`; `SliceInTableTests`). In a cell that spans rows the pages follow from the rules for rows: the page breaks at the latest
  breakpoint of the span (inside its last row), the span's missing height goes to its last row, and the next row
  comes below the last slice (case `a4-image-rowspan`; Chromium breaks between the spanned rows and draws the next row
  over the slice).
  Under a repeated header row the content is sliced right below the header, on a page after one that showed no body row
  other than slices of a piece that was itself sliced; after a page that did show one, the page before the first slice
  ends below the header and the row moves on once. That page is Chromium's: the first slice starts on Chromium's page
  and at Chromium's place in all eleven measured cases (`a4-header-*`, `a4-intro-header-image-row`; `SliceUnderHeaderTests`),
  each with a header of at most a quarter of the page area; a taller header is not repeated (above), and the piece is
  sliced as without a header. Chromium never ends a page with a header that repeats alone: it cuts the first body row
  below it and draws the row's cells empty down to the page end, their side lines and no line below them
  (`a4-header-image-228`, `a4-header2-image-228`, `a4-header-tall-line`, `a4-header-row-image-row`, and so every
  `a4-header-*` case); a header that does not repeat ends its page alone (`a4-header-unrepeated-image-228`). Under
  `EmptyRowParts.Allow`, the HTML front-end's value, the engine cuts the row there too, where the row's cells draw
  nothing on that page; under `Avoid`, the code front-end's default, that page holds the header alone, and so it does
  under `Allow` where a cell of the row would draw something on it (`fr-header-pad-box-72` above, where Chromium cuts
  the row; with an image beside text, not measured). The cut row's cells then go on below the header on the next page,
  where the part of a cell that continues starts at the centre of the grid line below the header, as in Chromium: below
  a 0.75 pt line at 28.125 pt, half a pixel below a whole one, which Chromium paints at the next whole pixel, 28.5 pt
  (`a4-header-image-228`; "c10" at 40.125 pt, painted at 40.5 pt, `a4-header-cut-row-image-row`; below a 2.25 pt line
  28.875 pt, painted at 29.25 pt, `a4-header-line2-25-image-228`, `a4-header-line2-25-cut-text-row`). That is the
  paint, not the layout: where the engine lays a baseline, an image edge or an edge of a horizontal grid line out half
  a pixel below a whole one, Chromium paints it at the next whole pixel in every case, except at the end of the rest of
  a sliced piece, which follows. On the page where a cell's part
  holds nothing but the rest of a piece sliced on an earlier page, Chromium ends the part where the rest ends, without
  its half of the grid line below it, so the line is centred on the rest's end (a 0.75 pt line at 75.75–76.5 pt below a
  rest that ends at 76.125 pt, `a4-header-image-228`; a 2.25 pt line at 75–77.25 pt, `a4-header-image-228-line3-below`;
  the text after the table that much higher, `a4-header-image-row`, `a4-header-image-174`, `-180`; in a cell with a
  background, `a4-header-image-228-background`, and inside a nested table, `a4-header-nested-image-228`, both open for
  other reasons). A part that lays content out on
  the page keeps its half (`a4-header-cut-text-row`). Under `SliceRestEnd.AtLineCentre`, the HTML front-end's value,
  the engine does the same (`Tracks/TableSegments.cs`, `AtRestEnd`; `SliceRestEndTests`); under `InsideCell`, the code
  front-end's default, the part holds its half as every cell does, and the line starts where the rest ends.
  On the later pages the policy `RepeatedHeaderRows` decides. Chromium lays the piece out whole below the header of the
  page where it starts, also a piece no taller than the page area, and lets it pass the page end; it repeats the header
  on every later page where the table still lays content out (a row still to come, a cell beside the piece that goes on),
  and not on a page that only shows the rest of content laid out before: there the rest starts at the top of the page
  area (`a4-header-image-228`, `a4-header2-image-228`, `a4-header-row-image-228`, `a4-header-image-row`,
  `a4-header-tall-line`, `a4-header-image-170`, `-174`, `-180`). Under `WhereContentIsLaidOut`, the HTML front-end's
  value, the engine does the same (`SliceUnderHeaderTests`). Under `AboveEverySlice`, the code front-end's default, a
  deliberate deviation from Chromium, the engine repeats the header above every slice and makes each slice as tall as the
  room below it, so the content is shown whole and once, and a piece taller than the room below the header but not than
  the page area goes on without the header. Under both values, on a page where the header is repeated over a slice,
  Chromium still moves the content up a whole page area and draws the header over 16.5 pt of it, which are never shown;
  the engine draws the header above the slice, the slice below it (Known limits, "A repeated header row over a slice").
  With text beside the image (`a4-header-image-beside-lines`, open) the engine also differs on the first page: it leaves
  the header alone where the cut row's other cell would draw text, where Chromium starts the row.
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
  cell on the page and lets the padding pass the page end; under `TableEndOverflow.EndsOnPage`, the code front-end's
  default, the engine never lets a cell's bottom padding pass the page end (under `ContinuesOnNextPage` it does, as
  Chromium, see the cut rows above: `a4-continued-cell-pad-bottom-30` is equal through the HTML front-end), and
  records no breakpoint inside content that continues from the page before (`FlowLayout`), so under
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
  `white-space-collapse` values `collapse`, `preserve` and `preserve-breaks` exist (per run, `TextStyle.WhiteSpaceCollapse`),
  and `break-spaces`, and `text-wrap-mode` `wrap` and `nowrap` (`TextStyle.TextWrapMode`); Chromium 159 prints
  `preserve-spaces` and `discard` as `collapse`, so the model has neither. A soft wrap opportunity
  belongs to the run of the character before it. Chromium decides by its element's tags where an element whose wrap mode
  differs from its parent's ends, and there the two differ (cases `ws-border`, pinned by
  `WhiteSpaceTests.Where_an_element_that_wraps_differently_from_its_parent_ends_the_engine_follows_the_character_before_not_Chromium`):
  after nowrap `aa-` before `bb` of a parent that wraps Chromium breaks, and it breaks after a wrapping `aa` before a
  nowrap ` bb` of its parent; the engine breaks at neither. The same holds beside an image (case `ws-image-border`): after a nowrap
  `ab` before an image of a paragraph that wraps, and after an image at the end of a nowrap element, Chromium breaks and the
  engine does not. For an opportunity after a space the engine's rule is CSS Text 3
  §5.5's ("properties on the box directly containing that character control the line breaking at that opportunity"). A tab in a run whose spaces are preserved is refused (`Documents.Input.Unsupported.PreservedTab`):
  there are no tab stops; where white space collapses a tab is a space like every other white space. A forced line break
  inside a paragraph is `LineBreak()` (U+2028) in every mode; a line feed is one only in a run that preserves breaks, and
  elsewhere collapses to a space, as a carriage return does (which a run that preserves spaces keeps as a space). U+000B, U+000C, U+0085 and U+2029 in the text are mandatory breaks too (UAX #14 classes BK
  and NL) and are not collapsible, so they also end the line: CSS Text 3 §5.5 says any character of classes BK and NL
  "must be treated as forced line breaks". (Its §4 asks for control characters other than tab, line feed and carriage
  return to be shown as visible glyphs; for U+000B, U+000C and U+0085 the two rules conflict, and the engine follows
  §5.5.) A
  soft hyphen (U+00AD) shows a hyphen only where the line breaks. Default-ignorable characters, such as U+2060 WORD
  JOINER and the zero-width space, take no space and need no glyph. A character the font has no glyph for is the
  error `GlyphMissing`; there is no fallback font and no substitute character: U+2011 NON-BREAKING HYPHEN, which Lato
  lacks, is refused where HarfBuzz would draw the ordinary hyphen. A hyphen before a letter that follows a space or an
  image in the line keeps the letter on its line (UAX #14 LB20a: "( sot | BK | CR | LF | NL | SP | ZW | CB | GL ) ( HY |
  HH ) × ( AL | HL )"); Chromium breaks after such a hyphen all the same (case `ii-hyphen`,
  `InlineImageTests.A_hyphen_after_an_image_keeps_its_letter_as_after_a_space_where_Chromium_breaks_after_it`).
- **Images in a line of text:** only images (the image algorithm) are atomic inlines; `vertical-align` is `baseline`
  only; Chromium's print puts a line's text at its line box's top rounded to a whole pixel and an image's edges at whole
  pixels, the engine keeps exact positions (less than half a pixel, case `ii-height`).
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
  lower half starting the next (as Chromium 159 does, cases `mf4-line-*`, `a2-row-break-line-6pt`). Chromium puts both
  edges of each half on the nearest pixel and draws at least one pixel, so each half of a one-pixel line prints one
  pixel thick; `LayoutProfile.SplitLineHalves` (`AtLeastOnePixel`, the HTML front-end's value) draws a half thinner than
  a pixel one pixel thick from its top edge, which gives Chromium's pixels, and moves nothing (cases `a2-cells-end`,
  `a2-row-break-line-1-5pt`, `a2-row-break-line-2-25pt`; `TableBreakLineHalfTests`). Where a cell that spans rows
  continues over the break, the next page draws nothing of the line, in any column, and keeps the half's room (case
  `b4-row-span-6pt`). In case `a2-split-line-1px` the next page's first row starts 0.375 pt, half a pixel, below the
  page area's top, and Chromium paints its text at the next whole pixel, 0.375 pt lower (pinned in `RowPartTests`); `Clone`, the code front-end's default for tables, ends the
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
- **Tagging and PDF/UA:** a header cell's `/Scope` is Column, Row or Both, from its role; the code front-end makes header
  cells only in header rows (Column), the HTML front-end by THE RULE FOR A HEADER CELL'S SCOPE IN HTML (Row or Column,
  never Both); no Headers/IDs for nested headers (HTML's `headers` attribute is not read); a block and a row of blocks are Div, so a row of blocks nests three Divs per column; the language of a
  box or an inline group is written as `/Lang` on its structure element where it differs from its parent's (the
  document's is in the catalog), never as marked content; an unknown language (HTML `lang=""`) cannot be written and is
  refused, as veraPDF rule 7.2-29 asks every PDF/UA-1 `/Lang` to be a tag (its PDF/A-3 rule 6.7.4-1 allows the empty
  one); no List, Caption, Note, Formula or TOC
  structure elements, no bookmarks; Link elements come from inline groups, which the code front-end (`Link`) and the
  HTML front-end (`a` with `href`) make, and Span elements from span groups, which the HTML front-end makes only around an
  inline element a link leads to or whose language differs from its parent's, and the code front-end around a span with
  a language; a link inside the document leads to a box or an inline group (an HTML image, and an inline element that draws nothing, is no target, and a link to it is
  refused), never to a
  named destination (no `/Dests`, no names in the file); a link
  inside an artifact (a page header or footer, a repeated copy of a table's header rows, a later slice of a line taller
  than the page) gets no annotation and no Link element, its text is only drawn, while the first, real occurrence of a
  header row keeps its links (Chromium 159 writes such a link on every page, twice: case `lk-header-row`, a listed
  difference); HTML's links: a host beyond ASCII is refused (no IDNA), dot segments of an absolute URL are written as
  given, and a percent-encoded dot (`%2e`) is not read as a dot when a relative URL is resolved; ActualText only on the
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
