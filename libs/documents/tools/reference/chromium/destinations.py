# Reads back the left edge of each link's destination inside the document, which links.txt does not hold (it holds the
# destination's page and top). Run by extract.sh in the reference tools image (PyMuPDF):
#
#   python3 destinations.py <file.pdf>
#
# One line per link annotation, in the order of links.txt (page by page in the order of each page's /Annots):
#
#   <page> dest <p> <left>     a link to a destination in the document: the page <p> and the left of its /XYZ
#                              destination (ISO 32000-1 §12.3.2, Table 151), in points from the left of that page, as the
#                              file holds it (a named destination looked up in the catalog's /Dests, as links.py does)
#   <page> uri                 a link with a URI action: no destination
#
# Chromium writes a destination at the target's place in the coordinates of the page area, so its left is one page
# margin left of where the target stands on the page, at a whole CSS pixel (README, links.txt). Writes nothing for a
# print without annotations.
import sys

import pymupdf

import links


def main(path):
    document = pymupdf.open(path)
    pages = [page.xref for page in document]
    for number, _, xref, _ in links.annotations(document):
        if links.value(document, xref, "A/S") == ("name", "/URI"):
            print(f"{number} uri")
            continue
        found = links.resolved(document, links.value(document, xref, "Dest")[1], number)
        target = document[pages.index(int(found.group(1)))]
        left = (pymupdf.Point(float(found.group(2)), 0) * target.transformation_matrix).x
        print(f"{number} dest {target.number + 1} {left:.3f}")


if __name__ == "__main__":
    main(sys.argv[1])
