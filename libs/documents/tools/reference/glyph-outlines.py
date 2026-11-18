# Writes the outline of every glyph of each font given, as fontTools decodes it, for the reference test of the engine's
# outline decoder (UnitTests.Extensions.Documents.Fonts/GlyphShapeReferenceTests.cs). Runs in the reference tools image
# (tools/reference/tools.Dockerfile, fontTools 4.57.0).
#
# Usage: python3 -I glyph-outlines.py <output file> <font file>...
#
# One line per glyph, in glyph order:  <font file name> <glyph id> <contour ends> <points>
#   contour ends: the index of the last point of each contour, joined by commas ("-" for none);
#   points:       x,y,on for each point, joined by ";" ("-" for none); x and y as Python's repr of the float, on = 1 for
#                 an on-curve point, 0 for an off-curve one.
# Composite glyphs are resolved by fontTools' Glyph.getCoordinates (no rounding), which is what the test compares with.
import os
import sys

from fontTools.ttLib import TTFont


def main(output, fonts):
    with open(output, "w", encoding="ascii", newline="\n") as out:
        for path in fonts:
            font = TTFont(path)
            glyf = font["glyf"]
            name = os.path.basename(path)
            for gid, glyph_name in enumerate(font.getGlyphOrder()):
                coordinates, ends, flags = glyf[glyph_name].getCoordinates(glyf)
                ends_text = ",".join(str(end) for end in ends) or "-"
                points = ";".join(
                    f"{float(x)!r},{float(y)!r},{flags[index] & 1}" for index, (x, y) in enumerate(coordinates)
                ) or "-"
                out.write(f"{name} {gid} {ends_text} {points}\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
