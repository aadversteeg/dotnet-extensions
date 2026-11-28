# Reads back the order in which a print paints its words, its fills and strokes and its images: each one's place in
# the page's drawing sequence (sequence.py: the order of the content stream, PyMuPDF's seqno). Run by extract.sh in
# the reference tools image (PyMuPDF):
#
#   python3 order.py <file.pdf>
#
# Per page, in points, origin at the top left of the page (y down), three decimals:
#
#   word <page> <text> <place> [<place> ...]            each word of print.txt, in its order: per glyph of glyphs.txt
#                                                       the place of the text that draws it (glyphs.py matches the
#                                                       glyphs; one word can be drawn by several texts, case ib-kerning:
#                                                       "VAV", each letter in an inline box of its own)
#   paint <page> <place> <x0> <y0> <x1> <y1> <colour>   each fill or stroke, in drawing order: the box it paints (a
#                                                       stroke's path widened by half the line width), cut to the
#                                                       clips around it; left out where it lies wholly outside them
#   image <page> <place> <x0> <y0> <x1> <y1>            each image, in drawing order, cut so
#
# A colour as in print.txt (#rrggbb, #rrggbbaa where it is not opaque): a fill's colour, or a stroke's. A clip is taken
# as its box, as print.txt takes it (visible.py). The places are what a case compares: which of two things that
# overlap is painted later; their numbers themselves depend on how the PDF batches its drawings.
import sys

import pymupdf

import glyphs
import positions
import sequence
import visible


def main(path):
    for number, page in enumerate(pymupdf.open(path), start=1):
        places = sequence.places(page, number)
        for _, text, _, at in glyphs.words(page, number):
            print(f"word {number} {text} {' '.join(str(place) for place in at)}".rstrip())
        for drawing, clip in visible.drawings(page):
            place = drawing["seqno"]
            if places[place][0] not in ("fill-path", "stroke-path"):
                raise ValueError(f"page {number}: the drawing at place {place} is a {places[place][0]}")
            for paint, opacity, half in ((drawing.get("fill"), drawing.get("fill_opacity"), 0), (drawing.get("color"), drawing.get("stroke_opacity"), (drawing.get("width") or 0) / 2)):
                box = (drawing["rect"].x0 - half, drawing["rect"].y0 - half, drawing["rect"].x1 + half, drawing["rect"].y1 + half)
                if paint is not None and (shown := visible.cut(box, clip)) is not None:
                    drawn = positions.colour(*(round(part * 255) for part in paint), round((opacity or 1) * 255))
                    print(f"paint {number} {place} {shown[0]:.3f} {shown[1]:.3f} {shown[2]:.3f} {shown[3]:.3f} {drawn}")
        images = page.get_image_info()
        drawn_images = [index for index, entry in enumerate(places) if entry[0] == "fill-image"]
        if len(images) != len(drawn_images):
            raise ValueError(f"page {number}: {len(images)} images listed, {len(drawn_images)} drawn")
        for image, place in zip(images, drawn_images):
            if (shown := visible.cut(tuple(image["bbox"]), places[place][2])) is not None:
                print(f"image {number} {place} {shown[0]:.3f} {shown[1]:.3f} {shown[2]:.3f} {shown[3]:.3f}")


if __name__ == "__main__":
    main(sys.argv[1])
