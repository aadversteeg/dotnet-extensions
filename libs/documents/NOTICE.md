# Notice

The code of the documents family (`libs/documents`) is licensed under the MIT licence (`LICENSE` at the root of the
repository). This file says what it was written from and lists the third-party data, fonts and tools it uses, with
their licences.

## Code

The code in this family was written for it. Some of it follows a part of another project closely, expression for
expression, without copying its text: those parts count as derived from that project, and the files they follow are
listed below with their copyright and licence. No code was copied.

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
- Colour: the sRGB colour profile in `Ave.Extensions.Documents.Pdf/SrgbIccProfile.cs` is written in code from the ICC
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
  §11.2, tRNS §11.3.2.1, sRGB §11.3.3.5, filter selection §12.8; JPEG: ITU-T T.81 (ISO/IEC 10918-1) Annex A (DCT,
  zig-zag order), Annex B (syntax), Annex C (Huffman tables), Annex F.2 (sequential decoding), Annex G.1.2 (progressive
  decoding), and ITU-T T.871 (JFIF) §7 for the YCbCr transform; PDF image XObjects and soft masks: ISO 32000-1 §8.9,
  §11.6.5.3, DCTDecode §7.4.8. No code of libjpeg, libjpeg-turbo, stb_image or another decoder was read or used.
  The ITU-T T.83 compliance test data (© ITU & ISO/IEC 1994) is downloaded by a test on first use and is not part of
  this repository.
- Graphics (`Ave.Extensions.Documents.Graphics`): paths, fill rules and image space as ISO 32000-1 §8.3.3, §8.5.2,
  §8.5.3.3 and §8.9.5.3 define them; exact-area anti-aliasing by signed-area accumulation as described (in words, not
  code) by S. Barrett, "How the stb_truetype Anti-Aliased Software Rasterizer v2 Works" (2015) and R. Levien, "Inside
  the fastest font renderer in the world" (2016), an idea credited to Raph Levien's LibArt; polygon clipping after
  I. E. Sutherland and G. W. Hodgman, "Reentrant polygon clipping" (CACM 17(1), 1974); source-over compositing after
  T. Porter and T. Duff, "Compositing Digital Images" (SIGGRAPH 1984). No code of stb_truetype, font-rs, FreeType,
  AGG, vello, MuPDF, poppler or PDFium was read or used.
- Chromium (159) was also used through Playwright to measure reference layouts that the tests pin as numbers
  (`tools/reference/chromium`).

Projects that may be studied under the project rules (krilla, MIT/Apache-2.0; PdfPig, Apache-2.0; Typst,
Apache-2.0): none of their source was used. PdfPig is used as a test-only dependency to read the
generated PDFs back (see below).

### Followed from Chromium (Blink layout), BSD-3-Clause

The layout follows these files of Chromium 159 (`chromium/src`, branch 8154, commit
`aa12292cc49e38a844763a7745c97fdfec2182db`), each "Copyright 20xx The Chromium Authors. Use of this source code is
governed by a BSD-style license that can be found in the LICENSE file":

- `third_party/blink/renderer/core/layout/block_layout_algorithm.cc` (Copyright 2016 The Chromium Authors): where a
  paragraph breaks under orphans and widows, and its best early break (`Layout/InlineLayout.cs`, `BreakWhereFull`,
  `BestBreakInside`); a box that lays itself out again to its early break and records that break as perfect
  (`Layout/LayoutResult.cs`, `KeepsItsBreak`, `KeptAppeal`; `Layout/BreakAvoidance.cs`, `Kept`);
- `third_party/blink/renderer/core/layout/early_break.h` (Copyright 2019 The Chromium Authors) and `break_appeal.h`:
  early breaks and the order of break appeals (`Layout/EarlyBreak.cs`, `Layout/BreakAppeal.cs`, `Layout/FlowLayout.cs`);
- `third_party/blink/renderer/core/layout/fragmentation_utils.cc` (Copyright 2017 The Chromium Authors):
  `FinishFragmentation`, bottom padding and borders that do not fit below a box's content (`Layout/SlicedBox.cs`), and
  `MovePastBreakpoint`, no breakpoint inside a box that continues from the page before, and the comparison of a break
  inside a child with the breakpoints before it (`Layout/FlowLayout.cs`); `CalculateBreakAppealInside`, a child's
  `break-inside: avoid` applied by its parent (`Layout/BreakAvoidance.cs`, `Kept`);
- `third_party/blink/renderer/core/layout/box_fragment_builder.cc` (`PropagateBreakInfo`): the `break-inside: avoid` of a
  box between a break and the box that compares it (`Layout/BreakAvoidance.cs`, `Kept`);
- `third_party/blink/renderer/platform/fonts/font_metrics.cc` (`FontMetrics::AscentDescentWithHacks`) and
  `simple_font_data.cc` (the line spacing): a font's ascent, descent and line gap each rounded to a whole pixel; and
  `third_party/blink/renderer/core/layout/inline/line_utils.cc` (`CalculateLeadingSpace`): the half-leading above the
  text floored to a whole pixel (`DocumentModel/LayoutProfile.cs`, `FontMetricRounding.WholePixels`;
  `Layout/LineBoxes.cs`, `InlineExtent`);
- the layout contract (a pure function of a box, a constraint space and a break token) is Blink LayoutNG's;
- where a line ends and how wide it is (`Typesetting/LineBreaking/GreedyLineBreaker.cs`, `Layout/InlineLayout.cs`
  `LineEndAtSpace`), as measured in Chromium's print and cited from `third_party/blink/renderer/core/layout/inline/`
  `line_breaker.cc` (`NeedsAccurateEndPosition`, `SetDontReshapeEndIfAtSpace`, `AddHyphen`, `BreakText`), `line_info.cc`
  (`ComputeNeedsAccurateEndPosition`), `hyphen_result.cc` (`HyphenResult::Shape`), and
  `third_party/blink/renderer/platform/fonts/shaping/` `shaping_line_breaker.cc` and `.h` (`ShapeLine`) and
  `shape_result.cc` (`OffsetForPosition`).

The test support follows `third_party/blink/renderer/platform/fonts/font_description.cc` and
`third_party/blink/renderer/platform/geometry/layout_unit.h` for Chromium's text widths
(`Ave.Extensions.Documents.TestSupport/ChromiumLayout.cs`).

Chromium's licence (`LICENSE`, the file those headers point to):

```text
// Copyright 2015 The Chromium Authors
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//    * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//    * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//    * Neither the name of Google LLC nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### Followed from Skia and Fontations (font line metrics)

`Fonts/FontParser.cs` (`LineMetricsOf`) and `FontMetrics.Line` follow how Chromium on Linux chooses a font's line
metrics: Skia's `src/ports/SkFontHost_FreeType.cpp` (Skia revision `8a65fa27c104d1bb5a58df5ec3e0c1655053fcf6`,
"Copyright 2006 The Android Open Source Project. Use of this source code is governed by a BSD-style license that can
be found in the LICENSE file") and Fontations' `skrifa/src/metrics.rs` (tag `skrifa-v0.49.0`; skrifa is licensed
"MIT OR Apache-2.0"; used here under MIT).

Skia's licence (`LICENSE`):

```text
Copyright (c) 2011 Google Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in
    the documentation and/or other materials provided with the
    distribution.

  * Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived
    from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

Fontations' licence (`LICENSE-MIT`):

```text
Copyright (c) 2019 Fontations Developers

Permission is hereby granted, free of charge, to any
person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the
Software without restriction, including without
limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software
is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice
shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

### Followed from HarfBuzz (normalization), Old MIT

`Typesetting/Unicode/Normalization.cs` follows HarfBuzz 15.1.0's `src/hb-ot-shape-normalize.cc` ("Copyright © 2011,2012
Google, Inc. This is part of HarfBuzz, a text shaping library."): which clusters are decomposed, reordered and
composed before shaping. The flag that marks where a glyph run is not safe to break (`Typesetting/Shaping`,
`ShapedGlyph.UnsafeToBreak`) follows HarfBuzz's `HB_GLYPH_FLAG_UNSAFE_TO_BREAK` (`src/hb-buffer.h`, `hb-buffer.hh`
`unsafe_to_break`) and how its pair positioning sets it (`hb-ot-layout-gpos-table`, PairPos). HarfBuzz's licence
(`COPYING`, the same at 15.0.0 and 15.1.0):

```text
HarfBuzz is licensed under the so-called "Old MIT" license.  Details follow.
For parts of HarfBuzz that are licensed under different licenses see individual
files names COPYING in subdirectories where applicable.

Copyright © 2010-2022  Google, Inc.
Copyright © 2015-2020  Ebrahim Byagowi
Copyright © 2019,2020  Facebook, Inc.
Copyright © 2012,2015  Mozilla Foundation
Copyright © 2011  Codethink Limited
Copyright © 2008,2010  Nokia Corporation and/or its subsidiary(-ies)
Copyright © 2009  Keith Stribley
Copyright © 2011  Martin Hosken and SIL International
Copyright © 2007  Chris Wilson
Copyright © 2005,2006,2020,2021,2022,2023  Behdad Esfahbod
Copyright © 2004,2007,2008,2009,2010,2013,2021,2022,2023  Red Hat, Inc.
Copyright © 1998-2005  David Turner and Werner Lemberg
Copyright © 2016  Igalia S.L.
Copyright © 2022  Matthias Clasen
Copyright © 2018,2021  Khaled Hosny
Copyright © 2018,2019,2020  Adobe, Inc
Copyright © 2013-2015  Alexei Podtelezhnikov

For full copyright notices consult the individual files in the package.


Permission is hereby granted, without written agreement and without
license or royalty fees, to use, copy, modify, and distribute this
software and its documentation for any purpose, provided that the
above copyright notice and the following two paragraphs appear in
all copies of this software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
```

## Unicode data

`assets/unicode/18.0.0/` contains files of the Unicode Character Database 18.0.0 (© Unicode, Inc.), licensed under
the Unicode License v3 (`assets/unicode/18.0.0/LICENSE.txt`). The table in
`Ave.Extensions.Documents.Typesetting/Unicode/UnicodePropertyRanges.g.cs` is generated from them, and so is
`Ave.Extensions.Documents.Typesetting/Unicode/UnicodeNormalizationData.g.cs` (canonical combining classes, canonical
decompositions and composition exclusions, from `UnicodeData.txt` and `DerivedNormalizationProps.txt`). The line
breaking algorithm is written from UAX #14, revision 57; normalization from UAX #15 and the Unicode Standard §3.11.
The Unicode License v3 asks for its copyright and permission notice "with all copies of the Data Files or Software,
or ... in associated Documentation": the notice is `assets/unicode/18.0.0/LICENSE.txt`, and it applies to the data
files and to the two generated tables.

## HTML Standard and Encoding Standard data

`Ave.Extensions.Documents.Html.Parsing/Tokenizing/named-character-references.txt` is generated from the HTML Standard's
table of named character references (§13.5, https://html.spec.whatwg.org/entities.json, SHA-256
`d741d877ac77c4194c4ad526b5b4a19aef8dfe411ab840a466891cdbb9f362e6`, downloaded 2026-11-18) by
`tools/generate-html-entities.py`; `Ave.Extensions.Documents.Core/EncodingLabels.g.cs`, the one table the HTML parser
and the CSS reader share, is generated from the Encoding Standard's table of labels (§4.2,
https://encoding.spec.whatwg.org/encodings.json, SHA-256
`078212b3697f60b81225b6671bd9da2604497abff0aa8e96e4d46605c02ac9e7`) by `tools/generate-encoding-labels.py`. Both
standards are Copyright © WHATWG (Apple, Google, Mozilla, Microsoft), licensed under the Creative Commons Attribution
4.0 International License; "to the extent portions of it are incorporated into source code, such portions in the source
code are licensed under the BSD 3-Clause License instead". The parsing algorithm itself is written from the HTML
Standard's text (§13.2, read 2026-11-18); no code was copied or ported.

`Ave.Extensions.Documents.Html/UserAgent/html.css`, the user-agent style sheet of the HTML front-end, holds the CSS of
the HTML Standard's rendering section (§15.3 and §15.4, read 2026-11-18): its rules, written element by element as CSS
the engine supports, with the values Chromium 159 computes where it differs from the standard (measured, and marked in
the file). It is CSS incorporated into source code, so it is under the same BSD 3-Clause License, Copyright © WHATWG.
It is not taken from Chromium's or WebKit's style sheet.

The BSD 3-Clause License, for the WHATWG data above (the named character references, the encoding labels, the
user-agent style sheet):

> Copyright © WHATWG (Apple, Google, Mozilla, Microsoft).
>
> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
> following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following
>    disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
>    disclaimer in the documentation and/or other materials provided with the distribution.
> 3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote
>    products derived from this software without specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
> INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
> DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
> SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
> SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
> WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
> OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## CSS specification data

`Ave.Extensions.Documents.Css/Values/NamedColors.g.cs` is generated by `tools/generate-css-named-colors.py` from the
colour keyword files of css-parsing-tests (`assets/test-suites/css-parsing-tests/color_keywords_3.json` and
`color_keywords_4.json`, CC0 1.0, see Test suites), and every name is checked against the colour Chromium 159 computes
for it (`tools/reference/chromium/computed-style/measured/named-colors.txt`); no W3C material is in it. The CSS reader
reads the encoding labels from Core's table (see above). It is written from the text of CSS Syntax 3, Selectors 4, CSS
Cascade 5, CSS Nesting 1, CSS Variables 1, CSS Values 4, CSS Color 4, Media Queries 4, CSS Conditional 3, CSS Fonts 4
and CSS Paged Media 3 (editor's drafts read 2026-11-18); no code was copied or ported. The conversion from HSL to RGB
(`Css/Values/Colors.cs`, `HslToRgb`) is the textbook formula with the chroma (C = (1 − |2L − 1|) · S, the hue's sector,
the match value m = L − C / 2), not the sample code of CSS Color 4. Where the engine follows Chromium 159 rather than a
draft (which names are identifiers, which pseudo-classes may follow a pseudo-element), Chromium was measured through
Playwright (`tools/reference/chromium/computed-style`); no Chromium code was read or used.

## Test suites

Vendored in `assets/test-suites/` (see the README there), used by the tests only:

- html5lib-tests, tokenizer tests, commit `c5495c539ca8a79c3c5107f5f14010bcff9a35d5`: Copyright (c) 2006-2013 James
  Graham, Geoffrey Sneddon, and other contributors; MIT licence (`assets/test-suites/html5lib-tests/LICENSE`).
- web-platform-tests, the tree-construction tests in `html/syntax/parsing/resources/` and the CSS cases extracted
  into `css/cases.json` (from `css/css-syntax`, `css/selectors`, `css/css-cascade`, `css/css-variables`,
  `css/css-values`), commit `93494ff9dfc26c59245941548bb7da799e8baaef`: Copyright © web-platform-tests contributors;
  3-Clause BSD License (`assets/test-suites/wpt/LICENSE.md`).
- css-parsing-tests by Simon Sapin, commit `203ce36bffd617db7f118c551e32794561fb273d`: dedicated to the public domain
  under CC0 1.0 (`assets/test-suites/css-parsing-tests/LICENSE`).

## Fonts

`assets/fonts/lato/` contains Lato 2.015 Regular, Bold and Italic by Łukasz Dziedzic (tyPoland), licensed under the
SIL Open Font License 1.1 (`assets/fonts/lato/OFL.txt`). Used by the tests and the samples only.

`assets/fonts/theano/` contains Theano Didot 2.0 Regular by Alexey Kryukov, version 2.0 released 2011 (FONTLOG.txt),
Copyright (c) 2006-2008 Alexey Kryukov (the notice in the author's OFL.txt), licensed under the SIL Open Font License
1.1 with the Reserved Font Name "Theano" (`assets/fonts/theano/OFL.txt`; the author's FONTLOG.txt next to it). Unmodified, from the author's release https://github.com/akryukov/theano/releases/download/v2.0/theano-2.0.ttf.zip
(zip SHA-256 `3cabb1e2b874f567606195f4e44e3d97dd23c6406e7f711f63fd848d2582e378`, font SHA-256
`6d8d8ead05960cd6fcb61a36f045091bfa57ff4f6bcce8b6d9c3b1108b327c3f`). Used by the tests only, for what Lato lacks: a
GDEF table, a `mkmk` feature, an old-style 'kern' table, a combining mark with a non-zero advance and a non-zero line
gap.

`assets/fonts/roboto/Roboto-Regular-subset.ttf` is a subset of Roboto Regular 2.138, "Copyright 2011 Google Inc. All
Rights Reserved." (the font's own copyright line), licensed under the Apache License, Version 2.0
(`assets/fonts/roboto/LICENSE-2.0.txt`). Modified: it is a subset with the glyphs of two characters (`I` and `Ɨ`),
made by `tools/make-roboto-subset.sh` from the Debian package fonts-roboto-unhinted 2:0~20170802-4
(`assets/fonts/roboto/README.md` has the SHA-256 values). Used by the tests only, for a glyph whose contours overlap.

## Dependencies

| Package | Licence | Used by |
| --- | --- | --- |
| Ave.Extensions.Functional, Ave.Extensions.ErrorPaths | MIT | the libraries |
| PdfPig 0.1.16 | Apache-2.0 | tests only |
| WebAssembly 2.1.0 (dotnet-webassembly) | Apache-2.0 | tests only: runs the reference shaper |
| harfbuzz.wasm from harfbuzzjs 1.6.4 (HarfBuzz 15.0.0) | MIT (harfbuzzjs), Old MIT (HarfBuzz) | tests only: the reference shaper, `UnitTests.Extensions.Documents.Typesetting/Reference/` |
| xunit, Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | tests only (xunit also in the test-support project, for the Docker-backed facts) |
| FluentAssertions 7.2.0 | Apache-2.0 | tests only (version 8 changed licence) |

Tools used outside the build, in `tools/verify-samples.ps1` and in the reference tests, inside throw-away
containers: veraPDF 1.30.4 (`verapdf/cli`, GPL-3.0-or-later / MPL-2.0), qpdf (Apache-2.0), poppler-utils (GPL-2.0),
MuPDF tools (AGPL-3.0), Pillow (MIT-CMU, for the side-by-side sheets), fontTools (MIT, the outline reference),
libjpeg-turbo's cjpeg and djpeg (IJG, BSD-3-Clause and zlib licences; the JPEG reference). They are run, not linked or
distributed. `tools/generate-test-images.py` uses Pillow to write the image test files (and writes `tools/reference/chromium/cases/tall.png` without it), `tools/generate-test-jpegs.py`
cjpeg to write the JPEG test files (`UnitTests.Extensions.Documents.Images/Assets/jpeg-*.jpg`).

## Sample data

`samples/Ave.Extensions.Documents.Samples/data/` holds synthetic timesheet data (invented customers and entries, no
real customer data), made for an earlier Typst version of the timesheet report;
`samples/Ave.Extensions.Documents.Samples/assets/logo.png` is that version's dummy logo, rasterised by
`tools/generate-test-images.py`. The bank account, VAT and Chamber of Commerce numbers in the sample's footer
(`Timesheet/Brand.cs`) are invented placeholders.
