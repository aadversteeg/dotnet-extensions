# Chromium reference cases

The layout tests compare the engine with Chromium's own layout of the same content. This folder holds everything
needed to measure those numbers again from a clone: the HTML cases, the script that prints them and reads them back,
and the text it produced the last time.

Nothing here is part of the engine or its build. The tests do not run Chromium; they pin the numbers measured here.

`computed-style/` is the same for the HTML/CSS front-end: Chromium's verdicts on the extracted web-platform-tests CSS
cases and its computed styles of a corpus of documents (see the README there). It uses this folder's npm packages.
`html-tree/` measures the trees Chromium builds from HTML, for the parser: the tree-construction tests where Chromium
differs from the suite, and the parser's own cases (see the README there). It uses this folder's npm packages too.

## Run it

With Docker only (Linux, macOS or Windows; Docker must run Linux containers):

```
pwsh -NoProfile -File measure.ps1                # every case
pwsh -NoProfile -File measure.ps1 mf4-line-0     # one or more cases by name
```

`measure.ps1` runs Chromium in the official Playwright container of the pinned version (`v1.65.1-noble`, pinned by
digest), and poppler's `pdftotext` and PyMuPDF (`extract.sh`) in the reference tools image, built from
`../tools.Dockerfile` (Debian pinned by digest, packages from snapshot.debian.org at a fixed date, exact versions).
On Linux and macOS the containers run as the current user, so nothing they write is root-owned.

On a machine with Node.js 20 or later, a Playwright Chromium and Docker, the same in one process:

```
npm ci
npx playwright install chromium
node measure.mjs                 # every case
node measure.mjs mf4-line-0      # one or more cases by name
```

The Chromium version comes from the Playwright version pinned in `package.json` (Playwright 1.65.1 installs Chromium
159). The output goes to `out/` (ignored by git):

- `<case>.pdf`: Chromium's print of the case (`preferCSSPageSize`, so the case's `@page` rule sets the page);
- `<case>.words.txt`: every word as `page xMin yMin xMax yMax text`, in points, origin top left (`pdftotext -bbox`);
- `<case>.boxes.txt`: width and height in points of every table cell, every element with an id and every grid item,
  with print media, measured in a browser window as wide as the print's page area (1 CSS px = 0.75 pt; see "Box
  sizes"): one layout without page breaks, so a box the print cuts over pages has its whole height here;
- `<case>.area`: the width in points of each page's page area, as `page width` (`page none` for a page whose body
  draws nothing), read from the print (`area.py`, PyMuPDF): the window width of `boxes.txt`. Not a measured file:
  `measured/` does not keep it;
- `<case>.lines.txt`: every filled rectangle as `page x0 y0 x1 y1 height`, in points, origin top left
  (`rectangles.py`, PyMuPDF): the pieces of the grid lines, each the part the print shows (see "Print positions");
- `<case>.print.txt`: where the print put things, page by page, read from the PDF alone (`positions.py`, PyMuPDF; see
  "Print positions" below);
- `<case>.ink.txt`, only for a print that has one: each underline or overline that Chromium cuts around the glyphs' ink
  (`text-decoration-skip-ink: auto`), as `fill page x0 y0 x1 y1` (the line as painted), `gap page x0 x1` (the rectangle each
  clip cuts out of it, raw, in paint order) and `piece page x0 x1` (what stays visible: the line minus the union of the gaps),
  in points, origin top left (`ink.py`, PyMuPDF). Chromium paints such a line whole, inside clips that each leave out a
  rectangle around one glyph's ink. A clip that cuts out a rectangle over only part of the line's height (the clip of an
  earlier line of the same text) is written as `gap page x0 x1 y0 y1`, and the line then as `part page x0 y0 x1 y1`, its
  visible rectangles, columns of equal rows joined; a clip that leaves out anything but one rectangle is written as `uneven`;
  a clip that is one rectangle (the page area's) cuts no gap: the line is written as the part inside it, as `print.txt`
  holds it;
- `versions.txt`: the Chromium, poppler and PyMuPDF versions.

`measured/` holds the text files of the last run, so a change in a new Chromium shows up as a diff. Copy
`out/*.txt` over it after a deliberate re-measurement.

The cases load their fonts from the family's `assets/fonts` by relative URL. Where a font file does not load, Chromium
prints the case with a fallback font without a word (a copy of this folder without `assets/fonts` beside it printed
seven cases in Liberation Serif, and their prints were committed so: `a4-cell-image-179-5`, `a4-cell-pad3-image-177`,
`a4-cell-pad3-line-177` and the four `a6-header-quarter-*`, since measured again). So `measure.mjs` lays the case out,
waits for `document.fonts.ready` and stops when a font face of the case failed to load. A copy of the tool needs the
family's `assets/fonts` four folders above `cases/`.

## Box sizes

`boxes.txt` is measured after the print, in a second layout: the browser window, with print media. A window lays the
document out without page breaks, so a box has one size there, the size it has before the print cuts it. That window
is as wide as the print's page area, so a box is as wide as in the print and a height that depends on the width (text
that wraps onto another line) is the print's too. The width is a fact of the print, not of the CSS: Chromium draws
each page's body under one clip, the page area at its own size (see "Print positions"), and `area.py` writes the width
of that clip on every page (`visible.body_clip`: the page's first clip outside any other). Chromium lays a page out in
the page area rounded up to a whole CSS pixel (a 280 pt page area, 373.33 px, is laid out 374 px wide), so the window is
the clip's width rounded up to a whole pixel. The clip is the page area at its own size in Chromium's unit of 1/64 px,
which can be a little over a whole pixel: a 200 pt page with 10 pt margins is 266.671875 px less 2 × 13.328125 px,
240.015625 px, clipped 180.012 pt wide, and laid out 241 px wide (the justified lines of `h-page-area-justify` end at
190.497 pt, 9.75 pt + 241 px; `f-theano-13-ab` has the same page). Only 0.001 px is taken off before rounding up, for
the three decimals `area.py` writes, so a page area of whole pixels stays that many pixels. A page whose body
draws nothing opens no clip (the empty page a forced break leaves, case `a1-after-forced-break`; page 1 of
`a1-header-row`, which holds only an empty block's margin): it gives no width, and the other pages give it. Page areas
of different widths give no one window width: the boxes step stops with an error naming the case, and so it does for a
print that draws nothing on any page but has boxes to measure (`h-root-display-none` has none).

## Print positions

For rules about pages (where a page starts, where it breaks, what moves to the next page) the reference is Chromium's
print itself, not a measurement on the screen. `positions.py` reads every case's PDF back and writes `<case>.print.txt`,
one line per item, in points, origin at the top left of the page (y down), three decimals:

```
page <page> <width> <height>
word <page> <x0> <x1> <baseline> <font size> <text> <colour> <face>
rect <page> <x0> <y0> <x1> <y1> <colour>
stroke <page> <x0> <y0> <x1> <y1> <colour>
image <page> <x0> <y0> <x1> <y1>
```

A word is a run of characters without white space on one baseline. Its extent is its advances (from the origin of its
first character to the origin of its last glyph plus that glyph's advance), and its height is its baseline, so neither
depends on the ascent and descent a PDF reader assumes for a font, as a bounding box does. A word that ends in a base
with a combining mark drawn after it (a font without the precomposed character) ends at the mark's origin plus its
advance, 0 where the font gives the mark none: PyMuPDF reads the base and its marks as one character with the base's
advance, so the end is read from the glyphs the PDF draws (case `sk-extreme`: "gạọụ" in Theano Didot ends at 53.489,
the dot below the "u", where the u's advance ends at 54.426). The engine's read-back (`PrintedPages.Engine`) and
poppler's `pdftotext -bbox` end such a word there too. `rect` is a filled rectangle
(a background, a border, a grid line, or a marker box a case draws on purpose to show where a box is); `image` is the
area an image is drawn into. A `rect`, `stroke` or `image` line is the part the print shows: the PDF draws each of
them under the clips open around it, and Chromium opens one clip per page around the page's body (the page area, less
the areas of the page-margin boxes, which it draws before it, outside any clip), yet draws what passes that clip all the
same (a grid line past the page end, a cell's or an image's slice drawn again offset up by the page area's height). So
each is cut to the rectangle its clips leave, and one that lies wholly outside it is not written (`visible.py`; `lines.txt`
the same). Words are written whole. A colour is
`#rrggbb`, with the opacity as a fourth byte (`#rrggbbaa`) where it is not 1. The face is the PostScript name of the
font a word is drawn with, without the subset tag (`ABCDEF+`) of the PDF: it names the font file, and with it the
weight and the style (synthetic bold or italic would not show). A `stroke` line is one of the four bands a stroked
rectangle paints: Chromium strokes a border of one colour and style on all four sides as one rectangle path
(`kb-v8-padded-box-kept-para`, `kb-v9-padded-box-kept-table-row`, `fr-header-cell-margin`), and `positions.py` writes
the top and bottom band across the outer width and the left and right band between them, in drawing order among the
`rect` lines; any other stroke stops the read-back. `lines.txt` (`rectangles.py`) keeps fills only.

The tests read these files through `PrintedPages` in the test-support project, which gives the engine's page model the
same form (`PrintedPages.Engine`), so a test compares the two page by page. What the tests compare: the words of each
page exactly, in every test that reads a print file; the distance between baselines in the line-height tests
(`LineHeightTests`); word x positions and absolute baselines in `LineHeightTests.Words_are_where_Chromium_prints_them…`
(case `fr-positions`) and the first baseline in `LineBoxReferenceTests`; the bottom padding's rectangle in
`BottomPaddingTests`; the page-relative baseline in the page-start tests. Other positions in the print files (most
rectangles, image areas, x positions in tables) are measured and kept, and pinned only by their line counts
(`MeasuredReferenceFilesTests`) and by the agreement of the two read-backs (`ChromiumReferenceTests`). Every case that the
HTML front-end draws is compared on every fact of its `print`, `lines`, `words` and `boxes` files, each with its bound
(`CaseStatusTests` and `HtmlCases` in `UnitTests.Extensions.Documents.Html`; docs/architecture.md, "The status file").
The harness takes any
HTML with an `@page` rule, so whole documents can be measured the same way as the small cases.

Chromium snaps more than layout units when it prints, which a case must allow for:

- the page area starts at the left and top margins rounded to whole CSS pixels (a 10 pt margin prints as 13 px =
  9.75 pt) and its size is rounded up to whole pixels; the sheet is the page size in whole points, then in whole
  1/300 inch (A4 prints 594.96 × 841.92 pt). So the cases that pin page breaks use sizes that are whole pixels
  (multiples of 0.75 pt; 12 pt margins, page areas of 150 or 180 pt);
- with `line-height: normal` the line height is the font's ascent, descent and line gap each rounded to whole pixels:
  Lato at 12 pt (16 px, no line gap) is 16 + 3 = 19 px (14.25 pt) where the font's own values give 19.2 px (14.4 pt).
  Every inline box's ascent and descent are rounded so, under any line height, and the half-leading above it is
  floored to a whole pixel; a used line height is snapped to 1/64 px (docs/architecture.md, "Against Chromium"). The cases that pin page breaks
  keep every decision clear of that difference, and their tests compare which words are on which page exactly and
  positions only where the difference does not enter.

## Whole documents

The cases are small on purpose: each shows one rule. `documents/` holds whole documents, written the way an author
writes HTML and CSS for print, that the HTML/CSS front-end is measured against as a whole: which parts of them it lays
out as Chromium prints them, and which it refuses. They are measured with the same tool and kept the same way, in a
folder of their own so that the case tests, which walk `cases/` and `measured/` and pin every case's files, stay apart
from them:

```
pwsh -NoProfile -File measure.ps1 -CaseFolder documents
```

and then `out/*.txt` of those documents (and `versions.txt`) over `documents/measured/`. In the timesheets Chromium
clips the right end of the table's grey lines at x 567.105, 0.105 pt into the right border: the page area's own width
from its start rounded to a whole pixel, where layout rounds the width up. `print.txt` and `lines.txt` hold those lines
cut there (see "Print positions").

The HTML front-end reads the documents as it reads the cases, with the same comparison (`CaseStatusTests`,
`UnitTests.Extensions.Documents.Html`): one status line per document in `documents/html-status.txt`, in the form of
`html-status.txt`. A document stays `open` with its first difference, or `refused` with its first refusal, until the
front-end lays it out as Chromium prints it.

| Document | What it is | Chromium's print |
| --- | --- | --- |
| `timesheet-one-page` | the samples' timesheet (`samples/Ave.Extensions.Documents.Samples`, `Timesheet/Timesheet.cs`) as HTML, in Dutch, with the edge-case data set (`data/sample-edge.json`) | 1 page |
| `timesheet-normal` | the same with the normal month (`data/sample-normal.json`), the data set of the samples' golden pictures (`UnitTests.Extensions.Documents.Samples/Golden/timesheet-normal.nl.page*.png`), so the two can be laid side by side | 2 pages |
| `timesheet-pages` | the same with the long month (`data/sample-long.json`): the header row repeats, entries with a description stay together | 5 pages |
| `invoice` | the invoice of the HtmlStyles sample (`samples/Ave.Extensions.Documents.Samples.HtmlStyles/data/invoice.html`) as an author keeps it for print: `invoice.html` with `invoice.css` and `invoice-theme.css` | 1 page |

The timesheet HTML is made from the samples' synthetic data by `timesheet-html.py` (in this folder; `python3
timesheet-html.py` writes the three files): the engine takes finished HTML, and filling a template with data is the
caller's job, so the script is that caller. It drops what Lato cannot show as the sample does (`FontCoverage`). The
documents load their fonts with `@font-face` from `assets/fonts/lato` and the logo from the samples' `assets/`, by
paths relative to this folder.

The timesheet's header is page furniture, in the `@page` margin boxes of every page, as in the sample. A margin box
holds no element, so the logo is generated content, and generated content shows an image at its own size (the
400 x 400 pixel logo would be 300pt square; `width` and `height` on the margin box do not change it). The resolution in
`image-set(url(…) 160dpcm)` gives the sample's 2.5 cm. The title follows the logo in the same box, after an em space;
an image in a line sits on the baseline, so the title's baseline is at the logo's bottom edge, where the sample centres
the title on the logo. A second margin box for the title would centre it vertically, but `@top-center` is centred on
the page, which puts the title further right than the sample and leaves the right box too narrow for the customer's
name.

The yardstick invoice is a copy, not the sample: the sample shows diagnostics on purpose (`cursor`, `float`) and names
its fonts without `@font-face`, so Chromium prints it in the container's fallback fonts. The copy loads Lato by
`@font-face`; the repo has no fixed-width face, so the amounts use Lato too, right-aligned. The floated closing
paragraph is a paragraph with `text-align: right`.

## The reference

The reference is Chromium of the pinned version (Playwright 1.65.1, Chromium 159.0.8154.6) in the pinned Playwright
container (`mcr.microsoft.com/playwright:v1.65.1-noble`, by digest, see `measure.ps1`), with font hinting off
(`--font-render-hinting=none`, set in `measure.mjs`). `versions.txt` records the version, the flag and the image.

Why hinting off: the engine lays text out with the unhinted advances of the font, which is what a PDF contains. Hinted
advances are a screen-rendering property of one platform: with the default hinting, Chromium on Linux rounds every glyph
advance to whole pixels and lines break differently. With hinting off, Chromium on Linux gives the same line breaks,
heights and page breaks as Chromium on Windows gave. The container makes the reference the same on every machine.

## The comparison rule

Chromium stores every layout length as a whole number of layout units of 1/64 CSS px; the engine keeps exact values. So a
test compares an engine length with a measured Chromium length as "equal within n layout units" (one layout unit is
0.75/64 pt = 0.0117 pt), where n is the number of snapped lengths that add up to the measured value. n comes from the
structure of the case (which paddings, borders, column widths and text runs make the value), is written in the test
next to the check together with what it counts, and a sum gets the sum of its parts' n. There is no other tolerance.
`ChromiumLayout` in the test-support project holds the layout unit and the comparison.

How Chromium snaps, read from its source (chromium/src main at 08469936, Chromium 160, and the 159 branch 8154; paths
under `third_party/blink/renderer/`):

- a layout unit is 1/64 px: `platform/geometry/layout_unit.h:473` (`using LayoutUnit = FixedPoint<6, int32_t>`); the
  plain conversion from float truncates (lines 125–130);
- specified widths and paddings are truncated: `platform/geometry/length_functions.h:52–57` (`LayoutUnit(length.Pixels())`),
  `core/layout/length_utils.cc:1521–1547` (padding), `core/layout/table/table_layout_algorithm_types.cc:38–39` (column
  widths). 100 pt becomes 133.328125 px, 4 pt 5.328125 px;
- border widths are whole pixels: `core/css/resolver/style_builder_converter.cc:1964–2000`. 0.75 pt is exactly 1 px, so the
  borders of the cases add nothing to n;
- every text item is ceiled to a layout unit and the items are summed: `platform/fonts/shaping/shape_result.h:170–171`
  (`SnappedWidth()` = `LayoutUnit::FromFloatCeil(width_)`), `core/layout/inline/inline_node.cc:2099`;
- a table's auto columns get truncated shares of the space to distribute, and the last column the remainder:
  `core/layout/table/table_layout_utils.cc:466` (`DistributeInlineSizeToComputedInlineSizeAuto`);
- a table wider than its columns' maximums (`width: 100%`, cases `tw-*`) gives the width left over to its auto columns in
  proportion to their maximums, or to its fixed columns in proportion to their widths when it has no auto column,
  each share truncated and the remainder to the last receiving column: the same function, case `kAboveMax`; under
  `table-layout: fixed`, `SynchronizeAssignableTableInlineSizeAndColumnsFixed` (a column of width 0 receives nothing);
  CSS Tables 3 §3.9.3.2 says the same (Working Draft of 16 December 2025);
- a `col` length in the automatic layout (case `tw-pref`) makes a constrained column: its minimum is its cells'
  min-content, its maximum the larger of the length and that min-content (`table_layout_algorithm_types.cc`,
  `TableTypes::CreateColumn`, `TableTypes::Column::Encompass`); it grows between the min and the specified guess, before
  the auto columns (`kSpecifiedGuess`), and past the maximums only without auto columns;
- grid `fr` tracks get truncated shares, the fractions carried to the next track: `core/layout/grid/grid_track_sizing_algorithm.cc:1281–1309`;
- printing scales the page by 0.75 (CSS px to points) and does not round coordinates in that transform:
  `components/printing/renderer/print_render_frame_helper.cc`, `printing/metafile_skia.cc`; but layout puts baselines at
  whole pixels (with a 21.90625 px line the baselines step 22, 22, 21, 22 px), so a baseline in the print is a whole
  pixel even where the line height is not.

## Where Chromium is not exact

At some font sizes Chromium's text is narrower than the font's advances. The engine follows it only under the layout
policy `FontSizeRounding.FlooredToPixelFractions` (`Typesetting/ShapingSize.cs`), which the HTML front-end sets; by
default it keeps the font's advances. Before the advances are computed, the font size is rounded twice:

1. Blink floors the font size to 1/100 px: `third_party/blink/renderer/platform/fonts/font_description.cc:263–274`
   (`FontDescription::EffectiveFontSize`, precision multiplier 100 in `font_cache_key.h:54`);
2. on Linux, web fonts go through Skia's Fontations backend, and its font library skrifa truncates the pixels per em to
   1/64 px to match FreeType: skrifa `src/instance.rs:52–64` (`fixed_linear_scale`), used for the advances in
   `src/metrics.rs:254, 299–319` (Skia `src/ports/SkTypeface_fontations.cpp:591–593`; the web-font typeface factory is
   `platform/fonts/web_font_typeface_factory.cc`).

At 10 pt (13.333 px) the advances are therefore computed for 13.33 px truncated to 853/64 = 13.328125 px: every advance
is 0.039 % (a factor of 0.99960938) smaller. The first size, 13.33 px, is the size of the font itself: the size in the PDF
(`Tf`; `print.txt` gives 7.995 pt for 8 pt) and HarfBuzz's scale (`shaping/harfbuzz_face.cc`,
`HarfBuzzFace::GetScaledFont`), so kerning and the place of a mark on its base are at it; only the font's own advances
are at the second. Both are computed in single precision (8.4 px is the float 8.3999996 and floors to 8.39 px). Nothing
is rounded per glyph, word or space: Skia writes a `Td` for every glyph, at the positions HarfBuzz gave (an "i" at 8 pt
advances 2.5575256 px = 480 × 10.65625 / 2000). Measured: `ta-sizes` (one line at 8, 9, 10, 10.5, 11, 12 and 13 pt: the
line is narrower by the ratio of the second size to the font size, constant along the line, 0.27 pt over 276 pt at
8 pt), `ta-px` (10.667px and 10.66px print as 8 pt; 10.65625px, already a multiple of 1/64 px, prints at 10.65 px and
advances at 10.640625 px), `ta-right` (right-aligned: the start of the line moves), `ta-kerning-scale` (the kerning of
30 "AV" pairs is that of 10.5 pt scaled to the first size within 0.0002 pt, not to the second), `ta-marks` (combining
marks on bases without a precomposed form at 8 pt, 10.65625px and 12 pt: x + U+0301 is drawn 4.7810059 px from x's
origin, 897 units at 10.66 px, not 4.7793 at 10.65625 px). Under the
policy every word of these cases is within 0.01 pt of Chromium's (`TextAdvanceTests`). `ta-kerning` and
`ta-kerning-scale` are refused for `font-kerning: none`. At 9 pt (12 px), 10.5 pt (14 px) and 12 pt (16 px) nothing is rounded.
Measured in the container (case `font-sizes`, widths in px; the text is "n", advance 1116 of 2000 units, no kerning):

| Font size | 10 × "n": font's advances → Chromium | 100 × "n": font's advances → Chromium |
| --- | --- | --- |
| 9 pt (12 px) | 66.96 → 66.96875 (ceiled) | 669.6 → 669.609375 (ceiled) |
| 10 pt (13.333 px) | 74.4 → 74.375 (2 layout units less) | 744.0 → 743.71875 (18 less) |
| 10.5 pt (14 px) | 78.12 → 78.125 (ceiled) | 781.2 → 781.203125 (ceiled) |
| 11 pt (14.667 px) | 81.84 → 81.78125 (4 less) | 818.4 → 817.8125 (38 less) |
| 12 pt (16 px) | 89.28 → 89.28125 (ceiled) | 892.8 → 892.796875 (one layout unit under the ceiling) |

At 12 pt the 100-glyph run is one layout unit under the ceiled value. skrifa holds its scale (pixels per em × 64 /
units per em) as a 16.16 fixed-point number: at 16 px, 1024/2000 = 0.512 becomes 33554/65536, 0.0013 % small, and 100
advances come to 892.7885 px instead of 892.8, which ceils to 892.796875. That stays within the one layout unit of the
ceil; at 10.5 pt the same effect is smaller (0.00044 %) and does not show in these runs. Each advance is that scale
times the units, rounded to 1/65536 px (skrifa `src/metrics.rs`, `FixedScaleFactor::apply`, `Fixed::mul_div`): the "i"
at 8 pt is 167610/65536 = 2.5575256 px, at 12 px 2.8800201 px, as the PDF's `Td` values show. The policy scales the
advances so (`ShapingSize.AdvanceInPoints`). It decides a layout unit where a width lies near one: in `ta-right` the
8 pt line is 23691.83 layout units with exact advances and starts one unit later than Chromium's, the 11 pt line
32585.01 and starts one earlier; with the fixed-point advances all three lines start where Chromium's do.

Chromium also rounds the width of every text item up to a layout unit (`ShapeResult::SnappedWidth`, "The comparison
rule"): an item is a run of text between two element boundaries, so a line of many inline elements gains up to one
layout unit per element. The engine follows it under the layout policy `LengthRounding.SixtyFourthPixels`, which the
HTML front-end sets: the width of each run's part on a line (`LineSegment.Width`) is rounded up, for where the next
part starts, for aligning the line and for whether it fits. Measured: `ta-items` (30 inline elements of two one-letter
words at 8 and 10.5 pt): every word within 0.0005 pt of the item widths rounded up; unrounded the last word starts
0.176 pt (8 pt) and 0.119 pt (10.5 pt) early, rounded per word 0.266 pt late. `ta-items-right` (the same line
right-aligned: the start moves by the sum of the steps) and `ta-items-fit` (a block, narrowed by its right margin,
wider than the unrounded widths and narrower than the rounded ones: the last word wraps at both sizes).

How large the effect is follows from the two steps: the pixels per em lose less than 0.01 px (the floor to 1/100 px)
plus less than 1/64 px (the truncation), so less than 0.0256 px in all. Relative to the font size that is 0.039 % at
10 pt, 0.071 % at 11 pt (the 100-glyph run above: 38 layout units of 818.4 px), 0.056 % at 7 pt and 14 pt, 0.098 % at
8 pt and 0.16 % at 5 pt; it is zero where the size in px is a whole multiple of 1/64 px that survives the floor to
1/100 px (12, 14 and 16 px). So at a font size Chromium rounds, its text is narrower than the font's advances by a
share that depends on the size, and a line that fits in Chromium by less than that share of its text width can break
one word earlier in the engine. The cases that pin lengths therefore use sizes Chromium keeps exact (`widths-exact` at
10.5 pt; the `mf4-line-*` cases at 12 pt). The 10 pt case `widths` stays: its test applies the factor above, as the
oracle's artefact, to the engine's exact text widths and then compares with the same rule, so a change in Chromium or
its font backend shows up there.

## Cases and the tests that use them

All cases use Lato from `assets/fonts/lato` and `cases/common.css`: `border-collapse: collapse`, `border: 0.75pt
solid`, `padding: 2pt 4pt`, Lato 10 pt unless the case sets another size. The border is 0.75 pt because Chromium draws a
1 pt border as one device pixel (0.75 pt at 96 dpi) and lays out with that width.

| Case | What it shows | Test (`UnitTests.Extensions.Documents.Layout`) |
| --- | --- | --- |
| `widths-exact` | at 10.5 pt: auto columns (`t1`), a fixed and two auto columns (`t2`), grid `fr` columns (`w3`, `w4`, their items): `boxes.txt` | `ChromiumReferenceTests`: the three width tests |
| `widths` | the same tables at 10 pt, a size Chromium rounds | `ChromiumReferenceTests.At_a_font_size_Chromium_rounds_its_text_is_the_font_s_advances_scaled_by_the_documented_factor` |
| `font-sizes` | 10 and 100 times "n" at 9, 10, 10.5, 11 and 12 pt: where Chromium's text width is exact (see above) | none; the numbers are in this README |
| `b1-repeat` | a long table breaks between rows and repeats its header row | `A_long_table_breaks_between_rows_and_repeats_its_header_like_Chromium`; through the print read-back (`print.txt`): `The_long_table_puts_Chromium_s_words_on_Chromium_s_pages` |
| `b2-split-row` | a row taller than the space left breaks inside its cells | `A_row_taller_than_the_space_left_breaks_inside_its_cells_like_Chromium` |
| `b3-keep-group` | a row group with `break-inside: avoid` moves to the next page whole | `A_row_group_kept_together_moves_to_the_next_page_whole_like_Chromium` |
| `b4-row-span` | four labels, each over six one-line rows: the page breaks between the rows under a label, and the label continues | `Rows_under_a_label_that_spans_them_break_between_them_like_Chromium` |
| `b5-row-span-kept-cell` | the same with `break-inside: avoid` on each label: every label moves to the next page with its rows | `Rows_under_a_label_kept_together_move_to_the_next_page_whole_like_Chromium` |
| `a1-*` | where a page starts (page area 276 × 150 pt, whole pixels): content that does not fit below an empty block's margin, its own top margin, a margin taller than the page, the margin after a forced break, a box's top padding, moves to the next page, where the margins are truncated; a forced break after only margins has no effect; a first child whose margin collapses through its box breaks inside the box as a last resort | `PageStartTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-*`, `a3-mirror` | cuts inside a table row where a part shows nothing (page area 276 × 180 pt): a blank stretch of a page's height between text (`a2-blank-part`, the engine prints the same pages), a row that shows nothing at all (`a2-padded-blocks`, `a2-line-breaks`), the rest of a row that would show nothing on the next page (`a3-mirror`); the last three are where Chromium leaves a part of a row that shows nothing, which the engine avoids by default and prints as Chromium does with `EmptyRowParts.Allow` (docs/architecture.md) | `RowPartTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-padded-blocks-px`, `a3-mirror-px`, `a2-rowspan-blocks`, `a2-split-line-1px`, `a2-cells-end*` | the row-part cases with blocks of 9 pt (12 px), so every length is whole pixels and grid lines compare exactly; a cell over two rows beside 10 pt blocks (grid lines up to 0.5 pt from Chromium's); a break between rows at a one-pixel line (Chromium draws it whole on both pages); a row whose cells end on the page with only the half line below past its end (Chromium moves it whole), with and without a header row and with text, and at the top of the first page (`a2-cells-end-page-top`: Chromium leaves page 1 blank and prints the row whole on page 2, where the engine cuts it: listed) | `RowPartTests` |
| `a4-*` | monolithic content taller than the page area (page area 276 × 180 pt; `cases/tall.png`, a 4 × 16 pixel image shown 102 × 408 pt; Lato 200 pt, a 240 pt line): an image at the top of a page, an image and a line after a line of text (moved to a fresh page first), an image in a table cell (the row is cut after the cell's top line); Chromium slices each, clipped at the page area, and starts what follows below the last slice, except in the cell case, where the next row is drawn over it | `MonolithicSliceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-header-*`, `a4-intro-header-image-row`, `a4-two-image-rows`, `a4-row-image-row` | the same image or line in a table with a repeated header row (thead), after one row, after rows that fill a page or a row cut over two pages, after a paragraph, two images in a row, an image that fits before one that does not; and two shapes without a header. Fixed table layout, line height 15 pt, whole pixels. Chromium starts the first slice below the header on the page after one that showed no body row but slices, after a page with only the header otherwise; on later pages it moves the image a whole page area and leaves the header out or draws it over the image | `SliceUnderHeaderTests` |
| `a4-header-empty-rowspan-line-image` | two columns, an empty repeated header row, a one-line cell over two rows beside an empty cell, and a 345 pt image in the second row (page area 276 × 180 pt): the line whole on page 1, the image sliced from page 3, as in Chromium; on page 4 the deviation of `a4-header-image-row` | `SliceUnderHeaderTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-image-beside-lines`, `a4-line-beside-lines`, `a4-image-nested-table`, `a4-image-rowspan`, `a4-image-single-cell` | the image or line beside 30 lines in the next cell, in a table inside a cell, in a cell that spans two rows, alone in a one-column table, each followed by a row | `SliceInTableTests` |
| `a4-slice2-*`, `a4-slice4-cell-row-after`, `a4-slice-near-end-*` | where content goes after an image sliced over pages (page area 276 × 180 pt, Lato 10.5 pt, `tall.png` at a quarter of its height wide): an image of 228 pt in the second cell of a row, then a row or a paragraph after the table; the same beside 30 lines; an image of 588 pt in the second cell, then a row; an image of 348 pt (its last slice ends 12 pt above the page end) beside 40 lines and as a block before a paragraph. Chromium lays the image out whole on its first page: on the next page every cell of its row goes on below what is left of the image, a cell that does not fit there goes on below the next slice, and the row ends on the first page where no cell has content left, as tall as the rest of the image, also past the page end. In a row of two cells what follows the row starts below its end, or at the top of the next page over the slices still to come (`a4-slice4-cell-row-after`: the next row on page 4, over a whole slice). Each page draws its slice first and the text over it | `CaseStatusTests` (`UnitTests.Extensions.Documents.Html`) |
| `a4-cell-pad3-image-177`, `a4-cell-pad3-line-177`, `a4-cell-image-179-5` | a piece that fits the page area but not with its cell's padding and grid lines (page area 276 × 180 pt): Chromium moves it whole to the next page and lets the cell's bottom inset pass the page end; the engine slices it under `Slice` (a listed difference) | `SliceAboveInsetTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-image-177-block-padding-3` | a block with 3 pt padding above and below a 177 pt image, then "after" (page area 276 × 180 pt): the image stays whole on page 1, the bottom padding starts page 2, and "after" comes below it, in Chromium and the engine | `SliceAboveInsetTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-image-block-padding-60`, `a4-image-block-padding-133` | the 408 pt image sliced in a block with 60 or 133 pt bottom padding, then "after" (page area 276 × 180 pt): the padding fits below the last slice (60) or starts the next page (133), and "after" comes once, below it | `MonolithicSliceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-nested-padded-image-160` | a 160 pt image in a table inside a cell, each cell with 15 pt top padding (page area 276 × 180 pt): Chromium and the engine under `Slice` 2 pages, under `Refuse` 3 (a listed difference) | `PinnedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-continued-cell-pad-bottom-30` | an outer one-cell table whose cell has 30 pt bottom padding, around a table with 6 pt cell padding whose rest on page 2 fits the page area but not with that padding (page area 276 × 180 pt): Chromium places the rest on page 2 and lets the padding pass the page end; the engine refuses it under `Refuse` and breaks it before its last row under `Slice` (a listed difference) | `PinnedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a4-continued-cell-pad-bottom-30-under-header` | an outer table with a header row "O" and one body cell with 30 pt bottom padding around a table of 19 one-line rows (page area 276 × 180 pt): Chromium repeats "O" on page 2 and lets the padding pass the page end; the engine goes on without "O" on page 2 under every profile (a listed difference) | `PinnedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-row-end-bottom-padding-30` | a row whose five lines fit on the page and whose 30 pt bottom padding does not (page area 276 × 180 pt): Chromium and the engine move it whole | `PinnedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-row-end-first-row`, `a2-row-end-first-row-cut` | a table that starts the page with a first row whose content is the page area exactly and whose grid line below does not fit, then a row "after" (page area 276 × 180 pt): a cell of 11 lines below 14.25 pt top padding, or of 12 lines of 14.9375 pt; Chromium leaves the first page empty, or cuts the row before its last two lines; the engine refuses both under `Allow` (a listed limit) | `RowEndTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-row-end-first-row-half-top`, `-block`, `-emptyblock`, `-break`, `-page2`, `-2col`, `-second-row`, `-alone` | a table that starts the page with a first row of 11 lines below 13.875 pt top padding, whose content ends 0.375 pt above the page end, so that only the outer half of its grid line below passes (page area 276 × 180 pt): at the top of the page, in a block, after an empty block, after a forced break, on page 2 after text, beside a second column, each with a paragraph "below" after the table; with a second row "after"; alone. Chromium places the row whole on the first page; alone, it ends with a page that shows nothing (a listed limit) | `RowEndTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-row-end-below-paragraph`, `a2-row-end-nested-under-header` | the first row of `a2-row-end-first-row` with 10 lines, in a table below a one-line paragraph; and a row of 10 lines below 14.25 pt top padding that starts page 2 of a table inside the one cell of a table with a repeated header row (page area 276 × 180 pt): Chromium moves the table to page 2, and cuts the row below the repeated header | `RowEndTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a2-row-end-nested-under-header-end`, `a2-row-end-nested-under-header-13875` | the shape of `a2-row-end-nested-under-header` with 9 lines below 12 pt top padding, or 10 lines below 13.875 pt: Chromium repeats the outer header on page 2 and lets the outer table's bottom line pass the page end (it draws the line again on a page 3), or cuts the row below the header; the engine goes on without the header in the first under every profile and in the second under `Allow` (refused under `Refuse` with `Allow`), listed differences | `RowEndTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a6-header-quarter-*` | a header row of one line with top padding above 60 one-line rows, on page areas of 180 and 360 pt, on both sides of a quarter of the page area (45.0 / 45.75 pt, 90.0 / 90.75 pt): repeated up to a quarter, not above | `HeaderRepeatTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a6-nested-header-pad12-h2925`, `a6-nested-under-header-h2400`, `a6-nested-under-header-h2700` | the header table of `a6-header-quarter-*` (30 rows) inside the one cell of an outer table: a cell with 12 pt padding and a header of 45.0 pt (repeated on every page), or a body cell under the outer table's own header row, with a header of 39.75 pt (repeated) or 42.75 pt (not repeated after page 1) | `HeaderRepeatTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a6-nested-header-h4425-n14`, `-n16`, `-n20`, `-n26`, `a6-nested-under-header-h2700-n16` | the same nested table with 14, 16, 20 or 26 rows and a header of 60 pt (44.25 pt top padding), a third of the page area: shown on page 1 only, whichever number of rows the last page holds; and the header of 42.75 pt under the outer header with 16 rows (2 pages, "O" on both, the inner header on page 1 only) | `HeaderRepeatTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a6-nested-late-h2521875`, `-h2525`, `-h253125`, `-h2700`, `a6-nested-late-clone-h2525`, `a6-nested-first-h2525`, `-h253125` | a table with a header "H" (40.96875, 41.0, 41.0625 or 42.75 pt) above 20 one-line rows in the last body row of an outer table with a header row "O", after 14 one-line rows (so it starts on the outer table's second page), or as its only body row (first page); `-clone` with `box-decoration-break: clone` on the outer table: on the second page repeated up to 40.96875 pt, on the first up to 41.0625 pt | `HeaderRepeatTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a6-nested-late6-h1800`, `-h1850`, `a6-nested-late-d3-h2100`, `-h2125` | the same with 6 pt grid lines and 10 rows before (header 39.0 pt repeated, 39.5 pt not), and three tables deep, a middle table with a header row "M" and 12 rows before (36.75 pt repeated, 37.0 pt not) | `HeaderRepeatTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a5-*` | keep rules (page area 276 × 180 pt): a cell kept together is not cut inside its own row while the row can move (and the same row without the keep is cut); a row kept together moves whole; a short kept cell beside a tall one moves the row, as a kept cell is counted by its box; kept content taller than a page moves to a fresh page first and breaks there | `KeepReferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `a3-pad-*` | a block with 20 pt padding at the top of a page (and lower on it) whose content fits under the top padding but not with the bottom padding too: it breaks before what follows the piece (page area 276 × 180 pt) | `PaddedBlockBreakTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `b-*` | keep rules that reach back (page area 276 × 150 pt, `orphans` and `widows` 1): every break between the boxes at the page end violates a keep, so the page breaks between the lines of an earlier paragraph, inside an earlier block (also three blocks deep), or between the rows of an earlier table | `ReachBackTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `c-*` | `orphans` and `widows` (page area 276 × 150 pt, ten lines): a paragraph that would leave one line moves whole; one that would leave one line for the next page takes a second with it; a paragraph too short for both keeps its orphans (Chromium's rule); custom values; values of 1; a table row whose cell would leave one line moves; a row whose cell has no line on the page yet moves | `OrphansWidowsTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `d-*` | `line-height` (page area 276 × 150 pt, line heights of whole pixels): a length, a number, a percentage; a number inherits as the number, a percentage as its length; the paragraph's strut keeps 6 pt text with a 9 pt line height on 18 pt lines; with `normal` the strut is the paragraph's own font's spacing | `LineHeightTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `d-typo-*` | which font table `line-height: normal` reads (page area 276 × 150 pt, 12 pt): Theano Didot sets USE_TYPO_METRICS with typographic metrics other than its 'hhea' ones, and Chromium's line is the typographic one (20 px; 'hhea' would give 24 px); Lato sets the bit with equal values. A scratch copy of Theano with the bit cleared (not committed) printed 24 px lines: 'hhea' | `LineHeightTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-forced-*`, `fr-header-cell-margin` | a margin after a forced break that leaves no room below it (page areas 36, 150 and 818 pt): one blank page, then the content without the margin; a padded block whose bottom padding then moves on; a cell under a repeated header whose content never fits (the engine refuses it, a listed limit) | `PageBreakProgressTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-header-pad-*` | a table with a header row and one cell holding a bordered box around "a", whose bottom (or top) padding is as tall as the page area or does not fit with it (page areas 36 and 72 pt, 18 pt lines), then "AFTER"; and the same without the header row: neither Chromium nor the engine repeats the 18 pt header on the 36 pt area (more than a quarter of it), and both print the box's line on page 2; on the 72 pt area both repeat it and Chromium cuts the row on page 1 (listed) | `PageBreakProgressTests`, `HeaderRepeatTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-pad-*`, `fr-border-*` | bottom padding and borders that do not fit below a box's content (page areas 60, 54, 44, 96 and 818.25 pt; `figure.png` is a 7 × 10 px picture): the box breaks before them and they start the next page; lower on a page the box moves whole; a box with a breakpoint inside breaks there and keeps that break against orphans, widows or a keep-with-next inside it (a box or paragraph kept together moves whole instead, cases `kb-*`); a box that continues moves only its border | `BottomPaddingTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-keep-*` | keep rules next to content that continues from the page before (page areas 45 and 90 pt): they never reach back into a continued paragraph or box; inside a box at a page end the paragraph before the keep is split | `BottomPaddingTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `h-default-page`, `h-kerning-*`, `h-page-*`, `h-root-display-none`, `h-char-*`, `h-break-after-last*`, `h-font-url-sheet` | facts the HTML front-end takes from Chromium's print: the page without an `@page` rule (US Letter, 612 × 792 pt, no margins, with the settings of `measure.mjs`); `font-kerning: auto` and `normal` print the same kerned text, `none` does not; page margins that are not whole CSS pixels (11, 11.5 and 11.75 pt) put the page area's left and top edge at a whole pixel, rounded. `h-page-size-*`, `h-page-area-*`, `h-page-margin-half`: the sheet of named sizes and lengths (whole points, then 1/300 inch) and where the page area ends (its size rounded up to whole pixels: right-aligned and justified text, a background, the last line of a page, half a pixel). `h-page-size-auto`, `h-page-landscape`, `h-page-portrait`, `h-page-default-media`: the page and the media of a document without a size of its own are the paper of the print call. `h-media-page-size-*`: media queries see that paper also where `@page` sets another size. `h-page-a4-*`: A4 without margins, whose page area reaches past the sheet (listed), and with 1 px margins (equal). `h-root-display-none`: one empty page of that paper. `h-char-*`: characters the HTML parser lets through (form feed prints as a missing glyph, the line and paragraph separators as a space that does not collapse; carriage return and tab as a space). `h-break-after-last*`: no page after a forced break after the last box. `h-font-url-sheet`: a font URL is resolved against the sheet that declares it (`cases/sheets/`). All cases are also read through the HTML front-end, one status line each in `html-status.txt` | `BoxGenerationTests`, `CaseStatusTests` (`UnitTests.Extensions.Documents.Html`) |
| `hb-*`, `hi-*`, `hw-*` | `br`, `img` and `white-space` through the HTML front-end (page width 300 pt, Lato 12 pt): a `br` first in a block makes an empty line, last in a block none; two and three in a row; between and inside spans of other sizes; with white space around it; alone in a block (one line); with `display: none`; between block boxes; in justified text. `img` sized by the `width` and `height` attributes (`figure.png`, 7 × 10 px), one with only a width, one inside a line, and a width and a height in another ratio than the file's, block-level and in a line (`hi-stretch`: Chromium stretches the image; refused). `white-space: pre-line` (spaces collapse, each line feed starts a line), `pre`, `pre-wrap`, `nowrap` (a line wider than the page: Chromium shrinks the whole page) and tabs under `pre` (refused: tab stops are not built). One status line each in `html-status.txt` | `LineBreakAndImageTests`, `CaseStatusTests` (`UnitTests.Extensions.Documents.Html`) |
| `kb-n*`, `kb-m*`, `kb-v8-*`, `kb-v9-*`, `kb-t*`, `kb-s*` | a break inside a box or paragraph kept together against a better break before the box. Form A (page area 150 pt, 18 pt lines, `orphans` and `widows` 1, `n12` 2): "i" with a bottom margin, then a kept box (or a kept paragraph, in a padded box, in a table cell, three paragraphs, a box around a padded box) whose content fits and whose bottom padding or border does not, then "z": Chromium moves the box whole (`i | a0 a1 a2 z`); the controls (not kept `n9`, fitting `n1c`, a kept table that ends above the padding `n11`) break inside. The same where the box has bottom padding 12 pt around a kept box (`m1`) or a kept paragraph (`m2`) whose bottom margin of 30 pt passes the page end: Chromium moves the box whole (`i | a0 a1 a2 z`). Form A in a table (`kb-t*`): the padded box holds a table of three rows whose row group is kept together; the best break lies between two rows of the group, and Chromium moves the box whole (`t1`: `i | r0 r1 r2 z`); a break before the kept group's first row (`t2`: `i r0 | r1 r2 z`) and a group not kept (`t3`: `i r0 r1 | r2 z`) stay inside. Form B (page area 180 pt, `orphans` and `widows` 2): a kept box whose best break lies before a box with keep-with-next inside it; Chromium breaks before the kept box (or inside the paragraph kept with it), and keeps the inner break where the outer box is not kept (`s1`, `s8`, `s9`). Fonts from `assets/` | `KeptBoxBreakTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-kwn-pad-*` | keep-with-next on the last paragraph inside two boxes with bottom padding (15 pt inside, 27 pt outside) that goes to a page of its own, then a paragraph of three lines and "z" (page area 54 pt, 18 pt lines, `orphans` and `widows` 2, or 1 where the name says so): Chromium breaks before the paragraph and page 3 holds only the padding, the engine holds the keep across the padding and breaks the paragraph (listed); the same pages with the keep on the outer box, with `orphans` 1, without padding or without the keep | `ListedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-moved-para-*` | two lines, then a paragraph of five lines with `widows` 4 (or 3, the control) that moves to page 2 whole and breaks there (page area 54 pt, 18 pt lines, `orphans` 2): Chromium counts the widows on that page too and breaks where it is full (`a0 a1 a2 | a3 a4`), the engine counts only the orphans (`a0 a1 | a2 a3 a4`; listed); with `widows` 3 both print the same | `ListedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-row-later-page-*` | a full first page ("i" and a row of seven lines), then a row of seven lines with 12 pt top and bottom padding that is 0.75 pt taller than the 150 pt page area, in one column or beside a second padded cell: Chromium prints all its lines on page 2 and lets the padding overflow, the engine cuts the row before its last line (listed) | `ListedDifferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-f4-*` … `fr-f9-*` | where tables break (page area 276 × 150 pt, 18 pt lines, 0.75 pt lines split at breaks): a row moves whole when a cell's lines fit and only its bottom padding or grid line does not (at the top of a page Chromium leaves the first page blank, the engine cuts: listed); a keep rule never reaches into a continued table; inside a kept row group the rows break between them; a kept row keeps the rows its spanning cell reaches into; a spanning cell fills the page; a box or a cell keeps the break it chose against orphans | `TableBreakReferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `fr-empty-span*`, `fr-lh-*` | line boxes (page width 300 pt, Lato): an inline box without a glyph on the line (an empty span, a span whose space collapses at the line end) counts for the line height; two font sizes on a line under a 15 pt line height (Chromium 1 page, the engine 2: a listed difference); the first baseline under `line-height: 1.5` at 10 pt (the engine 0.87 pt lower: listed) | `LineBoxReferenceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `ii-*` | an image in a line of text (Lato 12 pt on 18 pt lines; `cases/figure.png`, 7 × 10 px, at its own aspect ratio, and `cases/wide.png`, 16 × 4 px, written by `tools/generate-test-images.py`): its margin box on the baseline, lower and taller than the text, alone on a line, with margins, padding and a border (`ii-baseline`); a break opportunity before and after it, also between letters, beside a no-break space and before closing punctuation, in a paragraph 6 pt wide (`ii-breaks`); a hyphen before a letter after an image, after a space and after a letter (`ii-hyphen`: Chromium breaks after the hyphen in all three, where UAX #14 LB20a has no break after a space or an image, a listed difference); a word joiner, a zero width joiner, a narrow no-break space and a figure space on both sides of an image (`ii-joiners`: Chromium breaks on both sides of the image, where CSS Text 3 §5.5 asks for no break beside classes GL, WJ and ZWJ other than U+00A0; the engine follows Chromium, a deliberate deviation); first and last on a line, wrapping, wider than the paragraph, horizontal margins at a line start (`ii-wrap`); justified, right and centred lines, where the image is no expansion opportunity (`ii-align`); the minimum and maximum widths of automatic table columns (`ii-table`); a tall line that moves to the next page whole (`ii-page`); heights that are not whole pixels (`ii-height`: Chromium paints an image with its edges rounded to whole pixels, and the text of a line at its line box's top rounded to a whole pixel) | `InlineImageTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `ws-*` | white-space modes and `nowrap` (Lato 12 pt on 18 pt lines): per value of the `white-space` shorthand (`ws-pre`, `ws-pre-wrap`, `ws-pre-line`, `ws-nowrap`, `ws-break-spaces`, `ws-normal`) several spaces between words, spaces at the start and the end, a line feed, a line feed and spaces, an empty line, a text that ends with a line feed, a paragraph of a line feed and one of spaces only; preserved spaces at a line end, left, right, centred and justified, where `pre-wrap` hangs them at a soft wrap and keeps the part that fits before a forced break or at the paragraph end, and `break-spaces` keeps them and wraps them (`ws-hang-*`, `ws-forced`, `ws-justify`); spaces at the start of a narrow paragraph (`ws-lead`); runs with different modes side by side, with the space or the hyphen at the border on either side (`ws-border`); `nowrap` wider than its paragraph (`ws-overflow`); automatic table columns (`ws-table`, `ws-intrinsic`); a soft hyphen and a zero width space in `nowrap` (`ws-soft`); an image beside preserved spaces and in `nowrap` (`ws-image`), and at a border of wrap modes (`ws-image-border`: Chromium breaks on both sides of the image in all four rows); a justified line that ends in a no-break space, which gets no share (`ws-justify-nbsp`); kerning across a border where only the mode differs (`ws-kern`); a tab (`ws-tab`, for the record: tab stops are not built); the longhands `white-space-collapse` and `text-wrap-mode` and the two-value shorthand, where Chromium 159 prints `preserve-spaces` and `discard` as `collapse` (`ws-longhands`) | `WhiteSpaceTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `dl-*` | text decoration lines, all with `text-decoration-skip-ink: none` but `dl-skip` (Chromium paints a solid line as a filled rectangle, so the rectangles of the print are the lines): underline, line-through and overline at 12 pt and 24 pt in each font a case uses (`dl-lato`, `dl-lato-bold`, `dl-lato-italic`, `dl-theano`); two and three lines on one text, a colour of its own and the text colour (`dl-lines`); whose font decides, with a decorating element around text of another size, weight or style, and the paragraph as decorating box (`dl-whose`); nesting, an inner `text-decoration: none`, underline outside and line-through inside (`dl-nest`); neighbouring runs of one decorating element, one rectangle per run, touching (`dl-join`); the space between words, a collapsed space at a soft wrap, line starts and ends left, right, centred and justified, preserved spaces that hang under `pre-wrap` (`dl-space`); an image in a decorated line, which gets no line (`dl-image`); the line box keeps its height (`dl-height`); the initial `text-decoration-skip-ink: auto`, whose gaps are even-odd clip paths around the line, not separate rectangles (`dl-skip`, with its gaps in `dl-skip.ink.txt`; `SkipInkTests`); a soft wrap after "AV ", which Lato kerns with the space, with the space inside and outside the underlined element and at a width where the kerning decides the fit: Chromium shapes the end again only when the space lies in the decorated element (`dl-end`); line tops off the whole pixel (a block 10.5 px high, lines 18.4 px high), each line at 12 pt and 24 pt, where Chromium draws each line from its baseline painted at a whole pixel (`dl-fraction`, listed) | `TextDecorationTests` (`UnitTests.Extensions.Documents.Fluent`); through the HTML front-end one status line each in `html-status.txt` (`CaseStatusTests`, `TextDecorationMappingTests`) |
| `sk-*` | the gaps an underline or overline leaves around a glyph's ink, `text-decoration-skip-ink: auto` (the initial value; read back by `ink.py` into `<case>.ink.txt`), all line tops on whole pixels: descenders and letters without one, an overline over accented capitals and a line-through, at 12 pt and 24 pt, in each font (`sk-lato`, `sk-lato-bold`, `sk-lato-italic`, `sk-theano`); a decorating box of another font size than the text inside it (`sk-size`); pieces that touch, after a change of weight, style, colour or size (`sk-touch`); `none` on the text inside an element decorated with `auto` and the other way round (`sk-whose`); a justified line and hanging preserved spaces (`sk-space`); an image, a soft hyphen shown at a line end, ligatures (`sk-misc`); the bowl of "g" and the dot under "ạọụ" at nine sizes and two more faces (`sk-extreme`); two lines on one text, nested (`sk-nest`); an earlier line whose clip reaches only part of a later line's height, in both orders, a later line wholly inside it, the overline pair in Theano Didot (Lato's overlines are never cut), underline with line-through of one box, a mark below "g" (`sk-part`) | `SkipInkTests` (`UnitTests.Extensions.Documents.Fluent`); through the HTML front-end one status line each in `html-status.txt`, each clipped line read as its pieces (`CaseStatusTests`) |
| `sr-*` | shaping across the borders of inline elements (Lato 12 pt on 18 pt lines): a kerning pair and a ligature split by a change of colour only, a space on either side of the border, three runs in a row (`sr-same-font`: Chromium kerns and forms the ligature across the border, and its print reads `f<span>i</span>x` as "fx"); a change of weight, style, size or family, and of line height only (`sr-font-changes`: kerned only for the line height); twenty "To" split T + o over 117 pt, left and justified (`sr-line-left`, `sr-line-justify`: a wrapped line's first "To" is not kerned, its T shaped again without the o); a zero width non-joiner inside a text, at the start and at the end of an element (`sr-zwnj`: only at the start it breaks the kerning); `counter(page)` and generated "1" next to letters (`sr-counter`: `counter(page)` prints 0 in the page body; Lato kerns none of these digit pairs) | `ShapingAcrossRunsTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `f-*` | Chromium's whole-pixel font metrics (page area 276 × 180 pt, 240 px; 40 lines each, so 1/64 px per line would show): Lato and Theano Didot at 9, 10, 11 and 13 pt with `line-height: normal` (ascent, descent and line gap rounded on both sides of half a pixel), three sizes and two fonts on one line (`f-mixed`), `1.3` at 11 pt and 13 pt (`f-number`, `f-number-theano`), 13.3 pt at 10 pt (`f-length`). Normal lines: every baseline is whole pixels from the page top, as rounding each metric and flooring the half-leading predicts; numbers and lengths: Chromium's line height is in 1/64 px and its baselines are painted at whole pixels | `FontMetricRoundingTests` (`UnitTests.Extensions.Documents.Fluent`; the Theano cases and `f-mixed` with the USE_TYPO_METRICS change of the layout backlog branch) |
| `f-mixed-lato`, `f-tiny`, `f-tiny-038` | Lato 11 pt with a 13 pt word and a 9 pt bold word on every line (40 lines); fifty lines of Lato at 0.37 and 0.38 pt, then a 12 pt line: Chromium prints none of the tiny text and gives its lines no height | `FontMetricRoundingTests` |
| `f-theano-13-ab` | one line "Ab" in Theano Didot 13 pt, `line-height: normal` (page 200 × 100 pt, 10 pt margins, printed as 13 px): the line is 22 px (16.5 pt), from the OS/2 typographic metrics rounded (13 + 4 + 5 px), the baseline 2 + 13 px below the page area's top | `HalfLeadingTests` (`UnitTests.Extensions.Documents.Layout`) |
| `f-length-three`, `f-length-nine`, `f-letter-length`, `f-letter-percent`, `f-letter-number`, `f-length-15004`, `f-number-1501`, `f-length-floor` | line heights given as a length, a percentage or a number (20 pt on page areas of 60 and 180 pt; 14.4 pt, 120 % and 1.2 at 12 pt on US Letter with 36 pt margins; 15.004 pt and 1.501 at 10 pt; 14.245 pt at 11 pt): Chromium keeps them in 1/64 px, a length or a percentage rounded, a number times the font size floored | `ChromiumLineHeightTests` (`UnitTests.Extensions.Documents.Fluent`) |
| `tf-*` | footer groups (`tfoot`) through the HTML front-end (Lato 10.5 pt, collapsed borders): after the body, before it in the source, with a header group, two footer groups (the first is drawn last, the second in its place), an empty one, the invoice's shape (a 1.5 pt top border on the footer row, grey bottom borders); a table that breaks over four pages (page area 276 × 150 pt), where Chromium repeats the footer at the end of every page (`tf-pages`, listed), and the same with `break-inside: auto` on the footer, which Chromium does not repeat (`tf-pages-auto`) | `CaseStatusTests`, `HtmlListedDifferenceTests` (`UnitTests.Extensions.Documents.Html`) |
| `tp-*` | percent columns (`col` of `width: <percentage>`) under `table-layout: fixed`, collapsed borders, through the HTML front-end (page area 276 pt, 368 px; Lato 10.5 pt): percentages that sum to 100 (the timesheet's 14.29, 17.14, 57.14, 11.43), to 50 and to 130 (scaled to fill the table), 33.3 / 33.3 / 33.4; beside a column without a width (keeps its percentage, the other takes the rest) and beside a 60 pt column (Chromium widens the length column to what the percentages leave: refused); a `col` of span 2 and a `colgroup` of span 2 at 25 % (each column 25 %; the column group's width is refused); a 3 pt table border (the percentages are of the width less the outer half borders); a table of width 240 pt (the same rule; refused for the table width). Read from the grid lines in `lines.txt` | `CaseStatusTests` (`UnitTests.Extensions.Documents.Html`) |
| `mf4-line-*` | 3 pt lines (4 px, not snapped), Lato 12 pt, no padding; the page area ends at the centre of row 8's bottom line plus 0, 0.75, 1.5 or 2.25 pt | `TableBreakLineTests.A_row_that_fits_down_to_its_bottom_line_s_centre_stays_and_the_line_is_split_like_Chromium` |
| `ta-*` | text at font sizes that are not a whole number of CSS pixels (Lato, one line per size, `white-space: nowrap`, page 612 × 432 pt): seven sizes in points (`ta-sizes`), sizes given in pixels (`ta-px`), right-aligned (`ta-right`), `letter-spacing: 0` and `font-kerning: none` (`ta-kerning`), the kerning of 30 "AV" pairs with and without `font-kerning: none` (`ta-kerning-scale`), combining marks (`ta-marks`), 30 inline elements per line: left-aligned, right-aligned and in a block that fits only the unrounded widths (`ta-items`, `ta-items-right`, `ta-items-fit`). Chromium shapes at the font size floored to 1/100 px and advances at that size truncated to 1/64 px ("Where Chromium is not exact") | `ShapingSizeTests`, `TextItemWidthTests` (`UnitTests.Extensions.Documents.Typesetting`), `TextAdvanceTests`; one status line each in `html-status.txt` (`CaseStatusTests`) |
| `te-*` | the table's outer border where the lines along one edge differ (Lato 10.5 pt, two fixed columns in 200 pt, 1.5 pt lines, one 6 pt border in red): a wider left or right border in the first or in a later row (`te-left-first`, `te-left-later`, `te-right-first`, `te-right-later`, which also has a wider line between two columns), a wider top border over one cell of the first row (`te-top`), a wider bottom border under one cell of the last row (`te-bottom`), a 3 pt and a 0.75 pt border on the table itself (`te-table-border`), a wider left border on the second page (`te-break`). Chromium centres every row's outer line half of the widest line at that edge inside the table, on every page, and insets each cell by half of its own line; the wider line is painted inside the table's border box | `CollapsedCellInsetTests`; one status line each in `html-status.txt` (`CaseStatusTests`) |
| `tj-*` | the corners where the grid lines of a collapsed table meet (Lato 10.5 pt, fixed columns in 200 pt, solid lines of 0.75 to 4.5 pt in several colours): each side of every cell in its own colour (`tj-same-cell`); equal lines of different cells at a corner, the cell above right against the one below left and the left against the right (`tj-order`); a table's line against a cell's of the same width (`tj-cell-table`); a wider line at each of the four outer corners (`tj-outer`); four lines of four widths at two inner corners (`tj-four`); wide left lines in later rows and the right edge (`tj-right-edge`); wider outer side lines where a wider line elsewhere sets the top and bottom border (`tj-top-edge`); the half lines at a page break, 3 pt and 1.5 pt, with coloured lines below them (`tj-break`). Chromium gives each corner to the edge that wins there (wider, then cell over row over row group over table, then the earlier box in the table) and to every edge of that box as wide as it; the others stop at the corner. At a page break the vertical lines end and start at the line's centre | `CollapsedJointTests`; one status line each in `html-status.txt` (`CaseStatusTests`) |

What the `mf4-line-*` cases show, read from `words.txt` and `lines.txt`: in all four, rows 1–8 are
on page 1; the last line on page 1 is 1.5 pt high (the upper half of the 3 pt line); the first line on page 2 is 1.5
pt high (its lower half); and row 9's text on page 2 is 1.5 pt higher than row 1's on page 1. Chromium's rows are 17.25
pt apart where the engine's are 17.4 pt (Chromium snaps the 14.4 pt line height to whole pixels), so the test applies
the same rule at the engine's own row pitch rather than copying Chromium's numbers.
