# Chromium reference cases

The layout tests compare the engine with Chromium's own layout of the same content. This folder holds everything
needed to measure those numbers again from a clone: the HTML cases, the script that prints them and reads them back,
and the text it produced the last time.

Nothing here is part of the engine or its build. The tests do not run Chromium; they pin the numbers measured here.

## Run it

Needs Node.js 20 or later and Docker (for poppler's `pdftotext`, run in an Alpine image pinned by digest).

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
- `<case>.boxes.txt`: width and height in points of every table cell and every element with an id, laid out for
  print (1 CSS px = 0.75 pt);
- `versions.txt`: the Chromium and poppler versions.

`measured/` holds the text files of the last run, so a change in a new Chromium shows up as a diff. Copy
`out/*.txt` over it after a deliberate re-measurement.

## Cases and the tests that use them

All cases use Lato from `assets/fonts/lato` and `cases/common.css`: `border-collapse: collapse`, `border: 0.75pt
solid`, `padding: 2pt 4pt`, Lato 10 pt. The border is 0.75 pt because Chromium draws a 1 pt border as one device
pixel (0.75 pt at 96 dpi) and lays out with that width.

| Case | What it shows | Test (`src/UnitTests.Ave.Documents.Layout`) |
| --- | --- | --- |
| `widths` | auto columns (`t1`), a fixed and two auto columns (`t2`), grid `fr` columns (`w3`, `w4`): `boxes.txt` | `ChromiumReferenceTests`: the three width tests |
| `b1-repeat` | a long table breaks between rows and repeats its header row | `A_long_table_breaks_between_rows_and_repeats_its_header_like_Chromium` |
| `b2-split-row` | a row taller than the space left breaks inside its cells | `A_row_taller_than_the_space_left_breaks_inside_its_cells_like_Chromium` |
| `b3-keep-group` | a row group with `break-inside: avoid` moves to the next page whole | `A_row_group_kept_together_moves_to_the_next_page_whole_like_Chromium` |
| `mf4-line-*` | 3 pt lines (4 px, not snapped), Lato 12 pt, no padding; the page area ends at the centre of row 8's bottom line plus 0, 0.75, 1.5 or 2.25 pt | `TableBreakLineTests.A_row_that_fits_down_to_its_bottom_line_s_centre_stays_and_the_line_is_split_like_Chromium` |

What the `mf4-line-*` cases show, read from `words.txt` and from `mutool trace` of the PDFs: in all four, rows 1–8 are
on page 1; the last line on page 1 is 1.5 pt high (the upper half of the 3 pt line); the first line on page 2 is 1.5
pt high (its lower half); and row 9's text on page 2 is 1.5 pt higher than row 1's on page 1. Chromium's rows are 17.25
pt apart where the engine's are 17.4 pt (Chromium snaps the 14.4 pt line height to whole pixels), so the test applies
the same rule at the engine's own row pitch rather than copying Chromium's numbers.
