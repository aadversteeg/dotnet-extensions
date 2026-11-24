# Chromium's line ends (tests only)

`ChromiumLineEndTests` compares the line breaker with Chromium's print of these cases. Each case file holds short
paragraphs in Lato Regular 75 pt, one per box width, each after a label paragraph `#n` (6 pt) by which the print is
split back into paragraphs:

- `t-line-ends-left.html`, `-right`, `-center`, `-justify`: the same 16 texts in the four alignments. The texts end lines
  at soft hyphens after kerned letters (`xx Ver­lag`, `xx Y­o`), at spaces after kerned letters (`xx AV To`), beside
  ligatures (`xx Stoff­filter`, `ff` + U+200B + `fi`), at zero width spaces, at forced breaks with and without a space
  before them, and at the paragraph's end after a space.
- The widths are those around every place where Chromium's lines change (two pixels either side) and every 20 px,
  chosen from a first measurement of every width from 100 to 460 px in steps of 2 px (9 412 paragraphs, 2026-11-18).
- `*.print.txt`: the print read back by `tools/reference/chromium/positions.py` (format in that folder's README,
  "Print positions"), measured with Chromium 159.0.8154.6 in the pinned Playwright image, `--font-render-hinting=none`,
  PyMuPDF 1.25.4.

To measure again: copy the four `.html` files into `tools/reference/chromium/cases/`, run
`pwsh -NoProfile -File tools/reference/chromium/measure.ps1 t-line-ends-left t-line-ends-right t-line-ends-center t-line-ends-justify`,
copy `out/t-line-ends-*.print.txt` here, and remove the cases from `cases/` again (they are not part of the layout
reference set that `MeasuredReferenceFilesTests` checks).
