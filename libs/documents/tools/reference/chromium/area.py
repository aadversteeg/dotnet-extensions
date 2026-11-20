# The width of the page area of each page of a print, from the clip Chromium opens around the page's body (visible.py,
# body_clip): one line per page, "page width", the width in points of the clip's box, or "page none" for a page that
# opens no such clip (a page whose body draws nothing, such as the empty page a forced break leaves). Chromium clips the
# body to the page area's own size, so this is the width the page area has; measure.mjs lays the boxes out at it
# (README.md, "Box sizes").
#
#   python3 area.py <case>.pdf
import sys

import pymupdf

import visible

for number, page in enumerate(pymupdf.open(sys.argv[1]), 1):
    clip = visible.body_clip(page)
    print(f"{number} none" if clip is None else f"{number} {clip[2] - clip[0]:.3f}")
