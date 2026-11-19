# Lists the gaps Chromium leaves in an underline or overline around a glyph's ink (text-decoration-skip-ink: auto). Chromium
# paints such a line as one filled rectangle inside clips that each cut a rectangle out of the page (Blink
# TextPainter::ClipDecorationLine, "ClipOut"; PyMuPDF reports them as even-odd clip paths). For every filled rectangle
# painted under at least one such clip, in points with the origin at the top left of the page (as rectangles.py):
#
#   fill <page> <x0> <y0> <x1> <y1>      the rectangle as painted
#   gap <page> <x0> <x1>                 the hole one clip cuts across the rectangle's height, in paint order of the
#                                        clips, not cut to the rectangle (raw)
#   piece <page> <x0> <x1>               what is left visible: the rectangle minus the union of the gaps
#
# A clip that does not cut the rectangle across its whole height (so the visible line is not a set of rectangles) is
# written as "uneven <page> <x0> <y0> <x1> <y1>" with the clip's box; a curve in a clip as "curve <page>". Writes nothing
# for a print without such clips. Run by extract.sh in the reference tools image: python3 ink.py <file.pdf>
import sys

import pymupdf

EPSILON = 1e-3


def segments(items):
    """The straight segments of a clip path (rectangles and quads as their four sides), or None for a curve."""
    out = []
    for item in items:
        kind = item[0]
        if kind == "l":
            out.append((item[1], item[2]))
        elif kind == "re":
            r = item[1]
            corners = [pymupdf.Point(r.x0, r.y0), pymupdf.Point(r.x1, r.y0), pymupdf.Point(r.x1, r.y1), pymupdf.Point(r.x0, r.y1)]
            out += [(corners[i], corners[(i + 1) % 4]) for i in range(4)]
        elif kind == "qu":
            q = item[1]
            corners = [q.ul, q.ur, q.lr, q.ll]
            out += [(corners[i], corners[(i + 1) % 4]) for i in range(4)]
        else:
            return None
    return out


def cut(clip, fill):
    """The x intervals of the fill that the even-odd clip leaves out, or 'uneven' / 'curve'."""
    sides = segments(clip["items"])
    if sides is None:
        return "curve"
    vertical = []
    for a, b in sides:
        top, bottom = min(a.y, b.y), max(a.y, b.y)
        if abs(a.x - b.x) > EPSILON:
            # A horizontal or slanted side inside the fill's height makes the clip differ across the line.
            if fill.y0 + EPSILON < top < fill.y1 - EPSILON or fill.y0 + EPSILON < bottom < fill.y1 - EPSILON:
                if min(a.x, b.x) < fill.x1 and max(a.x, b.x) > fill.x0:
                    return "uneven"
            continue
        if bottom <= fill.y0 + EPSILON or top >= fill.y1 - EPSILON:
            continue
        if top > fill.y0 + EPSILON or bottom < fill.y1 - EPSILON:
            return "uneven"
        vertical.append(a.x)
    # Even-odd along the line: inside where an odd number of vertical sides lie to the left. The holes between the
    # sides, as the clip has them (not cut to the fill), and the part of the fill outside every side.
    vertical.sort()
    xs = sorted(set([fill.x0, fill.x1] + vertical))
    out = []
    for left, right in zip(xs, xs[1:]):
        middle = (left + right) / 2
        inside = sum(1 for x in vertical if x < middle) % 2 == 1
        if not inside and right - left > EPSILON and (fill.x0 < middle < fill.x1 or (vertical and vertical[0] < middle < vertical[-1])):
            if out and abs(out[-1][1] - left) <= EPSILON:
                out[-1] = (out[-1][0], right)
            else:
                out.append((left, right))
    return out


def main(path):
    for page_number, page in enumerate(pymupdf.open(path), start=1):
        clips = []
        for drawing in page.get_drawings(extended=True):
            level = drawing.get("level", 0)
            if drawing["type"] == "clip":
                clips = [clip for clip in clips if clip["level"] < level] + [drawing]
                continue
            if drawing["type"] == "group" or drawing.get("fill") is None:
                continue
            fill = drawing["rect"]
            active = [clip for clip in clips if clip["level"] < level and clip.get("even_odd")]
            gaps, rows = [], []
            for clip in active:
                result = cut(clip, fill)
                if isinstance(result, str):
                    box = clip["scissor"]
                    rows.append(f"{result} {page_number} {box.x0:.3f} {box.y0:.3f} {box.x1:.3f} {box.y1:.3f}")
                    continue
                for left, right in result:
                    gaps.append((left, right))
                    rows.append(f"gap {page_number} {left:.3f} {right:.3f}")
            if not rows:
                continue
            print(f"fill {page_number} {fill.x0:.3f} {fill.y0:.3f} {fill.x1:.3f} {fill.y1:.3f}")
            for row in rows:
                print(row)
            x = fill.x0
            for left, right in sorted(gaps):
                left, right = max(left, fill.x0), min(right, fill.x1)
                if right <= x:
                    continue
                if left > x + EPSILON:
                    print(f"piece {page_number} {x:.3f} {left:.3f}")
                x = max(x, right)
            if fill.x1 > x + EPSILON:
                print(f"piece {page_number} {x:.3f} {fill.x1:.3f}")


main(sys.argv[1])
