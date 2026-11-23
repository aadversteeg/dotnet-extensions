"""Generates the image test assets with an independent encoder (Pillow, which writes PNG through zlib and JPEG
through libjpeg), so the engine's own decoder is never tested against files it produced itself.

Run from the repository root:  python tools/generate-test-images.py
Writes UnitTests.Extensions.Documents.Images/Assets/*.png|*.jpg, samples/Ave.Extensions.Documents.Samples/assets/logo.png,
tools/reference/chromium/cases/tall.png (the image of the Chromium reference cases for content taller than a page),
cases/wide.png (the wide image of the cases of an image in a line) and cases/badge.png (the image of the cases of an image
in a page-margin box).
The pixel pattern is a formula (see pattern()), repeated in the C# tests, so every decoded pixel can be checked.
Only test and sample assets are made here; nothing of this runs in the engine.
"""
import io
import os
import struct
import zlib

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "UnitTests.Extensions.Documents.Images", "Assets")
SAMPLE_ASSETS = os.path.join(ROOT, "samples", "Ave.Extensions.Documents.Samples", "assets")
CHROMIUM_CASES = os.path.join(ROOT, "tools", "reference", "chromium", "cases")

# 7 x 5 pixels: an odd width, so rows of packed palette indices end inside a byte.
W, H = 7, 5


def pattern(x, y):
    """The colour, grey value, alpha and palette index of pixel (x, y). The C# tests use the same formulas."""
    rgb = (x * 36 % 256, y * 50 % 256, (x * y * 13) % 256)
    grey = (x * 30 + y * 20) % 256
    alpha = (x + y) * 25 % 256
    return rgb, grey, alpha


def image(mode, value):
    img = Image.new(mode, (W, H))
    img.putdata([value(x, y) for y in range(H) for x in range(W)])
    return img


def save(img, name, **options):
    img.save(os.path.join(ASSETS, name), **options)


def palette_image(bits):
    """Indexed colour with 2^bits entries; index = (x + 2y) mod entries; entry i = (i*40, 255-i*40, i*17)."""
    entries = 1 << bits
    img = Image.new("P", (W, H))
    flat = []
    for i in range(entries):
        flat += [i * 40 % 256, (255 - i * 40) % 256, i * 17 % 256]
    img.putpalette(flat)
    img.putdata([(x + 2 * y) % entries for y in range(H) for x in range(W)])
    return img


def png_with_every_filter():
    """A truecolour PNG of pattern() at 7 x 10 pixels whose row y uses filter type y mod 5 (PNG §9.2, Table 9.1)."""
    width, height, bpp = W, 10, 3
    rows = [bytes(v for x in range(width) for v in pattern(x, y)[0]) for y in range(height)]

    def paeth(a, b, c):
        p = a + b - c
        pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
        return a if pa <= pb and pa <= pc else b if pb <= pc else c

    raw = bytearray()
    for y, row in enumerate(rows):
        kind = y % 5
        above = rows[y - 1] if y > 0 else bytes(len(row))
        raw.append(kind)
        for i, value in enumerate(row):
            a = row[i - bpp] if i >= bpp else 0
            b = above[i]
            c = above[i - bpp] if i >= bpp else 0
            prediction = [0, a, b, (a + b) // 2, paeth(a, b, c)][kind]
            raw.append((value - prediction) % 256)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(raw))) + chunk(b"IEND", b"")


def solid_png(width, height, rgb):
    """A truecolour PNG of one colour, written without Pillow: IHDR, one IDAT (filter type 0 on every row, zlib level 9)
    and IEND, no other chunk. 4 x 16 pixels of (200, 60, 60) is cases/tall.png, 74 bytes."""
    raw = b"".join(b"\x00" + bytes(rgb) * width for _ in range(height))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def patch_ihdr(data, offset_in_ihdr, value):
    """Changes one byte of IHDR and fixes its CRC, to make a well-formed file of a variant Pillow cannot write."""
    ihdr_start = 8  # after the signature: length (4), type (4), data (13), crc (4)
    chunk = bytearray(data[ihdr_start:ihdr_start + 25])
    chunk[8 + offset_in_ihdr] = value
    crc = zlib.crc32(bytes(chunk[4:21])) & 0xFFFFFFFF
    chunk[21:25] = struct.pack(">I", crc)
    return data[:ihdr_start] + bytes(chunk) + data[ihdr_start + 25:]


def main():
    os.makedirs(ASSETS, exist_ok=True)
    os.makedirs(SAMPLE_ASSETS, exist_ok=True)

    save(image("L", lambda x, y: pattern(x, y)[1]), "grey8.png")
    save(image("RGB", lambda x, y: pattern(x, y)[0]), "rgb8.png")
    save(image("RGBA", lambda x, y: (*pattern(x, y)[0], pattern(x, y)[2])), "rgba8.png")
    save(image("LA", lambda x, y: (pattern(x, y)[1], pattern(x, y)[2])), "grey-alpha8.png")
    for bits in (1, 2, 4, 8):
        save(palette_image(bits), f"palette{bits}.png", bits=bits)
    # Palette with simple transparency: entry 0 transparent, entry 1 half, others opaque.
    save(palette_image(2), "palette2-trns.png", bits=2, transparency=bytes([0, 128]))
    # Colour keys: the pixel value that is fully transparent.
    save(image("L", lambda x, y: pattern(x, y)[1]), "grey8-trns.png", transparency=pattern(1, 1)[1])
    save(image("RGB", lambda x, y: pattern(x, y)[0]), "rgb8-trns.png", transparency=pattern(2, 3)[0])
    # A larger image, so libpng's adaptive filtering uses all five filter types (the test checks they occur).
    big = Image.new("RGB", (64, 48))
    big.putdata([((x * 7 + y * 3) % 256, (x * x + y) % 256, (x * y) % 256) for y in range(48) for x in range(64)])
    big.save(os.path.join(ASSETS, "rgb8-filters.png"))

    # Every filter type for certain: libpng picks filters by heuristic and skips Average on the image above, so this
    # file is encoded here, row y with filter type y mod 5, from PNG §9.2 (the pixels are pattern()'s RGB).
    open(os.path.join(ASSETS, "rgb8-all-filters.png"), "wb").write(png_with_every_filter())

    # Unsupported but well-formed variants.
    save(image("I;16", lambda x, y: pattern(x, y)[1] * 257), "grey16.png")
    rgb = open(os.path.join(ASSETS, "rgb8.png"), "rb").read()
    open(os.path.join(ASSETS, "rgb8-interlaced-header.png"), "wb").write(patch_ihdr(rgb, 12, 1))

    # JPEG: baseline grey and RGB, progressive RGB, CMYK (4 components, unsupported).
    photo = Image.new("RGB", (40, 30))
    photo.putdata([((x * 6) % 256, (y * 8) % 256, 128) for y in range(30) for x in range(40)])
    photo.save(os.path.join(ASSETS, "baseline-rgb.jpg"), quality=90)
    photo.convert("L").save(os.path.join(ASSETS, "baseline-grey.jpg"), quality=90)
    photo.save(os.path.join(ASSETS, "progressive-rgb.jpg"), quality=90, progressive=True)
    photo.convert("CMYK").save(os.path.join(ASSETS, "cmyk.jpg"), quality=90)

    # The timesheet sample's logo: the reporting PoC's dummy logo (an orange square with a white V, drawn as a path in
    # templates/assets/logo.svg, viewBox 100 x 100), rasterised at 4 px per unit with 4x supersampling.
    scale, supersample = 4, 4
    size = 100 * scale * supersample
    logo = Image.new("RGB", (size, size), (0xFF, 0xA5, 0x00))
    points = [(22, 26), (36, 26), (50, 62), (64, 26), (78, 26), (56, 78), (44, 78)]
    ImageDraw.Draw(logo).polygon([(x * scale * supersample, y * scale * supersample) for x, y in points], fill=(255, 255, 255))
    logo.resize((100 * scale, 100 * scale), Image.LANCZOS).save(os.path.join(SAMPLE_ASSETS, "logo.png"))
    with open(os.path.join(CHROMIUM_CASES, "tall.png"), "wb") as tall:
        tall.write(solid_png(4, 16, (200, 60, 60)))
    # A wide image for the cases of an image in a line of text (ii-*).
    with open(os.path.join(CHROMIUM_CASES, "wide.png"), "wb") as wide:
        wide.write(solid_png(16, 4, (60, 90, 160)))
    # The image of the cases of an image in the content of a page-margin box (pm-image-*).
    with open(os.path.join(CHROMIUM_CASES, "badge.png"), "wb") as badge:
        badge.write(solid_png(40, 24, (0x33, 0x66, 0xCC)))
    print("written:", sorted(os.listdir(ASSETS)), "samples/.../assets/logo.png and tools/reference/chromium/cases/tall.png, wide.png and badge.png")


if __name__ == "__main__":
    main()
