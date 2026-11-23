# Unit tests of the read-back of paths that are not rectangles (paths.py, through positions.py and rectangles.py): a
# disc, a ring stroked 1 px wide, the same ring filled between two circles, and a square, each in the box Chromium
# gives a list marker at 14 px (3.75 pt, five whole pixels). Each test writes a small PDF with the drawings as content
# stream operators, reads it back with the read-back scripts, and checks their lines. Run in the reference tools image
# (PyMuPDF), from any folder, with this folder's scripts beside it:
#
#   python3 paths_test.py
import math
import os
import subprocess
import sys
import tempfile
import unittest

import pymupdf

HERE = os.path.dirname(os.path.abspath(__file__))
KAPPA = 0.5522847498


def circle(cx, cy, r, curves=4):
    """A circle as content stream operators of cubic curves (4, as a PDF writer draws one, or 16, as Chromium does)."""
    step = 2 * math.pi / curves
    k = 4 / 3 * math.tan(step / 4)
    out = [f"{cx + r:.6f} {cy:.6f} m"]
    for index in range(curves):
        a0, a1 = index * step, (index + 1) * step
        p1 = (cx + r * (math.cos(a0) - k * math.sin(a0)), cy + r * (math.sin(a0) + k * math.cos(a0)))
        p2 = (cx + r * (math.cos(a1) + k * math.sin(a1)), cy + r * (math.sin(a1) - k * math.cos(a1)))
        p3 = (cx + r * math.cos(a1), cy + r * math.sin(a1))
        out.append(f"{p1[0]:.6f} {p1[1]:.6f} {p2[0]:.6f} {p2[1]:.6f} {p3[0]:.6f} {p3[1]:.6f} c")
    return " ".join(out) + " h"


def read(*pages, script="positions.py"):
    """The lines a read-back script writes for a PDF of 100 x 100 pt pages, each with the given content (operators in
    a coordinate system with its origin at the top left, y down, as the read-backs write)."""
    document = pymupdf.open()
    for content in pages:
        page = document.new_page(width=100, height=100)
        xref = document.get_new_xref()
        document.update_object(xref, "<<>>")
        document.update_stream(xref, f"1 0 0 -1 0 100 cm {content}".encode("ascii"))
        document.xref_set_key(page.xref, "Contents", f"{xref} 0 R")
    with tempfile.TemporaryDirectory() as folder:
        path = os.path.join(folder, "case.pdf")
        document.save(path)
        result = subprocess.run([sys.executable, os.path.join(HERE, script), path], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(result.stderr)
    return [line for line in result.stdout.splitlines() if not line.startswith("page ")]


# A disc in the box (30, 30)-(33.75, 33.75) and the ring of Chromium's circle marker in the same box: a stroke 0.75 pt
# wide whose centre line is the box's circle, and the same ink as a fill between circles 0.375 pt outside and inside it.
DISC = f"0 0 0 rg {circle(31.875, 31.875, 1.875)} f"
STROKED_RING = f"0 0 0 RG 0.75 w {circle(31.875, 31.875, 1.875, curves=16)} S"
FILLED_RING = f"0 0 0 rg {circle(31.875, 31.875, 2.25)} {circle(31.875, 31.875, 1.5)} f*"
SQUARE_PATH = "0 0 0 rg 30 30 m 33.75 30 l 33.75 33.75 l 30 33.75 l h f"
SQUARE_RECT = "0 0 0 rg 30 30 3.75 3.75 re f"


class PathReadBackTests(unittest.TestCase):

    def test_a_disc_is_written_as_the_area_it_paints(self):
        self.assertEqual(read(DISC), ["path 1 30.000 30.000 33.750 33.750 #000000 2cfc2/cfffc/fffff/cfffc/2cfc2"])

    def test_a_disc_and_a_square_in_the_same_box_differ(self):
        self.assertEqual(read(SQUARE_RECT), ["rect 1 30.000 30.000 33.750 33.750 #000000"])
        self.assertNotEqual(read(DISC), read(SQUARE_RECT))

    def test_a_square_stays_a_rectangle_also_drawn_as_four_lines(self):
        # PyMuPDF reports a closed path of four sides along the axes as a rectangle.
        for square in (SQUARE_RECT, SQUARE_PATH):
            self.assertEqual(read(square), ["rect 1 30.000 30.000 33.750 33.750 #000000"])
            self.assertEqual(read(square, script="rectangles.py"), ["1 30.000 30.000 33.750 33.750 3.750"])

    def test_a_ring_stroked_1px_wide_and_the_same_ring_filled_are_the_same(self):
        stroked, filled = read(STROKED_RING), read(FILLED_RING)
        self.assertEqual(stroked, filled)
        self.assertEqual(len(stroked), 1)
        self.assertTrue(stroked[0].startswith("path 1 29.250 29.250 34.500 34.500 #000000 "), stroked[0])
        rows = stroked[0].split(" ")[7].split("/")
        self.assertEqual(rows[3][3], "0", "the ring's hole is not painted")
        self.assertNotEqual(stroked, read(DISC))

    def test_curves_are_not_written_as_rectangles(self):
        self.assertEqual(read(DISC, STROKED_RING, FILLED_RING, script="rectangles.py"), [])

    def test_a_curve_is_cut_to_the_clip_around_it(self):
        # A clip that leaves the left half of the disc's box (2.5 px): the middle column keeps 7 of its 15 sample
        # columns, the right part is not written.
        clipped = read(f"q 0 0 100 100 re W n 30 0 1.875 100 re W n {DISC} Q")
        self.assertEqual(clipped, ["path 1 30.000 30.000 32.250 33.750 #000000 2c7/cf7/ff7/cf7/2c7"])
        # A curve wholly outside the clip is not written.
        self.assertEqual(read(f"q 50 50 10 10 re W n {DISC} Q"), [])

    def test_a_stroke_that_this_read_back_does_not_know_stops_it(self):
        with self.assertRaises(RuntimeError):
            read(f"0 0 0 RG 0.75 w [1 1] 0 d {circle(31.875, 31.875, 1.875)} S")
        with self.assertRaises(RuntimeError):
            read("0 0 0 RG 0.75 w 30 30 m 31 32 32 33 33 30 c S")

    def test_the_colour_is_the_paint_s(self):
        self.assertEqual(read(f"1 0 0 rg {circle(31.875, 31.875, 1.875)} f")[0].split(" ")[6], "#ff0000")
        self.assertEqual(read(f"0 0 1 RG 0.75 w {circle(31.875, 31.875, 1.875, curves=16)} S")[0].split(" ")[6], "#0000ff")


if __name__ == "__main__":
    unittest.main(verbosity=2)
