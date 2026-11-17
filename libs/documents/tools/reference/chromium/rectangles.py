# Lists every filled rectangle of a PDF, such as the pieces of a table's grid lines: one line per rectangle,
# "page x0 y0 x1 y1 height", in points with the origin at the top left of the page (as pdftotext -bbox reports words).
# Run by measure.mjs in the reference tools image (PyMuPDF): python3 rectangles.py <file.pdf>
import sys

import pymupdf

for page_number, page in enumerate(pymupdf.open(sys.argv[1]), start=1):
    for drawing in page.get_drawings():
        if drawing.get("fill") is not None:
            r = drawing["rect"]
            print(f"{page_number} {r.x0:.3f} {r.y0:.3f} {r.x1:.3f} {r.y1:.3f} {r.height:.3f}")
