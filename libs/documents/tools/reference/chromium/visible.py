# The part of each drawing of a print that the print shows. A PDF draws a fill, a stroke or an image under the clips
# that are open around it (ISO 32000-1 §8.5.4, "Clipping path operators"), and only what lies inside all of them is
# painted. Chromium opens one such clip per page around the page's body, the page area (less the areas of the
# page-margin boxes, which it draws before it, outside any clip), and draws parts of the body that pass the page area
# all the same: a grid line that passes the page end, the slice of a cell or an image drawn again offset up by the
# height of the page area. The read-backs (positions.py, rectangles.py) write each drawing cut to the rectangle its
# clips leave, and leave out a drawing that lies wholly outside it, so the measured files hold what the print shows.
#
# A clip is taken as its bounding box (PyMuPDF's "scissor": the box of the clip path, cut to the clips around it). A
# clip that leaves out a hole inside its box (Chromium's clips around the ink of a decoration line that skips ink) cuts
# nothing here; ink.py reads those.
from pymupdf import mupdf


def cut(rect, clip):
    """The part of rect (x0, y0, x1, y1) inside clip (a rectangle, or None for no clip), or None where nothing is."""
    if clip is None:
        return rect
    x0, y0, x1, y1 = max(rect[0], clip[0]), max(rect[1], clip[1]), min(rect[2], clip[2]), min(rect[3], clip[3])
    return (x0, y0, x1, y1) if x0 < x1 and y0 < y1 else None


def _within(outer, inner):
    return inner if outer is None else (max(outer[0], inner[0]), max(outer[1], inner[1]), min(outer[2], inner[2]), min(outer[3], inner[3]))


def drawings(page):
    """Each fill or stroke of the page (PyMuPDF's drawing dictionary) in drawing order, with the rectangle its clips leave
    (None where no clip is open around it)."""
    clips = {}
    for drawing in page.get_drawings(extended=True):
        level = drawing["level"]
        for open_level in [open_level for open_level in clips if open_level >= level]:
            del clips[open_level]
        if drawing["type"] == "clip":
            around = clips[max(clips)] if clips else None
            clips[level] = _within(around, tuple(drawing["scissor"]))
        elif drawing["type"] != "group":
            yield drawing, clips[max(clips)] if clips else None


class _ImageClips(mupdf.FzDevice2):
    """A MuPDF device that keeps the stack of open clips while the page runs, and the rectangle it leaves for each image
    drawn, in drawing order. PyMuPDF's image list does not say which clip an image is under, and its drawing list holds no
    images."""

    def __init__(self):
        super().__init__()
        self.stack = []
        self.images = []
        self.unit = mupdf.FzRect(0, 0, 1, 1)
        for name in ("clip_path", "clip_stroke_path", "clip_text", "clip_stroke_text", "clip_image_mask", "pop_clip", "fill_image"):
            getattr(self, "use_virtual_" + name)()

    def _push(self, box):
        box = mupdf.FzRect(box)
        self.stack.append(_within(self.stack[-1] if self.stack else None, (box.x0, box.y0, box.x1, box.y1)))

    def clip_path(self, ctx, path, even_odd, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_path(path, None, ctm), scissor))

    def clip_stroke_path(self, ctx, path, stroke, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_path(path, stroke, ctm), scissor))

    def clip_text(self, ctx, text, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_text(text, None, ctm), scissor))

    def clip_stroke_text(self, ctx, text, stroke, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_text(text, stroke, ctm), scissor))

    def clip_image_mask(self, ctx, image, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_transform_rect(self.unit.internal(), ctm), scissor))

    def pop_clip(self, ctx):
        self.stack.pop()

    def fill_image(self, ctx, image, ctm, alpha, color_params):
        self.images.append(self.stack[-1] if self.stack else None)


def image_clips(page):
    """The rectangle the clips leave for each image of the page, in the order of PyMuPDF's image list (drawing order)."""
    device = _ImageClips()
    mupdf.fz_run_page(page.this, device, mupdf.FzMatrix(), mupdf.FzCookie())
    return device.images
