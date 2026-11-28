# Reads back which picture each image of a print shows: the pixels of the image the PDF holds, decoded. Run by
# extract.sh in the reference tools image (PyMuPDF):
#
#   python3 images.py <file.pdf>
#
# One line per image of print.txt (the images that show, in drawing order), page by page:
#
#   <page> <width> <height> <sha256>
#
# the image's size in pixels and the SHA-256 (lowercase hexadecimal) of its pixels as 8-bit RGBA, row by row from the
# top, each pixel red, green, blue and alpha: the samples as the PDF's image holds them, decoded by its filter (PyMuPDF's
# Pixmap of the image's object), a gray sample written as three equal ones, and the alpha of its soft mask (/SMask), 255
# where it has none. Chromium writes a picture it was given as a PNG with a lossless filter (FlateDecode) at the PNG's
# own size, so those samples are the PNG's own; any image that is not 8-bit gray or RGB, or not an image object of its
# own, stops the read-back, so a print never loses what it draws without a word.
import hashlib
import sys

import pymupdf

import visible


def pixels(document, xref):
    """The image's pixels as 8-bit RGBA, with its size."""
    image = pymupdf.Pixmap(document, xref)
    if image.alpha or image.n not in (1, 3):
        raise ValueError(f"image {xref}: {image.n} components and alpha {image.alpha}, not gray or RGB")
    kind, value = document.xref_get_key(xref, "SMask")
    mask = pymupdf.Pixmap(document, int(value.split()[0])) if kind == "xref" else None
    if mask is not None and (mask.n != 1 or (mask.width, mask.height) != (image.width, image.height)):
        raise ValueError(f"image {xref}: a soft mask of {mask.n} components, {mask.width}x{mask.height}")
    samples, alpha = image.samples, mask.samples if mask is not None else None
    rgba = bytearray(image.width * image.height * 4)
    for index in range(image.width * image.height):
        rgba[4 * index:4 * index + 3] = samples[3 * index:3 * index + 3] if image.n == 3 else bytes([samples[index]]) * 3
        rgba[4 * index + 3] = alpha[index] if alpha is not None else 255
    return image.width, image.height, hashlib.sha256(rgba).hexdigest()


def main(path):
    document = pymupdf.open(path)
    for number, page in enumerate(document, start=1):
        images, clips = page.get_image_info(xrefs=True), visible.image_clips(page)
        for image, clip in zip(images, clips):
            if image["xref"] <= 0:
                raise ValueError(f"page {number}: an image that is no object of its own")
            if visible.cut(tuple(image["bbox"]), clip) is not None:
                width, height, digest = pixels(document, image["xref"])
                print(f"{number} {width} {height} {digest}")


if __name__ == "__main__":
    main(sys.argv[1])
