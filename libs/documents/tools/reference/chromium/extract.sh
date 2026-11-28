#!/usr/bin/env bash
# Reads Chromium's prints back with independent tools, in the reference tools image (../tools.Dockerfile): every word
# with its box (poppler's pdftotext -bbox), every filled rectangle (rectangles.py, PyMuPDF) and the print's positions
# page by page (positions.py, PyMuPDF: words with their baselines, rectangles, images, the area a curve paints), and the gaps of a decoration line
# that skips ink (ink.py, PyMuPDF; only for a print that has any), the link annotations (links.py, PyMuPDF; only for a
# print that has any), and the width of each page's page area (area.py,
# PyMuPDF), at which measure.mjs then measures the boxes. Run by measure.mjs and
# measure.ps1 with the output folder at /out and this folder at /src:
#
#   bash /src/extract.sh <case> [<case> ...]
#
# Writes /out/<case>.bbox.html, /out/<case>.lines.txt, /out/<case>.print.txt and /out/<case>.area per case,
# /out/<case>.ink.txt for a case
# whose print has a clipped decoration line, /out/<case>.links.txt for a case whose print has link annotations, and the
# tool versions to
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
  python3 /src/area.py "/out/$name.pdf" > "/out/$name.area"
  python3 /src/ink.py "/out/$name.pdf" > "/out/$name.ink.txt"
  if [ ! -s "/out/$name.ink.txt" ]; then rm "/out/$name.ink.txt"; fi
  python3 /src/links.py "/out/$name.pdf" > "/out/$name.links.txt"
  if [ ! -s "/out/$name.links.txt" ]; then rm "/out/$name.links.txt"; fi
  python3 /src/glyphs.py "/out/$name.pdf" > "/out/$name.glyphs.txt"
  python3 /src/order.py "/out/$name.pdf" > "/out/$name.order.txt"
  python3 /src/clips.py "/out/$name.pdf" > "/out/$name.clips.txt"
  python3 /src/images.py "/out/$name.pdf" > "/out/$name.images.txt"
  if [ ! -s "/out/$name.images.txt" ]; then rm "/out/$name.images.txt"; fi
done
cat /out/tools-versions.txt
