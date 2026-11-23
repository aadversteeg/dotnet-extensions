# The area a filled or stroked path that is not a rectangle paints (a curve, such as the disc or the circle Chromium draws
# as a list marker), on the grid of whole pixels the case comparison counts painted areas on: pixels of 0.75 pt (one
# CSS px) from the page's top left corner. positions.py writes such a path as the pixels it covers, each with the share
# of it that is painted; rectangles stay rectangles (rect, stroke).
#
# Why the painted area and not the path's segments: two paths that paint the same ink are the same on the page, and a
# print may paint one ink with different paths. Chromium strokes a circle of 16 cubic curves with a line 1 px wide; a
# ring filled between two circles of 4 curves each (even-odd) paints the same ink, and so do a stroke and a fill of any
# other shape whose outline is the stroke's. Their segments differ in number, kind and place; their areas do not. And a
# disc and a square in the same box paint different areas, so the form tells them apart, where the box of the path
# (what positions.py wrote for a filled curve before) does not.
#
# The share of a pixel that is painted is counted on 15 x 15 sample points inside it, at the centres of a regular grid
# (so no point lies on the pixel's edges): a point is painted where it lies inside the filled path by the drawing's fill
# rule (nonzero, or even-odd), or within half the line width of a stroked path's centre line, and inside the rectangle
# the clips around the drawing leave (visible.py). The share is written as one hexadecimal digit, the number of painted
# points of 225 in fifteenths, rounded half up: 0 is nothing, f the whole pixel. A pixel whose digit is 0 is not
# written; the rows of the smallest box of whole pixels that holds every other pixel are.
#
# A curve is flattened to straight segments, so few that no point of a segment lies more than 0.0005 pt from the
# curve (a thousandth of the 0.75 pt pixel's 1/15 sample step), and a stroke's centre line is that of each closed
# subpath. The join of a stroke is taken as round: a stroke of a smooth closed curve (Chromium's circle) turns by a tiny
# angle at each of its joins, where the mitre and the round join differ by far less than a sample step. A stroke with a
# dash pattern or with a subpath that does not close is not read: it stops the read-back (positions.py), as an
# unknown stroke of a rectangle does.
import math

PIXEL = 0.75
SAMPLES = 15
TOLERANCE = 0.0005


def _point(value):
    return (float(value.x), float(value.y)) if hasattr(value, "x") else (float(value[0]), float(value[1]))


def _cubic(p0, p1, p2, p3):
    """The cubic Bezier curve p0 p1 p2 p3 as points from p1's side on, p0 not included."""
    # The distance between the curve and its chord polygon with n segments is at most 3/4 M / n^2, with M the larger
    # norm of the control polygon's second differences.
    second = max(math.hypot(p0[0] - 2 * p1[0] + p2[0], p0[1] - 2 * p1[1] + p2[1]),
                 math.hypot(p1[0] - 2 * p2[0] + p3[0], p1[1] - 2 * p2[1] + p3[1]))
    count = max(1, min(1000, math.ceil(math.sqrt(0.75 * second / TOLERANCE))))
    points = []
    for step in range(1, count + 1):
        t = step / count
        u = 1 - t
        a, b, c, d = u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t
        points.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return points


def contours(items):
    """The subpaths of a PyMuPDF drawing's items ("l" line, "c" cubic curve, "re" rectangle, "qu" quad) as lists of
    points, curves flattened. A new subpath starts where an item does not start at the end of the one before it."""
    out, current = [], None

    def start(point):
        nonlocal current
        if current is None or math.dist(current[-1], point) > 1e-6:
            current = [point]
            out.append(current)

    for item in items:
        kind = item[0]
        if kind == "l":
            a, b = _point(item[1]), _point(item[2])
            start(a)
            current.append(b)
        elif kind == "c":
            p0, p1, p2, p3 = (_point(value) for value in item[1:5])
            start(p0)
            current.extend(_cubic(p0, p1, p2, p3))
        elif kind == "re":
            r = item[1]
            current = [(r.x0, r.y0), (r.x1, r.y0), (r.x1, r.y1), (r.x0, r.y1), (r.x0, r.y0)]
            out.append(current)
            current = None
        elif kind == "qu":
            q = item[1]
            current = [_point(q.ul), _point(q.ur), _point(q.lr), _point(q.ll), _point(q.ul)]
            out.append(current)
            current = None
        else:
            raise ValueError(f"a path item this read-back does not know: {item}")
    return out


def closed(contour):
    return len(contour) > 2 and math.dist(contour[0], contour[-1]) <= 1e-6


def _segments(contours_, close):
    for contour in contours_:
        points = contour + [contour[0]] if close and not closed(contour) else contour
        for a, b in zip(points, points[1:]):
            yield a, b


def _filled(segments, even_odd):
    """A test whether a point is inside the filled area, one sample row at a time: (y) -> list of (x0, x1) intervals."""
    def row(y):
        crossings = []
        for (ax, ay), (bx, by) in segments:
            if (ay <= y < by) or (by <= y < ay):
                crossings.append((ax + (y - ay) * (bx - ax) / (by - ay), 1 if by > ay else -1))
        crossings.sort()
        intervals, winding = [], 0
        for index, (x, direction) in enumerate(crossings):
            winding += direction
            inside = (winding % 2 == 1) if even_odd else winding != 0
            if inside and index + 1 < len(crossings):
                intervals.append((x, crossings[index + 1][0]))
        return intervals
    return row


def _distance_squared(px, py, a, b):
    (ax, ay), (bx, by) = a, b
    dx, dy = bx - ax, by - ay
    length = dx * dx + dy * dy
    t = 0.0 if length == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / length))
    x, y = ax + t * dx - px, ay + t * dy - py
    return x * x + y * y


def coverage(drawing_contours, clip, even_odd=None, width=None):
    """The painted share of each whole pixel, for a fill (even_odd True or False) or a stroke (width, the line width):
    (x0, y0, rows), the column and row of the box's top left pixel and the rows of digits ("0" to "f"), or None where
    no pixel is painted. clip is the rectangle the clips leave (x0, y0, x1, y1), or None."""
    stroke = width is not None
    segments = list(_segments(drawing_contours, close=not stroke))
    if not segments:
        return None
    half = width / 2 if stroke else 0.0
    xs = [x for segment in segments for x, _ in segment]
    ys = [y for segment in segments for _, y in segment]
    left, top, right, bottom = min(xs) - half, min(ys) - half, max(xs) + half, max(ys) + half
    if clip is not None:
        left, top, right, bottom = max(left, clip[0]), max(top, clip[1]), min(right, clip[2]), min(bottom, clip[3])
        if left >= right or top >= bottom:
            return None
    columns = range(math.floor(left / PIXEL), math.ceil(right / PIXEL))
    rows = range(math.floor(top / PIXEL), math.ceil(bottom / PIXEL))
    step = PIXEL / SAMPLES
    inside_clip = (lambda x, y: True) if clip is None else (lambda x, y: clip[0] <= x < clip[2] and clip[1] <= y < clip[3])
    fill_row = None if stroke else _filled(segments, even_odd)
    counts = {}
    for row in rows:
        for sample_row in range(SAMPLES):
            y = (row * SAMPLES + sample_row + 0.5) * step
            if stroke:
                near = [segment for segment in segments if min(segment[0][1], segment[1][1]) - half <= y <= max(segment[0][1], segment[1][1]) + half]
                limit = half * half
            else:
                intervals = fill_row(y)
            for column in columns:
                for sample_column in range(SAMPLES):
                    x = (column * SAMPLES + sample_column + 0.5) * step
                    if not inside_clip(x, y):
                        continue
                    if stroke:
                        painted = any(_distance_squared(x, y, a, b) <= limit for a, b in near)
                    else:
                        painted = any(x0 <= x < x1 for x0, x1 in intervals)
                    if painted:
                        counts[(column, row)] = counts.get((column, row), 0) + 1
    digits = {cell: (count + SAMPLES // 2) // SAMPLES for cell, count in counts.items()}
    digits = {cell: digit for cell, digit in digits.items() if digit > 0}
    if not digits:
        return None
    c0, c1 = min(c for c, _ in digits), max(c for c, _ in digits)
    r0, r1 = min(r for _, r in digits), max(r for _, r in digits)
    return c0, r0, ["".join(f"{digits.get((c, r), 0):x}" for c in range(c0, c1 + 1)) for r in range(r0, r1 + 1)]


def is_rectangle(drawing):
    """Whether a drawing is made of rectangles only, which the read-backs write as rectangles (rect, stroke). PyMuPDF
    reports a closed path of four sides along the axes as a rectangle ("re") however the PDF draws it."""
    return all(item[0] == "re" for item in drawing["items"])


def line(page, drawing, clip, fill):
    """The path line positions.py writes for a drawing that is not a rectangle: its fill (fill True) or its stroke,
    "path <page> <x0> <y0> <x1> <y1> <colour> <rows>", the box of whole pixels in points and the digits of each row
    joined by "/"; or None where the clip leaves nothing of it."""
    found = contours(drawing["items"])
    if drawing.get("closePath"):
        found = [contour if closed(contour) else contour + [contour[0]] for contour in found]
    if fill:
        painted = coverage(found, clip, even_odd=bool(drawing.get("even_odd")))
    else:
        if drawing.get("dashes") not in (None, "[] 0") or not all(closed(contour) for contour in found):
            raise ValueError(f"a stroke this read-back does not know: {drawing}")
        painted = coverage(found, clip, width=drawing["width"])
    if painted is None:
        return None
    column, row, rows = painted
    return (column * PIXEL, row * PIXEL, (column + len(rows[0])) * PIXEL, (row + len(rows)) * PIXEL, "/".join(rows))
