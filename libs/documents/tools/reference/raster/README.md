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

## Measured: the samples (2026-11-18)

E = engine, M = MuPDF, P = poppler. In mean squared error the engine's distance to MuPDF is 0.19 to 0.34 of the
viewers' distance to each other at 96 dpi and 0.17 to 0.50 at 300 dpi; its distance to poppler is 0.55 to 0.97 of it. At
300 dpi the rule holds on 22 of 23 pages; the exception is
page 1 of timesheet-edge.nl, where 12,753 pixels differ by more than 128 steps from poppler against 12,569 between the
viewers, while the same page has 552 such pixels against MuPDF. The test pins that exception to those two numbers.

| Sample | dpi | Page | E–M away | E–M MSE | E–M over 32 / 64 / 128 | E–P away | E–P MSE | E–P over 32 / 64 / 128 | M–P away | M–P MSE | M–P over 32 / 64 / 128 |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| sample-a-letter | 300 | 1 | 1 | 93.14 | 116880 / 65022 / 6765 | 0 | 211.86 | 155742 / 108344 / 40679 | 1 | 250.06 | 143193 / 109204 / 50378 |
| sample-a-letter | 300 | 2 | 1 | 18.25 | 24336 / 13836 / 833 | 0 | 55.29 | 33533 / 24597 / 12477 | 1 | 101.39 | 41732 / 33350 / 20459 |
| sample-a-letter | 96 | 1 | 1 | 107.78 | 19109 / 8557 / 123 | 0 | 266.48 | 29529 / 14978 / 3919 | 1 | 344.44 | 28498 / 13634 / 6077 |
| sample-a-letter | 96 | 2 | 0 | 19.83 | 3913 / 1312 / 8 | 0 | 87.08 | 7208 / 4231 / 1442 | 0 | 100.88 | 6675 / 3923 / 1889 |
| sample-b-long | 300 | 1 | 0 | 372.28 | 387139 / 243132 / 35486 | 0 | 638.19 | 456081 / 326609 / 112228 | 0 | 757.99 | 415726 / 312762 / 146568 |
| sample-b-long | 300 | 2 | 0 | 383.32 | 400168 / 250747 / 36798 | 0 | 669.07 | 472867 / 339153 / 117937 | 0 | 787.82 | 429542 / 323136 / 153841 |
| sample-b-long | 300 | 3 | 0 | 375.73 | 392937 / 246062 / 35961 | 0 | 657.57 | 465164 / 333759 / 115966 | 0 | 771.79 | 421948 / 317197 / 150401 |
| sample-b-long | 300 | 4 | 0 | 397.05 | 413452 / 259312 / 38573 | 0 | 697.71 | 493155 / 353156 / 123078 | 0 | 820.00 | 446651 / 336021 / 159315 |
| sample-b-long | 300 | 5 | 0 | 31.58 | 34530 / 21081 / 2724 | 0 | 59.64 | 41822 / 29701 / 10850 | 0 | 63.65 | 36824 / 27156 / 12204 |
| sample-b-long | 96 | 1 | 0 | 342.47 | 60179 / 24603 / 267 | 0 | 858.42 | 87566 / 45222 / 12632 | 0 | 1069.98 | 86677 / 41677 / 18366 |
| sample-b-long | 96 | 2 | 0 | 347.87 | 61062 / 25781 / 215 | 0 | 790.78 | 86020 / 42731 / 11352 | 0 | 1013.51 | 84395 / 39672 / 17215 |
| sample-b-long | 96 | 3 | 0 | 340.66 | 60623 / 25523 / 186 | 0 | 784.40 | 85452 / 42510 / 11397 | 0 | 1013.27 | 83198 / 39529 / 17347 |
| sample-b-long | 96 | 4 | 0 | 340.29 | 63068 / 24722 / 210 | 0 | 899.05 | 92104 / 47489 / 13700 | 0 | 1164.70 | 91967 / 45305 / 20291 |
| sample-b-long | 96 | 5 | 0 | 21.51 | 4685 / 1588 / 3 | 0 | 79.53 | 7727 / 4068 / 1370 | 0 | 114.35 | 8132 / 4499 / 2086 |
| timesheet-edge.en | 300 | 1 | 0 | 14.30 | 23769 / 11005 / 552 | 0 | 73.56 | 50659 / 37346 / 12004 | 0 | 77.04 | 45563 / 33505 / 12230 |
| timesheet-edge.en | 96 | 1 | 0 | 35.93 | 5817 / 2504 / 313 | 0 | 115.13 | 12217 / 8224 / 2036 | 0 | 162.68 | 12037 / 8616 / 2816 |
| timesheet-edge.nl | 300 | 1 | 0 | 13.69 | 23398 / 10639 / 552 | 0 | 76.26 | 51627 / 38045 / 12753 | 0 | 78.45 | 46833 / 34192 / 12569 |
| timesheet-edge.nl | 96 | 1 | 0 | 35.31 | 5670 / 2487 / 307 | 0 | 114.22 | 12158 / 8227 / 2013 | 0 | 161.79 | 12066 / 8617 / 2834 |
| timesheet-long.en | 300 | 1 | 0 | 107.02 | 168687 / 81494 / 4852 | 0 | 379.67 | 255394 / 168427 / 65159 | 0 | 478.25 | 261321 / 181819 / 81934 |
| timesheet-long.en | 300 | 2 | 0 | 115.38 | 178977 / 84805 / 5926 | 0 | 418.76 | 261402 / 175539 / 79448 | 0 | 602.01 | 306481 / 221356 / 108395 |
| timesheet-long.en | 300 | 3 | 0 | 104.75 | 165247 / 76815 / 5301 | 0 | 414.52 | 256531 / 172973 / 76866 | 0 | 563.91 | 285737 / 204890 / 100171 |
| timesheet-long.en | 300 | 4 | 0 | 119.21 | 185883 / 90282 / 5489 | 0 | 421.06 | 275771 / 186473 / 79012 | 0 | 598.64 | 308042 / 224408 / 108125 |
| timesheet-long.en | 300 | 5 | 0 | 88.68 | 140962 / 66507 / 4022 | 0 | 344.86 | 222914 / 151650 / 60870 | 0 | 454.47 | 241816 / 172671 / 78344 |
| timesheet-long.en | 96 | 1 | 0 | 167.53 | 31546 / 11410 / 350 | 0 | 517.59 | 55699 / 29104 / 8081 | 0 | 718.21 | 54853 / 33027 / 13231 |
| timesheet-long.en | 96 | 2 | 0 | 198.85 | 37893 / 13364 / 385 | 0 | 531.24 | 59736 / 30325 / 8037 | 0 | 742.77 | 57800 / 33702 / 13546 |
| timesheet-long.en | 96 | 3 | 0 | 181.32 | 32688 / 12184 / 383 | 0 | 506.45 | 55077 / 28036 / 7772 | 0 | 673.69 | 52761 / 29856 / 12107 |
| timesheet-long.en | 96 | 4 | 0 | 186.19 | 35185 / 12913 / 347 | 0 | 550.75 | 59032 / 30509 / 8376 | 0 | 699.64 | 55660 / 31141 / 12677 |
| timesheet-long.en | 96 | 5 | 0 | 147.42 | 27801 / 10699 / 316 | 0 | 444.19 | 48642 / 26545 / 6800 | 0 | 623.14 | 48304 / 29403 / 11263 |
| timesheet-long.nl | 300 | 1 | 0 | 112.00 | 175594 / 85457 / 5207 | 0 | 410.36 | 271241 / 179703 / 70653 | 0 | 508.55 | 277451 / 192990 / 87046 |
| timesheet-long.nl | 300 | 2 | 0 | 122.64 | 188421 / 92028 / 6536 | 0 | 409.40 | 273136 / 182442 / 76209 | 0 | 577.19 | 299429 / 215835 / 104464 |
| timesheet-long.nl | 300 | 3 | 0 | 111.64 | 174852 / 83329 / 5699 | 0 | 412.33 | 265846 / 180791 / 77324 | 0 | 568.53 | 290098 / 210617 / 102361 |
| timesheet-long.nl | 300 | 4 | 0 | 112.06 | 176055 / 86126 / 4985 | 0 | 430.66 | 275921 / 187261 / 77857 | 0 | 554.17 | 291796 / 208749 / 98253 |
| timesheet-long.nl | 300 | 5 | 0 | 93.34 | 147100 / 70912 / 4360 | 0 | 332.87 | 226386 / 151017 / 59734 | 0 | 447.10 | 238517 / 171848 / 79010 |
| timesheet-long.nl | 96 | 1 | 0 | 178.79 | 33203 / 12147 / 363 | 0 | 530.53 | 57667 / 29877 / 8096 | 0 | 721.00 | 56074 / 33079 / 13209 |
| timesheet-long.nl | 96 | 2 | 0 | 163.64 | 33075 / 11197 / 313 | 0 | 488.19 | 55104 / 28011 / 7493 | 0 | 661.12 | 54077 / 30292 / 11868 |
| timesheet-long.nl | 96 | 3 | 0 | 171.95 | 32358 / 11627 / 358 | 0 | 478.64 | 53895 / 27526 / 7221 | 0 | 639.98 | 51596 / 29254 / 11494 |
| timesheet-long.nl | 96 | 4 | 0 | 180.82 | 33468 / 12351 / 383 | 0 | 580.63 | 59600 / 32096 / 9173 | 0 | 745.90 | 56797 / 33752 / 13816 |
| timesheet-long.nl | 96 | 5 | 0 | 149.19 | 27936 / 10568 / 342 | 0 | 408.76 | 47143 / 24874 / 5715 | 0 | 547.17 | 44768 / 26552 / 9652 |
| timesheet-normal.en | 300 | 1 | 0 | 96.65 | 158182 / 75411 / 3456 | 0 | 387.45 | 249246 / 172018 / 73051 | 0 | 524.69 | 261900 / 194106 / 95833 |
| timesheet-normal.en | 300 | 2 | 0 | 33.76 | 54609 / 25259 / 1514 | 0 | 131.39 | 91239 / 62505 / 24508 | 0 | 179.96 | 95891 / 71246 / 32185 |
| timesheet-normal.en | 96 | 1 | 0 | 158.04 | 29665 / 10458 / 369 | 0 | 488.91 | 49230 / 26347 / 7667 | 0 | 632.69 | 47216 / 27292 / 11481 |
| timesheet-normal.en | 96 | 2 | 0 | 55.46 | 10502 / 3985 / 296 | 0 | 168.02 | 18906 / 10886 / 2498 | 0 | 208.36 | 17589 / 10469 / 3419 |
| timesheet-normal.nl | 300 | 1 | 0 | 96.41 | 159325 / 75143 / 3450 | 0 | 410.33 | 257161 / 178374 / 77942 | 0 | 544.59 | 269585 / 199942 / 99585 |
| timesheet-normal.nl | 300 | 2 | 0 | 27.54 | 45523 / 20256 / 1380 | 0 | 121.15 | 79837 / 56049 / 22104 | 0 | 156.67 | 87686 / 63053 / 26736 |
| timesheet-normal.nl | 96 | 1 | 0 | 165.40 | 30396 / 11012 / 369 | 0 | 496.90 | 50111 / 26987 / 7665 | 0 | 630.98 | 47314 / 27267 / 11457 |
| timesheet-normal.nl | 96 | 2 | 0 | 56.55 | 9283 / 3970 / 322 | 0 | 180.86 | 18442 / 11422 / 2856 | 0 | 225.28 | 18068 / 11651 / 4000 |

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
but an image or a few lines of text. On the timesheet pages the viewers' distance to each other is large, because
poppler snaps thin rectangles and draws heavier edges, and that distance is the allowance. A page with little but an
image or a few text lines has no such allowance, so two differences that are present in every sample become visible:
image interiors and baselines on whole pixels (below). Nine rows break the rule; each is a named exception in
`RasterReferenceTests` (`NamedExceptions`): its raw numbers are pinned, and with its named causes removed the rule
holds. A row that broke the rule with its causes removed would be an unknown difference; there is none.

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
| 96 | 3 | image interior and baseline | 0 | 7.57 | 149 / 0 / 0 | 0 | 17.73 | 464 / 2 / 0 | 0 | 333.93 | 1763 / 1111 / 535 |
| 96 | 5 | baseline | 0 | 4.02 | 62 / 0 / 0 | 0 | 15.82 | 588 / 18 / 0 | 0 | 26.25 | 856 / 140 / 0 |
| 96 | 6 | baseline | 0 | 0.62 | 0 / 0 / 0 | 0 | 36.89 | 758 / 740 / 0 | 0 | 42.72 | 757 / 736 / 0 |
| 96 | 7 | image interior and baseline | 0 | 0.55 | 0 / 0 / 0 | 0 | 23.15 | 220 / 202 / 0 | 0 | 26.46 | 219 / 198 / 0 |
| 96 | 8 | baseline | 0 | 6.69 | 316 / 48 / 0 | 0 | 41.06 | 1144 / 787 / 0 | 0 | 47.58 | 1212 / 742 / 0 |
| 300 | 6 | baseline | 1 | 0.62 | 145 / 0 / 0 | 0 | 9.37 | 1651 / 311 / 144 | 1 | 10.77 | 1728 / 355 / 160 |
| 300 | 7 | image interior and baseline | 1 | 1.47 | 145 / 0 / 0 | 0 | 17.91 | 811 / 312 / 144 | 1 | 21.24 | 1572 / 357 / 160 |
| 300 | 8 | image interior and baseline | 1 | 1.47 | 353 / 18 / 0 | 0 | 17.42 | 1411 / 726 / 334 | 1 | 19.38 | 1590 / 826 / 356 |

Page 2 at 96 dpi is the logo alone: with its interior left out nothing of the page is compared, and the rule holds
trivially; page 3 at 96 dpi holds with either cause removed alone, and both are on the page. With the painter planted
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

So MuPDF draws the baseline on the nearest whole pixel row (a half goes down the page) and poppler on the row above it
(it floors), at both resolutions.

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
