"""Generates the JPEG test files of the decoder with libjpeg-turbo's cjpeg (an independent encoder), each in one of the
variants the decoder must read: chroma sampling 4:4:4, 4:2:2, 4:4:0, 4:2:0, 4:1:1 and 3:1:1, mixed factors, restart
intervals, progressive coding (libjpeg's default script: spectral selection and successive approximation), grey, RGB
without colour transform (Adobe marker transform 0), 16-bit quantisation tables, odd sizes, a 1 x 1 image and images
narrow enough that a chroma row has 2 samples (or, for contrast, 3); and, by editing the segments of a file with one
scan per component, files the decoder must refuse or read with care: frame components that no scan covers (with a
defined and with an undefined quantisation table), and a quantisation table redefined between scans (T.81 §B.2.4.1: a
component keeps the table in force when its first scan starts).

Runs in the reference tools image (tools/reference/tools.Dockerfile: Pillow and libjpeg-turbo 2.1.5), from the family
folder (libs/documents):

  docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/work" -w /work ave-extensions-documents-tools:<tag> \\
      python3 -I tools/generate-test-jpegs.py

Writes UnitTests.Extensions.Documents.Images/Assets/jpeg-*.jpg. The tests compare the decoder with djpeg on the same
files at test time (JpegDecoderReferenceTests), so the pixels are not repeated here.
"""
import os
import subprocess
import tempfile

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "UnitTests.Extensions.Documents.Images", "Assets")


def photo(width, height):
    """Smooth colour gradients, a sharp diagonal edge, a thin line and a little noise (a fixed linear congruential
    generator), so every part of the decoder has something to do."""
    state = 12345
    pixels = []
    for y in range(height):
        for x in range(width):
            state = (state * 1103515245 + 12345) % (1 << 31)
            noise = (state >> 16) % 9 - 4
            r = (x * 255 // max(1, width - 1) + noise) % 256
            g = (y * 255 // max(1, height - 1) - noise) % 256
            b = 200 if x + 2 * y > width else 40
            if x == width // 3:
                r, g, b = 250, 250, 20
            pixels.append((r, g, b))
    image = Image.new("RGB", (width, height))
    image.putdata(pixels)
    return image


def cjpeg(source, name, *options):
    subprocess.run(["cjpeg", *options, "-outfile", os.path.join(ASSETS, name), source], check=True)


def segments(data):
    """The marker segments of a JPEG file in order: (marker, start, end), a scan's entropy-coded data included in its
    SOS segment (it runs to the next marker that is not a stuffed FF 00 or a restart marker)."""
    result = []
    position = 2
    while position < len(data):
        marker = data[position + 1]
        if marker == 0xD9:
            result.append((marker, position, position + 2))
            break
        end = position + 2 + int.from_bytes(data[position + 2:position + 4], "big")
        if marker == 0xDA:
            while not (data[end] == 0xFF and data[end + 1] not in (0x00, *range(0xD0, 0xD8))):
                end += 1
        result.append((marker, position, end))
        position = end
    return result


def edited(source, name, edit):
    """Writes a copy of an asset with its marker segments changed by edit(data, segments) -> bytes."""
    data = open(os.path.join(ASSETS, source), "rb").read()
    open(os.path.join(ASSETS, name), "wb").write(edit(data, segments(data)))


def frame_tables(data, segments_, tables):
    """The file with the quantisation table selector of each frame component set to tables[i] (None: unchanged)."""
    out = bytearray(data)
    marker, start, _ = next(segment for segment in segments_ if segment[0] in (0xC0, 0xC1, 0xC2))
    for index, table in enumerate(tables):
        if table is not None:
            out[start + 12 + (3 * index)] = table
    return bytes(out)


def without_scans(data, segments_, keep):
    """The file with only the scans whose index (0, 1, …) is in keep."""
    scans = [segment for segment in segments_ if segment[0] == 0xDA]
    parts = [data[:scans[0][1]]]
    parts += [data[start:end] for index, (_, start, end) in enumerate(scans) if index in keep]
    parts.append(data[scans[-1][2]:])
    return b"".join(parts)


def table_redefined(data, segments_):
    """A three-scan file whose chroma components use table 0, redefined as the original chroma table (table 1) between
    the luma scan and the chroma scans: valid by T.81 §B.2.4.1, and the right decoding is the original's."""
    tables = {}
    for marker, start, end in segments_:
        if marker == 0xDB:
            position = start + 4
            while position < end:
                precision, table = data[position] >> 4, data[position] & 15
                size = 64 if precision == 0 else 128
                tables[table] = data[position + 1:position + 1 + size]
                position += 1 + size
    redefine = b"\xff\xdb" + (3 + len(tables[1])).to_bytes(2, "big") + bytes([0]) + tables[1]
    scans = [segment for segment in segments_ if segment[0] == 0xDA]
    out = frame_tables(data, segments_, [None, 0, 0])
    return out[:scans[1][1]] + redefine + out[scans[1][1]:]


def main():
    os.makedirs(ASSETS, exist_ok=True)
    with tempfile.TemporaryDirectory() as folder:
        small = os.path.join(folder, "small.ppm")
        large = os.path.join(folder, "large.ppm")
        single = os.path.join(folder, "single.ppm")
        photo(61, 43).save(small)
        photo(257, 193).save(large)
        photo(1, 1).save(single)
        narrow4 = os.path.join(folder, "narrow4.ppm")
        narrow3 = os.path.join(folder, "narrow3.ppm")
        narrow5 = os.path.join(folder, "narrow5.ppm")
        photo(4, 37).save(narrow4)
        photo(3, 29).save(narrow3)
        photo(5, 23).save(narrow5)

        cjpeg(small, "jpeg-444.jpg", "-quality", "85", "-sample", "1x1")
        cjpeg(small, "jpeg-422.jpg", "-quality", "85", "-sample", "2x1")
        cjpeg(small, "jpeg-440.jpg", "-quality", "85", "-sample", "1x2")
        cjpeg(small, "jpeg-420.jpg", "-quality", "85", "-sample", "2x2")
        cjpeg(small, "jpeg-411.jpg", "-quality", "85", "-sample", "4x1")
        cjpeg(small, "jpeg-311.jpg", "-quality", "85", "-sample", "3x1")
        cjpeg(small, "jpeg-mixed.jpg", "-quality", "85", "-sample", "2x2,1x2,2x1")
        cjpeg(small, "jpeg-restart.jpg", "-quality", "85", "-sample", "2x2", "-restart", "3B")
        cjpeg(small, "jpeg-grey.jpg", "-quality", "85", "-grayscale")
        cjpeg(small, "jpeg-rgb.jpg", "-quality", "85", "-rgb")
        cjpeg(small, "jpeg-16bit-tables.jpg", "-quality", "3")
        cjpeg(single, "jpeg-1x1.jpg", "-quality", "85")
        # Narrow images: chroma rows of 2 samples (djpeg replicates them) and, for contrast, of 3 (djpeg interpolates).
        cjpeg(narrow4, "jpeg-420-narrow.jpg", "-quality", "85", "-sample", "2x2")
        cjpeg(narrow3, "jpeg-422-narrow.jpg", "-quality", "85", "-sample", "2x1")
        cjpeg(narrow5, "jpeg-420-width5.jpg", "-quality", "85", "-sample", "2x2")
        cjpeg(large, "jpeg-progressive.jpg", "-quality", "85", "-progressive")
        cjpeg(large, "jpeg-progressive-restart.jpg", "-quality", "85", "-progressive", "-restart", "1")
        cjpeg(large, "jpeg-progressive-grey.jpg", "-quality", "85", "-progressive", "-grayscale")
        cjpeg(large, "jpeg-progressive-444.jpg", "-quality", "92", "-progressive", "-sample", "1x1")

        # A sequential file with one scan per component, and three variants made from it by editing its segments.
        scans = os.path.join(folder, "scans.txt")
        open(scans, "w").write("0;\n1;\n2;\n")
        cjpeg(small, "jpeg-three-scans.jpg", "-quality", "85", "-sample", "1x1", "-scans", scans)
        edited("jpeg-three-scans.jpg", "jpeg-unscanned-components.jpg", lambda data, s: without_scans(data, s, [0]))
        edited("jpeg-three-scans.jpg", "jpeg-unscanned-undefined-table.jpg", lambda data, s: without_scans(frame_tables(data, s, [None, 3, 3]), segments(frame_tables(data, s, [None, 3, 3])), [0]))
        edited("jpeg-three-scans.jpg", "jpeg-table-redefined.jpg", table_redefined)
    print("written:", sorted(name for name in os.listdir(ASSETS) if name.startswith("jpeg-")))


if __name__ == "__main__":
    main()
