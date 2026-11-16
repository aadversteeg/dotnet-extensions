#!/usr/bin/env python3
"""Builds docs/conformance-matrix.md: every rule of the veraPDF profiles PDF/A-3b, PDF/A-3a and PDF/UA-1 and how the
engine meets it.

The rules come from the pinned veraPDF image (the same digest the reference tests use), read out of its jar. Each rule
gets one class:

- by construction: the writer cannot violate it; the reason says why;
- checked: the input could violate it; the engine refuses such input, and the entry names the error code and the test;
- cannot occur: the rule is about a feature the engine never writes.

The script stops when a rule has no class, so a new veraPDF version cannot add a rule nobody decided about.

Usage: python tools/conformance/build-matrix.py [--profiles DIR]   (DIR: PDFA-3B.xml, PDFA-3A.xml, PDFUA-1.xml;
       without it the files are read from the pinned image with Docker)
"""

import argparse
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
IMAGE = "verapdf/cli@sha256:f085855d442ad415e79fabbabcdd1803c2db4ee097cd5b94506c95ecdcfa1728"
JAR = "/opt/verapdf/bin/cli-1.30.4.jar"
PROFILES = {"PDF/A-3b": "PDFA-3B.xml", "PDF/A-3a": "PDFA-3A.xml", "PDF/UA-1": "PDFUA-1.xml"}
NS = {"v": "http://www.verapdf.org/ValidationProfile"}

BY = "by construction"
CHECKED = "checked"
NEVER = "cannot occur"

# Tests named in the "checked" entries live in these classes.
LIMITS = "Fluent ProfileLimitTests; Render.Pdf PageModelValidationTests"
CONTRACT = "Render.Pdf PageModelContractTests"
UA = "Fluent ConformanceRefusalTests; Render.Pdf PageModelValidationTests"

# Each entry: rule id or a clause prefix ending in '*' -> (class, how). The most specific match wins.
CLASSIFICATION = {
    # PDF/A-3: file structure (ISO 19005-3 6.1)
    "6.1.2-*": (BY, "PdfWriter writes `%PDF-1.7`, a line feed and a comment of four bytes above 127."),
    "6.1.3-1": (CHECKED, "A trailer /ID is always written; a given identifier that is empty or unset is refused: `Pdf.Write.InvalidFileIdentifier` (Render.Pdf PageModelContractTests)."),
    "6.1.3-2": (NEVER, "The engine never encrypts."),
    "6.1.3-3": (BY, "PdfWriter ends the file with `%%EOF` and one line feed."),
    "6.1.4-*": (BY, "PdfWriter writes the cross-reference table with single line feeds."),
    "6.1.6-*": (BY, "Hexadecimal strings are written as two hex digits per byte (PdfSerializer)."),
    "6.1.7.1-*": (BY, "The writer sets /Length itself (a given /Length is `Pdf.Write.InvalidObject`), writes `stream` + LF and LF + `endstream`, and never writes /F, /FFilter or /FDecodeParms."),
    "6.1.7.2-1": (BY, "Only /FlateDecode and /DCTDecode are written."),
    "6.1.8-1": (BY, "Names are encoded from .NET strings as UTF-8; structure types are the fixed standard names."),
    "6.1.9-1": (BY, "PdfWriter writes `n 0 obj` with single spaces."),
    "6.1.10-1": (NEVER, "No inline images."),
    "6.1.12-*": (NEVER, "No permissions or signature dictionaries."),
    "6.1.13-1": (BY, "`PdfInteger` holds a C# int, the rule's range; `PdfNumber.From` makes a whole number outside it a real, which `PdfSerializer` always writes with a decimal point (Pdf PdfObjectRulesTests). The front-end also refuses any length above 14 400 pt (Fluent LengthLimitTests)."),
    "6.1.13-2": (CHECKED, "A real beyond ±3.403e38 is `Pdf.Write.InvalidNumber` (Pdf PdfObjectRulesTests)."),
    "6.1.13-3": (CHECKED, "Title, author, language, attachment name and description, alternative text and replacement text longer than 32 767 bytes as a string: `Pdf.Conformance.PdfA3.TextTooLong` (" + LIMITS + ")."),
    "6.1.13-4": (CHECKED, "A media type or font name longer than 127 bytes as a name: `Pdf.Conformance.PdfA3.NameTooLong` (" + LIMITS + ")."),
    "6.1.13-5": (BY, "Reals are written with four decimals, so nothing closer to zero than 0.0001 but zero itself."),
    "6.1.13-7": (CHECKED, "More than 8 388 607 objects: `Pdf.Conformance.PdfA3.TooManyObjects` (Render.Pdf PageModelValidationTests)."),
    "6.1.13-8": (BY, "Each drawn item is one q/Q pair; pairs are never nested."),
    "6.1.13-9": (NEVER, "No DeviceN colour spaces."),
    "6.1.13-10": (BY, "Codes are 16-bit glyph ids under Identity-H, so no CID above 65 535."),
    "6.1.13-11": (CHECKED, "A page side outside 3..14 400 units: `Pdf.Conformance.PdfA3.PageSize` (" + LIMITS + ")."),
    # graphics (6.2)
    "6.2.2-1": (BY, "Content streams use only the operators of ContentOperators, all defined in ISO 32000-1."),
    "6.2.2-2": (BY, "Every page has its own /Resources with the fonts and images it uses."),
    "6.2.3-*": (BY, "One PDF/A output intent with the embedded sRGB monitor profile (SrgbIccProfile)."),
    "6.2.4.2-1": (NEVER, "No ICCBased colour spaces: images are DeviceRGB or DeviceGray under the output intent."),
    "6.2.4.2-2": (NEVER, "No CMYK, no overprint."),
    "6.2.4.3-2": (BY, "DeviceRGB is used under the RGB output intent."),
    "6.2.4.3-3": (NEVER, "No DeviceCMYK: a CMYK JPEG is `Image.Unsupported`."),
    "6.2.4.3-4": (BY, "DeviceGray is used under the output intent."),
    "6.2.4.4-*": (NEVER, "No DeviceN or Separation colour spaces."),
    "6.2.5-*": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.6-1": (NEVER, "No rendering intent is written."),
    "6.2.8-1": (NEVER, "No /Alternates."),
    "6.2.8-2": (NEVER, "No /OPI."),
    "6.2.8-3": (NEVER, "No /Interpolate."),
    "6.2.8-4": (BY, "Image samples are written with 8 bits per component."),
    "6.2.8-5": (NEVER, "No image masks; transparency is a soft-mask image."),
    "6.2.8.3-*": (NEVER, "No JPEG 2000."),
    "6.2.9-*": (NEVER, "No form, reference or PostScript XObjects; only image XObjects."),
    "6.2.10-1": (NEVER, "No blend modes."),
    "6.2.10-2": (BY, "A PDF/A file always has the output intent."),
    # fonts (6.2.11)
    "6.2.11.2-*": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.3.1-1": (BY, "Encoding /Identity-H with CIDSystemInfo Adobe-Identity-0."),
    "6.2.11.3.2-1": (BY, "/CIDToGIDMap /Identity."),
    "6.2.11.3.3-*": (BY, "Only the predefined CMap Identity-H is used."),
    "6.2.11.4.1-1": (BY, "Every font is embedded."),
    "6.2.11.4.1-2": (CHECKED, "A glyph id the font does not have is `Pdf.Write.InvalidPageModel` (Render.Pdf PageModelValidationTests); the subset keeps every glyph drawn."),
    "6.2.11.4.2-1": (NEVER, "No Type 1 fonts."),
    "6.2.11.4.2-2": (NEVER, "No /CIDSet is written."),
    "6.2.11.5-1": (BY, "The /W widths come from the 'hmtx' of the embedded font program."),
    "6.2.11.6-*": (NEVER, "No simple TrueType fonts; every font is composite."),
    "6.2.11.7.2-1": (BY, "Every font gets a ToUnicode map with every glyph it shows (a glyph without text is `Pdf.Write.InvalidPageModel`)."),
    "6.2.11.7.2-2": (CHECKED, "Text with U+0000 and other control characters or noncharacters is `Input.Invalid.Text` in the front-end (Fluent TextCharacterTests); glyph text with U+0000, U+FEFF or U+FFFE is `Pdf.Conformance.Tagged.UnmappableText` (Render.Pdf PageModelValidationTests)."),
    "6.2.11.7.3-1": (CHECKED, "Glyph text in the Private Use Area at level A: `Pdf.Conformance.PdfA3.PrivateUseText` (Render.Pdf PageModelValidationTests)."),
    "6.2.11.8-1": (CHECKED, "The front-end never draws glyph 0 (a missing glyph is `Font.GlyphMissing`); a page model that does is `Pdf.Conformance.Glyphs.NotdefGlyph` (Render.Pdf PageModelValidationTests)."),
    # annotations, forms, actions (6.3-6.5)
    "6.3.*": (NEVER, "No annotations."),
    "6.4.1-*": (NEVER, "No interactive forms."),
    "6.4.2-1": (NEVER, "No interactive forms."),
    "6.4.2-2": (NEVER, "No /NeedsRendering."),
    "6.4.3-*": (NEVER, "No signatures."),
    "6.5.*": (NEVER, "No actions."),
    # metadata (6.6)
    "6.6.2.1-1": (BY, "The catalog's /Metadata is the XMP stream of type /Metadata, subtype /XML."),
    "6.6.2.1-2": (BY, "The xpacket header has no bytes attribute (XmpPacket)."),
    "6.6.2.1-3": (BY, "The xpacket header has no encoding attribute (XmpPacket)."),
    "6.6.2.1-4": (CHECKED, "The packet is written by an XML writer; metadata text with a character XML forbids is `Input.Invalid.MetadataText` (Fluent MetadataTextTests) or `Pdf.Write.InvalidText`, a name that is not an XML name `Pdf.Write.InvalidPropertyName` (Pdf ConformanceBuildingBlockTests)."),
    "6.6.2.1-5": (BY, "UTF-8 without a byte-order mark."),
    "6.6.2.3.1-*": (BY, "Only Dublin Core, xmp, pdf and pdfaid properties, and pdfuaid with its extension schema."),
    "6.6.2.3.2-1": (BY, "The one extension schema (pdfuaid) is written with every field the container schema defines."),
    "6.6.2.3.3-*": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.4-*": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    # logical structure (6.7)
    "6.7.2.2-1": (BY, "Tagged profiles write /MarkInfo << /Marked true >>."),
    "6.7.3.3-1": (BY, "Tagged profiles write /StructTreeRoot."),
    "6.7.3.4-*": (BY, "Only standard structure types are written; there is no role map."),
    "6.7.4-1": (CHECKED, "The front-end refuses a language that is not a language tag (`Input.Invalid.Language`); the back-end refuses one in a page model (`Pdf.Conformance.Tagged.InvalidLanguage`, Render.Pdf PageModelValidationTests). One pattern: Core LanguageTags."),
    # embedded files (6.8)
    "6.8-1": (CHECKED, "A media type outside the rule's pattern: `Pdf.Conformance.PdfA3.MediaType` (" + LIMITS + "); a missing one is `Pdf.Write.InvalidPageModel`."),
    "6.8-2": (BY, "Every file specification has /F and /UF."),
    "6.8-3": (CHECKED, "An attachment without a relationship: `Pdf.Conformance.PdfA3.AttachmentWithoutRelationship` (Fluent ConformanceRefusalTests)."),
    "6.8-4": (BY, "Every attachment is in the catalog's /AF."),
    # optional content, alternates, requirements (6.9-6.11)
    "6.9-*": (NEVER, "No optional content."),
    "6.10-*": (NEVER, "No alternate presentations or presentation steps."),
    "6.11-1": (NEVER, "No /Requirements."),
    # PDF/UA-1
    "5-*": (BY, "pdfuaid:part 1 with the prefix pdfuaid, declared by the extension schema."),
    "6.1-1": (BY, "PdfWriter writes `%PDF-1.7`."),
    "6.2-1": (BY, "/MarkInfo << /Marked true >>."),
    "7.1-1": (BY, "Each content run is in exactly one marked-content sequence: a structure MCID or an Artifact, never one inside the other (PageContentStream)."),
    "7.1-2": (BY, "As 7.1-1; a structure marker inside an artifact is `Pdf.Write.InvalidStructure` (PageEvents)."),
    "7.1-3": (CHECKED, "Content outside every structure node and artifact: `Pdf.Conformance.Tagged.UnmarkedContent` (Render.Pdf TaggingTests)."),
    "7.1-4": (BY, "No /Suspects is written."),
    "7.1-5": (BY, "Only standard structure types; no role map."),
    "7.1-6": (BY, "No role map."),
    "7.1-7": (BY, "No role map."),
    "7.1-8": (BY, "The catalog has the XMP /Metadata."),
    "7.1-9": (CHECKED, "No title: `Pdf.Conformance.PdfUa1.MissingTitle` (" + UA + ")."),
    "7.1-10": (BY, "/ViewerPreferences << /DisplayDocTitle true >>."),
    "7.1-11": (BY, "/StructTreeRoot."),
    "7.1-12": (BY, "Every structure element has /P."),
    "7.2-2": (NEVER, "No outlines."),
    "7.2-3": (CHECKED, "Table kids other than rows and row groups: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-4": (CHECKED, "A TR outside a table or row group: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-5": (CHECKED, "A THead outside a table: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-6": (CHECKED, "A TBody outside a table: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-7": (NEVER, "No TFoot role; the nesting rule covers it too."),
    "7.2-8": (CHECKED, "A TH outside a row: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-9": (CHECKED, "A TD outside a row: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-10": (CHECKED, "Row kids other than cells: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-11": (BY, "The front-end makes at most one header group per table (TableTranslator)."),
    "7.2-12": (NEVER, "No TFoot role."),
    "7.2-13": (NEVER, "No TFoot role."),
    "7.2-14": (CHECKED, "Header rows are a THead only when the table has body rows (Samples TaggedPdfTests); a hand-made THead without TBody is `Pdf.Conformance.PdfUa1.TableHeadWithoutBody` (" + CONTRACT + "). Found by GeneratedDocumentTests."),
    "7.2-15": (CHECKED, "Overlapping cells: `Pdf.Conformance.PdfUa1.IrregularTable` (TableSlots Overlap; Render.Pdf PageModelValidationTests)."),
    "7.2-16": (NEVER, "No Caption role."),
    "7.2-17": (NEVER, "No list roles."),
    "7.2-18": (NEVER, "No list roles."),
    "7.2-19": (NEVER, "No list roles."),
    "7.2-20": (NEVER, "No list roles."),
    "7.2-21": (CHECKED, "PDF/UA-1 requires the catalog /Lang: `Pdf.Conformance.PdfUa1.MissingLanguage` (" + UA + ")."),
    "7.2-22": (CHECKED, "As 7.2-21: alternative text takes the catalog's language."),
    "7.2-23": (NEVER, "No /E entries."),
    "7.2-24": (NEVER, "No annotations."),
    "7.2-25": (NEVER, "No form fields."),
    "7.2-26": (NEVER, "No TOC roles."),
    "7.2-27": (NEVER, "No TOC roles."),
    "7.2-28": (NEVER, "No TOC roles."),
    "7.2-29": (CHECKED, "As 6.7.4-1: `Input.Invalid.Language`, `Pdf.Conformance.Tagged.InvalidLanguage`."),
    "7.2-30": (CHECKED, "Replacement-text spans take the catalog's language, which PDF/UA-1 requires (`MissingLanguage`)."),
    "7.2-31": (NEVER, "No /Alt on marked content; spans carry only /ActualText."),
    "7.2-32": (NEVER, "No /E on marked content."),
    "7.2-33": (CHECKED, "The XMP language alternatives take the catalog's language, which PDF/UA-1 requires (`MissingLanguage`)."),
    "7.2-34": (CHECKED, "No language: `Pdf.Conformance.PdfUa1.MissingLanguage` (" + UA + ")."),
    "7.2-36": (CHECKED, "THead kids other than rows: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-37": (CHECKED, "TBody kids other than rows: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-38": (NEVER, "No TFoot role."),
    "7.2-39": (NEVER, "No Caption role."),
    "7.2-40": (NEVER, "No list roles."),
    "7.2-41": (CHECKED, "Irregular grid: `Pdf.Conformance.PdfUa1.IrregularTable` (TableRegularity; " + UA + ")."),
    "7.2-42": (CHECKED, "As 7.2-41."),
    "7.2-43": (CHECKED, "As 7.2-41."),
    "7.3-1": (CHECKED, "A figure without alternative text: `Pdf.Conformance.PdfUa1.FigureWithoutAlternativeText` (" + UA + ")."),
    "7.4.2-1": (CHECKED, "`Pdf.Conformance.PdfUa1.FirstHeadingNotH1`, `HeadingLevelSkipped` (" + UA + ")."),
    "7.4.4-1": (BY, "Only numbered headings H1-H6 are written, never H."),
    "7.4.4-2": (BY, "Only numbered headings are written."),
    "7.4.4-3": (BY, "Only numbered headings are written."),
    "7.5-*": (BY, "Header cells get /Scope /Column (StructureTypes)."),
    "7.7-1": (NEVER, "No Formula role."),
    "7.9-*": (NEVER, "No Note role."),
    "7.10-*": (NEVER, "No optional content."),
    "7.11-1": (CHECKED, "An attachment without a file name: `Input.Invalid.Attachment` in the front-end, `Pdf.Write.InvalidPageModel` in the back-end (" + CONTRACT + ")."),
    "7.15-1": (NEVER, "No forms."),
    "7.16-1": (NEVER, "No encryption."),
    "7.18.*": (NEVER, "No annotations, widgets, links or media clips."),
    "7.20-*": (NEVER, "No form or reference XObjects."),
    "7.21.3.1-1": (BY, "As 6.2.11.3.1-1."),
    "7.21.3.2-1": (BY, "As 6.2.11.3.2-1."),
    "7.21.3.3-*": (BY, "As 6.2.11.3.3."),
    "7.21.4.1-1": (BY, "As 6.2.11.4.1-1."),
    "7.21.4.1-2": (CHECKED, "As 6.2.11.4.1-2."),
    "7.21.4.2-1": (NEVER, "No Type 1 fonts."),
    "7.21.4.2-2": (NEVER, "No /CIDSet is written."),
    "7.21.5-1": (BY, "As 6.2.11.5-1."),
    "7.21.6-*": (NEVER, "No simple TrueType fonts."),
    "7.21.7-1": (BY, "As 6.2.11.7.2-1."),
    "7.21.7-2": (CHECKED, "As 6.2.11.7.2-2."),
    "7.21.8-1": (CHECKED, "As 6.2.11.8-1."),
}


def classify(rule_id):
    if rule_id in CLASSIFICATION:
        return CLASSIFICATION[rule_id]
    # "6.2.11.6-*" covers every test of clause 6.2.11.6; "7.18.*" every clause below 7.18, at any depth.
    clause = rule_id.rsplit("-", 1)[0]
    if clause + "-*" in CLASSIFICATION:
        return CLASSIFICATION[clause + "-*"]
    parts = clause.split(".")
    for depth in range(len(parts) - 1, 0, -1):
        candidate = ".".join(parts[:depth]) + ".*"
        if candidate in CLASSIFICATION:
            return CLASSIFICATION[candidate]
    return None


def extract(folder):
    names = " ".join(f"org/verapdf/pdfa/validation/{name}" for name in PROFILES.values())
    subprocess.run(
        ["docker", "run", "--rm", "--cpus", "1", "-v", f"{folder}:/out", "--entrypoint", "sh", IMAGE, "-c", f"cd /out && unzip -o -j -q {JAR} {names}"],
        check=True)


def rules(folder):
    found = {}
    for profile, file in PROFILES.items():
        root = ElementTree.parse(Path(folder) / file).getroot()
        for rule in root.iter("{http://www.verapdf.org/ValidationProfile}rule"):
            identity = rule.find("v:id", NS)
            rule_id = f"{identity.get('clause')}-{identity.get('testNumber')}"
            description = " ".join(rule.find("v:description", NS).text.split())
            entry = found.setdefault(rule_id, {"profiles": [], "object": rule.get("object"), "description": description})
            entry["profiles"].append(profile)
    return found


def sort_key(rule_id):
    clause, test = rule_id.rsplit("-", 1)
    return [int(part) for part in clause.split(".")] + [int(test)]


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--profiles", help="folder with the three profile XML files")
    arguments = parser.parse_args()
    folder = arguments.profiles or tempfile.mkdtemp(prefix="verapdf-profiles-")
    if not arguments.profiles:
        extract(folder)
    found = rules(folder)
    missing = [rule_id for rule_id in found if classify(rule_id) is None]
    if missing:
        sys.exit("No class for: " + ", ".join(sorted(missing, key=sort_key)))

    counts = {BY: 0, CHECKED: 0, NEVER: 0}
    rows = []
    for rule_id in sorted(found, key=sort_key):
        entry = found[rule_id]
        kind, how = classify(rule_id)
        counts[kind] += 1
        description = entry["description"]
        short = description if len(description) <= 140 else description[:137].rsplit(" ", 1)[0] + " …"
        profiles = ", ".join(entry["profiles"])
        rows.append(f"| {rule_id} | {profiles} | {short.replace('|', '/')} | {kind} | {how.replace('|', '/')} |")

    text = f"""# Conformance matrix

Every rule of the three veraPDF validation profiles the engine claims (PDF/A-3b, PDF/A-3a, PDF/UA-1), from the pinned
image `{IMAGE}` (veraPDF 1.30.4), and how the engine meets it. Built by `tools/conformance/build-matrix.py`, which stops
when a rule has no class; run it again after a veraPDF update. The descriptions are veraPDF's rule texts, shortened.

- **by construction**: the writer cannot violate the rule; the last column says why.
- **checked**: input could violate it; the engine refuses such input with the error code named, and the test named
  pins the refusal. Codes are written without the leading `Documents.`.
- **cannot occur**: the rule is about a feature the engine never writes.

A rule that PDF/A-3a adds to PDF/A-3b lists only PDF/A-3a. The generated-documents reference test
(`UnitTests.Ave.Documents.Samples/GeneratedDocumentTests.cs`) checks the whole matrix at once: documents drawn from a
fixed seed are refused or pass veraPDF.

{counts[BY]} rules by construction, {counts[CHECKED]} checked, {counts[NEVER]} cannot occur ({len(found)} rules).

| Rule | Profiles | veraPDF rule text | Class | How |
| --- | --- | --- | --- | --- |
""" + "\n".join(rows) + "\n"
    (ROOT / "docs" / "conformance-matrix.md").write_text(text, encoding="utf-8", newline="\n")
    print(f"{len(found)} rules: {counts}", file=sys.stderr)


if __name__ == "__main__":
    main()
