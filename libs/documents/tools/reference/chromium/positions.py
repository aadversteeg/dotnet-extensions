# Reads back where Chromium's print put things, page by page, from the PDF alone: every page with its size, every word
# with its page, its baseline and its horizontal extent, and every filled rectangle (backgrounds, borders, grid lines,
# marker boxes a case draws on purpose). Run by extract.sh in the reference tools image (PyMuPDF):
#
#   python3 positions.py <file.pdf>
#
# One line per item, in points, origin at the top left of the page (y grows downwards), three decimals:
#
#   page <page> <width> <height>
#   word <page> <x0> <x1> <baseline> <font size> <text> <colour> <face>
#   rect <page> <x0> <y0> <x1> <y1> <colour>
#   stroke <page> <x0> <y0> <x1> <y1> <colour>
#   image <page> <x0> <y0> <x1> <y1>
#
# A word is a run of characters without white space on one baseline, in the order the PDF draws them; x0 is the
# origin of its first character and x1 the origin of its last plus that character's advance, so the extent is the
# advances of the word (not its ink), which is what a layout engine places. The baseline is the origin of the
# characters: it does not depend on the ascent and descent a reader assumes for the font, as a bounding box does.
# Words come per page sorted by baseline and then x0; rectangles in drawing order; an image is the rectangle it is
# drawn into. A rectangle, a stroke's band and an image are written as the part the print shows, inside the clips they
# are drawn under, and not at all where they lie wholly outside them (visible.py: Chromium clips each page's body to the
# page area and draws what passes it all the same); words are written whole. A colour is #rrggbb, with the
# opacity as a fourth byte (#rrggbbaa) where it is not 1: a word's is its text's fill colour, a rectangle's its fill.
# The face is the PostScript name of the font the word is drawn with, without the subset tag ("ABCDEF+") a PDF writer
# puts before it: it names the font file, and with it the weight and the style. The colour and the face come last on a
# line, after the fields that were there before them, so a file measured before they were added differs only in them.
# A stroked rectangle (Chromium strokes a border of one colour and style on all four sides as one rectangle path, with
# mitred corners) is written as the four rectangles its stroke paints, in the stroke's colour and in drawing order
# among the filled ones: the top and the bottom band across the whole outer width, the left and the right band between
# them, each on a stroke line (rectangles.py reads fills only, so lines.txt does not hold them). Any other stroke stops
# the read-back, so a print never loses what it paints without a word.
import re
import sys

import pymupdf

import visible


def colour(red, green, blue, alpha=255):
    value = f"#{red:02x}{green:02x}{blue:02x}"
    return value if alpha == 255 else f"{value}{alpha:02x}"


def face(name):
    return re.sub(r"^[A-Z]{6}\+", "", name)


def stroked(drawing):
    if [item[0] for item in drawing["items"]] != ["re"] or drawing.get("dashes") not in (None, "[] 0") \
            or drawing.get("lineJoin") != 0:
        raise ValueError(f"a stroke this read-back does not know: {drawing}")
    r, half = drawing["items"][0][1], drawing["width"] / 2
    return [(r.x0 - half, r.y0 - half, r.x1 + half, r.y0 + half), (r.x0 - half, r.y1 - half, r.x1 + half, r.y1 + half),
            (r.x0 - half, r.y0 + half, r.x0 + half, r.y1 - half), (r.x1 - half, r.y0 + half, r.x1 + half, r.y1 - half)]


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
        rgb = span["color"]
        drawn = colour(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF, span["alpha"])
        result.append((baseline, x0, x1, span["size"], "".join(char["c"] for char in chars), drawn, face(span["font"])))
    return sorted(result, key=lambda word: (round(word[0], 3), round(word[1], 3)))


for number, page in enumerate(pymupdf.open(sys.argv[1]), start=1):
    print(f"page {number} {page.rect.width:.3f} {page.rect.height:.3f}")
    for baseline, x0, x1, size, text, drawn, name in words(page):
        print(f"word {number} {x0:.3f} {x1:.3f} {baseline:.3f} {size:.3f} {text} {drawn} {name}")
    for drawing, clip in visible.drawings(page):
        if drawing.get("fill") is not None and (shown := visible.cut(tuple(drawing["rect"]), clip)) is not None:
            fill = colour(*(round(part * 255) for part in drawing["fill"]), round((drawing.get("fill_opacity") or 1) * 255))
            print(f"rect {number} {shown[0]:.3f} {shown[1]:.3f} {shown[2]:.3f} {shown[3]:.3f} {fill}")
        if drawing.get("color") is not None:
            for band in stroked(drawing):
                if (shown := visible.cut(band, clip)) is not None:
                    ink = colour(*(round(part * 255) for part in drawing["color"]), round((drawing.get("stroke_opacity") or 1) * 255))
                    print(f"stroke {number} {shown[0]:.3f} {shown[1]:.3f} {shown[2]:.3f} {shown[3]:.3f} {ink}")
    images, clips = page.get_image_info(), visible.image_clips(page)
    if len(images) != len(clips):
        raise ValueError(f"page {number}: {len(images)} images listed, {len(clips)} drawn")
    for image, clip in zip(images, clips):
        if (shown := visible.cut(tuple(image["bbox"]), clip)) is not None:
            print(f"image {number} {shown[0]:.3f} {shown[1]:.3f} {shown[2]:.3f} {shown[3]:.3f}")
