# The drawing sequence of a page: every fill, stroke, text and image the page draws, in the order the content stream
# draws it, with its place in that order and the clips open around it. PyMuPDF numbers the drawings of its drawing list
# and the text of its text trace by that place ("seqno"); its bounding-box log (get_bboxlog) holds one entry per place,
# images included, which neither list holds. A MuPDF device run over the page gives the clips: it counts the places as
# MuPDF does (a clip takes none) and keeps the stack of open clips, each as the rectangle it leaves and whether its
# path is one rectangle. Each place is checked against the bounding-box log's entry of the same place (the same kind of
# drawing), so a page whose places the device does not count as PyMuPDF does stops the read-back.
from pymupdf import mupdf

import paths

KINDS = {"fill_path": "fill-path", "stroke_path": "stroke-path", "fill_text": "fill-text", "stroke_text": "stroke-text",
         "ignore_text": "ignore-text", "fill_image": "fill-image", "fill_image_mask": "fill-imgmask", "fill_shade": "fill-shade"}


def _within(outer, inner):
    return inner if outer is None else (max(outer[0], inner[0]), max(outer[1], inner[1]), min(outer[2], inner[2]), min(outer[3], inner[3]))


class _Sequence(mupdf.FzDevice2):
    def __init__(self, rectangles):
        super().__init__()
        self.stack = []
        self.places = []
        self.rectangles = list(rectangles)
        for name in list(KINDS) + ["clip_path", "clip_stroke_path", "clip_text", "clip_stroke_text", "clip_image_mask", "pop_clip"]:
            getattr(self, "use_virtual_" + name)()

    def _push(self, box, rectangle):
        box = mupdf.FzRect(box)
        around = self.stack[-1] if self.stack else (None, True)
        self.stack.append((_within(around[0], (box.x0, box.y0, box.x1, box.y1)), around[1] and rectangle))

    def _place(self, kind):
        self.places.append((kind,) + (self.stack[-1] if self.stack else (None, True)))

    def fill_path(self, ctx, path, even_odd, ctm, colorspace, color, alpha, color_params):
        self._place("fill-path")

    def stroke_path(self, ctx, path, stroke, ctm, colorspace, color, alpha, color_params):
        self._place("stroke-path")

    def fill_text(self, ctx, text, ctm, colorspace, color, alpha, color_params):
        self._place("fill-text")

    def stroke_text(self, ctx, text, stroke, ctm, colorspace, color, alpha, color_params):
        self._place("stroke-text")

    def ignore_text(self, ctx, text, ctm):
        self._place("ignore-text")

    def fill_image(self, ctx, image, ctm, alpha, color_params):
        self._place("fill-image")

    def fill_image_mask(self, ctx, image, ctm, colorspace, color, alpha, color_params):
        self._place("fill-imgmask")

    def fill_shade(self, ctx, shade, ctm, alpha, color_params):
        self._place("fill-shade")

    def clip_path(self, ctx, path, even_odd, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_path(path, None, ctm), scissor), self.rectangles.pop(0) if self.rectangles else None)

    def clip_stroke_path(self, ctx, path, stroke, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_path(path, stroke, ctm), scissor), False)

    def clip_text(self, ctx, text, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_text(text, None, ctm), scissor), False)

    def clip_stroke_text(self, ctx, text, stroke, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_bound_text(text, stroke, ctm), scissor), False)

    def clip_image_mask(self, ctx, image, ctm, scissor):
        self._push(mupdf.ll_fz_intersect_rect(mupdf.ll_fz_transform_rect(mupdf.FzRect(0, 0, 1, 1).internal(), ctm), scissor), False)

    def pop_clip(self, ctx):
        self.stack.pop()


def places(page, number):
    """Each place of the page's drawing sequence: (kind, the bounding box PyMuPDF gives it, the rectangle the clips open
    around it leave or None for no clip, whether every one of those clips is one rectangle)."""
    clips = [drawing for drawing in page.get_drawings(extended=True) if drawing["type"] == "clip"]
    device = _Sequence([len(clip["items"]) == 1 and paths.is_rectangle(clip) for clip in clips])
    mupdf.fz_run_page(page.this, device, mupdf.FzMatrix(), mupdf.FzCookie())
    log = page.get_bboxlog()
    if device.rectangles or any(place[2] is None for place in device.places):
        raise ValueError(f"page {number}: the {len(clips)} clips of PyMuPDF's drawing list are not the clip paths the page draws")
    if len(log) != len(device.places) or any(entry[0] != place[0] for entry, place in zip(log, device.places)):
        raise ValueError(f"page {number}: the drawing sequence of {len(device.places)} places does not match the {len(log)} of PyMuPDF's log")
    return [(place[0], tuple(entry[1]), place[1], place[2]) for entry, place in zip(log, device.places)]
