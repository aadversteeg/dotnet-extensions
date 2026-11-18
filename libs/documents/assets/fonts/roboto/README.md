# Test font: a subset of Roboto Regular 2.138

`Roboto-Regular-subset.ttf` is a **subset** of Roboto Regular, version 2.138 (2017), by Google Inc., under the Apache
License, Version 2.0 (see `LICENSE-2.0.txt`). It holds two glyphs besides `.notdef`:

- `I` (U+0049): one rectangle, x 183 to 376, y 0 to 1456 (2048 units per em);
- `Ɨ` (U+0197): a composite of `I`, moved 55 units right, and a bar, x 20 to 657, y 651 to 803. The two contours are
  wound the same way and overlap, so the non-zero rule fills their union.

The raster back-end's page tests use it (`UnitTests.Extensions.Documents.Render.Raster/PageGeometryTests.cs`): because
both outlines are rectangles, the exact ink of every pixel follows from the geometry alone. The other test fonts
(Lato, Theano Didot) have no glyph with overlapping contours.

Made by `tools/make-roboto-subset.sh` (fontTools 4.57.0 subsetter, `pyftsubset`) from `Roboto-Regular.ttf` of the Debian
package `fonts-roboto-unhinted` 2:0~20170802-4, from the same pinned snapshot of the Debian archive as the reference
tools image. Changes against the original: only the subsetting (glyphs, cmap and the tables that refer to them; no
layout features). The script checks the inputs and makes the same file on every run.

SHA-256:

- package `fonts-roboto-unhinted_2:0~20170802-4_all.deb` 048f1e05cfb1ad1e5cd1c283cd61f2276c6a36f75e85032ddfd65db252b4634e
- original `Roboto-Regular.ttf` 797e35f7f5d6020a5c6ea13b42ecd668bcfb3bbc4baa0e74773527e5b6cb3174
- subset `Roboto-Regular-subset.ttf` 2419e66f39e5b612e73215deb00628be3221ab3bf20036ed43654732e9660100

Used only by the tests; the engine itself ships no fonts.
