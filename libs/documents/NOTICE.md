# Notice

## Code

All code in this repository was written for it. No code was copied or ported from another project.

Written from the specifications:

- PDF: ISO 32000-1:2008 (PDF 1.7) — object syntax §7.3, file structure §7.5, content streams §7.8, graphics
  §8, text §9 (composite fonts §9.7, font descriptors §9.8, embedded font programs §9.9, ToUnicode §9.10.3),
  document information §14.3, file identifiers §14.4. CMap syntax: Adobe Technical Notes #5014 and #5411.
- Fonts: the OpenType specification (table directory, head, hhea, maxp, hmtx, cmap formats 4 and 12, OS/2,
  name, post, loca).
- Layout and text: CSS 2.1 (§8.3, §9.4, §10.3.2, §10.3.3, §10.6.2, §10.8, §17 tables, Appendix E), CSS Text 3
  (§4.1, §5.5), CSS Fragmentation 3 (§3.1, §3.2, §4.1, §4.4, §5.2–§5.4), CSS Paged Media 3 (page box, page-margin
  boxes, `:first`, page counters), CSS Grid 1 (§7.2.4 `fr`, §11.7.1), CSS Fonts 4 (§2, §5), CSS Backgrounds 3 (§4),
  CSS Box Model 3, CSS Sizing 3 (§5), the HTML Standard's rendering section (heading sizes) and table model (slot
  filling).
- Images: the PNG Specification, Second Edition (ISO/IEC 15948:2004) — chunks §5, filtering §9, IHDR/PLTE/IDAT/IEND
  §11.2, tRNS §11.3.2.1; JPEG: ITU-T T.81 (ISO/IEC 10918-1) Annex B, marker and frame header syntax; PDF image
  XObjects and soft masks: ISO 32000-1 §8.9, §11.6.5.3.
- Break appeal (choosing among breakpoints by how many keep rules they violate): the idea of Chromium's
  `BreakAppeal`, as described in the project's research notes; no Chromium code was read or used. Chromium 157 was
  used once, through Playwright, to measure reference layouts that the tests pin as numbers.
- Layout contract shape: Chromium LayoutNG as described in the project's research notes (pure layout function,
  constraint space, break tokens); no Chromium code was read or used.

Projects that may be studied under the project rules (krilla, MIT/Apache-2.0; PdfPig, Apache-2.0; Typst,
Apache-2.0): none of their source was used for stage 0. PdfPig is used as a test-only dependency to read the
generated PDFs back (see below).

## Unicode data

`assets/unicode/18.0.0/` contains files of the Unicode Character Database 18.0.0 (© Unicode, Inc.), licensed under
the Unicode License v3 (`assets/unicode/18.0.0/LICENSE.txt`). The table in
`src/Ave.Documents.Typesetting/Unicode/UnicodePropertyRanges.g.cs` is generated from them. The line breaking
algorithm is written from UAX #14, revision 57.

## Fonts

`assets/fonts/lato/` contains Lato 2.015 Regular, Bold and Italic by Łukasz Dziedzic (tyPoland), licensed under the
SIL Open Font License 1.1 (`assets/fonts/lato/OFL.txt`). Used by the tests and the samples only.

## Dependencies

| Package | Licence | Used by |
| --- | --- | --- |
| Ave.Extensions.Functional, Ave.Extensions.ErrorPaths | MIT | the libraries |
| PdfPig 0.1.16 | Apache-2.0 | tests only |
| WebAssembly 2.1.0 (dotnet-webassembly) | Apache-2.0 | tests only: runs the reference shaper |
| harfbuzz.wasm from harfbuzzjs 1.6.4 (HarfBuzz 15.0.0) | MIT (harfbuzzjs), Old MIT (HarfBuzz) | tests only: the reference shaper, `src/UnitTests.Ave.Documents.Typesetting/Reference/` |
| xunit, Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | tests only |
| FluentAssertions 7.2.0 | Apache-2.0 | tests only (version 8 changed licence) |

Tools used outside the build, in `tools/verify-samples.ps1` only, inside a throw-away container: qpdf
(Apache-2.0), poppler-utils (GPL-2.0), MuPDF tools (AGPL-3.0), Pillow (MIT-CMU, for the side-by-side sheets). They
are run, not linked or distributed. `tools/generate-test-images.py` uses Pillow to write the image test files.

## Sample data

`samples/Ave.Documents.Samples/data/` holds the reporting PoC's synthetic timesheet data (no customer data);
`samples/Ave.Documents.Samples/assets/logo.png` is the PoC's dummy logo, rasterised by `tools/generate-test-images.py`.
