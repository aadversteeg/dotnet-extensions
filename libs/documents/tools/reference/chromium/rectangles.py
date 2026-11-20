# Lists every filled rectangle of a PDF, such as the pieces of a table's grid lines: one line per rectangle,
# "page x0 y0 x1 y1 height", in points with the origin at the top left of the page (as pdftotext -bbox reports words).
# Each rectangle is the part of the fill that the print shows, inside the clips it is drawn under; a fill wholly outside
# them is not listed (visible.py).
# Run by measure.mjs in the reference tools image (PyMuPDF): python3 rectangles.py <file.pdf>
import sys

import pymupdf

import visible

for page_number, page in enumerate(pymupdf.open(sys.argv[1]), start=1):
    for drawing, clip in visible.drawings(page):
        if drawing.get("fill") is not None and (shown := visible.cut(tuple(drawing["rect"]), clip)) is not None:
            x0, y0, x1, y1 = shown
            print(f"{page_number} {x0:.3f} {y0:.3f} {x1:.3f} {y1:.3f} {y1 - y0:.3f}")
