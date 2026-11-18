# Raster reference: the engine's pages against MuPDF and poppler

The raster back-end (`Ave.Extensions.Documents.Render.Raster`) draws a page of the page model; MuPDF 1.25.1
(`mutool draw -r <dpi> -A 8`, glyphs unhinted when anti-aliased) and poppler 25.03.0 (`pdftoppm -r <dpi> -png`, FreeType
unhinted) render the engine's own PDF of the same page model at the same resolution, in the reference tools image
(`tools/reference/tools.Dockerfile`). PDFium is not a reference: it hints TrueType glyphs and has no switch to stop it.
The test is `UnitTests.Extensions.Documents.Samples/RasterReferenceTests.cs`; the metrics, the edge rule and the rule are
stated once in `UnitTests.Extensions.Documents.Samples/RasterComparison.cs`.

## How to run

```
pwsh -NoProfile -File tools/test.ps1 -Project UnitTests.Extensions.Documents.Samples -Filter "FullyQualifiedName~RasterReferenceTests"
```

With the environment variable `RASTER_REFERENCE_OUT` set to a folder, the test also writes `distances.md` (the rows
below) and the three images of every timesheet page side by side (engine, MuPDF, poppler).

## The metrics and the rule

Per page, over the pixels the images share, on red, green and blue:

- **away**: the largest channel difference over the pixels away from edges. A pixel is at an edge when the exact coverage
  of some item of the page (a glyph, a rectangle, an image's rectangle), taken on its own, is not the same over the
  pixel's 3 × 3 neighbourhood. On pages whose images do not lie on the pixel grid, image rectangles grown by one pixel are
  left out too: both viewers fit an image to whole pixels before they draw it, and the engine maps it exactly onto its
  rectangle, as the page model says, so the image's content lands up to half a pixel apart.
- **MSE**: Σ d² / (3 × pixels), in 8-bit steps squared.
- **over 32 / 64 / 128**: the pixels where some channel differs by more than that many steps.

The rule: for away, MSE and over 128 (a wrong pixel), the engine's distance to EACH reference is not larger than the
references' distance to each other. Over 32 and over 64 measure how an edge is drawn, and poppler is the odd one out for
a known reason (heavier glyph edges, thin rectangles snapped to whole pixels), so there the engine must not be the
outlier: its distance to the NEARER reference is not larger than the references' distance to each other. No other
tolerance; the golden PNG files (`UnitTests.Extensions.Documents.Samples/Golden/*.png`) catch any change over time.

## Measured: the samples (2026-11-18, all pass)

E = engine, M = MuPDF, P = poppler. The engine is about a
third as far from MuPDF as either viewer is from poppler.

| Sample | dpi | Page | E–M away | E–M MSE | E–M over 32 / 64 / 128 | E–P away | E–P MSE | E–P over 32 / 64 / 128 | M–P away | M–P MSE | M–P over 32 / 64 / 128 |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| sample-a-letter | 300 | 1 | 1 | 93.14 | 116880 / 65022 / 6765 | 0 | 211.86 | 155742 / 108344 / 40679 | 1 | 250.06 | 143193 / 109204 / 50378 |
| sample-a-letter | 300 | 2 | 1 | 18.25 | 24336 / 13836 / 833 | 0 | 55.29 | 33533 / 24597 / 12477 | 1 | 101.39 | 41732 / 33350 / 20459 |
| sample-a-letter | 96 | 1 | 1 | 107.78 | 19109 / 8557 / 123 | 0 | 266.48 | 29529 / 14978 / 3919 | 1 | 344.44 | 28498 / 13634 / 6077 |
| sample-a-letter | 96 | 2 | 0 | 19.83 | 3913 / 1312 / 8 | 0 | 87.08 | 7208 / 4231 / 1442 | 0 | 100.88 | 6675 / 3923 / 1889 |
| sample-b-long | 96 | 1 | 0 | 342.47 | 60179 / 24603 / 267 | 0 | 858.42 | 87566 / 45222 / 12632 | 0 | 1069.98 | 86677 / 41677 / 18366 |
| sample-b-long | 96 | 2 | 0 | 347.87 | 61062 / 25781 / 215 | 0 | 790.78 | 86020 / 42731 / 11352 | 0 | 1013.51 | 84395 / 39672 / 17215 |
| sample-b-long | 96 | 3 | 0 | 340.66 | 60623 / 25523 / 186 | 0 | 784.40 | 85452 / 42510 / 11397 | 0 | 1013.27 | 83198 / 39529 / 17347 |
| sample-b-long | 96 | 4 | 0 | 340.29 | 63068 / 24722 / 210 | 0 | 899.05 | 92104 / 47489 / 13700 | 0 | 1164.70 | 91967 / 45305 / 20291 |
| sample-b-long | 96 | 5 | 0 | 21.51 | 4685 / 1588 / 3 | 0 | 79.53 | 7727 / 4068 / 1370 | 0 | 114.35 | 8132 / 4499 / 2086 |
| timesheet-edge.en | 96 | 1 | 0 | 35.93 | 5817 / 2504 / 313 | 0 | 115.13 | 12217 / 8224 / 2036 | 0 | 162.68 | 12037 / 8616 / 2816 |
| timesheet-edge.nl | 96 | 1 | 0 | 35.31 | 5670 / 2487 / 307 | 0 | 114.22 | 12158 / 8227 / 2013 | 0 | 161.79 | 12066 / 8617 / 2834 |
| timesheet-long.en | 96 | 1 | 0 | 167.53 | 31546 / 11410 / 350 | 0 | 517.59 | 55699 / 29104 / 8081 | 0 | 718.21 | 54853 / 33027 / 13231 |
| timesheet-long.en | 96 | 2 | 0 | 198.85 | 37893 / 13364 / 385 | 0 | 531.24 | 59736 / 30325 / 8037 | 0 | 742.77 | 57800 / 33702 / 13546 |
| timesheet-long.en | 96 | 3 | 0 | 181.32 | 32688 / 12184 / 383 | 0 | 506.45 | 55077 / 28036 / 7772 | 0 | 673.69 | 52761 / 29856 / 12107 |
| timesheet-long.en | 96 | 4 | 0 | 186.19 | 35185 / 12913 / 347 | 0 | 550.75 | 59032 / 30509 / 8376 | 0 | 699.64 | 55660 / 31141 / 12677 |
| timesheet-long.en | 96 | 5 | 0 | 147.42 | 27801 / 10699 / 316 | 0 | 444.19 | 48642 / 26545 / 6800 | 0 | 623.14 | 48304 / 29403 / 11263 |
| timesheet-long.nl | 96 | 1 | 0 | 178.79 | 33203 / 12147 / 363 | 0 | 530.53 | 57667 / 29877 / 8096 | 0 | 721.00 | 56074 / 33079 / 13209 |
| timesheet-long.nl | 96 | 2 | 0 | 163.64 | 33075 / 11197 / 313 | 0 | 488.19 | 55104 / 28011 / 7493 | 0 | 661.12 | 54077 / 30292 / 11868 |
| timesheet-long.nl | 96 | 3 | 0 | 171.95 | 32358 / 11627 / 358 | 0 | 478.64 | 53895 / 27526 / 7221 | 0 | 639.98 | 51596 / 29254 / 11494 |
| timesheet-long.nl | 96 | 4 | 0 | 180.82 | 33468 / 12351 / 383 | 0 | 580.63 | 59600 / 32096 / 9173 | 0 | 745.90 | 56797 / 33752 / 13816 |
| timesheet-long.nl | 96 | 5 | 0 | 149.19 | 27936 / 10568 / 342 | 0 | 408.76 | 47143 / 24874 / 5715 | 0 | 547.17 | 44768 / 26552 / 9652 |
| timesheet-normal.en | 96 | 1 | 0 | 158.04 | 29665 / 10458 / 369 | 0 | 488.91 | 49230 / 26347 / 7667 | 0 | 632.69 | 47216 / 27292 / 11481 |
| timesheet-normal.en | 96 | 2 | 0 | 55.46 | 10502 / 3985 / 296 | 0 | 168.02 | 18906 / 10886 / 2498 | 0 | 208.36 | 17589 / 10469 / 3419 |
| timesheet-normal.nl | 300 | 1 | 0 | 96.41 | 159325 / 75143 / 3450 | 0 | 410.33 | 257161 / 178374 / 77942 | 0 | 544.59 | 269585 / 199942 / 99585 |
| timesheet-normal.nl | 300 | 2 | 0 | 27.54 | 45523 / 20256 / 1380 | 0 | 121.15 | 79837 / 56049 / 22104 | 0 | 156.67 | 87686 / 63053 / 26736 |
| timesheet-normal.nl | 96 | 1 | 0 | 165.40 | 30396 / 11012 / 369 | 0 | 496.90 | 50111 / 26987 / 7665 | 0 | 630.98 | 47314 / 27267 / 11457 |
| timesheet-normal.nl | 96 | 2 | 0 | 56.55 | 9283 / 3970 / 322 | 0 | 180.86 | 18442 / 11422 / 2856 | 0 | 225.28 | 18068 / 11651 / 4000 |

## Measured: images on the pixel grid (2026-11-18; not passed, the test is skipped until the rule is decided)

One image per page, starting on a whole pixel at 96 dpi, at whole-number scales. What the numbers show:

- At 1:1 and 3× the engine draws exactly what MuPDF draws for PNG (0 everywhere) and within 1–2 steps for JPEG (the two
  JPEG decoders' rounding). Reductions: the engine is close to MuPDF (JPEG 1, a fine PNG test pattern 24–36).
- At 2× both viewers smooth the image (they interpolate), while the engine shows each image pixel as a sharp square (PDF's
  `/Interpolate false`, the default): there the engine is the outlier (away 81–111 against MuPDF, the viewers 24–84
  apart).
- Poppler resamples every image drawn below 4×, also at 1:1, so it is far from both (MSE in the hundreds or thousands);
  the engine and MuPDF are then at the same distance from it, and the rule "to EACH reference" fails by the JPEG
  decoders' rounding (for example MSE 140.25 against 140.24).

| Image at scale | dpi | Page | E–M away | E–M MSE | E–M over 32 / 64 / 128 | E–P away | E–P MSE | E–P over 32 / 64 / 128 | M–P away | M–P MSE | M–P over 32 / 64 / 128 |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| baseline-grey.jpg at 0.5 | 96 | 1 | 1 | 0.13 | 0 / 0 / 0 | 14 | 552.41 | 36 / 26 / 5 | 14 | 551.46 | 36 / 26 / 5 |
| baseline-grey.jpg at 1 | 96 | 1 | 1 | 0.01 | 0 / 0 / 0 | 8 | 341.53 | 71 / 48 / 10 | 8 | 341.52 | 71 / 48 / 10 |
| baseline-grey.jpg at 2 | 96 | 1 | 4 | 2.06 | 0 / 0 / 0 | 4 | 197.57 | 141 / 94 / 19 | 2 | 195.60 | 141 / 94 / 19 |
| baseline-grey.jpg at 3 | 96 | 1 | 1 | 0.01 | 0 / 0 / 0 | 5 | 140.25 | 211 / 139 / 28 | 5 | 140.24 | 211 / 139 / 28 |
| baseline-rgb.jpg at 0.5 | 96 | 1 | 1 | 0.16 | 0 / 0 / 0 | 18 | 813.61 | 36 / 36 / 21 | 18 | 812.89 | 36 / 36 / 21 |
| baseline-rgb.jpg at 1 | 96 | 1 | 2 | 0.22 | 0 / 0 / 0 | 9 | 526.14 | 71 / 71 / 39 | 10 | 526.15 | 71 / 71 / 39 |
| baseline-rgb.jpg at 2 | 96 | 1 | 4 | 2.23 | 0 / 0 / 0 | 5 | 310.78 | 141 / 141 / 75 | 2 | 308.49 | 141 / 141 / 75 |
| baseline-rgb.jpg at 3 | 96 | 1 | 2 | 0.29 | 0 / 0 / 0 | 6 | 221.61 | 211 / 211 / 111 | 6 | 221.58 | 211 / 211 / 111 |
| grey8.png at 1 | 96 | 1 | 0 | 0.00 | 0 / 0 / 0 | 29 | 1164.02 | 15 / 10 / 4 | 29 | 1164.02 | 15 / 10 / 4 |
| grey8.png at 2 | 96 | 1 | 99 | 93.36 | 7 / 1 / 0 | 138 | 1166.49 | 28 / 18 / 10 | 39 | 1054.02 | 25 / 15 / 9 |
| grey8.png at 3 | 96 | 1 | 0 | 0.00 | 0 / 0 / 0 | 104 | 1162.91 | 44 / 30 / 12 | 104 | 1162.91 | 44 / 30 / 12 |
| rgb8-filters.png at 0.25 | 96 | 1 | 24 | 26.09 | 0 / 0 / 0 | 216 | 1958.56 | 168 / 85 / 40 | 195 | 1825.12 | 148 / 74 / 38 |
| rgb8-filters.png at 0.5 | 96 | 1 | 36 | 60.34 | 2 / 0 / 0 | 243 | 3299.25 | 690 / 494 / 175 | 234 | 2831.51 | 681 / 460 / 144 |
| rgb8-filters.png at 1 | 96 | 1 | 0 | 0.00 | 0 / 0 / 0 | 243 | 2824.87 | 2023 / 1455 / 348 | 243 | 2824.87 | 2023 / 1455 / 348 |
| rgb8-filters.png at 2 | 96 | 1 | 111 | 437.77 | 4442 / 467 / 0 | 173 | 862.18 | 4358 / 1376 / 252 | 84 | 419.43 | 873 / 239 / 185 |
| rgb8-filters.png at 3 | 96 | 1 | 0 | 0.00 | 0 / 0 / 0 | 205 | 940.08 | 10324 / 3675 / 979 | 205 | 940.08 | 10324 / 3675 / 979 |
| rgba8.png at 1 | 96 | 1 | 2 | 0.21 | 0 / 0 / 0 | 29 | 668.94 | 16 / 15 / 6 | 28 | 668.81 | 16 / 15 / 6 |
| rgba8.png at 2 | 96 | 1 | 81 | 33.62 | 11 / 1 / 0 | 105 | 626.18 | 32 / 29 / 10 | 24 | 586.41 | 25 / 25 / 10 |
| rgba8.png at 3 | 96 | 1 | 2 | 0.54 | 0 / 0 / 0 | 81 | 578.55 | 54 / 44 / 16 | 81 | 581.07 | 54 / 44 / 16 |

## Measured: rectangles that meet inside a pixel (2026-11-18)

Two rectangles drawn one after the other that meet at a boundary inside a pixel, on white. The boundary pixel, at the
boundary's fraction f of a pixel (same values at 96 and 300 dpi):

| Case | f | exact (one shape) | engine | MuPDF | poppler |
| --- | ---: | --- | --- | --- | --- |
| same colour 51,102,153 | 0.25 | 51,102,153 | 89,131,172 | 84,127,169 | 51,102,153 |
| same colour | 0.5 | 51,102,153 | 102,140,178 | 95,135,175 | 51,102,153 |
| same colour | 0.75 | 51,102,153 | 89,130,172 | 84,126,169 | 51,102,153 |
| red 200,0,0 then blue 0,0,200 | 0.25 | 50,0,150 | 60,48,198 | 55,42,194 | 0,0,200 |
| red then blue | 0.5 | 100,0,100 | 113,63,164 | 105,55,161 | 200,0,0 |
| red then blue | 0.75 | 150,0,50 | 160,48,98 | 149,41,100 | 200,0,0 |

The engine and MuPDF composite each rectangle with its exact (engine) or sampled (MuPDF) coverage, so the page shows
through the seam (a quarter of it at f = 0.5); poppler snaps the rectangles' edges to whole pixels, so there is no seam
but the boundary moves to a pixel edge. The samples have no such rectangles: the timesheet's 62 rectangles of page 1
overlap where they cross and never meet edge to edge.
