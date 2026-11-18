# Reads back where Chromium's print put things, page by page, from the PDF alone: every page with its size, every word
# with its page, its baseline and its horizontal extent, and every filled rectangle (backgrounds, borders, grid lines,
# marker boxes a case draws on purpose). Run by extract.sh in the reference tools image (PyMuPDF):
#
#   python3 positions.py <file.pdf>
#
# One line per item, in points, origin at the top left of the page (y grows downwards), three decimals:
#
#   page <page> <width> <height>
#   word <page> <x0> <x1> <baseline> <font size> <text>
#   rect <page> <x0> <y0> <x1> <y1>
#   image <page> <x0> <y0> <x1> <y1>
#
# A word is a run of characters without white space on one baseline, in the order the PDF draws them; x0 is the
# origin of its first character and x1 the origin of its last plus that character's advance, so the extent is the
# advances of the word (not its ink), which is what a layout engine places. The baseline is the origin of the
# characters: it does not depend on the ascent and descent a reader assumes for the font, as a bounding box does.
# Words come per page sorted by baseline and then x0; rectangles in drawing order; an image is the rectangle it is
# drawn into, which may reach past the page when the page shows only a slice of it.
import sys

import pymupdf


def words(page):
    found = []
    for block in page.get_text("rawdict")["blocks"]:
        for line in block.get("lines", []):
            for span in line["spans"]:
                current = []
                for char in span["chars"]:
                    if char["c"].isspace():
                        if current:
                            found.append((span, current))
                        current = []
                    else:
                        current.append(char)
                if current:
                    found.append((span, current))
    result = []
    for span, chars in found:
        x0 = chars[0]["origin"][0]
        x1 = chars[-1]["bbox"][2]
        baseline = chars[0]["origin"][1]
        result.append((baseline, x0, x1, span["size"], "".join(char["c"] for char in chars)))
    return sorted(result, key=lambda word: (round(word[0], 3), round(word[1], 3)))


for number, page in enumerate(pymupdf.open(sys.argv[1]), start=1):
    print(f"page {number} {page.rect.width:.3f} {page.rect.height:.3f}")
    for baseline, x0, x1, size, text in words(page):
        print(f"word {number} {x0:.3f} {x1:.3f} {baseline:.3f} {size:.3f} {text}")
    for drawing in page.get_drawings():
        if drawing.get("fill") is not None:
            r = drawing["rect"]
            print(f"rect {number} {r.x0:.3f} {r.y0:.3f} {r.x1:.3f} {r.y1:.3f}")
    for image in page.get_image_info():
        x0, y0, x1, y1 = image["bbox"]
        print(f"image {number} {x0:.3f} {y0:.3f} {x1:.3f} {y1:.3f}")
