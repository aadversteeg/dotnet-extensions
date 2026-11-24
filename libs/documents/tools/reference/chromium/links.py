# Lists the link annotations of a print: one line per annotation, page by page in the order of each page's /Annots
# array, in points with the origin at the top left of the page (as rectangles.py and positions.py):
#
#   <page> <x0> <y0> <x1> <y1> uri <URI>       a link whose action is a URI action (ISO 32000-1 §12.6.4.7), with
#                                              the URI as the file holds it
#   <page> <x0> <y0> <x1> <y1> dest <dest>     a link to a destination in the document (§12.3.2), the /Dest entry as
#                                              the file holds it (a name, or an explicit destination array)
#
# The rectangle is the annotation's /Rect (§12.5.2, Table 164), turned into the page's top-left coordinates. Any other
# annotation, a link with another action, or a link with neither an action nor a destination stops the read-back, so a
# print never loses what it holds without a word. Writes nothing for a print without annotations. Run by extract.sh in
# the reference tools image: python3 links.py <file.pdf>
import sys

import pymupdf


def value(document, xref, key):
    kind, text = document.xref_get_key(xref, key)
    return None if kind == "null" else (kind, text)


def target(document, xref, number):
    action = value(document, xref, "A/S")
    if action == ("name", "/URI"):
        kind, uri = value(document, xref, "A/URI")
        if kind != "string":
            raise ValueError(f"page {number}: a URI action whose URI is a {kind}: {uri}")
        return f"uri {uri}"
    if action is None and (dest := value(document, xref, "Dest")) is not None:
        return f"dest {dest[1]}"
    raise ValueError(f"page {number}: a link this read-back does not know (action {action}, xref {xref})")


document = pymupdf.open(sys.argv[1])
for number, page in enumerate(document, start=1):
    for xref, _, _ in page.annot_xrefs():
        if value(document, xref, "Subtype") != ("name", "/Link"):
            raise ValueError(f"page {number}: an annotation that is not a link: {document.xref_object(xref)}")
        kind, text = value(document, xref, "Rect")
        if kind != "array":
            raise ValueError(f"page {number}: a link whose /Rect is a {kind}: {text}")
        rect = (pymupdf.Rect([float(part) for part in text.strip("[]").split()]) * page.transformation_matrix).normalize()
        print(f"{number} {rect.x0:.3f} {rect.y0:.3f} {rect.x1:.3f} {rect.y1:.3f} {target(document, xref, number)}")
