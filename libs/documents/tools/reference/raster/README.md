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
  pixel's 3 × 3 neighbourhood. Image rectangles grown by one pixel are left out too: both viewers fit an image to whole
  pixels before they draw it and smooth some enlargements, while the engine maps it exactly onto its rectangle as
  sample squares, as the PDF defines it (ISO 32000-1 §8.9.5.3; PDF/A forbids asking for interpolation, ISO 19005-3
  §6.2.8, veraPDF rule 6.2.8-3).
- **MSE**: Σ d² / (3 × pixels), in 8-bit steps squared.
- **over 32 / 64 / 128**: the pixels where some channel differs by more than that many steps.

The rule: for away, MSE and over 128 (a wrong pixel), the engine's distance to EACH reference is not larger than the
references' distance to each other. Over 32 and over 64 measure how an edge is drawn, and poppler is the odd one out for
a known reason (heavier glyph edges, thin rectangles snapped to whole pixels), so there the engine must not be the
outlier: its distance to the NEARER reference is not larger than the references' distance to each other. No other
tolerance; the golden PNG files (`UnitTests.Extensions.Documents.Samples/Golden/*.png`) catch any change over time.

## A sanity check, not the proof

The comparison catches gross errors, not small ones; the proof that pages are drawn right is analytic
(`UnitTests.Extensions.Documents.Render.Raster/PageGeometryTests.cs` for whole pages, the Graphics tests for coverage,
compositing and images). The rule for over 32 and over 64 was chosen after the numbers were seen: the plain rule "to
EACH reference" fails there on most page rows. The review of 2026-11-18 planted errors and ran the comparison alone. It
caught a shift of all text by 0.3 pixel or more, text 6 % larger and a lighter text colour. It did not catch text 1 %
larger, curves drawn as coarse polygons, every rectangle one pixel to the right, an image snapped to whole pixels or an
image's transparency ignored. Letters with three or more contours missing and text 3 % larger were caught only at
300 dpi: at 96 dpi body text has no pixel away from edges, so the strictest metric does not see text at all. That is why
every sample page is compared at 300 dpi as well.

Images: the image rule (a device pixel is the area-weighted mean of the sample squares it covers) is proven exactly in
`UnitTests.Extensions.Documents.Graphics/ImageResamplingTests.cs`, against analytic areas, for enlargements and
reductions by whole and fractional factors, offsets inside a pixel, mirrors, rotations and shears. The viewers are
used only where resampling plays no part: an image at 1:1 on the pixel grid, engine against MuPDF, PNG equal and JPEG
within the bound derived against djpeg for the file's sampling (1 grey, 3 colour); all pass. Poppler is not a reference
for image interiors (below).

## Measured: the samples (2026-11-18; the timesheets again on 2026-11-20)

E = engine, M = MuPDF, P = poppler. The timesheet rows were measured again after each table cell was inset by half of
its own grid line: the text in the first and last column of every row but the total row moved 0.25 pt towards the
table's edges; the ranges below did not change. In mean squared error the engine's distance to MuPDF is 0.20 to 0.34 of the
viewers' distance to each other at 96 dpi and 0.17 to 0.49 at 300 dpi; its distance to poppler is 0.55 to 0.97 of it. At
300 dpi the rule holds on 22 of 23 pages; the exception is
page 1 of timesheet-edge.nl, where 13,166 pixels differ by more than 128 steps from poppler against 13,046 between the
viewers, while the same page has 551 such pixels against MuPDF. The test pins that exception to those two numbers.

| Sample | dpi | Page | E–M away | E–M MSE | E–M over 32 / 64 / 128 | E–P away | E–P MSE | E–P over 32 / 64 / 128 | M–P away | M–P MSE | M–P over 32 / 64 / 128 |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| sample-a-letter | 300 | 1 | 1 | 93.14 | 116880 / 65022 / 6765 | 0 | 211.86 | 155742 / 108344 / 40679 | 1 | 250.06 | 143193 / 109204 / 50378 |
| sample-a-letter | 300 | 2 | 1 | 18.25 | 24336 / 13836 / 833 | 0 | 55.29 | 33533 / 24597 / 12477 | 1 | 101.39 | 41732 / 33350 / 20459 |
| sample-a-letter | 96 | 1 | 1 | 107.78 | 19109 / 8557 / 123 | 0 | 266.48 | 29529 / 14978 / 3919 | 1 | 344.44 | 28498 / 13634 / 6077 |
| sample-a-letter | 96 | 2 | 0 | 19.83 | 3913 / 1312 / 8 | 0 | 87.08 | 7208 / 4231 / 1442 | 0 | 100.88 | 6675 / 3923 / 1889 |
| sample-b-long | 300 | 1 | 0 | 371.77 | 386665 / 242916 / 35291 | 0 | 644.63 | 457329 / 328319 / 114106 | 0 | 763.36 | 417238 / 314358 / 148029 |
| sample-b-long | 300 | 2 | 0 | 382.64 | 399536 / 250459 / 36538 | 0 | 677.66 | 474531 / 341433 / 120441 | 0 | 794.98 | 431558 / 325264 / 155789 |
| sample-b-long | 300 | 3 | 0 | 374.88 | 392147 / 245702 / 35636 | 0 | 668.32 | 467244 / 336609 / 119096 | 0 | 780.74 | 424468 / 319857 / 152836 |
| sample-b-long | 300 | 4 | 0 | 386.92 | 403694 / 253051 / 37225 | 0 | 689.23 | 483581 / 347057 / 122484 | 0 | 805.70 | 437814 / 329703 / 156937 |
| sample-b-long | 300 | 5 | 0 | 41.20 | 43814 / 27126 / 3877 | 0 | 74.56 | 52644 / 37510 / 13322 | 0 | 83.33 | 47173 / 35070 / 16043 |
| sample-b-long | 96 | 1 | 0 | 342.43 | 60220 / 24588 / 261 | 0 | 859.02 | 87786 / 45272 / 12647 | 0 | 1071.02 | 86979 / 41684 / 18358 |
| sample-b-long | 96 | 2 | 0 | 347.72 | 61124 / 25770 / 215 | 0 | 791.50 | 86282 / 42791 / 11359 | 0 | 1014.92 | 84818 / 39679 / 17203 |
| sample-b-long | 96 | 3 | 0 | 340.43 | 60715 / 25494 / 186 | 0 | 785.33 | 85787 / 42598 / 11417 | 0 | 1014.96 | 83681 / 39543 / 17335 |
| sample-b-long | 96 | 4 | 0 | 338.27 | 62618 / 24686 / 204 | 0 | 896.70 | 91463 / 47510 / 13721 | 0 | 1159.24 | 90731 / 45072 / 20268 |
| sample-b-long | 96 | 5 | 0 | 36.40 | 6620 / 2597 / 24 | 0 | 93.44 | 9787 / 4943 / 1389 | 0 | 116.14 | 9403 / 4457 / 2005 |
| timesheet-edge.en | 300 | 1 | 0 | 14.21 | 23485 / 10948 / 549 | 0 | 75.76 | 51121 / 37846 / 12424 | 0 | 79.22 | 46241 / 34126 / 12580 |
| timesheet-edge.en | 96 | 1 | 0 | 35.96 | 5827 / 2509 / 314 | 0 | 115.37 | 12268 / 8253 / 2029 | 0 | 162.33 | 12042 / 8632 / 2809 |
| timesheet-edge.nl | 300 | 1 | 0 | 13.68 | 23282 / 10618 / 551 | 0 | 78.27 | 52034 / 38458 / 13166 | 0 | 81.09 | 47492 / 34832 / 13046 |
| timesheet-edge.nl | 96 | 1 | 0 | 35.56 | 5727 / 2485 / 310 | 0 | 114.39 | 12254 / 8233 / 2014 | 0 | 161.75 | 12100 / 8655 / 2838 |
| timesheet-long.en | 300 | 1 | 0 | 106.80 | 168116 / 81349 / 4841 | 0 | 393.60 | 258386 / 171298 / 68126 | 0 | 493.52 | 265359 / 185333 / 84600 |
| timesheet-long.en | 300 | 2 | 0 | 115.24 | 178562 / 84745 / 5911 | 0 | 433.83 | 264465 / 178460 / 82539 | 0 | 618.75 | 310210 / 224748 / 111283 |
| timesheet-long.en | 300 | 3 | 0 | 104.66 | 164204 / 76941 / 5293 | 0 | 429.78 | 259526 / 175382 / 80074 | 0 | 580.65 | 289466 / 208282 / 103059 |
| timesheet-long.en | 300 | 4 | 0 | 119.07 | 185700 / 90262 / 5485 | 0 | 437.04 | 278924 / 189733 / 82457 | 0 | 616.01 | 312483 / 228316 / 111206 |
| timesheet-long.en | 300 | 5 | 0 | 88.59 | 140901 / 66591 / 4014 | 0 | 356.87 | 225362 / 154065 / 63347 | 0 | 467.66 | 245015 / 175523 / 80657 |
| timesheet-long.en | 96 | 1 | 0 | 167.97 | 31691 / 11420 / 353 | 0 | 520.73 | 56007 / 29159 / 8136 | 0 | 718.09 | 54765 / 32839 / 13274 |
| timesheet-long.en | 96 | 2 | 0 | 199.54 | 38001 / 13460 / 390 | 0 | 534.56 | 60046 / 30448 / 8136 | 0 | 743.23 | 57748 / 33526 / 13643 |
| timesheet-long.en | 96 | 3 | 0 | 182.19 | 32828 / 12211 / 384 | 0 | 510.21 | 55409 / 28046 / 7857 | 0 | 673.75 | 52602 / 29500 / 12195 |
| timesheet-long.en | 96 | 4 | 0 | 187.01 | 35328 / 12965 / 350 | 0 | 554.23 | 59273 / 30641 / 8467 | 0 | 699.63 | 55452 / 30906 / 12743 |
| timesheet-long.en | 96 | 5 | 0 | 147.77 | 27921 / 10702 / 318 | 0 | 446.57 | 48841 / 26630 / 6832 | 0 | 622.77 | 48209 / 29224 / 11319 |
| timesheet-long.nl | 300 | 1 | 0 | 112.05 | 175410 / 85405 / 5204 | 0 | 424.99 | 274026 / 182498 / 74074 | 0 | 524.99 | 281576 / 196631 / 89954 |
| timesheet-long.nl | 300 | 2 | 0 | 122.76 | 188485 / 92096 / 6527 | 0 | 424.60 | 276087 / 185464 / 79658 | 0 | 594.23 | 303745 / 219641 / 107498 |
| timesheet-long.nl | 300 | 3 | 0 | 111.80 | 174393 / 83515 / 5695 | 0 | 427.75 | 268931 / 183330 / 80891 | 0 | 585.57 | 294414 / 214423 / 105395 |
| timesheet-long.nl | 300 | 4 | 0 | 112.15 | 176180 / 86121 / 4986 | 0 | 446.57 | 278893 / 190296 / 81356 | 0 | 571.90 | 296420 / 212799 / 101428 |
| timesheet-long.nl | 300 | 5 | 0 | 93.50 | 147551 / 71047 / 4361 | 0 | 344.28 | 228573 / 153322 / 62307 | 0 | 459.92 | 241793 / 174734 / 81302 |
| timesheet-long.nl | 96 | 1 | 0 | 180.48 | 33510 / 12174 / 376 | 0 | 531.95 | 57806 / 29897 / 8116 | 0 | 720.06 | 55965 / 32971 / 13256 |
| timesheet-long.nl | 96 | 2 | 0 | 165.36 | 33351 / 11313 / 317 | 0 | 490.02 | 55321 / 28071 / 7542 | 0 | 661.06 | 54000 / 30172 / 11939 |
| timesheet-long.nl | 96 | 3 | 0 | 173.79 | 32559 / 11724 / 362 | 0 | 480.75 | 54115 / 27484 / 7279 | 0 | 639.48 | 51416 / 28949 / 11585 |
| timesheet-long.nl | 96 | 4 | 0 | 182.40 | 33790 / 12448 / 396 | 0 | 582.83 | 59711 / 32160 / 9246 | 0 | 745.19 | 56505 / 33550 / 13900 |
| timesheet-long.nl | 96 | 5 | 0 | 150.61 | 28167 / 10719 / 353 | 0 | 409.72 | 47169 / 24913 / 5716 | 0 | 546.72 | 44645 / 26423 / 9693 |
| timesheet-normal.en | 300 | 1 | 0 | 96.35 | 157369 / 75015 / 3434 | 0 | 406.22 | 253322 / 176406 / 77052 | 0 | 544.83 | 267370 / 199290 / 99465 |
| timesheet-normal.en | 300 | 2 | 0 | 33.69 | 54481 / 25227 / 1505 | 0 | 135.78 | 92225 / 63590 / 25429 | 0 | 184.48 | 97040 / 72372 / 33001 |
| timesheet-normal.en | 96 | 1 | 0 | 159.30 | 29988 / 10584 / 371 | 0 | 491.66 | 49706 / 26483 / 7694 | 0 | 632.77 | 47311 / 27215 / 11529 |
| timesheet-normal.en | 96 | 2 | 0 | 55.63 | 10577 / 4008 / 296 | 0 | 168.51 | 19013 / 10932 / 2488 | 0 | 208.07 | 17626 / 10460 / 3411 |
| timesheet-normal.nl | 300 | 1 | 0 | 96.46 | 158793 / 74924 / 3441 | 0 | 429.77 | 261351 / 182782 / 82058 | 0 | 565.96 | 275161 / 205289 / 103473 |
| timesheet-normal.nl | 300 | 2 | 0 | 27.59 | 45619 / 20241 / 1379 | 0 | 124.59 | 80543 / 56755 / 22804 | 0 | 160.74 | 88478 / 63902 / 27456 |
| timesheet-normal.nl | 96 | 1 | 0 | 167.59 | 30836 / 11155 / 383 | 0 | 498.79 | 50485 / 27048 / 7675 | 0 | 630.85 | 47350 / 27156 / 11536 |
| timesheet-normal.nl | 96 | 2 | 0 | 56.98 | 9338 / 4001 / 325 | 0 | 181.07 | 18511 / 11439 / 2868 | 0 | 225.24 | 18101 / 11663 / 4014 |

## Measured: images on the pixel grid (2026-11-18; information, not a check)

One image per page, starting on a whole pixel at 96 dpi, at whole-number scales. The viewers differ from the engine
because they do not show the sample squares the PDF defines: both smooth an image enlarged 2× (MuPDF does not at 3×), and
poppler resamples every image drawn below 4×. They differ from each other because each has its own rule for when and how
to smooth. What the numbers show:

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

## The sliced sample (2026-11-18)

`sample-c-sliced` (eight pages of 300 × 204 pt: a logo and a 200 pt line sliced over pages, then a logo sliced in a table
under its repeated header row; every slice clipped) joins the comparison. It is the only sample whose pages hold little
but an image or a few lines of text. On the timesheet pages the viewers' distance to each other is large, and that
distance is the allowance. Most of it is text: where a line's baseline has a fraction of a half or more, MuPDF draws it
one whole pixel row lower than poppler (below), and poppler's thin rectangles snapped to whole pixels are the smaller
part. On page 1 of timesheet-long.en at 96 dpi the engine's mean squared error to poppler is 517.59; with its lines
drawn on poppler's rows it is 74.67, which bounds everything else, rectangles included. A page whose baselines happen to
have fractions below a half gets both viewers on the same row and so little allowance, while the engine is up to half
a pixel from both: so for text the rule is as strict as the baselines' fractions make it, and where the viewers are a
row apart a line drawn up to a row off has room to pass. On the sliced sample's pages with little but an image or a few
text lines, two differences that are present in every sample become visible: image interiors and baselines on whole
pixels (below). Nine rows break the rule; each is a named exception in `RasterReferenceTests` (`NamedExceptions`): its
raw numbers are pinned, and with its named causes removed the rule holds. A row that broke the rule with its causes
removed would be an unknown difference; there is none.

The two causes are not part of the general rule, which stays the raw comparison: a cause is removed only on a named
row where the raw rule fails, with that row's raw numbers pinned, and each named row has the fewest causes with which
it holds. Made general, the image cause would judge no clip edge around an image to the pixel: the part it leaves out
is grown by one pixel, and a clip edge one pixel off lies inside that ring. Such an edge is held by
`ClipTests.An_image_half_outside_its_clip_shows_the_exact_area_of_its_part_inside`, which fails with the clip drawn one
pixel taller or one pixel lower, as five other tests of `ClipTests` do.

| Sample | dpi | Page | E–M away | E–M MSE | E–M over 32 / 64 / 128 | E–P away | E–P MSE | E–P over 32 / 64 / 128 | M–P away | M–P MSE | M–P over 32 / 64 / 128 |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| sample-c-sliced | 96 | 1 | 0 | 75.24 | 1896 / 832 / 0 | 0 | 369.51 | 3375 / 1955 / 863 | 0 | 489.75 | 3269 / 2164 / 1148 |
| sample-c-sliced (named exception) | 96 | 2 | 0 | 39.60 | 835 / 669 / 270 | 0 | 76.09 | 1218 / 957 / 564 | 0 | 18.11 | 628 / 371 / 83 |
| sample-c-sliced (named exception) | 96 | 3 | 0 | 79.33 | 1504 / 666 / 427 | 0 | 232.70 | 2323 / 1604 / 873 | 0 | 241.29 | 1895 / 1181 / 588 |
| sample-c-sliced | 96 | 4 | 0 | 0.59 | 0 / 0 / 0 | 0 | 3.90 | 71 / 0 / 0 | 0 | 5.13 | 30 / 0 / 0 |
| sample-c-sliced (named exception) | 96 | 5 | 0 | 83.40 | 1170 / 721 / 25 | 0 | 93.84 | 1517 / 750 / 28 | 0 | 26.25 | 856 / 140 / 0 |
| sample-c-sliced (named exception) | 96 | 6 | 0 | 9.25 | 121 / 81 / 4 | 0 | 45.33 | 862 / 813 / 4 | 0 | 42.72 | 757 / 736 / 0 |
| sample-c-sliced (named exception) | 96 | 7 | 0 | 26.82 | 993 / 484 / 37 | 0 | 70.31 | 1769 / 1512 / 150 | 0 | 48.91 | 1358 / 979 / 3 |
| sample-c-sliced (named exception) | 96 | 8 | 0 | 21.53 | 572 / 211 / 4 | 0 | 55.59 | 1349 / 945 / 4 | 0 | 47.58 | 1212 / 742 / 0 |
| sample-c-sliced | 300 | 1 | 0 | 54.32 | 10040 / 4135 / 343 | 0 | 137.46 | 13386 / 8627 / 2947 | 0 | 164.53 | 11511 / 8614 / 3931 |
| sample-c-sliced | 300 | 2 | 0 | 5.84 | 2867 / 1490 / 93 | 0 | 29.62 | 5978 / 3377 / 882 | 0 | 35.86 | 6293 / 3817 / 1741 |
| sample-c-sliced | 300 | 3 | 0 | 12.70 | 4014 / 2082 / 6 | 0 | 153.93 | 10426 / 8071 / 3845 | 0 | 183.82 | 9737 / 7651 / 4766 |
| sample-c-sliced | 300 | 4 | 0 | 0.18 | 0 / 0 / 0 | 255 | 22.28 | 3403 / 490 / 237 | 255 | 22.96 | 3366 / 487 / 237 |
| sample-c-sliced | 300 | 5 | 0 | 17.57 | 4660 / 1170 / 4 | 0 | 65.65 | 7085 / 3936 / 1376 | 0 | 75.57 | 6224 / 4437 / 1750 |
| sample-c-sliced (named exception) | 300 | 6 | 1 | 2.53 | 424 / 232 / 0 | 0 | 11.04 | 1758 / 418 / 163 | 1 | 10.77 | 1728 / 355 / 160 |
| sample-c-sliced (named exception) | 300 | 7 | 1 | 12.42 | 3672 / 1637 / 885 | 0 | 21.79 | 6101 / 2611 / 849 | 1 | 21.97 | 6735 / 2250 / 757 |
| sample-c-sliced (named exception) | 300 | 8 | 1 | 6.83 | 1973 / 623 / 147 | 0 | 22.27 | 2886 / 1996 / 511 | 1 | 18.63 | 2572 / 827 / 356 |

The named exceptions with their causes removed (same columns; the mean squared error and the counts leave the image
interiors out where that cause is named, for all three pairs):

| dpi | Page | Causes removed | E–M away | E–M MSE | E–M over 32 / 64 / 128 | E–P away | E–P MSE | E–P over 32 / 64 / 128 | M–P away | M–P MSE | M–P over 32 / 64 / 128 |
| ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| 96 | 2 | image interior | 0 | 0.00 | 0 / 0 / 0 | 0 | 0.00 | 0 / 0 / 0 | 0 | 0.00 | 0 / 0 / 0 |
| 96 | 3 | baseline | 0 | 48.74 | 664 / 445 / 427 | 0 | 65.29 | 1040 / 515 / 494 | 0 | 241.29 | 1895 / 1181 / 588 |
| 96 | 5 | baseline | 0 | 4.02 | 62 / 0 / 0 | 0 | 15.82 | 588 / 18 / 0 | 0 | 26.25 | 856 / 140 / 0 |
| 96 | 6 | baseline | 0 | 0.62 | 0 / 0 / 0 | 0 | 36.89 | 758 / 740 / 0 | 0 | 42.72 | 757 / 736 / 0 |
| 96 | 7 | image interior and baseline | 0 | 0.55 | 0 / 0 / 0 | 0 | 23.15 | 220 / 202 / 0 | 0 | 26.46 | 219 / 198 / 0 |
| 96 | 8 | baseline | 0 | 6.69 | 316 / 48 / 0 | 0 | 41.06 | 1144 / 787 / 0 | 0 | 47.58 | 1212 / 742 / 0 |
| 300 | 6 | baseline | 1 | 0.62 | 145 / 0 / 0 | 0 | 9.37 | 1651 / 311 / 144 | 1 | 10.77 | 1728 / 355 / 160 |
| 300 | 7 | image interior and baseline | 1 | 1.47 | 145 / 0 / 0 | 0 | 17.91 | 811 / 312 / 144 | 1 | 21.24 | 1572 / 357 / 160 |
| 300 | 8 | image interior and baseline | 1 | 1.47 | 353 / 18 / 0 | 0 | 17.42 | 1411 / 726 / 334 | 1 | 19.38 | 1590 / 826 / 356 |

Page 2 at 96 dpi is the logo alone: with its interior left out nothing of the page is compared, and the rule holds
trivially; page 3 at 96 dpi holds with either cause removed alone, and is named with the baseline alone, so its image
slice is still judged against the viewers. With the painter planted
to ignore the clip, the comparison fails after the causes are removed: page 2 at 96 dpi (engine to each viewer, mean
squared error 3758.03 against 0.00, 4246 pixels over 128 against 0: the image drawn outside its clip) and page 7 at
300 dpi (2084.62 against 21.24), and pages 2 to 5 at 300 dpi under the plain rule.

### Cause: image interiors

The engine draws an image as exact sample squares (ISO 32000-1 §8.9.5.3); the viewers fit it to whole pixels and
smooth it. Removed by leaving out of the mean squared error and the counts over 32, 64 and 128 the part of every image
that is drawn: its rectangle intersected with the clips open around it, grown by one pixel
(`RasterComparison.ImageInteriors`). Outside the clips the page is still compared, so a painter that ignores a clip
shows there.

### Cause: baselines on whole pixels

Both viewers draw a glyph's baseline on a whole pixel row, MuPDF up to an em of 256 pixels, poppler at every size; the
engine draws it where the page model puts it. Measured on the engine's own PDF of lines of Lato 11 pt ("Hxgb") whose
baselines lie at 0.1, 0.3, 0.476, 0.5, 0.6, 0.8 and 0.95 of a pixel, by the shift of each line's ink centre from the
engine's, at 96 and 300 dpi:

| Fraction | MuPDF 96 | poppler 96 | MuPDF 300 | poppler 300 |
| ---: | ---: | ---: | ---: | ---: |
| 0.1 | −0.107 | −0.110 | −0.104 | −0.111 |
| 0.3 | −0.312 | −0.315 | −0.307 | −0.314 |
| 0.476 | −0.492 | −0.494 | −0.481 | −0.488 |
| 0.5 | +0.485 | −0.518 | +0.494 | −0.512 |
| 0.6 | +0.386 | −0.617 | +0.396 | −0.611 |
| 0.8 | +0.191 | −0.812 | +0.197 | −0.810 |
| 0.95 | +0.045 | −0.958 | +0.048 | −0.959 |

So MuPDF draws the baseline on the nearest whole pixel row and poppler on the row above it (it floors), at both
resolutions. An exact half is not decided for MuPDF at every resolution: measured on lines of Lato at 6 to 200 pt, it
went down the page in all 74 cases at 72 dpi and all 74 at 96 dpi, where half a pixel is an exact number of points
(0.5 and 0.375 pt), and either way at 120 dpi (20 of 74 up) and 300 dpi (25 of 58 up), where it is not (0.3 and
0.12 pt). The 0.5 line of the table above is one that went down; the comparison sends a half down
(`RasterComparison.MupdfRow`).

A larger glyph MuPDF draws at its exact place. Measured on the letter "H" with its baseline on a whole row and 0.3 pixel
below it, two pages of one PDF: MuPDF's two pages are equal up to an em of 256 pixels and differ from an em of
256.0001 pixels on (the second page's ink centre 0.17 to 0.31 pixel lower), at 72, 96 and 300 dpi, in Lato Regular
and Theano Didot alike; ems of 100, 200, 230, 250, 255, 255.99 and 256 equal, 256.0001, 256.001, 256.01, 256.1, 256.5,
257, 260 and 267 different. Poppler floors at every size measured (an em up to 833 pixels). The sliced sample's
200 pt "W" has an em of 266.7 pixels at 96 dpi, so MuPDF draws it at its exact place, as the engine does.
`RasterReferenceTests.MuPDF_puts_a_baseline_on_a_whole_pixel_row_up_to_an_em_of_256_pixels` holds MuPDF to that step.

In the sliced sample, page 5's line has its baseline at 110.476 px at 96 dpi; both
viewers draw it at 110 (ink centre row 105.33 in both, 105.82 in the engine, with equal ink). Removed by drawing the
engine's page again from a page model in which every glyph run's baseline is on the viewer's row, for MuPDF only the
runs with an em of at most 256 pixels (`RasterComparison.BaselinesOnPixelRows`, `MupdfRow`, `PopplerRow`,
`MupdfLargestEmOnRow`), a transformation of the test, not an option of the engine. The difference is there on every
sample (`docs/architecture.md`, "Different from the viewers").

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
