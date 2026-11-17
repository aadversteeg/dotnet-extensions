#!/usr/bin/env python3
"""Builds docs/conformance-matrix.md: every rule of the veraPDF profiles PDF/A-3b, PDF/A-3a and PDF/UA-1 and how the
engine meets it.

The rules come from the pinned veraPDF image (the same digest the reference tests use), read out of its jar. Each rule
gets one class:

- by construction: the writer cannot violate it; the reason says why;
- checked: the input could violate it; the engine refuses such input, and the entry names the error code and the test;
- cannot occur: the rule is about a feature the engine never writes.

Every rule id is listed on its own: there are no wildcard classes, so a new veraPDF version cannot add a rule nobody
decided about. The script stops when a rule of the profiles has no class, and when a class names a rule the profiles
do not have (a renamed or removed rule). The test ConformanceMatrixTests (UnitTests.Extensions.Documents.Architecture) checks
the written matrix: every error code it names exists, every test class it names exists, and each such test class of a
"checked" rule tests one of the rule's codes.

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

# Rules of PDF/UA-1 that repeat a PDF/A-3 rule, and rules that share one refusal: one text each, so the entries cannot
# drift apart.
GLYPH_IN_FONT = "A glyph id the font does not have is `Pdf.Write.InvalidPageModel` (Render.Pdf PageModelValidationTests); the subset keeps every glyph drawn."
UNMAPPABLE = "Text with U+0000 and other control characters or noncharacters is `Input.Invalid.Text` in the front-end (Fluent TextCharacterTests); glyph text with U+0000, U+FEFF or U+FFFE is `Pdf.Conformance.Tagged.UnmappableText` (Render.Pdf PageModelValidationTests)."
NOTDEF = "The front-end never draws glyph 0 (a missing glyph is `Font.GlyphMissing`); a page model that does is `Pdf.Conformance.Glyphs.NotdefGlyph` (Render.Pdf PageModelValidationTests)."
LANGUAGE_TAG = "The front-end refuses a language that is not a language tag (`Input.Invalid.Language`); the back-end refuses one in a page model (`Pdf.Conformance.Tagged.InvalidLanguage`, Render.Pdf PageModelValidationTests). One pattern: Core LanguageTags."
CATALOG_LANGUAGE = "takes the catalog's language, which PDF/UA-1 requires: `Pdf.Conformance.PdfUa1.MissingLanguage` (" + UA + ")."
IRREGULAR = "Irregular grid, filled as wide as the first row: `Pdf.Conformance.PdfUa1.IrregularTable` (TableRegularity; " + UA + "); a grid of more than 4 194 304 slots is not filled: `Pdf.Conformance.PdfUa1.TableTooLarge` (" + CONTRACT + ")."

# Each entry: rule id -> (class, how). One entry per rule, no wildcards (see the module text).
CLASSIFICATION = {
    # PDF/A-3: file structure (ISO 19005-3 6.1)
    "6.1.2-1": (BY, "PdfWriter writes `%PDF-1.7`, a line feed and a comment of four bytes above 127."),
    "6.1.2-2": (BY, "PdfWriter writes `%PDF-1.7`, a line feed and a comment of four bytes above 127."),
    "6.1.3-1": (CHECKED, "A trailer /ID is always written; a given identifier that is empty or unset is refused: `Pdf.Write.InvalidFileIdentifier` (Render.Pdf PageModelContractTests)."),
    "6.1.3-2": (NEVER, "The engine never encrypts."),
    "6.1.3-3": (BY, "PdfWriter ends the file with `%%EOF` and one line feed."),
    "6.1.4-2": (BY, "PdfWriter writes the cross-reference table with single line feeds."),
    "6.1.6-1": (BY, "Hexadecimal strings are written as two hex digits per byte (PdfSerializer)."),
    "6.1.6-2": (BY, "Hexadecimal strings are written as two hex digits per byte (PdfSerializer)."),
    "6.1.7.1-1": (BY, "The writer sets /Length itself (a given /Length is `Pdf.Write.InvalidObject`), writes `stream` + LF and LF + `endstream`, and never writes /F, /FFilter or /FDecodeParms."),
    "6.1.7.1-2": (BY, "The writer sets /Length itself (a given /Length is `Pdf.Write.InvalidObject`), writes `stream` + LF and LF + `endstream`, and never writes /F, /FFilter or /FDecodeParms."),
    "6.1.7.1-3": (BY, "The writer sets /Length itself (a given /Length is `Pdf.Write.InvalidObject`), writes `stream` + LF and LF + `endstream`, and never writes /F, /FFilter or /FDecodeParms."),
    "6.1.7.2-1": (BY, "Only /FlateDecode and /DCTDecode are written."),
    "6.1.8-1": (BY, "Names are encoded from .NET strings as UTF-8; structure types are the fixed standard names."),
    "6.1.9-1": (BY, "PdfWriter writes `n 0 obj` with single spaces."),
    "6.1.10-1": (NEVER, "No inline images."),
    "6.1.12-1": (NEVER, "No permissions or signature dictionaries."),
    "6.1.12-2": (NEVER, "No permissions or signature dictionaries."),
    "6.1.13-1": (BY, "`PdfInteger` holds a C# int, the rule's range; `PdfNumber.From` makes a whole number outside it a real, which `PdfSerializer` always writes with a decimal point (Pdf PdfObjectRulesTests). The front-end also refuses any length above 14 400 pt (Fluent LengthLimitTests)."),
    "6.1.13-2": (CHECKED, "A real beyond ±3.403e38 is `Pdf.Write.InvalidNumber` (Pdf PdfObjectRulesTests)."),
    "6.1.13-3": (CHECKED, "Title, author, language, attachment name and description, alternative text and replacement text longer than 32 767 bytes as a string: `Pdf.Conformance.PdfA3.TextTooLong` (" + LIMITS + "). A file identifier longer than 32 767 bytes: `Pdf.Write.InvalidFileIdentifier` (" + CONTRACT + "). Glyph codes by construction: a TJ string holds at most 16 383 two-byte codes (PageContentStream.MaxGlyphsPerString, pinned by the written-value test A_long_run_of_glyphs_is_shown_with_strings_of_at_most_32767_bytes)."),
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
    "6.2.3-1": (BY, "One PDF/A output intent with the embedded sRGB monitor profile (SrgbIccProfile)."),
    "6.2.3-2": (BY, "One PDF/A output intent with the embedded sRGB monitor profile (SrgbIccProfile)."),
    "6.2.3-3": (BY, "One PDF/A output intent with the embedded sRGB monitor profile (SrgbIccProfile)."),
    "6.2.4.2-1": (NEVER, "No ICCBased colour spaces: images are DeviceRGB or DeviceGray under the output intent."),
    "6.2.4.2-2": (NEVER, "No CMYK, no overprint."),
    "6.2.4.3-2": (BY, "DeviceRGB is used under the RGB output intent."),
    "6.2.4.3-3": (NEVER, "No DeviceCMYK: a CMYK JPEG is `Image.Unsupported`."),
    "6.2.4.3-4": (BY, "DeviceGray is used under the output intent."),
    "6.2.4.4-1": (NEVER, "No DeviceN or Separation colour spaces."),
    "6.2.4.4-2": (NEVER, "No DeviceN or Separation colour spaces."),
    "6.2.5-1": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.5-2": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.5-3": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.5-4": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.5-5": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.5-6": (NEVER, "No extended graphics state, halftones or transfer functions."),
    "6.2.6-1": (NEVER, "No rendering intent is written."),
    "6.2.8-1": (NEVER, "No /Alternates."),
    "6.2.8-2": (NEVER, "No /OPI."),
    "6.2.8-3": (NEVER, "No /Interpolate."),
    "6.2.8-4": (BY, "Image samples are written with 8 bits per component."),
    "6.2.8-5": (NEVER, "No image masks; transparency is a soft-mask image."),
    "6.2.8.3-1": (NEVER, "No JPEG 2000."),
    "6.2.8.3-2": (NEVER, "No JPEG 2000."),
    "6.2.8.3-3": (NEVER, "No JPEG 2000."),
    "6.2.8.3-4": (NEVER, "No JPEG 2000."),
    "6.2.8.3-5": (NEVER, "No JPEG 2000."),
    "6.2.9-1": (NEVER, "No form, reference or PostScript XObjects; only image XObjects."),
    "6.2.9-2": (NEVER, "No form, reference or PostScript XObjects; only image XObjects."),
    "6.2.9-3": (NEVER, "No form, reference or PostScript XObjects; only image XObjects."),
    "6.2.10-1": (NEVER, "No blend modes."),
    "6.2.10-2": (BY, "A PDF/A file always has the output intent."),
    # fonts (6.2.11)
    "6.2.11.2-1": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.2-2": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.2-3": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.2-4": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.2-5": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.2-6": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.2-7": (BY, "Every font is a Type0 font with a CIDFontType2 descendant and an embedded FontFile2 (FontEmbedding); the simple-font rules do not apply."),
    "6.2.11.3.1-1": (BY, "Encoding /Identity-H with CIDSystemInfo Adobe-Identity-0."),
    "6.2.11.3.2-1": (BY, "/CIDToGIDMap /Identity."),
    "6.2.11.3.3-1": (BY, "Only the predefined CMap Identity-H is used."),
    "6.2.11.3.3-2": (BY, "Only the predefined CMap Identity-H is used."),
    "6.2.11.3.3-3": (BY, "Only the predefined CMap Identity-H is used."),
    "6.2.11.4.1-1": (BY, "Every font is embedded."),
    "6.2.11.4.1-2": (CHECKED, GLYPH_IN_FONT),
    "6.2.11.4.2-1": (NEVER, "No Type 1 fonts."),
    "6.2.11.4.2-2": (NEVER, "No /CIDSet is written."),
    "6.2.11.5-1": (BY, "The /W widths come from the 'hmtx' of the embedded font program."),
    "6.2.11.6-1": (NEVER, "No simple TrueType fonts; every font is composite."),
    "6.2.11.6-2": (NEVER, "No simple TrueType fonts; every font is composite."),
    "6.2.11.6-3": (NEVER, "No simple TrueType fonts; every font is composite."),
    "6.2.11.6-4": (NEVER, "No simple TrueType fonts; every font is composite."),
    "6.2.11.7.2-1": (BY, "Every font gets a ToUnicode map with every glyph it shows (a glyph without text is `Pdf.Write.InvalidPageModel`)."),
    "6.2.11.7.2-2": (CHECKED, UNMAPPABLE),
    "6.2.11.7.3-1": (CHECKED, "Glyph text in the Private Use Area at level A: `Pdf.Conformance.PdfA3.PrivateUseText` (Render.Pdf PageModelValidationTests)."),
    "6.2.11.8-1": (CHECKED, NOTDEF),
    # annotations, forms, actions (6.3-6.5)
    "6.3.1-1": (NEVER, "No annotations."),
    "6.3.2-1": (NEVER, "No annotations."),
    "6.3.2-2": (NEVER, "No annotations."),
    "6.3.3-1": (NEVER, "No annotations."),
    "6.3.3-2": (NEVER, "No annotations."),
    "6.3.3-3": (NEVER, "No annotations."),
    "6.3.3-4": (NEVER, "No annotations."),
    "6.4.1-1": (NEVER, "No interactive forms."),
    "6.4.1-2": (NEVER, "No interactive forms."),
    "6.4.1-3": (NEVER, "No interactive forms."),
    "6.4.2-1": (NEVER, "No interactive forms."),
    "6.4.2-2": (NEVER, "No /NeedsRendering."),
    "6.4.3-1": (NEVER, "No signatures."),
    "6.4.3-2": (NEVER, "No signatures."),
    "6.4.3-3": (NEVER, "No signatures."),
    "6.5.1-1": (NEVER, "No actions."),
    "6.5.1-2": (NEVER, "No actions."),
    "6.5.2-1": (NEVER, "No actions."),
    "6.5.2-2": (NEVER, "No actions."),
    # metadata (6.6)
    "6.6.2.1-1": (BY, "The catalog's /Metadata is the XMP stream of type /Metadata, subtype /XML."),
    "6.6.2.1-2": (BY, "The xpacket header has no bytes attribute (XmpPacket)."),
    "6.6.2.1-3": (BY, "The xpacket header has no encoding attribute (XmpPacket)."),
    "6.6.2.1-4": (CHECKED, "The packet is written by an XML writer; metadata text with a character XML forbids is `Input.Invalid.MetadataText` (Fluent MetadataTextTests) or `Pdf.Write.InvalidText`, a name that is not an XML name `Pdf.Write.InvalidPropertyName` (Pdf ConformanceBuildingBlockTests)."),
    "6.6.2.1-5": (BY, "UTF-8 without a byte-order mark."),
    "6.6.2.3.1-1": (BY, "Only Dublin Core, xmp, pdf and pdfaid properties, and pdfuaid with its extension schema."),
    "6.6.2.3.1-2": (BY, "Only Dublin Core, xmp, pdf and pdfaid properties, and pdfuaid with its extension schema."),
    "6.6.2.3.2-1": (BY, "The one extension schema (pdfuaid) is written with every field the container schema defines."),
    "6.6.2.3.3-1": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-2": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-3": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-4": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-5": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-6": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-7": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-8": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-9": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-10": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-11": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-12": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-13": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-14": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-15": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-16": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-17": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.2.3.3-18": (BY, "The extension schema container is written with the namespaces, prefixes and value types the rule names (XmpSchemaDescription)."),
    "6.6.4-1": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    "6.6.4-2": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    "6.6.4-3": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    "6.6.4-4": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    "6.6.4-5": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    "6.6.4-6": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    "6.6.4-7": (BY, "pdfaid:part 3 and pdfaid:conformance A or B, with the prefix pdfaid."),
    # logical structure (6.7)
    "6.7.2.2-1": (BY, "Tagged profiles write /MarkInfo << /Marked true >>."),
    "6.7.3.3-1": (BY, "Tagged profiles write /StructTreeRoot."),
    "6.7.3.4-1": (BY, "Only standard structure types are written; there is no role map."),
    "6.7.3.4-2": (BY, "Only standard structure types are written; there is no role map."),
    "6.7.3.4-3": (BY, "Only standard structure types are written; there is no role map."),
    "6.7.4-1": (CHECKED, LANGUAGE_TAG),
    # embedded files (6.8)
    "6.8-1": (CHECKED, "A media type outside the rule's pattern, matched against the whole text (a final line feed too): `Pdf.Conformance.PdfA3.MediaType` (" + LIMITS + "); a missing one is `Pdf.Write.InvalidPageModel`."),
    "6.8-2": (BY, "Every file specification has /F and /UF."),
    "6.8-3": (CHECKED, "An attachment without a relationship: `Pdf.Conformance.PdfA3.AttachmentWithoutRelationship` (Fluent ConformanceRefusalTests)."),
    "6.8-4": (BY, "Every attachment is in the catalog's /AF."),
    # optional content, alternates, requirements (6.9-6.11)
    "6.9-1": (NEVER, "No optional content."),
    "6.9-2": (NEVER, "No optional content."),
    "6.9-3": (NEVER, "No optional content."),
    "6.9-4": (NEVER, "No optional content."),
    "6.10-1": (NEVER, "No alternate presentations or presentation steps."),
    "6.10-2": (NEVER, "No alternate presentations or presentation steps."),
    "6.11-1": (NEVER, "No /Requirements."),
    # PDF/UA-1
    "5-1": (BY, "pdfuaid:part 1 with the prefix pdfuaid, declared by the extension schema."),
    "5-2": (BY, "pdfuaid:part 1 with the prefix pdfuaid, declared by the extension schema."),
    "5-3": (BY, "pdfuaid:part 1 with the prefix pdfuaid, declared by the extension schema."),
    "5-4": (BY, "pdfuaid:part 1 with the prefix pdfuaid, declared by the extension schema."),
    "5-5": (BY, "pdfuaid:part 1 with the prefix pdfuaid, declared by the extension schema."),
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
    "7.2-11": (CHECKED, "A table with more than one THead, or a THead after another row group: `Pdf.Write.InvalidPageModel` (" + CONTRACT + "). The front-end makes at most one header group per table."),
    "7.2-12": (NEVER, "No TFoot role."),
    "7.2-13": (NEVER, "No TFoot role."),
    "7.2-14": (CHECKED, "The front-end makes header rows a THead only when the table has body rows (the tagged-PDF test A_table_of_header_rows_only_is_tagged_as_a_body_of_header_cells); a hand-made THead without TBody is `Pdf.Conformance.PdfUa1.TableHeadWithoutBody` (" + CONTRACT + "). Found by the generated-documents test."),
    "7.2-15": (CHECKED, "Overlapping cells: `Pdf.Conformance.PdfUa1.IrregularTable` (TableSlots Overlap; Render.Pdf PageModelValidationTests)."),
    "7.2-16": (NEVER, "No Caption role."),
    "7.2-17": (NEVER, "No list roles."),
    "7.2-18": (NEVER, "No list roles."),
    "7.2-19": (NEVER, "No list roles."),
    "7.2-20": (NEVER, "No list roles."),
    "7.2-21": (NEVER, "No /ActualText on structure elements; replacement text is written on marked-content spans only (7.2-30)."),
    "7.2-22": (CHECKED, "Alternative text " + CATALOG_LANGUAGE),
    "7.2-23": (NEVER, "No /E entries."),
    "7.2-24": (NEVER, "No annotations."),
    "7.2-25": (NEVER, "No form fields."),
    "7.2-26": (NEVER, "No TOC roles."),
    "7.2-27": (NEVER, "No TOC roles."),
    "7.2-28": (NEVER, "No TOC roles."),
    "7.2-29": (CHECKED, LANGUAGE_TAG),
    "7.2-30": (CHECKED, "Replacement text on marked-content spans " + CATALOG_LANGUAGE),
    "7.2-31": (NEVER, "No /Alt on marked content; spans carry only /ActualText."),
    "7.2-32": (NEVER, "No /E on marked content."),
    "7.2-33": (CHECKED, "The XMP language alternatives' text " + CATALOG_LANGUAGE),
    "7.2-34": (CHECKED, "No language: `Pdf.Conformance.PdfUa1.MissingLanguage` (" + UA + ")."),
    "7.2-36": (CHECKED, "THead kids other than rows: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-37": (CHECKED, "TBody kids other than rows: `Pdf.Write.InvalidPageModel` (" + CONTRACT + ")."),
    "7.2-38": (NEVER, "No TFoot role."),
    "7.2-39": (NEVER, "No Caption role."),
    "7.2-40": (NEVER, "No list roles."),
    "7.2-41": (CHECKED, IRREGULAR),
    "7.2-42": (CHECKED, IRREGULAR),
    "7.2-43": (CHECKED, IRREGULAR),
    "7.3-1": (CHECKED, "A figure without alternative text: `Pdf.Conformance.PdfUa1.FigureWithoutAlternativeText` (" + UA + ")."),
    "7.4.2-1": (CHECKED, "`Pdf.Conformance.PdfUa1.FirstHeadingNotH1`, `Pdf.Conformance.PdfUa1.HeadingLevelSkipped` (" + UA + ")."),
    "7.4.4-1": (BY, "Only numbered headings H1-H6 are written, never H."),
    "7.4.4-2": (BY, "Only numbered headings are written."),
    "7.4.4-3": (BY, "Only numbered headings are written."),
    "7.5-1": (BY, "Header cells get /Scope /Column (StructureTypes)."),
    "7.5-2": (BY, "Header cells get /Scope /Column (StructureTypes)."),
    "7.7-1": (NEVER, "No Formula role."),
    "7.9-1": (NEVER, "No Note role."),
    "7.9-2": (NEVER, "No Note role."),
    "7.10-1": (NEVER, "No optional content."),
    "7.10-2": (NEVER, "No optional content."),
    "7.11-1": (CHECKED, "An attachment without a file name: `Input.Invalid.Attachment` in the front-end, `Pdf.Write.InvalidPageModel` in the back-end (" + CONTRACT + ")."),
    "7.15-1": (NEVER, "No forms."),
    "7.16-1": (NEVER, "No encryption."),
    "7.18.1-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.1-2": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.1-3": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.2-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.3-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.4-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.4-2": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.5-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.5-2": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.6.2-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.6.2-2": (NEVER, "No annotations, widgets, links or media clips."),
    "7.18.8-1": (NEVER, "No annotations, widgets, links or media clips."),
    "7.20-1": (NEVER, "No form or reference XObjects."),
    "7.20-2": (NEVER, "No form or reference XObjects."),
    "7.21.3.1-1": (BY, "As 6.2.11.3.1-1."),
    "7.21.3.2-1": (BY, "As 6.2.11.3.2-1."),
    "7.21.3.3-1": (BY, "As 6.2.11.3.3."),
    "7.21.3.3-2": (BY, "As 6.2.11.3.3."),
    "7.21.3.3-3": (BY, "As 6.2.11.3.3."),
    "7.21.4.1-1": (BY, "As 6.2.11.4.1-1."),
    "7.21.4.1-2": (CHECKED, GLYPH_IN_FONT),
    "7.21.4.2-1": (NEVER, "No Type 1 fonts."),
    "7.21.4.2-2": (NEVER, "No /CIDSet is written."),
    "7.21.5-1": (BY, "As 6.2.11.5-1."),
    "7.21.6-1": (NEVER, "No simple TrueType fonts."),
    "7.21.6-2": (NEVER, "No simple TrueType fonts."),
    "7.21.6-3": (NEVER, "No simple TrueType fonts."),
    "7.21.6-4": (NEVER, "No simple TrueType fonts."),
    "7.21.7-1": (BY, "As 6.2.11.7.2-1."),
    "7.21.7-2": (CHECKED, UNMAPPABLE),
    "7.21.8-1": (CHECKED, NOTDEF),
}


def classify(rule_id):
    return CLASSIFICATION.get(rule_id)


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
    unknown = [rule_id for rule_id in CLASSIFICATION if rule_id not in found]
    if unknown:
        sys.exit("Classified, but not a rule of the profiles: " + ", ".join(sorted(unknown, key=sort_key)))

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
image `{IMAGE}` (veraPDF 1.30.4), and how the engine meets it. Built by `tools/conformance/build-matrix.py`, which lists
every rule on its own and stops when a rule has no class; run it again after a veraPDF update. The descriptions are
veraPDF's rule texts, shortened. `ConformanceMatrixTests` checks that every error code and test class named here exists,
and that the test classes of a checked rule test its codes.

- **by construction**: the writer cannot violate the rule; the last column says why.
- **checked**: input could violate it; the engine refuses such input with the error code named, and the test named
  pins the refusal. Codes are written without the leading `Documents.`.
- **cannot occur**: the rule is about a feature the engine never writes.

A rule that PDF/A-3a adds to PDF/A-3b lists only PDF/A-3a. The generated-documents reference test
(`UnitTests.Extensions.Documents.Samples/GeneratedDocumentTests.cs`) checks the whole matrix at once: documents drawn from a
fixed seed (100 per profile: borders, backgrounds, spans, nested and broken tables, page areas, images with alpha,
font sizes, lengths at and past the limits, long words) are refused, with a code of an expected family, or pass veraPDF.

{counts[BY]} rules by construction, {counts[CHECKED]} checked, {counts[NEVER]} cannot occur ({len(found)} rules).

| Rule | Profiles | veraPDF rule text | Class | How |
| --- | --- | --- | --- | --- |
""" + "\n".join(rows) + "\n"
    (ROOT / "docs" / "conformance-matrix.md").write_text(text, encoding="utf-8", newline="\n")
    print(f"{len(found)} rules: {counts}", file=sys.stderr)


if __name__ == "__main__":
    main()
