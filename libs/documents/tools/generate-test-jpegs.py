"""Generates the JPEG test files of the decoder with libjpeg-turbo's cjpeg (an independent encoder), each in one of the
variants the decoder must read: chroma sampling 4:4:4, 4:2:2, 4:4:0, 4:2:0, 4:1:1 and 3:1:1, mixed factors, restart
intervals, progressive coding (libjpeg's default script: spectral selection and successive approximation), grey, RGB
without colour transform (Adobe marker transform 0), 16-bit quantisation tables, odd sizes and a 1 x 1 image.

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


def main():
    os.makedirs(ASSETS, exist_ok=True)
    with tempfile.TemporaryDirectory() as folder:
        small = os.path.join(folder, "small.ppm")
        large = os.path.join(folder, "large.ppm")
        single = os.path.join(folder, "single.ppm")
        photo(61, 43).save(small)
        photo(257, 193).save(large)
        photo(1, 1).save(single)

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
        cjpeg(large, "jpeg-progressive.jpg", "-quality", "85", "-progressive")
        cjpeg(large, "jpeg-progressive-restart.jpg", "-quality", "85", "-progressive", "-restart", "1")
        cjpeg(large, "jpeg-progressive-grey.jpg", "-quality", "85", "-progressive", "-grayscale")
        cjpeg(large, "jpeg-progressive-444.jpg", "-quality", "92", "-progressive", "-sample", "1x1")
    print("written:", sorted(name for name in os.listdir(ASSETS) if name.startswith("jpeg-")))


if __name__ == "__main__":
    main()
