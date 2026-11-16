# PDF profiles and accessibility

This page is for people who write templates. It explains which PDF standard to choose, what the engine does for you,
what you must give it, and what it refuses. The last part lists the PDF/UA checks that no program can make, and how
this engine meets each one or leaves it to you.

## Choose a profile

You choose the standard when you render:

```csharp
var options = new PdfRenderOptions(CreationDate: date) { Profile = PdfProfile.PdfA3a.AndPdfUa1() };
var pdf = document.ToPdf(fonts, options);
```

| Profile | What the file is | Use it for |
| --- | --- | --- |
| `PdfProfile.Plain` | plain PDF 1.7 (the default) | drafts, previews |
| `PdfProfile.PdfA3b` | PDF/A-3 level B: archivable, looks the same in 30 years | archives that need only the picture |
| `PdfProfile.PdfA3a` | PDF/A-3 level A: level B plus a logical structure (tagged PDF) | archives that must stay searchable and readable |
| `PdfProfile.PdfUa1` | PDF/UA-1: accessible for people who use a screen reader | documents for the public |
| `PdfProfile.PdfA3a.AndPdfUa1()` | both at once | business documents: invoices, timesheets, letters |

For a business document, choose `PdfA3a.AndPdfUa1()`. The samples do.

## What the engine adds for you

You do not build any of this; the profile does.

- **Metadata.** The title, author, language and dates go into the XMP metadata and the document information, with
  the same values. The file says which standards it follows.
- **Colour.** PDF/A needs device-independent colour. The engine embeds an sRGB colour profile, so your RGB colours,
  greys and images are defined exactly.
- **Structure (tagged PDF).** Every paragraph, heading, table, row, cell and image becomes a structure element, in
  reading order. Reading order is the order you wrote the elements in.
- **Decoration and page furniture.** Backgrounds, borders and table grid lines are marked as decoration. The page
  header and footer are marked as page furniture, so a screen reader does not read "page 3 of 7" between two
  paragraphs. A table's header row that repeats on every page is in the structure once.
- **Tables.** Header cells (from `HeaderRow`) are marked as column headers. Cells that span columns or rows say so.
- **Attachments.** Attached files are embedded as PDF/A-3 asks: with their media type, a description and their
  relationship to the document.

## What you must give

The engine cannot guess these. If a profile needs one and you did not give it, rendering fails with an error that
names the rule and points at your line of code. The engine never invents a value to make the file pass.

| You give | How | Needed by | Error when missing |
| --- | --- | --- | --- |
| a title | `Document(…, title: "…")` | PDF/UA | `Documents.Pdf.Conformance.PdfUa1.MissingTitle` |
| the language | `Document(…, language: "nl-NL")` | PDF/UA | `Documents.Pdf.Conformance.PdfUa1.MissingLanguage` |
| what each image shows | `Image(bytes).Alt("Company logo")` | PDF/UA | `Documents.Pdf.Conformance.PdfUa1.FigureWithoutAlternativeText` |
| …or that it is only decoration | `Image(bytes).Decorative()` | PDF/UA | (same) |
| headings that start at level 1 and do not skip a level going down | `Heading(1, …)`, then `Heading(2, …)` | PDF/UA | `…PdfUa1.FirstHeadingNotH1`, `…PdfUa1.HeadingLevelSkipped` |
| tables whose rows fill every column (spans count) | as many cells (or spans) as columns | PDF/UA | `…PdfUa1.IrregularTable` |
| the relationship of each attachment | `.WithRelationship(AttachmentRelationship.Data)` | PDF/A-3 | `Documents.Pdf.Conformance.PdfA3.AttachmentWithoutRelationship` |

A profile also refuses what it cannot carry, however it was given:

| Refused | Profile | Error |
| --- | --- | --- |
| a page side below 3 or above 14 400 units (veraPDF rule 6.1.13-11) | PDF/A-3 | `Documents.Pdf.Conformance.PdfA3.PageSize` |
| a title, author, language, attachment name or description, alternative text or replacement text longer than 32 767 bytes as a PDF string (6.1.13-3) | PDF/A-3 | `…PdfA3.TextTooLong` |
| a media type or font name longer than 127 bytes as a PDF name (6.1.13-4) | PDF/A-3 | `…PdfA3.NameTooLong` |
| a media type outside veraPDF's pattern, letters, digits and `_ - + .` on both sides of the slash (6.8-1) | PDF/A-3 | `…PdfA3.MediaType` |
| text in the Private Use Area (6.2.11.7.3-1) | PDF/A-3a | `…PdfA3.PrivateUseText` |
| more than 8 388 607 objects (6.1.13-7) | PDF/A-3 | `…PdfA3.TooManyObjects` |
| a document language that is not a language tag (6.7.4-1, 7.2-29) | PDF/A-3a, PDF/UA | `Documents.Pdf.Conformance.Tagged.InvalidLanguage` |
| glyph text with U+0000, U+FEFF or U+FFFE (6.2.11.7.2-2, 7.21.7-2) | PDF/A-3a, PDF/UA | `…Tagged.UnmappableText` |
| content outside every structure element and artifact (7.1-3) | PDF/A-3a, PDF/UA | `…Tagged.UnmarkedContent` |
| the .notdef glyph (6.2.11.8-1, 7.21.8-1) | PDF/A-3, PDF/UA | `Documents.Pdf.Conformance.Glyphs.NotdefGlyph` |
| a table head without body rows (7.2-14) | PDF/UA | `…PdfUa1.TableHeadWithoutBody` |
| a table whose grid has more than 4 194 304 slots, too large to check for regularity | PDF/UA | `…PdfUa1.TableTooLarge` |

With the code front-end, four of these cannot happen, because it refuses or prevents them earlier: a language that
is not a tag (`Documents.Input.Invalid.Language`), the .notdef glyph (`Documents.Font.GlyphMissing`), unmarked content
and a table head without body rows. A page model made by another program can hit them.

The problems are reported in stages, each stage all together: first the input (the front-end's checks, then the
back-end's contract for page models), then the profile's refusals above, then the checks that need the structure
tree (irregular tables, a table head without a body, a table too large). A later stage runs only when the earlier ones
found nothing, so a missing title hides a table problem until the title is given. Plain PDF refuses none of the
profile's problems.

Some values are checked in every profile, because they would be wrong in any PDF: a language that is not a language
tag such as `nl-NL` (`Documents.Input.Invalid.Language`); an attachment without a file name, with a media type that
is not `type/subtype`, or with the same name as another (`Documents.Input.Invalid.Attachment`); an image that has
both alternative text and `.Decorative()`, or blank alternative text (`Documents.Input.Invalid.ImageDescription`).

### Writing good alternative text

Say what the image means, not what it looks like: "Logo of Versteeg IT Solutions", not "blue square with letters".
If the image only decorates the page, mark it `.Decorative()`. An image in a page header or footer is page
furniture and is not read out; give it no alternative text there.

### Attaching a file

```csharp
var xml = Attachment("timesheet.xml", bytes, "application/xml")
    .WithRelationship(AttachmentRelationship.Data)
    .WithDescription("Timesheet summary")
    .WithModificationDate(reportDate);
var document = Document([...], title: "…", language: "nl-NL").Attach(xml);
```

Relationships: `Data` (the document's data in machine-readable form, such as an invoice's XML), `Source` (what the
document was made from), `Alternative` (another form of the same content), `Supplement`, `Unspecified`.

## How the result is checked

The reference is veraPDF 1.30.4, the PDF Association's validator. The tests run it in Docker on every sample for
PDF/A-3b, PDF/A-3a and PDF/UA-1 (`ReferenceValidationTests`; skipped with a message when Docker is not there). The
script `tools/verify-samples.ps1` does the same and prints every rule that fails. The tests also read the structure
back with PdfPig, an independent reader, and extract the attachment with qpdf to compare it byte for byte.

## The checks no program can make (PDF/UA, Matterhorn Protocol 1.1)

The Matterhorn Protocol 1.1 (PDF Association, April 2021) lists 137 ways a file can fail PDF/UA-1. For 48 of them
the "How" column says **H**: a person has to judge. (The protocol's own introduction still says 136 and 47; the 1.1
change log added one and moved two from machine to human.) The table lists all 48 and says, for this engine's output,
whether the condition cannot occur, is met by the design, or is left to you or still open.

"Not possible" means the engine cannot produce the feature at all (no forms, links, annotations, sound, scripts).

| Condition | What a person checks | This engine |
| --- | --- | --- |
| 01-001 | an artifact is tagged as real content | Met by design: only backgrounds, borders, grid lines, decorative images, page header and footer, and repeated header rows are artifacts; the painter decides, not the author. Text you write in the body is content, even if you meant it as decoration. |
| 01-002 | real content is marked as an artifact | **Your part.** Everything in a page header or footer is an artifact. Put nothing there that is not also in the body. In the timesheet sample, the title shown in the first-page header is therefore not read out; the document title in the metadata carries it. |
| 01-006, 09-003 | a structure type does not fit its content | Met where the element says what it is: Heading → H1–H6, Paragraph → P, Table → Table, Image → Figure. Blocks and rows of blocks are Div, which carries no meaning. **Your part:** a bold paragraph used as a heading is a P; use `Heading`. |
| 02-002 | a role mapping is inappropriate | Not possible: only standard structure types are written, so there is no role map. |
| 03-001, 03-002, 03-003 | flickering actions, media or scripts | Not possible. |
| 04-001 | meaning carried only by colour, format or layout | **Your part.** Bold and colour are not tagged as emphasis. Do not let colour alone say "overdue". |
| 05-001, 05-002, 05-003 | sound without another form | Not possible. |
| 06-004 | the title does not identify the document | **Your part:** the title you pass (the timesheet uses "Urenstaat customer period"). |
| 08-001, 08-002 | OCR text errors, untagged OCR text | Not possible: no OCR. |
| 09-001 | tags not in logical reading order | Met by design: reading order is document order; columns of a row of blocks are read left to right. |
| 09-002 | structure nested in a meaningless way | Mostly met: the front-end cannot put a table inside a heading or paragraph. A table inside a table cell is allowed and tagged as written. |
| 11-007 | the declared language is wrong | **Your part:** one language per document. Open: no language per paragraph or span yet, so a quote in another language is read in the document's language. |
| 12-001 | stretched characters not represented | Not possible: justification stretches spaces, never glyphs. |
| 13-001 | a graphic that is not text or artifact is not a Figure | Met: images are Figure or decorative; filled rectangles are always decoration. |
| 13-002 | a link with a meaningful background | Not possible: no links yet. |
| 13-003 | a caption not tagged as Caption | **Open:** there is no caption element; a caption is a paragraph. |
| 13-005 | ActualText used where Alt fits better | Met: images and figures get Alt. ActualText is written only on the marked content of a glyph whose text differs from its ToUnicode entry (a ligature, a shared hyphen glyph), to give that glyph's own text, where Alt does not apply. |
| 13-006 | a graphic that means something only in its group, tagged alone | Not possible: an image is always one picture. |
| 13-007 | a more accessible form is not used | **Your part.** |
| 13-008 | an image of text without ActualText | **Open:** an image that shows words (a logo with a name) gets only Alt; there is no ActualText option yet. |
| 14-001 | headings not tagged | Met when you use `Heading`. **Your part** otherwise (see 01-006). |
| 14-004 | numbered headings not in Arabic numerals | Met: H1–H6. |
| 14-005 | a 7th-level heading not tagged H7 | **Open (limit):** levels above 6 are refused as input; such content cannot be expressed. |
| 15-001 | a row header not tagged as header | **Open (limit):** header cells exist only in header rows; there are no row headers. |
| 15-002 | a column header not tagged as header | Met when you use `HeaderRow`; **your part** to use it. |
| 15-004 | a table used for non-tabular content | Met for `Row(...)`: a row of blocks is a layout, tagged as Div, not Table. **Your part** for `Table(...)`. |
| 15-005 | a cell's header cannot be determined | Met for simple tables: every header cell has Scope Column and its spans. **Open** for header rows that nest (a header spanning two sub-headers): no Headers/IDs attributes yet. |
| 16-001, 16-002, 16-003 | lists without list tags or numbering | **Open (limit):** there is no list element; a list written as paragraphs is tagged as paragraphs. |
| 17-001 | a formula not tagged as Formula | **Open (limit):** no formula element. |
| 18-001 | headers and footers not pagination artifacts | Met. |
| 18-002 | header and footer artifacts without Header/Footer subtype | Met. |
| 19-001, 19-002 | notes and references not tagged | Not possible: no footnotes or references. |
| 22-001 | article threads out of order | Not possible. |
| 24-001 | non-interactive forms without PrintFields | Not possible. |
| 28-001, 28-003, 28-013 | annotations out of order, used for formatting, image maps | Not possible: no annotations. |
| 29-001 | scripts that need keystroke timing | Not possible. |
| 31-010 | a font embedded that may not legally be embedded | Partly met: the engine refuses a font whose licence bits forbid embedding (`Documents.Pdf.Write.FontEmbeddingNotPermitted`). The bits are not the licence; **your part** to use fonts you may embed (Lato: SIL OFL). |

Source: Matterhorn Protocol 1.1, https://pdfa.org/wp-content/uploads/2021/04/Matterhorn-Protocol-1-1.pdf (fetched
from the Wayback Machine copy of 2026-09-21; pdfa.org refuses scripted downloads).
