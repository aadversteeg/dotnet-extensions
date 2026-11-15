# Notice

## Code

All code in this repository was written for it. No code was copied or ported from another project.

Written from the specifications:

- PDF: ISO 32000-1:2008 (PDF 1.7) — object syntax §7.3, file structure §7.5, content streams §7.8, graphics
  §8, text §9 (composite fonts §9.7, font descriptors §9.8, embedded font programs §9.9, ToUnicode §9.10.3),
  document information §14.3, file identifiers §14.4, marked content §14.6, logical structure §14.7, tagged PDF
  §14.8, accessibility §14.9, output intents §14.11.5, embedded files §7.11. CMap syntax: Adobe Technical Notes
  #5014 and #5411.
- Conformance: ISO 19005-3 (PDF/A-3) and ISO 14289-1 (PDF/UA-1), as their rules are worded in the veraPDF 1.30.4
  validation profiles (PDFA-3B.xml, PDFA-3A.xml, PDFUA-1.xml, read from the veraPDF jar; rule ids such as 6.8-3 are
  quoted in the code); ISO 32000-2 §7.11.3 and §14.13 for associated files. Matterhorn Protocol 1.1 (PDF Association)
  for the list of human checks.
- XMP: ISO 16684-1 (XMP Specification Part 1) for the packet and RDF serialization; the PDF/A extension schema
  container as ISO 19005-3 §6.6.2.3 describes it.
- Colour: the sRGB colour profile in `src/Ave.Documents.Pdf/SrgbIccProfile.cs` is written in code from the ICC
  profile format ICC.1:2001-04 and the sRGB values of IEC 61966-2-1; no profile file was copied. Its colorant and
  white-point numbers equal those of the ICC's `sRGB2014.icc` (and the colorants those of the HP/Microsoft `sRGB
  IEC61966-2.1` profile), which were decoded only to confirm the values. The generated profile needs no licence of
  its own.
- Fonts: the OpenType specification (table directory, head, hhea, maxp, hmtx, cmap formats 4 and 12, OS/2,
  name, post, loca, glyf; GSUB, GPOS and GDEF with the common layout table formats; the old-style kern table).
- Layout and text: CSS 2.1 (§8.3, §9.4, §10.3.2, §10.3.3, §10.6.2, §10.8, §17 tables, Appendix E), CSS Text 3
  (§4.1, §5.4, §6), CSS Fragmentation 3 (§3.1, §3.2, §4.1, §4.4, §5.2–§5.4), CSS Paged Media 3 (page box, page-margin
  boxes, `:first`, page counters), CSS Grid 1 (§7.2.4 `fr`, §11.7.1), CSS Fonts 4 (§2, §5), CSS Backgrounds 3 (§2.2, §3),
  CSS Box Model 3, CSS Sizing 3 (§5), the HTML Standard's rendering section (heading sizes) and table model (slot
  filling).
- Images: the PNG Specification, Second Edition (ISO/IEC 15948:2004) — chunks §5, filtering §9, IHDR/PLTE/IDAT/IEND
  §11.2, tRNS §11.3.2.1; JPEG: ITU-T T.81 (ISO/IEC 10918-1) Annex B, marker and frame header syntax; PDF image
  XObjects and soft masks: ISO 32000-1 §8.9, §11.6.5.3.
- Break appeal (choosing among breakpoints by how many keep rules they violate): the idea of Chromium's
  `BreakAppeal`, known from Chromium's public design documents; no Chromium code was read or used. Chromium (157,
  then 159) was used through Playwright to measure reference layouts that the tests pin as numbers
  (`tools/reference/chromium`).
- Layout contract shape: Chromium LayoutNG as its public design documents describe it (pure layout function,
  constraint space, break tokens); no Chromium code was read or used.

Projects that may be studied under the project rules (krilla, MIT/Apache-2.0; PdfPig, Apache-2.0; Typst,
Apache-2.0): none of their source was used. PdfPig is used as a test-only dependency to read the
generated PDFs back (see below).

## Unicode data

`assets/unicode/18.0.0/` contains files of the Unicode Character Database 18.0.0 (© Unicode, Inc.), licensed under
the Unicode License v3 (`assets/unicode/18.0.0/LICENSE.txt`). The table in
`src/Ave.Documents.Typesetting/Unicode/UnicodePropertyRanges.g.cs` is generated from them. The line breaking
algorithm is written from UAX #14, revision 57.

## Fonts

`assets/fonts/lato/` contains Lato 2.015 Regular, Bold and Italic by Łukasz Dziedzic (tyPoland), licensed under the
SIL Open Font License 1.1 (`assets/fonts/lato/OFL.txt`). Used by the tests and the samples only.

`assets/fonts/theano/` contains Theano Didot 2.0 Regular by Alexey Kryukov (2011), licensed under the SIL Open Font
License 1.1 with the Reserved Font Name "Theano" (`assets/fonts/theano/OFL.txt`; the author's FONTLOG.txt next to
it). Unmodified, from the author's release https://github.com/akryukov/theano/releases/download/v2.0/theano-2.0.ttf.zip
(zip SHA-256 `3cabb1e2b874f567606195f4e44e3d97dd23c6406e7f711f63fd848d2582e378`, font SHA-256
`6d8d8ead05960cd6fcb61a36f045091bfa57ff4f6bcce8b6d9c3b1108b327c3f`). Used by the tests only, for what Lato lacks: a
GDEF table, a `mkmk` feature, an old-style 'kern' table, a combining mark with a non-zero advance and a non-zero line
gap.

## Dependencies

| Package | Licence | Used by |
| --- | --- | --- |
| Ave.Extensions.Functional, Ave.Extensions.ErrorPaths | MIT | the libraries |
| PdfPig 0.1.16 | Apache-2.0 | tests only |
| WebAssembly 2.1.0 (dotnet-webassembly) | Apache-2.0 | tests only: runs the reference shaper |
| harfbuzz.wasm from harfbuzzjs 1.6.4 (HarfBuzz 15.0.0) | MIT (harfbuzzjs), Old MIT (HarfBuzz) | tests only: the reference shaper, `src/UnitTests.Ave.Documents.Typesetting/Reference/` |
| xunit, Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | tests only (xunit also in the test-support project, for the Docker-backed facts) |
| FluentAssertions 7.2.0 | Apache-2.0 | tests only (version 8 changed licence) |

Tools used outside the build, in `tools/verify-samples.ps1` and in the reference tests, inside throw-away
containers: veraPDF 1.30.4 (`verapdf/cli`, GPL-3.0-or-later / MPL-2.0), qpdf (Apache-2.0), poppler-utils (GPL-2.0),
MuPDF tools (AGPL-3.0), Pillow (MIT-CMU, for the side-by-side sheets). They are run, not linked or distributed. `tools/generate-test-images.py` uses Pillow to write the image test files.

## Sample data

`samples/Ave.Documents.Samples/data/` holds the reporting PoC's synthetic timesheet data (no customer data);
`samples/Ave.Documents.Samples/assets/logo.png` is the PoC's dummy logo, rasterised by `tools/generate-test-images.py`.
