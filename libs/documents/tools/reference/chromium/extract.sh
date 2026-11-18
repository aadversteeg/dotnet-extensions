#!/usr/bin/env bash
# Reads Chromium's prints back with independent tools, in the reference tools image (../tools.Dockerfile): every word
# with its box (poppler's pdftotext -bbox), every filled rectangle (rectangles.py, PyMuPDF) and the print's positions
# page by page (positions.py, PyMuPDF: words with their baselines, rectangles, images). Run by measure.mjs and
# measure.ps1 with the output folder at /out and this folder at /src:
#
#   bash /src/extract.sh <case> [<case> ...]
#
# Writes /out/<case>.bbox.html, /out/<case>.lines.txt and /out/<case>.print.txt per case, and the tool versions to
# /out/tools-versions.txt.
set -eu -o pipefail
{
  pdftotext -v 2>&1 | head -1
  python3 -c "import pymupdf; print('PyMuPDF', pymupdf.VersionBind)"
} > /out/tools-versions.txt
for name in "$@"; do
  pdftotext -bbox "/out/$name.pdf" "/out/$name.bbox.html"
  python3 /src/rectangles.py "/out/$name.pdf" > "/out/$name.lines.txt"
  python3 /src/positions.py "/out/$name.pdf" > "/out/$name.print.txt"
done
cat /out/tools-versions.txt
