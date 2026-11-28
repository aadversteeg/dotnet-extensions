# Reads back the clips each word of a print is drawn under: the rectangle the clips open around the text that draws
# the word's first glyph leave (sequence.py: a clip taken as its box, as print.txt takes the clips of rectangles and images, visible.py),
# and whether every one of those clips is one rectangle or one of them is another path (an even-odd path with a hole, a
# curve). Run by extract.sh in the reference tools image (PyMuPDF):
#
#   python3 clips.py <file.pdf>
#
# One line per word of print.txt, in its order, in points, origin at the top left of the page (y down), three decimals:
#
#   <page> <text> <x0> <y0> <x1> <y1> rect|path     the rectangle the clips leave
#   <page> <text> none                              no clip is open around the word
#
# Only the box of a clip path is kept: a word under a clip that is no rectangle is compared with the clip's box, and
# the line says so.
import sys

import pymupdf

import glyphs
import sequence


def main(path):
    for number, page in enumerate(pymupdf.open(path), start=1):
        places = sequence.places(page, number)
        for _, text, _, at in glyphs.words(page, number):
            if not at:
                raise ValueError(f"page {number}: the word '{text}' draws no glyph")
            kind, _, clip, rectangle = places[at[0]]
            if kind not in ("fill-text", "stroke-text", "ignore-text"):
                raise ValueError(f"page {number}: the text of '{text}' is at place {place}, a {kind}")
            if clip is None:
                print(f"{number} {text} none")
            else:
                print(f"{number} {text} {clip[0]:.3f} {clip[1]:.3f} {clip[2]:.3f} {clip[3]:.3f} {'rect' if rectangle else 'path'}")


if __name__ == "__main__":
    main(sys.argv[1])
