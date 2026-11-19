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
# A clip that cuts a rectangle out of only part of the line's height (the clip of an earlier line of the same text) is
# written with that rectangle's top and bottom, and the line then with its visible ink as rectangles, columns of equal
# rows joined:
#
#   gap <page> <x0> <x1> <y0> <y1>       the rectangle such a clip leaves out (raw)
#   part <page> <x0> <y0> <x1> <y1>      a visible rectangle of a line cut by such a clip
#
# A clip that leaves out anything other than one rectangle is written as "uneven <page> <x0> <y0> <x1> <y1>" with the
# clip's box; a curve in a clip as "curve <page>". Writes nothing for a print without such clips. Run by extract.sh in the
# reference tools image: python3 ink.py <file.pdf>
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


def hole(clip):
    """The one rectangle (x0, x1, y0, y1) an even-odd clip of straight sides leaves out of its box, or None."""
    sides = segments(clip["items"])
    if sides is None or any(abs(a.x - b.x) > EPSILON and abs(a.y - b.y) > EPSILON for a, b in sides):
        return None
    box = clip["scissor"]
    xs = sorted({round(v, 4) for a, b in sides for v in (a.x, b.x)} | {round(box.x0, 4), round(box.x1, 4)})
    ys = sorted({round(v, 4) for a, b in sides for v in (a.y, b.y)} | {round(box.y0, 4), round(box.y1, 4)})
    vertical = [(a.x, min(a.y, b.y), max(a.y, b.y)) for a, b in sides if abs(a.x - b.x) <= EPSILON]
    out = []
    for left, right in zip(xs, xs[1:]):
        for top, bottom in zip(ys, ys[1:]):
            cx, cy = (left + right) / 2, (top + bottom) / 2
            if not (box.x0 < cx < box.x1 and box.y0 < cy < box.y1):
                continue
            if sum(1 for x, y0, y1 in vertical if x > cx and y0 < cy < y1) % 2 == 0:
                out.append((left, right, top, bottom))
    if not out:
        return None
    x0, x1 = min(c[0] for c in out), max(c[1] for c in out)
    y0, y1 = min(c[2] for c in out), max(c[3] for c in out)
    area = sum((c[1] - c[0]) * (c[3] - c[2]) for c in out)
    return (x0, x1, y0, y1) if abs(area - (x1 - x0) * (y1 - y0)) < 1e-6 else None


def parts(page_number, fill, holes):
    """The visible rectangles of the fill minus the holes: columns split at every hole end, equal columns joined."""
    xs = sorted({fill.x0, fill.x1} | {min(max(v, fill.x0), fill.x1) for h in holes for v in h[:2]})
    columns = []
    for left, right in zip(xs, xs[1:]):
        if right - left <= EPSILON:
            continue
        middle = (left + right) / 2
        rows = [(fill.y0, fill.y1)]
        for x0, x1, y0, y1 in holes:
            if x0 < middle < x1:
                rows = [piece for top, bottom in rows for piece in
                        ((top, min(bottom, y0)), (max(top, y1), bottom)) if piece[1] - piece[0] > EPSILON]
        if columns and columns[-1][2] == rows and abs(columns[-1][1] - left) <= EPSILON:
            columns[-1] = (columns[-1][0], right, rows)
        else:
            columns.append((left, right, rows))
    return [f"part {page_number} {left:.3f} {top:.3f} {right:.3f} {bottom:.3f}" for left, right, rows in columns for top, bottom in rows]


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
            gaps, rows, holes = [], [], []
            for clip in active:
                result = cut(clip, fill)
                if result == "uneven" and (rectangle := hole(clip)) is not None:
                    if rectangle[0] < fill.x1 and rectangle[1] > fill.x0 and rectangle[2] < fill.y1 and rectangle[3] > fill.y0:
                        holes.append(rectangle)
                        rows.append(f"gap {page_number} {rectangle[0]:.3f} {rectangle[1]:.3f} {rectangle[2]:.3f} {rectangle[3]:.3f}")
                    continue
                if isinstance(result, str):
                    box = clip["scissor"]
                    rows.append(f"{result} {page_number} {box.x0:.3f} {box.y0:.3f} {box.x1:.3f} {box.y1:.3f}")
                    continue
                for left, right in result:
                    gaps.append((left, right))
                    holes.append((left, right, fill.y0, fill.y1))
                    rows.append(f"gap {page_number} {left:.3f} {right:.3f}")
            if not rows:
                continue
            print(f"fill {page_number} {fill.x0:.3f} {fill.y0:.3f} {fill.x1:.3f} {fill.y1:.3f}")
            for row in rows:
                print(row)
            if any(top > fill.y0 + EPSILON or bottom < fill.y1 - EPSILON for _, _, top, bottom in holes):
                for row in parts(page_number, fill, holes):
                    print(row)
                continue
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
