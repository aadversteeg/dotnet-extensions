#!/usr/bin/env bash
# Reads Chromium's prints back with independent tools, in the reference tools image (../tools.Dockerfile): every word
# with its box (poppler's pdftotext -bbox) and every filled rectangle (rectangles.py, PyMuPDF). Run by measure.mjs and
# measure.ps1 with the output folder at /out and this folder at /src:
#
#   bash /src/extract.sh <case> [<case> ...]
#
# Writes /out/<case>.bbox.html and /out/<case>.lines.txt per case, and the tool versions to /out/tools-versions.txt.
set -eu -o pipefail
{
  pdftotext -v 2>&1 | head -1
  python3 -c "import pymupdf; print('PyMuPDF', pymupdf.VersionBind)"
} > /out/tools-versions.txt
for name in "$@"; do
  pdftotext -bbox "/out/$name.pdf" "/out/$name.bbox.html"
  python3 /src/rectangles.py "/out/$name.pdf" > "/out/$name.lines.txt"
done
cat /out/tools-versions.txt
