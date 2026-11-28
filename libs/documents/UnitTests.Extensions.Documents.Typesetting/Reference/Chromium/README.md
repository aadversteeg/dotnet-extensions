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

# Chromium's line breaks (tests only)

`line-breaks.txt` is Chromium's print of the line-break matrix, which `ChromiumLineBreakingTests` checks the line-break
rules of the HTML front-end against (`ChromiumLineBreaking`), and from which it generates the ASCII pair table
(`ChromiumLineBreaking.AsciiPairs.g.cs`). Format: lines starting with `#` are comments; `@<document>` starts the rows of
one measured document; each row is `<code points>;<breaks>`: the row's text as code points in hexadecimal, separated by
spaces, and the places where Chromium's print breaks it, each the number of visible characters before the break, in
order, separated by spaces (empty for none). Spaces and the invisible characters (U+00A0, U+202F, U+3000, U+00AD, U+200B,
U+2060, U+200E, U+200F, U+FEFF, tab, line feed) are not counted: a break beside one of them is the same print. A row met
in an earlier document is not written again (it breaks alike in both). 34,223 rows. Measured by
`tools/reference/chromium/line-breaks.py` (how to measure again: the README there, "The line-break matrix"), Lato
Regular 10 pt in a box 1 pt wide, with Chromium 159.0.8154.6 in the pinned Playwright image, `--font-render-hinting=none`,
PyMuPDF 1.25.4. The test reads the file with its line endings normalised, so a checkout's line endings do not matter.

`line-breaks-running.txt` is Chromium's print of the running-text documents, which `ChromiumRunningTextTests` checks
the same rules against, run once over each string as the engine runs them over a paragraph. The matrix's box 1 pt wide
shows only the first place after each line's start; these blocks show the places a line of some width ends at. Format:
as `line-breaks.txt`, but each row is `<code points>;<block>;<block>…`, one block per position of the string, in order:
the position (in code points: the block is as wide as the string up to that position plus half of the next character)
and then the places where Chromium's print ends the block's lines, counted as above, all separated by spaces.
Positions before a space, a soft hyphen, a zero width space or a word joiner have no block. A string met in an earlier
document is not written again. 1,646 strings, 6,362 blocks. Measured by `tools/reference/chromium/line-breaks.py` (`write-running`,
`read-running`; the README there, "The line-break matrix"), Lato Regular 10.5 pt on 12 pt lines, with the same Chromium,
image, flag and PyMuPDF as `line-breaks.txt`. A position after a soft hyphen before a hyphen has no block either: the block
there holds the hyphen the soft hyphen shows, as wide as the hyphen after it, so it cannot tell the two places apart.
Read with its line endings normalised.
