# Chromium reference cases

The layout tests compare the engine with Chromium's own layout of the same content. This folder holds everything
needed to measure those numbers again from a clone: the HTML cases, the script that prints them and reads them back,
and the text it produced the last time.

Nothing here is part of the engine or its build. The tests do not run Chromium; they pin the numbers measured here.

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
  laid out for print (1 CSS px = 0.75 pt);
- `<case>.lines.txt`: every filled rectangle as `page x0 y0 x1 y1 height`, in points, origin top left
  (`rectangles.py`, PyMuPDF): the pieces of the grid lines;
- `<case>.print.txt`: where the print put things, page by page, read from the PDF alone (`positions.py`, PyMuPDF; see
  "Print positions" below);
- `versions.txt`: the Chromium, poppler and PyMuPDF versions.

`measured/` holds the text files of the last run, so a change in a new Chromium shows up as a diff. Copy
`out/*.txt` over it after a deliberate re-measurement.

## Print positions

For rules about pages (where a page starts, where it breaks, what moves to the next page) the reference is Chromium's
print itself, not a measurement on the screen. `positions.py` reads every case's PDF back and writes `<case>.print.txt`,
one line per item, in points, origin at the top left of the page (y down), three decimals:

```
page <page> <width> <height>
word <page> <x0> <x1> <baseline> <font size> <text>
rect <page> <x0> <y0> <x1> <y1>
image <page> <x0> <y0> <x1> <y1>
```

A word is a run of characters without white space on one baseline. Its extent is its advances (from the origin of its
first character to the origin of its last plus that character's advance), and its height is its baseline, so neither
depends on the ascent and descent a PDF reader assumes for a font, as a bounding box does. `rect` is a filled rectangle
(a background, a border, a grid line, or a marker box a case draws on purpose to show where a box is); `image` is the
area an image is drawn into, which reaches past the page when the page shows only a slice of it.

The tests read these files through `PrintedPages` in the test-support project, which gives the engine's page model the
same form (`PrintedPages.Engine`), so a test compares the two page by page: the words of each page exactly, positions
with the comparison rule below. The harness takes any HTML with an `@page` rule, so whole documents can be measured
the same way as the small cases.

Chromium snaps more than layout units when it prints, which a case must allow for:

- the page margins and the page size become whole CSS pixels (a 10 pt margin prints as 13 px = 9.75 pt), so the cases
  that pin page breaks use sizes that are whole pixels (multiples of 0.75 pt; 12 pt margins, page areas of 150 or 180 pt);
- with `line-height: normal` the line height is the font's ascent and descent each rounded to whole pixels: Lato at 12 pt
  (16 px) is 16 + 3 = 19 px (14.25 pt) where the font's own values give 19.2 px (14.4 pt). The cases that pin page breaks
  keep every decision clear of that difference, and their tests compare which words are on which page exactly and
  positions only where the difference does not enter.

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
- grid `fr` tracks get truncated shares, the fractions carried to the next track: `core/layout/grid/grid_track_sizing_algorithm.cc:1281–1309`;
- printing scales the page by 0.75 (CSS px to points) and does not round coordinates: `components/printing/renderer/print_render_frame_helper.cc`,
  `printing/metafile_skia.cc`.

## Where Chromium is not exact

At some font sizes Chromium's text is narrower than the font's advances, and the engine deliberately does not follow it.
Before the advances are computed, the font size is rounded twice:

1. Blink floors the font size to 1/100 px: `third_party/blink/renderer/platform/fonts/font_description.cc:263–274`
   (`FontDescription::EffectiveFontSize`, precision multiplier 100 in `font_cache_key.h:54`);
2. on Linux, web fonts go through Skia's Fontations backend, and its font library skrifa truncates the pixels per em to
   1/64 px to match FreeType: skrifa `src/instance.rs:52–64` (`fixed_linear_scale`), used for the advances in
   `src/metrics.rs:254, 299–319` (Skia `src/ports/SkTypeface_fontations.cpp:591–593`; the web-font typeface factory is
   `platform/fonts/web_font_typeface_factory.cc`).

At 10 pt (13.333 px) the advances are therefore computed for 13.33 px truncated to 853/64 = 13.328125 px: every advance
is 0.039 % (a factor of 0.99960938) smaller. At 9 pt (12 px), 10.5 pt (14 px) and 12 pt (16 px) nothing is rounded.
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
ceil; at 10.5 pt the same effect is smaller (0.00044 %) and does not show in these runs.

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
| `mf4-line-*` | 3 pt lines (4 px, not snapped), Lato 12 pt, no padding; the page area ends at the centre of row 8's bottom line plus 0, 0.75, 1.5 or 2.25 pt | `TableBreakLineTests.A_row_that_fits_down_to_its_bottom_line_s_centre_stays_and_the_line_is_split_like_Chromium` |

What the `mf4-line-*` cases show, read from `words.txt` and `lines.txt`: in all four, rows 1–8 are
on page 1; the last line on page 1 is 1.5 pt high (the upper half of the 3 pt line); the first line on page 2 is 1.5
pt high (its lower half); and row 9's text on page 2 is 1.5 pt higher than row 1's on page 1. Chromium's rows are 17.25
pt apart where the engine's are 17.4 pt (Chromium snaps the 14.4 pt line height to whole pixels), so the test applies
the same rule at the engine's own row pitch rather than copying Chromium's numbers.
