# Notice

## Code

All code in this repository was written for it. No code was copied or ported from another project.

Written from the specifications:

- PDF: ISO 32000-1:2008 (PDF 1.7) — object syntax §7.3, file structure §7.5, content streams §7.8, graphics
  §8, text §9 (composite fonts §9.7, font descriptors §9.8, embedded font programs §9.9, ToUnicode §9.10.3),
  document information §14.3, file identifiers §14.4. CMap syntax: Adobe Technical Notes #5014 and #5411.
- Fonts: the OpenType specification (table directory, head, hhea, maxp, hmtx, cmap formats 4 and 12, OS/2,
  name, post, loca).
- Layout and text: CSS 2.1 (§8.3, §9.4, §10.3.3, §10.8, Appendix E), CSS Text 3 (§4.1, §5.5), CSS
  Fragmentation 3 (§3.1, §4.1, §5.4), CSS Paged Media 3 (§3), CSS Fonts 4 (§2, §5), CSS Box Model 3, the HTML
  Standard's rendering section (heading sizes).
- Layout contract shape: Chromium LayoutNG as described in the project's research notes (pure layout function,
  constraint space, break tokens); no Chromium code was read or used.

Projects that may be studied under the project rules (krilla, MIT/Apache-2.0; PdfPig, Apache-2.0; Typst,
Apache-2.0): none of their source was used for stage 0. PdfPig is used as a test-only dependency to read the
generated PDFs back (see below).

## Fonts

`assets/fonts/lato/` contains Lato 2.015 Regular and Bold by Łukasz Dziedzic (tyPoland), licensed under the
SIL Open Font License 1.1 (`assets/fonts/lato/OFL.txt`). Used by the tests and the samples only.

## Dependencies

| Package | Licence | Used by |
| --- | --- | --- |
| Ave.Extensions.Functional, Ave.Extensions.ErrorPaths | MIT | the libraries |
| PdfPig 0.1.16 | Apache-2.0 | tests only |
| xunit, Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | tests only |
| FluentAssertions 7.2.0 | Apache-2.0 | tests only (version 8 changed licence) |

Tools used outside the build, in `tools/verify-samples.ps1` only, inside a throw-away container: qpdf
(Apache-2.0), poppler-utils (GPL-2.0), MuPDF tools (AGPL-3.0). They are run, not linked or distributed.
