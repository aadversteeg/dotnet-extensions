# Citations audit

Every citation and quotation in the code, tests and documentation, checked against the public text it names.
Date of the check: 2026-11-15, second check 2026-11-16, third check (quotations) 2026-11-16. Branch `m1-fixes`.

## How

- **Extraction.** `tools/extract-citations.py` reads every comment block of the source files and every paragraph
  of the Markdown files, and keeps each block that names an outside text: a section (§), a table, a rule id, a
  numbered standard, or a quotation. A block with a place (clause, table, rule) or a quotation is a citation; a
  block that only names a specification ("PNG", "HarfBuzz") is a mention and claims no place. Since the third check
  it also keeps every block that names no specification but holds a quotation of five words or more (kind
  "quotation"), with the nearest specification named above it in the file as its context: such a block usually quotes
  the specification its member's or its class's summary names. Quotation marks are paired from left to right, so the
  closing mark of a short quoted value never opens a long false quotation; fenced code in Markdown is not read. Run it
  again after a change to see what is new. Generated data, third-party texts, golden files and the two generated
  documents (this audit and the conformance matrix) are not read.
- **Count.** At the first check, 768 blocks: 572 citations and 196 mentions. Now (third check, after its fixes):
  1 209 blocks: 922 citations (187 with a quotation), 64 quotations whose block names no specification, and 223
  mentions. The growth since the first check is the fix rounds' text and the conformance matrix builder, which now
  lists every rule id on its own line (each line names a rule).
- **Check.** Every citation was checked against the public text in batches by a research agent (S1 to S10 below):
  does the place exist and say what the sentence claims, and is every quotation verbatim. Sources:
  - ISO 32000-1:2008 in Adobe's free copy `PDF32000_2008.pdf` (clause and table numbers of ISO 32000-1:2008);
  - ISO 19005-3 (PDF/A-3) and ISO 14289-1 (PDF/UA-1) are not public: only through the rule texts of the veraPDF
    validation profiles 1.30.4 (`veraPDF-validation-profiles`, the same profiles the pinned image uses) and the
    Matterhorn Protocol 1.1; the public preview of ISO 19005-3 for its table of contents. A clause of these
    standards is cited only where a veraPDF rule names it;
  - ISO 16684-1 (XMP) is not public: Adobe's XMP Specification Part 1 (2012), on which it is based;
  - IEC 61966-2-1 (sRGB) is not public: its clause number is no longer cited;
  - W3C CSS specifications (the dated version or the editor's draft, as each verdict says), the HTML Living
    Standard, the Unicode Standard 18 and its annexes, OpenType 1.9.1, the PNG specification (2nd and 3rd edition),
    ITU-T T.81, XML 1.0 (5th edition), RFC 3066 and RFC 6838, ICC.1, Adobe Technical Note #5014, and the HarfBuzz
    source.
- **Fixes.** Every citation that was not right was corrected or removed in the same change as this document; a
  quotation that is not verbatim was replaced by the verbatim text or turned into plain prose.

## Result

Of 572 citations: 486 right; 18 name a specification without a checkable place
(left as they are); 68 were wrong and are fixed: 27 misquoted, 17 claims the text
does not support, 13 wrong clauses, 9 paraphrases written as quotations, 2 clause numbers
of a non-public standard.

Citations written or changed after the check (the corrections themselves, and the text of the fix round) are
listed at the end: 68 changed blocks carry the verdict of the block they replace; 67 new blocks
quote only the veraPDF rule texts (read from the pinned image's profiles) or texts the check above confirmed
verbatim; each says which.

Third check (quotations, see the last section): of 95 quoted texts in 72 blocks the earlier extraction did not keep,
61 were verbatim, 28 close and 6 not in the text named; all 34 are fixed (verbatim now, or prose without quotation
marks). Ten blocks quote no outside text (own words in quotation marks). The 236 blocks new or changed in fix round 3
are listed with their check.

## The fixed citations

| Id | Place (at the check) | Verdict | What the text says (the correction) | Batch |
| --- | --- | --- | --- | --- |
| C006 | `NOTICE.md:27` | WRONG-CLAUSE | CSS Backgrounds 3 §4 is "Rounded Corners" (border-radius); borders are §3, background-color §2.2 — wrong unless the engine implements border-radius. All other clauses exist and match their topics: CSS 2.1 §8.3, §9.4, §10.3.2, §10.3.3, §10.6.2, §10.8, §17, App. E; Text 3 §4.1 white space, §5.4 overflow-wrap, §6 alignment; Break 3 §3.1, §3.2, §4.1, §4.4, §5.2–5.4; Grid 1 §7.2.4 fr, §11.7.1 Find the Size of an fr; Fonts 4 §2 basic font properties, §5 font matching; Sizing 3 §5 intrinsic sizes; Paged Media 3 :first §4.2.2, margin boxes §5, counters §6.1; HTML 15.3.6 + 4.9.12.1 | S5 |
| C029 | `docs/architecture.md:84` | WRONG-CLAUSE | Cascade 4 §7.1 is "Initial Values"; inheritance is §7.2, user-agent origin §6.2. §4 Value Processing right; HTML 15.3.6 heading sizes right | S6/S7 |
| C092 | `src/Ave.Documents.Core/DocumentErrorCodes.cs:61` | NOT-SUPPORTED | production [2] is right, but Char allows #x7F–#x9F (DEL and C1 controls are only "discouraged" in the §2.2 note); excluded are #x0–#x8, #xB, #xC, #xE–#x1F, surrogates, #xFFFE, #xFFFF | S9/S10 |
| C108 | `src/Ave.Documents.Core/SemanticRole.cs:35` | WRONG-CLAUSE | P is defined in 14.8.4.3.2 "Paragraphlike Elements", Table 335 ("(Paragraph) A low-level division of text."). 14.8.4.3.1 is "General" (it only lists P in Table 334 as a block-level element) | S1 |
| C118 | `src/Ave.Documents.Core/TableSlots.cs:7` | WRONG-CLAUSE | Quote OK: HTML §4.9.12.1 heading "Forming a table" (case only). But CSS 2.1 §17.2 does not rely on it: it says "The CSS table model is based on the HTML4 table model". Cell placement on the grid is CSS 2.1 §17.5 ("The rectangle must be as far to the left as possible, but the part of the cell in the first column it occupies must not overlap with any other cell box"; "A cell box cannot extend beyond the last row box of a table or row group"); CSS 2.1 does not define span counts | S5 |
| C138 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:53` | WRONG-CLAUSE | §17.2.1 is "Anonymous table objects", nothing about spans. Spans: §17.5 "Cells may span several rows or columns. (Although CSS 2.1 does not define how the number of spanned rows or columns is determined…)"; colspan/rowspan come from HTML (§4.9.12.1) | S5 |
| C159 | `src/Ave.Documents.DocumentModel/TextStyle.cs:5` | WRONG-CLAUSE | Color 4 §3.1 is "Accessibility and Conveying Information By Color"; the color property is §3.2 "Foreground Color". Fonts 4 §2.1/§2.5/§2.2/§2.4 right | S6/S7 |
| C162 | `src/Ave.Documents.Fluent/Cascade.cs:6` | NOT-SUPPORTED | §6 does not say "a later, more specific origin wins": an inline style and a class rule are the same (author) origin and differ by §6.1 Specificity ("declarations that do not belong to a style rule (such as the contents of a style attribute) are considered to have a specificity higher than any selector"); the parent's value is inheritance §7.2, not cascade | S6/S7 |
| C173 | `src/Ave.Documents.Fluent/InputChecks.cs:41` | WRONG-CLAUSE | the quote is verbatim but sits in Box 3 §4.1 (padding-top… longhands), not §4.2 (shorthand) | S6/S7 |
| C177 | `src/Ave.Documents.Fluent/InputChecks.cs:143` | MISQUOTED | "Negative values are invalid" is not in Grid 1 §7.2.4 (nor anywhere for fr); closest: §7.2.1 "<flex [0,∞]> A non-negative dimension with the unit fr specifying the track's flex factor." | S6/S7 |
| C179 | `src/Ave.Documents.Fluent/InputChecks.cs:252` | PARAPHRASE | Quote 1 is verbatim. Quote 2 is not: 14.9.2.2 says "the empty text string, to indicate that the language is unknown". RFC 3066 §2.1 matches: Primary-subtag = 1*8ALPHA, Subtag = 1*8(ALPHA / DIGIT) | S1 |
| C188 | `src/Ave.Documents.Fluent/TableTranslator.cs:20` | NOT-SUPPORTED | §17.2.1 generates no anonymous row group: a 'table-row' is a "proper table child" of a table, and is misparented only "if its parent is neither a row group box nor a 'table' or 'inline-table' box". The anonymous group is the engine's choice | S5 |
| C193 | `src/Ave.Documents.Fluent/UserAgentStyles.cs:11` | NOT-SUPPORTED | initial medium is §2.5, but Fonts 4 gives no px value for medium: "An <absolute-size> keyword refers to an entry in a table of font sizes computed and kept by the user agent" (§2.5.1: scaling factor 1). 16px = 12pt is browser convention | S6/S7 |
| C205 | `src/Ave.Documents.Fonts/FontSubsetter.cs:13` | PARAPHRASE | "may be a subset" is not in 9.9. Verbatim (last paragraph of 9.9): "As discussed in 9.6.4, "Font Subsets", an embedded font program may contain only the subset of glyphs that are used in the PDF document." The trailing "(§9…" reference is cut off in the batch and was not judged | S1 |
| C209 | `src/Ave.Documents.Fonts/FontSubsetter.cs:159` | NOT-SUPPORTED | equal offsets = no outline is right ("if a glyph has no outline or instructions, then loca[n] = loca[n+1]"); but the spec does NOT recommend 4-byte glyph padding — Recommendations says "Local offsets should be 16-bit aligned, in both the short and long formats of this table." (4-byte is allowed, not recommended). Quote ", OpenType spec" is an extraction artefact, not a quote | S8 |
| C211 | `src/Ave.Documents.Fonts/FontSubsetter.cs:238` | MISQUOTED | verbatim: "Table checksums are the unsigned sum of the uint32 units of a given table." Heading "Calculating Checksums" right; zero-padding rule right | S8 |
| C214 | `src/Ave.Documents.Fonts/OpenType/ClassDefinition.cs:8` | MISQUOTED | verbatim (Class definition format 1): "Any glyph not included in the range of covered glyph IDs is assigned to Class 0." (no "automatically belongs to"); format 2: "The glyph ranges must not overlap." | S8 |
| C218 | `src/Ave.Documents.Fonts/OpenType/LanguageSystem.cs:5` | MISQUOTED | verbatim: "Index of a feature required for this language system; if no required features, set to 0xFFFF." | S8 |
| C223 | `src/Ave.Documents.Fonts/OpenType/LegacyKerning.cs:46` | PARAPHRASE | the three quotes are field-name lists of the KernHeader, subtable header and KernSubtableFormat0 tables, not text in the spec; order and fields right; "Apple … extensions are not supported in OpenType" supports the Apple claim | S8 |
| C231 | `src/Ave.Documents.Fonts/Tables/BigEndian.cs:7` | MISQUOTED | verbatim (Data Types): "All OpenType fonts use big-endian (network) byte order" | S8 |
| C235 | `src/Ave.Documents.Fonts/Tables/CmapTable.cs:8` | MISQUOTED | "no glyph for this character" is nowhere in cmap; closest: "character codes that do not correspond to any glyph in the font should be mapped to glyph index 0. The glyph at this location must be a special glyph representing a missing character, commonly known as .notdef." | S8 |
| C249 | `src/Ave.Documents.Fonts/Tables/NameTable.cs:76` | WRONG-CLAUSE | 9.6.2.1 is Type 1 fonts (Table 111). For a TrueType font program the rule is 9.6.3 TrueType Fonts: "If the TrueType font program's "name" table contains a PostScript name, it shall be used." For the CIDFontType2 the engine writes, BaseFont is in 9.7.4.1, Table 117 ("The PostScript name of the CIDFont") | S1 |
| C256 | `src/Ave.Documents.Fonts/Tables/TableDirectory.cs:19` | WRONG-CLAUSE | 0x00010000, 'OTTO' and the Apple 'true' note are in Table Directory; 'ttcf' is not an sfntVersion value — it is the TTC Header ttcTag ("Font Collection ID string: 'ttcf'") under Font Collections | S8 |
| C271 | `src/Ave.Documents.Images/Jpeg/JpegReader.cs:77` | MISQUOTED | T.81 B.2.1: "a frame shall begin with a frame header and shall contain one or more scans" (cf. 3.1.59 "frame: A group of one or more scans"); claim supported | S9/S10 |
| C278 | `src/Ave.Documents.Images/Png/PngChunk.cs:70` | MISQUOTED | not in 2nd/3rd ed (it is PNG 1.2 wording). 2nd ed §5.3: "Each byte of a chunk type is restricted to the decimal values 65 to 90 and 97 to 122. These correspond to the uppercase and lowercase ISO 646 letters (A-Z and a-z)" (3rd ed: "hexadecimal values 41 to 5A and 61 to 7A") | S9/S10 |
| C282 | `src/Ave.Documents.Images/Png/PngDecoder.cs:52` | MISQUOTED | §5.6 Table 5.3 (3rd ed Table 7) says "Multiple IDAT chunks shall be consecutive"; 2nd ed §11.2.4: "There may be multiple IDAT chunks; if so, they shall appear consecutively with no other intervening chunks."; IHDR first / PLTE before first IDAT right | S9/S10 |
| C293 | `src/Ave.Documents.Images/Png/PngSamples.cs:8` | MISQUOTED | §7.2 (both editions): "These samples are packed into bytes with the leftmost sample in the high-order bits of a byte followed by the other samples for the scanline."; the comment's wording is PNG 1.2 | S9/S10 |
| C295 | `src/Ave.Documents.Images/Png/PngSamples.cs:67` | MISQUOTED | §11.2.3 says "The number of palette entries shall not exceed the range that can be represented in the image bit depth … any out-of-range pixel value found in the image data is an error"; claim supported; 3rd ed §13.1 now requires showing it as opaque black | S9/S10 |
| C297 | `src/Ave.Documents.Images/Png/ScanlineFilters.cs:13` | PARAPHRASE | "bpp" does not occur in 2nd/3rd ed (PNG 1.2 term); §9.2 defines a as "the byte immediately before x, when the bit depth is less than 8", same effect | S9/S10 |
| C319 | `src/Ave.Documents.Layout/Fragment.cs:8` | MISQUOTED | §2 right; real: "The portion of a box that belongs to exactly one fragmentainer." | S6/S7 |
| C326 | `src/Ave.Documents.Layout/ImageLayout.cs:48` | MISQUOTED | No "used height … =" in §10.6.2. Verbatim: "Otherwise, if 'height' has a computed value of 'auto', and the element has an intrinsic ratio then the used value of 'height' is: (used width) / (intrinsic ratio)" (§10.6.2); the stated deviation for width+height is accurate | S5 |
| C338 | `src/Ave.Documents.Layout/MarginCollapsing.cs:7` | MISQUOTED | "the maximum of the adjoining margins" is not in §8.3.1; verbatim: "the resulting margin width is the maximum of the collapsing margins' widths" (negative case: "the maximum of the positive adjoining margins"). Other 4 quotes verbatim (with ellipses) | S5 |
| C343 | `src/Ave.Documents.Layout/PageAreas.cs:8` | NOT-SUPPORTED | §5 never says a page-margin box is its own block formatting context ("formatting context" does not occur in Paged Media 3); §5.3 only gives "the equivalent of CSS2.1 Sections 10.3 and 10.6" | S6/S7 |
| C347 | `src/Ave.Documents.Layout/PagePainter.cs:36` | MISQUOTED | E.2 claims right (step 4 = block backgrounds/borders of in-flow descendants, step 7 = line boxes/inline content). Quote "artifacts … not part of the authored content" found in no source; ISO 32000-1 §14.8.2.2.1: "Artifacts are graphics objects that are not part of the author's original content but rather are generated by the conforming writer in the course of pagination, layout, or other strictly mechanical processes." | S5 |
| C352 | `src/Ave.Documents.Layout/SlicedBox.cs:59` | NOT-SUPPORTED | §5.3 quote verbatim, §4.1 right; but §4.4 does not "allow a UA to overflow": "if there are no possible break points below the top of the fragmentainer, and not all the content fits, the UA may break anywhere in order to avoid losing content off the edge of the fragmentainer" | S6/S7 |
| C369 | `src/Ave.Documents.Layout/Tracks/TrackSizing.cs:109` | MISQUOTED | §11.7.1 right; heading is "Find the Size of an fr" (case); text says "restart this algorithm treating all such tracks as inflexible" (not "treated as inflexible"); sum-at-least-1 right | S6/S7 |
| C375 | `src/Ave.Documents.Pdf/ContentOperation.cs:5` | MISQUOTED | Not in 7.8.2. Verbatim: "It consists of PDF objects denoting operands and operators. The operands needed by an operator shall precede it in the stream." (7.8.2 para 3); NOTE 1 below it: "This postfix notation, in which an operator is preceded by its operands" | S1 |
| C382 | `src/Ave.Documents.Pdf/ContentOperators.cs:41` | NOT-SUPPORTED | cm in Table 57 and 8.3.4 are right. The image sentence is worded wrongly: 8.9.4 says "the unit square of user space … corresponds to the boundary of the image in image space" (image space is w × h, not a unit square). Correct: the image fills the unit square of user space, and cm w 0 0 h x y maps that square onto (x, y, w, h). The effect described is right | S1 |
| C400 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:62` | WRONG-CLAUSE | quote is verbatim (rule 7.1-3) and 14.8.2.2 (Real Content and Artifacts) is right, but 14.8.2.4 is "Extraction of Character Properties" (Unicode mapping), not about tagging; for content belonging to a structure element use 14.7.4 "Structure Content" | S4 |
| C409 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:110` | WRONG-CLAUSE | quote verbatim (continues "(taking into account column spans)"), but that text is rules 7.2-42 and 7.2-43; 7.2-41 is "Table columns shall have the same number of rows (taking into account row spans)" — cite 7.2-42 and 7.2-43 | S4 |
| C424 | `src/Ave.Documents.Pdf/PdfProfile.cs:3` | PARAPHRASE | "visual appearance preserved" is not in the text; closest: 3.10 "conformance level encompassing the requirements of this part of ISO 19005 regarding the visual appearance of electronic documents", and 5.3 NOTE 1 "the rendered visual appearance of a conforming file is preservable over the long term" | S4 |
| C449 | `src/Ave.Documents.Pdf/PdfTrees.cs:8` | MISQUOTED | verbatim 7.9.6: "The tree shall always have exactly one root node, which shall contain a single entry: either Kids or Names but not both. If the root node has a Names entry, it shall be the only node in the tree." Claim supported; 7.9.7 same with Nums | S2/S3 |
| C455 | `src/Ave.Documents.Pdf/PdfWriter.cs:59` | NOT-SUPPORTED | 7.5.4 does not require numbers 1..N without gaps: the table "shall contain one entry for each object number from 0 to the maximum … even if one or more … do not actually occur" and a never-updated file has "only one subsection, whose object numbering begins at 0"; 7.3.10: numbers "may be assigned in any arbitrary order". 1..N is the writer's own rule | S2/S3 |
| C461 | `src/Ave.Documents.Pdf/PdfWriter.cs:155` | NOT-SUPPORTED | "same content, same id" is not spec wording. 14.4 suggests MD5 over "The current time", file location, file size and Info entries — not the content — and its NOTE says the calculation "need not be reproducible; all that matters is that the identifier is likely to be unique". Only "a permanent identifier based on the contents of the file" supports a content hash | S2/S3 |
| C467 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:146` | UNVERIFIABLE | ICC parts RIGHT: 6.5.3 Table 37 'curv', reserved, count, entries; quote verbatim. IEC 61966-2-1 §5.2 not public, clause number unverifiable (0.04045 / 2.4 values match public sRGB descriptions) | S2/S3 |
| C468 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:166` | UNVERIFIABLE | IEC 61966-2-1 §5.2 — text not public | S2/S3 |
| C474 | `src/Ave.Documents.Pdf/Xmp/XmpNamespace.cs:3` | WRONG-CLAUSE | XMP 6.2 "XMP names" and the quote are right (verbatim in XMP Part 1 2012 6.2; ISO 16684-1:2019 TOC keeps 6.2), but rule 5-3 is a PDF/UA-1 rule (pdfuaid prefix, ISO 14289-1 clause 5), not PDF/A; the PDF/A prefix rules are 6.6.4-4 to -7 | S4 |
| C515 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:73` | NOT-SUPPORTED | Table 117 CIDToGIDMap Identity = identity mapping: RIGHT; widths consistent with program: 9.7.4.3. But ISO 32000-1 nowhere says Adobe-Identity-0 means "CIDs have no meaning outside this font" (only Table 119 lists it for Identity-H/V, and Identity-H "may be used with CIDFonts using any Registry, Ordering, and Supplement values") | S2/S3 |
| C520 | `src/Ave.Documents.Render.Pdf/ImageEmbedding.cs:15` | MISQUOTED | not in 11.6.5.3. Nearest: Table 89 SMask "a soft-mask image … that shall be used as a source of mask shape or mask opacity values … The alpha source parameter in the graphics state determines whether the mask values shall be interpreted as shape or opacity." SMask is PDF 1.4: RIGHT; DCT default ColorTransform 1 for 3 components: RIGHT (7.4.8, Table 13) | S2/S3 |
| C530 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:55` | MISQUOTED | verbatim 8.9.4: "the unit square of user space, bounded by user coordinates (0, 0) and (1, 1), corresponds to the boundary of the image in image space"; Table 145: "mapped to the unit square in user space (as are all images)". Do is in 8.8 | S2/S3 |
| C534 | `src/Ave.Documents.Render.Pdf/PageModelRules.cs:47` | NOT-SUPPORTED | 7.7.3.2 (Table 29) defines page tree nodes; no clause of ISO 32000-1 says a PDF needs at least one page | S2/S3 |
| C536 | `src/Ave.Documents.Render.Pdf/PdfRenderOptions.cs:7` | NOT-SUPPORTED | §14.3.3 CreationDate/ModDate right; §14.4 File Identifiers: "an array of two byte strings", "should be computed by means of a message digest algorithm such as MD5" — no minimum length; "at least 16 bytes" is the library's rule | S9/S10 |
| C538 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:110` | WRONG-CLAUSE | StructTreeRoot is defined in 14.7.2 "Structure Hierarchy" (Table 28 says "see 14.7.2"), not 14.7.1 or 14.8.1; MarkInfo is 14.7.1 (Table 321). The rest is right: 7.7.2 Table 28, 14.9.2, 14.3.2, 6.6.2.1, 7.1 (rule 7.1-8), 14.11.5, 6.2.3. "GTS_PDFA1, the name every PDF/A part uses" is confirmed for parts 1–3 only (19005-3 6.2.3 NOTE 2) | S4 |
| C548 | `src/Ave.Documents.Render.Pdf/Tagging/ElementBounds.cs:10` | MISQUOTED | first quote: 32000-1 Table 344 BBox reads "(Optional for Annot; required for any figure or table appearing in its entirety on a single page; not inheritable)". Second quote verbatim. Figure/Formula/Form = Table 340 | S2/S3 |
| C552 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTree.cs:29` | MISQUOTED | no such sentence in 14.7.2 or anywhere. Nearest: 14.8.2.3.1 "the logical structure order defined by a depth-first traversal of the page's logical structure hierarchy" | S2/S3 |
| C564 | `src/Ave.Documents.Render.Pdf/ToUnicodeCMap.cs:15` | MISQUOTED | TN #5014 v1.0, ch. 7 "CMap Reference", beginbfchar: "where int can be ≤ 100" and "There can be a maximum of 100 lines in each ~bfchar set." 9.10.3 does name #5014 | S2/S3 |
| C579 | `src/Ave.Documents.Typesetting/LineJustification.cs:13` | MISQUOTED | real §7.1: "Word-separator characters are typographic character units whose primary purpose and general usage is to separate words. In Unicode this includes (but is not exhaustively defined as) the space (U+0020), the no-break space (U+00A0), the Ethiopic word space (U+1361), the Aegean word separators (U+10100,U+10101), the Ugaritic word divider (U+1039F), and the Phoenician word separator (U+1091F)." | S6/S7 |
| C593 | `src/Ave.Documents.Typesetting/Shaping/GlyphFilter.cs:34` | NOT-SUPPORTED | (1) quote absent: Unicode 18 §23.2.2 says "U+200C ZERO WIDTH NON-JOINER is intended to break both cursive connections and ligatures in rendering." (2) "Every other default-ignorable character is matched … This is HarfBuzz's rule" is wrong: HarfBuzz SKIPS every default ignorable in GSUB input except ZWNJ and "hidden" ones (CGJ U+034F, Mongolian FVS, TAG chars); e.g. U+00AD, U+200B, U+2060 do not stop a ligature. GPOS skips ZWJ, ZWNJ and hidden ones; GSUB never skips hidden ones | S8 |
| C596 | `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:20` | PARAPHRASE | "Advance widths are not affected." is verbatim only in mark-to-mark (type 6); mark-to-base (type 4) says "Placement of the base glyph and advances of both glyphs are not affected." HarfBuzz part right: default shaper HB_OT_SHAPE_ZERO_WIDTH_MARKS_BY_GDEF_LATE, zero_mark_widths_by_gdef after GPOS | S8 |
| C610 | `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs:14` | NOT-SUPPORTED | first quote verbatim §4.1.1 (the sentence ends "…is collapsed to have zero advance width"). "identically to spaces" is about CR only, in the §4 intro, not §4.1: "Carriage returns (U+000D) are treated identically to spaces (U+0020) in all respects." LF is a segment break (§4.1.3); CR is not | S6/S7 |
| C626 | `src/UnitTests.Ave.Documents.Fonts/FontRuleTests.cs:45` | PARAPHRASE | field list, not spec text; array field is kernPairs[nPairs]; format = coverage bits 8–15, formats 0 and 2 defined | S8 |
| C631 | `src/UnitTests.Ave.Documents.Fonts/OpenTypeTableTests.cs:87` | PARAPHRASE | behaviour right (classDef2 is an OffsetTo<ClassDef> with null allowed → Null ClassDef → get_class returns 0), but "every glyph class 0" is not HarfBuzz text | S8 |
| C632 | `src/UnitTests.Ave.Documents.Fonts/OpenTypeTableTests.cs:113` | MISQUOTED | quote 1 verbatim (source adds " :("); quote 2 actual: "try with 'latn'; some old fonts put their features there even though they're really trying to support Thai, for example :(" ("even though", not "even if"). Order requested → DFLT → dflt → latn right | S8 |
| C646 | `src/UnitTests.Ave.Documents.Images/PngRuleTests.cs:70` | MISQUOTED | as C295 | S9/S10 |
| C663 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:64` | NOT-SUPPORTED | §17.5.2.2 step 3 does say how: "If possible, widen all spanned columns by approximately the same amount." The engine's equal widening follows it; only true sense of "open": the whole algorithm is non-normative ("UAs are not required to implement this algorithm") | S5 |
| C702 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:145` | NOT-SUPPORTED | PDF/A-3 has no Info/XMP equivalence rule: 6.1.5 says an Info dictionary "may be present in a conforming file and a PDF/A-3 compliant reader shall ignore it"; veraPDF PDFA-3A/3B have no 6.6.3 rule. The equivalence rule is PDF/A-1 (ISO 19005-1 6.7.3). ISO 32000-1 14.3.3 defines Info; 14.3.2 only says which source is "authoritative" by date, it does not require equal values | S4 |
| C706 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:217` | MISQUOTED | as C564: "There can be a maximum of 100 lines in each ~bfchar set." | S2/S3 |
| C718 | `src/UnitTests.Ave.Documents.Typesetting/LineBreakTestFileTests.cs:10` | PARAPHRASE | header says only "÷ wherever there is a break opportunity, and × wherever there is not."; leading × / trailing ÷ come from the data (rules [2.0], [3.0]), and "does not distinguish mandatory" is an inference, not header text; quoted fragments are the comment's own | S9/S10 |

## Every citation

| Id | Place (at the check) | References | Verdict | Source |
| --- | --- | --- | --- | --- |
| C001 | `NOTICE.md:9` | ISO 32000-1:2008, §14.11.5, §14.3, §14.4, §14.6, §14.7, §14.8, §14.9, §7.11., §7.3, §7.5, §7.8, §8, §9, §9.10.3, §9.7, §9.8, §9.9 | RIGHT | PDF32000_2008.pdf, contents + §§ listed, Bibliography |
| C002 | `NOTICE.md:14` | 6.8-3, ISO 14289-1, ISO 19005-3, ISO 32000-2, Matterhorn, PDF/A-3, PDF/UA-1, §14.13, §7.11.3 | RIGHT | github.com/veraPDF/veraPDF-validation-profiles/tree/v1.30.4 ; pdfa.org Matterhorn-Protocol-1-1.pdf |
| C003 | `NOTICE.md:18` | ISO 16684-1, ISO 19005-3, XMP, §6.6.2.3 | RIGHT | PDF_A/PDFA-3A.xml (v1.30.4), clauses 6.6.2.3.x |
| C004 | `NOTICE.md:20` | ICC.1 | NO-CITATION | — |
| C006 | `NOTICE.md:27` | CSS 2.1, CSS Backgrounds 3, CSS Box Model 3, CSS Fonts 4, CSS Fragmentation 3, CSS Grid 1, CSS Paged Media 3, CSS Sizing 3, CSS Text 3, HTML, §10.3.2, §10.3.3, §10.6.2, §10.8, §11.7.1, §17, §2, §3.1, §3.2, §4, §4.1, §4.4, §5, §5.2, §5.4, §6, §7.2.4, §8.3, §9.4 | WRONG-CLAUSE | https://www.w3.org/TR/css-backgrounds-3/#corners (CRD 2024-03-11); https://www.w3.org/TR/CSS21/ |
| C007 | `NOTICE.md:32` | ISO 32000-1, ISO/IEC 10918-1, ISO/IEC 15948:2004, ITU-T T.81, PNG, §11.2, §11.3.2.1, §11.6.5.3., §5, §8.9, §9 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/ §5, §9, §11.2, §11.3.2.1; w3.org/Graphics/JPEG/itu-t81.pdf Annex B; PDF32000_2008.pdf §8.9, §11.6.5.3 |
| C011 | `NOTICE.md:48` | UAX #14, Unicode | RIGHT | https://www.unicode.org/reports/tr14/tr14-57.html header; https://www.unicode.org/license.txt |
| C013 | `README.md:8` | OpenType, UAX #14, Unicode | NO-CITATION | — |
| C017 | `README.md:54` | OpenType, PNG, TrueType, UAX #14, Unicode, XMP | NO-CITATION | — |
| C021 | `README.md:146` | CSS 2.1, CSS Fragmentation 3, Chromium, OpenType, PDF/A-3a, PDF/A-3b, PDF/UA-1, PNG, TrueType, UAX #14, Unicode, Unicode 18.0.0, XMP, §17, §5.2, §5.4, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins; https://www.w3.org/TR/css-break-3/#break-margins |
| C025 | `docs/architecture.md:29` | OpenType, UAX #14 | NO-CITATION | — |
| C029 | `docs/architecture.md:84` | CSS Cascade 4, HTML, §4, §7.1 | WRONG-CLAUSE | https://www.w3.org/TR/css-cascade-4/ §4, §6.2, §7.1, §7.2 (CRS 2022-01-13); https://html.spec.whatwg.org/multipage/rendering.html §15.3.6 |
| C030 | `docs/architecture.md:86` | CSS 2.1, CSS Fragmentation 3, §3, §4, §9.4.1 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#block-formatting; https://www.w3.org/TR/css-break-3/ |
| C031 | `docs/architecture.md:87` | CSS 2.1, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C032 | `docs/architecture.md:88` | CSS Fragmentation 3, §5.2, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/ (CR 2018-12-04; ED 2025-10-22 same) |
| C033 | `docs/architecture.md:89` | CSS 2.1, §10.8, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#inline-formatting; https://www.w3.org/TR/CSS21/visudet.html#line-height |
| C034 | `docs/architecture.md:90` | CSS 2.1, §11.7.1, §17, §7.2.4 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#width-layout; https://www.w3.org/TR/css-grid-1/#fr-unit |
| C035 | `docs/architecture.md:91` | CSS 2.1, §10.3.2 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#inline-replaced-width |
| C036 | `docs/architecture.md:92` | CSS Paged Media 3, §4.2.2, §5, §6.1 | RIGHT | https://www.w3.org/TR/css-page-3/ (WD 2023-09-14) |
| C037 | `docs/architecture.md:93` | ISO 32000-1, §14.8.2.2 | RIGHT | PDF32000_2008.pdf §14.8.2.2 (14.8.2.2.1–14.8.2.2.3) |
| C038 | `docs/architecture.md:94` | UAX #14, Unicode | RIGHT | https://www.unicode.org/reports/tr14/tr14-57.html header |
| C040 | `docs/architecture.md:96` | CSS Text 3, §6.1, §6.4 | RIGHT | https://www.w3.org/TR/css-text-3/ (CRD 2026-08-14) |
| C042 | `docs/architecture.md:98` | ISO 32000-1, §7.3, §7.5 | RIGHT | PDF32000_2008.pdf §7.3, §7.5 |
| C043 | `docs/architecture.md:99` | ISO 32000-1, §8.9, §9.10, §9.7 | RIGHT | PDF32000_2008.pdf §9.7–§9.10, §8.9 |
| C044 | `docs/architecture.md:100` | ISO 14289-1, ISO 19005-3 | NO-CITATION | — |
| C045 | `docs/architecture.md:101` | ISO 32000-1, §14.6, §14.8 | RIGHT | PDF32000_2008.pdf §14.6–§14.8 |
| C046 | `docs/architecture.md:102` | ISO 16684-1, ISO 19005-3, XMP, §6.6 | RIGHT | cdn.standards.iteh.ai/samples/57229/c82288be2ba4477380a07c31c3818eef/ISO-19005-3-2012.pdf, TOC |
| C047 | `docs/architecture.md:103` | ICC.1 | NO-CITATION | — |
| C048 | `docs/architecture.md:104` | ISO 19005-3, ISO 32000-1, §6.8, §7.11 | RIGHT | opensource.adobe.com PDF32000_2008.pdf 7.11; ISO-19005-3 sample TOC |
| C054 | `docs/architecture.md:181` | ISO 32000-1, Table 344, XMP | RIGHT | PDF32000_2008.pdf §14.8.5.4.3 |
| C060 | `docs/architecture.md:226` | CSS Fragmentation 3, Chromium, LayoutNG, §4.5 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.5; chromium/src third_party/blink/renderer/core/layout/break_appeal.h |
| C061 | `docs/code-front-end.md:53` | PDF/A-3a, PDF/UA-1 | NO-CITATION | — |
| C063 | `docs/code-front-end.md:85` | HTML | NO-CITATION | https://www.unicode.org/Public/18.0.0/ucd/LineBreak.txt |
| C064 | `docs/code-front-end.md:94` | PNG | NO-CITATION | — |
| C065 | `docs/code-front-end.md:146` | CSS 2.1, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C066 | `docs/code-front-end.md:176` | CSS 2.1, §17.6.2.1 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C078 | `samples/Ave.Documents.Samples/SampleDocuments.cs:21` | CSS 2.1, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C085 | `samples/Ave.Documents.Samples/Timesheet/Timesheet.cs:21` | CSS 2.1, §17.6.2.1 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C086 | `src/Ave.Documents.Core/Attachment.cs:5` | ISO 19005-3, ISO 32000-1, PDF/A-3, veraPDF rule 6.8-1, §6.8, §7.11.4 | RIGHT | PDF32000_2008.pdf 7.11.4; PDFA-3A.xml rule 6.8-1 |
| C087 | `src/Ave.Documents.Core/Attachment.cs:23` | ISO 19005-3, PDF/A-3, veraPDF rule 6.8-3, §6.8 | RIGHT | PDFA-3A.xml / PDFA-3B.xml rule 6.8-3 |
| C088 | `src/Ave.Documents.Core/AttachmentRelationship.cs:3` | ISO 19005-3, ISO 32000-2, PDF/A-3, Table 43, §14.13, §7.11.3 | RIGHT | ISO-19005-3 sample TOC; ISO 32000-2 7.11.3 / 14.13 (round-2 check) |
| C089 | `src/Ave.Documents.Core/CellSpan.cs:3` | ISO 32000-1, Table 349, §14.8.5.7 | RIGHT | PDF32000_2008.pdf §14.8.5.7, Table 349 |
| C090 | `src/Ave.Documents.Core/Color.cs:3` | ISO 19005-3, ISO 32000-1, §6.2.3, §8.6.4.3 | RIGHT | PDF32000_2008.pdf 8.6.4.3; ISO-19005-3 sample 6.2.3 |
| C091 | `src/Ave.Documents.Core/DocumentErrorCodes.cs:43` | CSS Fonts 4, §2.2 | RIGHT | https://www.w3.org/TR/css-fonts-4/ §2.2 (WD 2026-11-12) |
| C092 | `src/Ave.Documents.Core/DocumentErrorCodes.cs:61` | XML 1.0, XMP, §2.2 | NOT-SUPPORTED | https://www.w3.org/TR/xml/#charsets §2.2 |
| C093 | `src/Ave.Documents.Core/DocumentErrorCodes.cs:87` | ISO 32000-1, RFC 3066, §14.9.2.2 | RIGHT | PDF32000_2008.pdf §14.9.2.2 Language Identifiers |
| C094 | `src/Ave.Documents.Core/DocumentMetadata.cs:3` | ISO 14289-1, XMP, §7.1 | RIGHT | PDF_UA/PDFUA-1.xml rule 7.1-9 |
| C095 | `src/Ave.Documents.Core/DocumentMetadata.cs:12` | ISO 14289-1, ISO 32000-1, XMP, §14.9.2, §7.2 | RIGHT | PDF32000_2008.pdf 14.9.2; PDFUA-1.xml rule 7.2-34 |
| C096 | `src/Ave.Documents.Core/Edges.cs:3` | CSS Box Model 3, §3.2, §4.2 | RIGHT | https://www.w3.org/TR/css-box-3/ §3.2, §4.2 (REC 2024-04-11) |
| C098 | `src/Ave.Documents.Core/FontStyle.cs:3` | CSS Fonts 4, §2.4 | RIGHT | https://www.w3.org/TR/css-fonts-4/ §2.4 |
| C099 | `src/Ave.Documents.Core/FontWeight.cs:3` | CSS Fonts 4, OpenType, §2.2 | RIGHT | https://www.w3.org/TR/css-fonts-4/ §2.2 |
| C100 | `src/Ave.Documents.Core/Geometry.cs:3` | ISO 32000-1, §8.3.2.3 | RIGHT | PDF32000_2008.pdf §8.3.2.3 (and NOTE 1) |
| C101 | `src/Ave.Documents.Core/InvariantNumber.cs:12` | ISO 32000-1, §7.3.3 | RIGHT | PDF32000_2008.pdf §7.3.3 |
| C102 | `src/Ave.Documents.Core/Length.cs:3` | CSS Values 4, ISO 32000-1, §6.2, §8.3.2.3 | RIGHT | PDF32000_2008.pdf §8.3.2.3; https://www.w3.org/TR/css-values-4/ §6.2 (WD 2024-03-12) |
| C103 | `src/Ave.Documents.Core/Length.cs:35` | CSS Values 4, §6.2 | RIGHT | https://www.w3.org/TR/css-values-4/ §6.2 |
| C104 | `src/Ave.Documents.Core/SemanticRole.cs:3` | ISO 32000-1, §14.8.2.2, §14.8.4 | RIGHT | PDF32000_2008.pdf §14.8.4, §14.8.2.2 |
| C105 | `src/Ave.Documents.Core/SemanticRole.cs:21` | ISO 32000-1, §14.8.4 | RIGHT | PDF32000_2008.pdf §14.8.4 |
| C106 | `src/Ave.Documents.Core/SemanticRole.cs:29` | §14.8.4.2 | RIGHT | PDF32000_2008.pdf §14.8.4.2, Table 333 |
| C107 | `src/Ave.Documents.Core/SemanticRole.cs:32` | §14.8.4.2 | RIGHT | PDF32000_2008.pdf §14.8.4.2, Table 333 |
| C108 | `src/Ave.Documents.Core/SemanticRole.cs:35` | §14.8.4.3.1 | WRONG-CLAUSE | PDF32000_2008.pdf §14.8.4.3.2, Table 335 |
| C109 | `src/Ave.Documents.Core/SemanticRole.cs:38` | §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C110 | `src/Ave.Documents.Core/SemanticRole.cs:41` | §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C111 | `src/Ave.Documents.Core/SemanticRole.cs:44` | §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C112 | `src/Ave.Documents.Core/SemanticRole.cs:47` | §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C113 | `src/Ave.Documents.Core/SemanticRole.cs:50` | §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C114 | `src/Ave.Documents.Core/SemanticRole.cs:53` | §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C115 | `src/Ave.Documents.Core/SemanticRole.cs:56` | ISO 32000-1, §14.8.2.2.2 | RIGHT | PDF32000_2008.pdf §14.8.2.2.2 |
| C116 | `src/Ave.Documents.Core/SemanticRole.cs:62` | §14.8.4.5, §14.9.3 | RIGHT | PDF32000_2008.pdf §14.8.4.5, §14.9.3; github.com/veraPDF/veraPDF-validation-profiles PDF_UA/PDFUA-1.xml rule 7.3-1 |
| C117 | `src/Ave.Documents.Core/SemanticRole.cs:68` | §14.8.4.3.2 | RIGHT | PDF32000_2008.pdf §14.8.4.3.2, Table 335 |
| C118 | `src/Ave.Documents.Core/TableSlots.cs:7` | CSS 2.1, HTML, §17.2 | WRONG-CLAUSE | https://html.spec.whatwg.org/multipage/tables.html#forming-a-table; https://www.w3.org/TR/CSS21/tables.html (§17.5) |
| C120 | `src/Ave.Documents.Core/TableSlots.cs:36` | HTML, §4.9.11 | RIGHT | https://html.spec.whatwg.org/multipage/tables.html#attributes-common-to-td-and-th-elements |
| C121 | `src/Ave.Documents.Core/TableSlots.cs:43` | HTML | RIGHT | same URL, §4.9.11 |
| C123 | `src/Ave.Documents.Core/TextAlign.cs:3` | CSS Text 3, §6.1 | RIGHT | https://www.w3.org/TR/css-text-3/ §6.1 |
| C124 | `src/Ave.Documents.Core/TextAlign.cs:19` | CSS Text 3, §6.4 | RIGHT | https://www.w3.org/TR/css-text-3/ §6.3, §6.4 |
| C125 | `src/Ave.Documents.Core/XmlCharacters.cs:3` | XML 1.0, XMP, §2.2 | RIGHT | #xA / #xD / [#x20-#xD7FF] / [#xE000-#xFFFD] / [#x10000-#x10FFFF] / XML 1.0 5th ed §2.2 |
| C126 | `src/Ave.Documents.DocumentModel/Border.cs:5` | CSS Backgrounds 3, §3 | RIGHT | https://www.w3.org/TR/css-backgrounds-3/ §3, §3.2 (CRD 2024-03-11) |
| C127 | `src/Ave.Documents.DocumentModel/Border.cs:25` | CSS Box Model 3, §2 | RIGHT | https://www.w3.org/TR/css-box-3/ §2 |
| C128 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:14` | CSS Box Model 3, §3 | RIGHT | https://www.w3.org/TR/css-box-3/ §3 |
| C129 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:17` | CSS Box Model 3, §4 | RIGHT | https://www.w3.org/TR/css-box-3/ §4 |
| C130 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:20` | CSS Backgrounds 3, §3 | RIGHT | https://www.w3.org/TR/css-backgrounds-3/ §3 |
| C131 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:23` | CSS Backgrounds 3, §2.2 | RIGHT | https://www.w3.org/TR/css-backgrounds-3/ §2.2, §2.7 |
| C132 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:27` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1 |
| C133 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:30` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1 |
| C134 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:34` | CSS Fragmentation 3, §3.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.2 |
| C135 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:37` | CSS Text 3, §6.1 | RIGHT | https://www.w3.org/TR/css-text-3/ §6.1 |
| C136 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:41` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C137 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:49` | CSS Display 3, §2.4 | RIGHT | https://www.w3.org/TR/css-display-3/ §2.4 (CRD 2026-06-05) |
| C138 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:53` | CSS 2.1, HTML, §17.2.1 | WRONG-CLAUSE | https://www.w3.org/TR/CSS21/tables.html (§17.5); https://html.spec.whatwg.org/multipage/tables.html#forming-a-table |
| C139 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:58` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1 |
| C140 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:65` | §4.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.4 |
| C141 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:72` | CSS Fragmentation 3, §3.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.2 |
| C142 | `src/Ave.Documents.DocumentModel/BoxStyle.cs:82` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C143 | `src/Ave.Documents.DocumentModel/LayoutAlgorithmId.cs:3` | CSS Display 3, §2.2 | RIGHT | https://www.w3.org/TR/css-display-3/ §2.2 |
| C144 | `src/Ave.Documents.DocumentModel/LayoutAlgorithmId.cs:11` | CSS 2.1, §9.4.1 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#block-formatting |
| C145 | `src/Ave.Documents.DocumentModel/LayoutAlgorithmId.cs:14` | CSS 2.1, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#inline-formatting |
| C146 | `src/Ave.Documents.DocumentModel/LayoutAlgorithmId.cs:17` | CSS 2.1, §17 | RIGHT | https://www.w3.org/TR/CSS21/tables.html |
| C147 | `src/Ave.Documents.DocumentModel/LayoutAlgorithmId.cs:23` | CSS 2.1, §10.3.2 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#inline-replaced-width |
| C148 | `src/Ave.Documents.DocumentModel/Node.cs:14` | CSS 2.1, CSS Display 3, §2.1, §9.2.1 | RIGHT | https://www.w3.org/TR/css-display-3/#outer-role (CRD 2026-06-05); https://www.w3.org/TR/CSS21/visuren.html#block-boxes |
| C149 | `src/Ave.Documents.DocumentModel/Node.cs:49` | CSS Text 3, §4.1 | RIGHT | https://www.w3.org/TR/css-text-3/ §4.1 |
| C150 | `src/Ave.Documents.DocumentModel/Node.cs:56` | CSS Paged Media 3, §6.1 | RIGHT | https://www.w3.org/TR/css-page-3/ §6.1 |
| C151 | `src/Ave.Documents.DocumentModel/Node.cs:67` | CSS Paged Media 3, §6.1 | RIGHT | https://www.w3.org/TR/css-page-3/ §6.1 |
| C152 | `src/Ave.Documents.DocumentModel/Node.cs:77` | CSS 2.1, §10.3.2 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#inline-replaced-width |
| C153 | `src/Ave.Documents.DocumentModel/PageStyle.cs:5` | CSS Paged Media 3, §3 | RIGHT | https://www.w3.org/TR/css-page-3/ §3, §7.1 |
| C154 | `src/Ave.Documents.DocumentModel/PageStyle.cs:18` | CSS Paged Media 3, §4.2.2 | RIGHT | https://www.w3.org/TR/css-page-3/ §4.2.2 |
| C155 | `src/Ave.Documents.DocumentModel/PageStyle.cs:34` | CSS Paged Media 3, §5 | RIGHT | https://www.w3.org/TR/css-page-3/ §5 |
| C156 | `src/Ave.Documents.DocumentModel/StyledDocument.cs:5` | CSS Cascade 4, §4.4 | RIGHT | https://www.w3.org/TR/css-cascade-4/ §4.4 |
| C157 | `src/Ave.Documents.DocumentModel/TableDisplay.cs:3` | CSS Display 3, §2.4 | RIGHT | https://www.w3.org/TR/css-display-3/ §2.4 |
| C158 | `src/Ave.Documents.DocumentModel/TableDisplay.cs:13` | CSS 2.1, §17.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#table-display |
| C159 | `src/Ave.Documents.DocumentModel/TextStyle.cs:5` | CSS Color 4, CSS Fonts 4, §2.1, §2.2, §2.4, §2.5, §3.1 | WRONG-CLAUSE | https://www.w3.org/TR/css-color-4/ §3.2 (CRD 2026-11-12) |
| C160 | `src/Ave.Documents.DocumentModel/TrackSize.cs:5` | CSS 2.1, CSS Grid 1, §17.5.2.1, §17.5.2.2, §7.2.4 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#width-layout; https://www.w3.org/TR/css-grid-1/#fr-unit |
| C162 | `src/Ave.Documents.Fluent/Cascade.cs:6` | CSS Cascade 4, §6 | NOT-SUPPORTED | https://www.w3.org/TR/css-cascade-4/ §6.1, §7.2 |
| C164 | `src/Ave.Documents.Fluent/DocumentTranslator.cs:13` | CSS Cascade 4, §4 | RIGHT | https://www.w3.org/TR/css-cascade-4/ §4 |
| C165 | `src/Ave.Documents.Fluent/DocumentTranslator.cs:75` | CSS Cascade 4, §7.2 | RIGHT | https://www.w3.org/TR/css-cascade-4/ §7.2 |
| C166 | `src/Ave.Documents.Fluent/DocumentTranslator.cs:86` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1 |
| C168 | `src/Ave.Documents.Fluent/Elements.cs:141` | CSS Text 3, HTML, UAX #14, §6.3 | RIGHT | https://www.unicode.org/reports/tr14/ (rev 57) BK; https://www.w3.org/TR/css-text-3/ §6.3 |
| C171 | `src/Ave.Documents.Fluent/ImageElement.cs:30` | ISO 32000-1, §14.9.3 | RIGHT | PDF32000_2008.pdf §14.9.3 |
| C172 | `src/Ave.Documents.Fluent/InputChecks.cs:18` | CSS Paged Media 3, §3 | RIGHT | https://www.w3.org/TR/css-page-3/ §3 |
| C173 | `src/Ave.Documents.Fluent/InputChecks.cs:41` | CSS Box Model 3, §4.2 | WRONG-CLAUSE | https://www.w3.org/TR/css-box-3/ §4.1 |
| C174 | `src/Ave.Documents.Fluent/InputChecks.cs:50` | CSS Fonts 4, §2.2, §2.5 | RIGHT | https://www.w3.org/TR/css-fonts-4/ §2.5, §2.2 |
| C175 | `src/Ave.Documents.Fluent/InputChecks.cs:72` | ISO 32000-1, §14.8.4.3.2 | RIGHT | PDF32000_2008.pdf §14.8.4.3.2, Table 335 |
| C176 | `src/Ave.Documents.Fluent/InputChecks.cs:139` | CSS Backgrounds 3, §3.3 | RIGHT | https://www.w3.org/TR/css-backgrounds-3/ §3.3 |
| C177 | `src/Ave.Documents.Fluent/InputChecks.cs:143` | CSS Grid 1, §7.2.4 | MISQUOTED | https://www.w3.org/TR/css-grid-1/ §7.2.1 (CRD 2025-03-26) |
| C178 | `src/Ave.Documents.Fluent/InputChecks.cs:189` | CSS 2.1, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C179 | `src/Ave.Documents.Fluent/InputChecks.cs:252` | ISO 32000-1, RFC 3066, §14.9.2.2, §2.1 | PARAPHRASE | PDF32000_2008.pdf §14.9.2.2; rfc-editor.org/rfc/rfc3066 §2.1 |
| C180 | `src/Ave.Documents.Fluent/InputChecks.cs:263` | XML 1.0, XMP, §2.2 | RIGHT | XML 1.0 5th ed §2.2 |
| C181 | `src/Ave.Documents.Fluent/InputChecks.cs:271` | XML 1.0 | NO-CITATION | — |
| C182 | `src/Ave.Documents.Fluent/InputChecks.cs:278` | ISO 32000-1, RFC 6838, §4.2, §7.9.6 | RIGHT | PDF32000_2008.pdf §7.9.6; rfc-editor.org/rfc/rfc6838 §4.2 |
| C184 | `src/Ave.Documents.Fluent/InputChecks.cs:322` | RFC 3066, §2.1 | RIGHT | https://www.rfc-editor.org/rfc/rfc3066 §2.1 |
| C185 | `src/Ave.Documents.Fluent/InputChecks.cs:326` | RFC 6838, §4.2 | RIGHT | https://www.rfc-editor.org/rfc/rfc6838 §4.2 |
| C186 | `src/Ave.Documents.Fluent/PageSetup.cs:5` | CSS Paged Media 3, §3 | RIGHT | https://www.w3.org/TR/css-page-3/ §3 |
| C187 | `src/Ave.Documents.Fluent/TableTranslator.cs:12` | CSS 2.1, §17.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#table-display |
| C188 | `src/Ave.Documents.Fluent/TableTranslator.cs:20` | CSS 2.1, §17.2.1 | NOT-SUPPORTED | https://www.w3.org/TR/CSS21/tables.html#anonymous-boxes |
| C189 | `src/Ave.Documents.Fluent/TableTranslator.cs:38` | CSS 2.1, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C190 | `src/Ave.Documents.Fluent/TableTranslator.cs:161` | CSS 2.1, §17.5 | RIGHT | https://www.w3.org/TR/CSS21/tables.html (§17.5) |
| C191 | `src/Ave.Documents.Fluent/TextFormat.cs:5` | CSS Cascade 4, §7.2 | RIGHT | https://www.w3.org/TR/css-cascade-4/ §7.2 |
| C192 | `src/Ave.Documents.Fluent/UserAgentStyles.cs:5` | CSS Cascade 4, §6.2 | RIGHT | https://www.w3.org/TR/css-cascade-4/ §6.2 |
| C193 | `src/Ave.Documents.Fluent/UserAgentStyles.cs:11` | CSS Fonts 4, §2.5 | NOT-SUPPORTED | https://www.w3.org/TR/css-fonts-4/ §2.5, §2.5.1 |
| C194 | `src/Ave.Documents.Fluent/UserAgentStyles.cs:14` | CSS 2.1, HTML, §8.3.1 | RIGHT | https://html.spec.whatwg.org/multipage/rendering.html#sections-and-headings |
| C202 | `src/Ave.Documents.Fonts/FontSet.cs:26` | CSS Fonts 4, §5.1, §5.2 | RIGHT | https://www.w3.org/TR/css-fonts-4/ §5.1, §5.2 |
| C203 | `src/Ave.Documents.Fonts/FontSubset.cs:6` | ISO 32000-1, TrueType, §9.6.4 | RIGHT | PDF32000_2008.pdf §9.6.4 |
| C204 | `src/Ave.Documents.Fonts/FontSubset.cs:20` | ISO 32000-1, §9.6.4 | RIGHT | PDF32000_2008.pdf §9.6.4 |
| C205 | `src/Ave.Documents.Fonts/FontSubsetter.cs:13` | ISO 32000-1, TrueType, §9.7.4.2, §9.9 | PARAPHRASE | PDF32000_2008.pdf §9.9 (and §9.6.4) |
| C206 | `src/Ave.Documents.Fonts/FontSubsetter.cs:29` | OpenType | RIGHT | https://learn.microsoft.com/en-us/typography/opentype/spec/glyf#composite-glyph-description |
| C207 | `src/Ave.Documents.Fonts/FontSubsetter.cs:77` | OpenType | RIGHT | glyf#composite-glyph-description |
| C208 | `src/Ave.Documents.Fonts/FontSubsetter.cs:94` | OpenType | RIGHT | glyf#glyph-headers, #composite-glyph-description |
| C209 | `src/Ave.Documents.Fonts/FontSubsetter.cs:159` | OpenType | NOT-SUPPORTED | loca (intro); https://learn.microsoft.com/en-us/typography/opentype/spec/recom 'loca' Table |
| C210 | `src/Ave.Documents.Fonts/FontSubsetter.cs:193` | OpenType | RIGHT | otff#organization-of-an-opentype-font, #table-directory, #calculating-checksums |
| C211 | `src/Ave.Documents.Fonts/FontSubsetter.cs:238` | OpenType | MISQUOTED | otff#calculating-checksums |
| C212 | `src/Ave.Documents.Fonts/FontSubsetter.cs:261` | ISO 32000-1, §9.6.4 | RIGHT | PDF32000_2008.pdf §9.6.4 |
| C213 | `src/Ave.Documents.Fonts/GlyphId.cs:3` | OpenType | RIGHT | cmap (intro, Format 2); loca |
| C214 | `src/Ave.Documents.Fonts/OpenType/ClassDefinition.cs:8` | OpenType | MISQUOTED | https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2#class-definition-table |
| C215 | `src/Ave.Documents.Fonts/OpenType/Coverage.cs:7` | OpenType | RIGHT | chapter2#coverage-table |
| C216 | `src/Ave.Documents.Fonts/OpenType/FeatureTable.cs:5` | OpenType | RIGHT | chapter2#feature-table, #featurelist-table |
| C217 | `src/Ave.Documents.Fonts/OpenType/GlyphDefinitions.cs:7` | OpenType | RIGHT | gdef#glyph-class-definition-table, chapter2 LookupFlag bit enumeration |
| C218 | `src/Ave.Documents.Fonts/OpenType/LanguageSystem.cs:5` | OpenType | MISQUOTED | chapter2#language-system-table |
| C219 | `src/Ave.Documents.Fonts/OpenType/LayoutTable.cs:8` | OpenType | RIGHT | chapter2#table-organization |
| C220 | `src/Ave.Documents.Fonts/OpenType/LayoutTable.cs:24` | HarfBuzz, OpenType, hb-ot-layout.cc | RIGHT | gsub#gsub-table-organization; chapter2#scriptlist-table; hb-ot-layout.cc hb_ot_layout_table_select_script |
| C221 | `src/Ave.Documents.Fonts/OpenType/LayoutTableParser.cs:12` | OpenType | RIGHT | gsub#gsub-header, gpos#gpos-header |
| C222 | `src/Ave.Documents.Fonts/OpenType/LegacyKerning.cs:10` | OpenType | RIGHT | https://learn.microsoft.com/en-us/typography/opentype/spec/kern |
| C223 | `src/Ave.Documents.Fonts/OpenType/LegacyKerning.cs:46` | OpenType | PARAPHRASE | kern (header, Format 0) |
| C224 | `src/Ave.Documents.Fonts/OpenType/Lookup.cs:5` | OpenType | RIGHT | chapter2#lookup-table |
| C225 | `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:3` | OpenType | RIGHT | chapter2#lookup-table (LookupFlag bit enumeration) |
| C226 | `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:110` | OpenType | RIGHT | gpos#value-record |
| C227 | `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:146` | OpenType | RIGHT | gpos#anchor-table |
| C229 | `src/Ave.Documents.Fonts/OpenType/ScriptTable.cs:4` | OpenType | RIGHT | chapter2#script-table |
| C230 | `src/Ave.Documents.Fonts/OpenType/TableReader.cs:10` | HarfBuzz, OpenType | RIGHT | chapter2 |
| C231 | `src/Ave.Documents.Fonts/Tables/BigEndian.cs:7` | OpenType | MISQUOTED | otff#data-types |
| C232 | `src/Ave.Documents.Fonts/Tables/CmapFormat12.cs:9` | OpenType, Unicode | RIGHT | cmap#format-12-segmented-coverage |
| C233 | `src/Ave.Documents.Fonts/Tables/CmapFormat12.cs:44` | Unicode | RIGHT | https://learn.microsoft.com/en-us/typography/opentype/spec/cmap (Format 12) |
| C234 | `src/Ave.Documents.Fonts/Tables/CmapFormat4.cs:9` | OpenType | RIGHT | cmap#format-4-segment-mapping-to-delta-values |
| C235 | `src/Ave.Documents.Fonts/Tables/CmapTable.cs:8` | OpenType, Unicode | MISQUOTED | cmap (intro, Table overview) |
| C236 | `src/Ave.Documents.Fonts/Tables/CmapTable.cs:20` | OpenType, Unicode, Unicode 2.0 | RIGHT | cmap#encoding-records-and-encodings, #platform-ids |
| C240 | `src/Ave.Documents.Fonts/Tables/HeadTable.cs:7` | OpenType | RIGHT | head |
| C241 | `src/Ave.Documents.Fonts/Tables/HheaTable.cs:7` | OpenType | RIGHT | hhea |
| C242 | `src/Ave.Documents.Fonts/Tables/HmtxTable.cs:8` | OpenType | RIGHT | hmtx |
| C243 | `src/Ave.Documents.Fonts/Tables/LocaTable.cs:8` | OpenType | RIGHT | loca |
| C244 | `src/Ave.Documents.Fonts/Tables/LocaTable.cs:19` | OpenType | RIGHT | glyf#glyph-headers |
| C245 | `src/Ave.Documents.Fonts/Tables/MaxpTable.cs:7` | OpenType | RIGHT | maxp |
| C246 | `src/Ave.Documents.Fonts/Tables/NameTable.cs:10` | OpenType | RIGHT | name |
| C249 | `src/Ave.Documents.Fonts/Tables/NameTable.cs:76` | ISO 32000-1, §9.6.2.1 | WRONG-CLAUSE | PDF32000_2008.pdf §9.6.3; §9.7.4.1, Table 117 |
| C252 | `src/Ave.Documents.Fonts/Tables/Os2Table.cs:7` | OpenType | RIGHT | os2#fsselection, #usweightclass, #scapheight |
| C254 | `src/Ave.Documents.Fonts/Tables/PostTable.cs:7` | OpenType | RIGHT | post#header |
| C255 | `src/Ave.Documents.Fonts/Tables/TableDirectory.cs:9` | OpenType, TrueType | RIGHT | otff#table-directory |
| C256 | `src/Ave.Documents.Fonts/Tables/TableDirectory.cs:19` | OpenType, TrueType | WRONG-CLAUSE | otff#table-directory, #ttc-header |
| C261 | `src/Ave.Documents.Fonts/TrueTypeFont.cs:97` | OpenType | RIGHT | glyf#glyph-headers |
| C262 | `src/Ave.Documents.Images/ImageColorSpace.cs:3` | ISO 32000-1, §8.6.4 | RIGHT | PDF32000_2008.pdf §8.6.4 |
| C264 | `src/Ave.Documents.Images/ImageDecoder.cs:12` | ITU-T T.81, PNG, §5.2, §B.1.1.3 | RIGHT | https://www.w3.org/TR/png-3/#5PNG-file-signature; https://www.w3.org/Graphics/JPEG/itu-t81.pdf B.1.1.3 |
| C268 | `src/Ave.Documents.Images/ImageLimits.cs:3` | PNG, §11.2.2 | RIGHT | https://www.w3.org/TR/2003/REC-PNG-20031110/ §11.2.2, §7.1 |
| C269 | `src/Ave.Documents.Images/Jpeg/JpegReader.cs:8` | ISO 32000-1, ISO/IEC 10918-1, ITU-T T.81, T.81, §7.4.8, §B.1.1.2 | RIGHT | PDF32000_2008.pdf §7.4.8; w3.org/Graphics/JPEG/itu-t81.pdf §B.1.1.2 |
| C270 | `src/Ave.Documents.Images/Jpeg/JpegReader.cs:37` | §B.1.1.2 | RIGHT | w3.org/Graphics/JPEG/itu-t81.pdf §B.1.1.2 |
| C271 | `src/Ave.Documents.Images/Jpeg/JpegReader.cs:77` | T.81, §B.2.1 | MISQUOTED | itu-t81.pdf B.2.1 |
| C272 | `src/Ave.Documents.Images/Jpeg/JpegReader.cs:116` | §B.2.2 | RIGHT | itu-t81.pdf B.2.2 |
| C273 | `src/Ave.Documents.Images/JpegImage.cs:5` | ISO 32000-1, §7.4.8 | RIGHT | PDF32000_2008.pdf §7.4.8 |
| C274 | `src/Ave.Documents.Images/Png/Crc32.cs:5` | ISO 3309, ITU-T V.42, PNG, §5.5 | RIGHT | https://www.w3.org/TR/png-3/ §5.5 |
| C275 | `src/Ave.Documents.Images/Png/PngChunk.cs:10` | PNG, §5.3 | RIGHT | PNG 2nd/3rd ed §5.3 |
| C276 | `src/Ave.Documents.Images/Png/PngChunk.cs:16` | §5.4 | RIGHT | PNG 2nd ed §5.4 |
| C277 | `src/Ave.Documents.Images/Png/PngChunk.cs:22` | §11.2.5, §5.3 | RIGHT | PNG 2nd ed §5.3, §11.2.5 |
| C278 | `src/Ave.Documents.Images/Png/PngChunk.cs:70` | §5.3 | MISQUOTED | PNG 2nd ed §5.3 Table 5.1 |
| C279 | `src/Ave.Documents.Images/Png/PngDecoder.cs:11` | ISO 32000-1, ISO/IEC 15948:2004, PNG, §10, §11.6.5.3, §8.9.5, §9 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/; PDF32000_2008.pdf §8.9.5, §11.6.5.3 |
| C280 | `src/Ave.Documents.Images/Png/PngDecoder.cs:25` | PNG, §5.2 | RIGHT | PNG 2nd ed §5.2 |
| C282 | `src/Ave.Documents.Images/Png/PngDecoder.cs:52` | §5.6 | MISQUOTED | PNG 2nd ed §5.6, §11.2.4 |
| C283 | `src/Ave.Documents.Images/Png/PngDecoder.cs:75` | §10, §11.2.4 | RIGHT | PNG 2nd ed §11.2.4 |
| C284 | `src/Ave.Documents.Images/Png/PngHeader.cs:7` | PNG, Table 11, §11.2.2 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/ §11.2.2, Table 11.1 |
| C285 | `src/Ave.Documents.Images/Png/PngHeader.cs:26` | PNG, §11.2.2 | RIGHT | PNG 2nd ed §11.2.2 |
| C286 | `src/Ave.Documents.Images/Png/PngHeader.cs:36` | Table 11 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/ §11.2.2, Table 11.1 |
| C287 | `src/Ave.Documents.Images/Png/PngHeader.cs:48` | §7.2 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/ §7.2 |
| C288 | `src/Ave.Documents.Images/Png/PngHeader.cs:51` | §10, §8.2, §9 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/ §8.2, §9, §10, §11.2.2 |
| C290 | `src/Ave.Documents.Images/Png/PngHeader.cs:102` | Table 11 | RIGHT | w3.org/TR/2003/REC-PNG-20031110/ §11.2.2, Table 11.1 |
| C292 | `src/Ave.Documents.Images/Png/PngPalette.cs:9` | PNG, §11.2.3, §11.3.2.1 | RIGHT | PNG 2nd ed §11.3.2.1 |
| C293 | `src/Ave.Documents.Images/Png/PngSamples.cs:8` | PNG, §7.2 | MISQUOTED | PNG 2nd ed §7.2 |
| C294 | `src/Ave.Documents.Images/Png/PngSamples.cs:15` | §11.3.2.1 | RIGHT | PNG 2nd ed §11.3.2.1 |
| C295 | `src/Ave.Documents.Images/Png/PngSamples.cs:67` | §11.2.3 | MISQUOTED | PNG 2nd ed §11.2.3; https://www.w3.org/TR/png-3/ §11.2.2, §13.1 |
| C296 | `src/Ave.Documents.Images/Png/ScanlineFilters.cs:5` | PNG, §9, §9.2 | RIGHT | PNG 2nd ed §9.2 |
| C297 | `src/Ave.Documents.Images/Png/ScanlineFilters.cs:13` | §9.2 | PARAPHRASE | PNG 2nd ed §9.2 |
| C298 | `src/Ave.Documents.Images/Png/ScanlineFilters.cs:55` | §9.4 | RIGHT | PNG 2nd ed §9.4 |
| C299 | `src/Ave.Documents.Images/RasterImage.cs:5` | ISO 32000-1, §8.9.3 | RIGHT | PDF32000_2008.pdf §8.9.3, §8.9.4 |
| C300 | `src/Ave.Documents.Layout/BreakAppeal.cs:3` | CSS Fragmentation 3, Chromium, §4.4, §4.5 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.4, §4.5 |
| C303 | `src/Ave.Documents.Layout/BreakAvoidance.cs:6` | CSS Fragmentation 3, §3.1, §3.2, §4.1, §4.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.4 |
| C304 | `src/Ave.Documents.Layout/BreakAvoidance.cs:20` | §3.1.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1.1 |
| C305 | `src/Ave.Documents.Layout/BreakToken.cs:40` | CSS 2.1, §17.5.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#width-layout |
| C306 | `src/Ave.Documents.Layout/ConstraintSpace.cs:3` | CSS 2.1, Chromium, LayoutNG, §10.3.3, §8.3.1, §9.4.1 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#blockwidth; https://github.com/chromium/chromium/blob/17cc58cf8788382401b18b8e6570db558ee7f2c1/third_party/blink/render |
| C307 | `src/Ave.Documents.Layout/FlowLayout.cs:12` | CSS 2.1, §8.3.1, §9.4.1 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#block-formatting |
| C308 | `src/Ave.Documents.Layout/FlowLayout.cs:20` | CSS Fragmentation 3, Chromium, §3.1, §3.2, §4.1, §4.5, §9.4.1 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#block-formatting |
| C309 | `src/Ave.Documents.Layout/FlowLayout.cs:71` | CSS Sizing 3, §5.1, §5.2 | RIGHT | https://www.w3.org/TR/css-sizing-3/ (WD 2026-09-04) |
| C310 | `src/Ave.Documents.Layout/FlowLayout.cs:99` | CSS Fragmentation 3, §4.1, §5.2, §8.3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.1, §5.2 |
| C311 | `src/Ave.Documents.Layout/FlowLayout.cs:153` | §5.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.2 |
| C312 | `src/Ave.Documents.Layout/FlowLayout.cs:180` | CSS Fragmentation 3, §5.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.2 |
| C313 | `src/Ave.Documents.Layout/FlowLayout.cs:198` | §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C314 | `src/Ave.Documents.Layout/FlowLayout.cs:213` | §5.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.2 |
| C315 | `src/Ave.Documents.Layout/FlowLayout.cs:254` | §5.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.2 |
| C316 | `src/Ave.Documents.Layout/FlowLayout.cs:270` | CSS Fragmentation 3, §5.2, §8.3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.2 |
| C317 | `src/Ave.Documents.Layout/ForcedBreaks.cs:6` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1 |
| C318 | `src/Ave.Documents.Layout/ForcedBreaks.cs:9` | CSS Fragmentation 3, §3.1.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1.1 |
| C319 | `src/Ave.Documents.Layout/Fragment.cs:8` | CSS Fragmentation 3, §2 | MISQUOTED | https://www.w3.org/TR/css-break-3/ §2 (box fragment) |
| C320 | `src/Ave.Documents.Layout/Fragment.cs:27` | CSS 2.1, CSS Fragmentation 3, §17.2, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/#break-decoration; https://www.w3.org/TR/CSS21/tables.html#table-display |
| C321 | `src/Ave.Documents.Layout/Fragment.cs:45` | CSS 2.1, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C322 | `src/Ave.Documents.Layout/Fragment.cs:53` | CSS 2.1, §10.8, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#inline-formatting |
| C323 | `src/Ave.Documents.Layout/ImageLayout.cs:9` | CSS 2.1, CSS Fragmentation 3, §10.3.2, §4.1 | RIGHT | https://www.w3.org/TR/css-break-3/#possible-breaks |
| C324 | `src/Ave.Documents.Layout/ImageLayout.cs:17` | CSS Values 4, JFIF, PNG, §6.2 | RIGHT | https://www.w3.org/TR/css-values-4/ §6.2 |
| C325 | `src/Ave.Documents.Layout/ImageLayout.cs:36` | CSS Sizing 3, §5.1 | RIGHT | https://www.w3.org/TR/css-sizing-3/ §5.1 |
| C326 | `src/Ave.Documents.Layout/ImageLayout.cs:48` | CSS 2.1, §10.3.2, §10.6.2 | MISQUOTED | https://www.w3.org/TR/CSS21/visudet.html#inline-replaced-height |
| C327 | `src/Ave.Documents.Layout/InFlowChild.cs:21` | CSS 2.1, §10.3.3, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#blockwidth |
| C328 | `src/Ave.Documents.Layout/InlineLayout.cs:14` | CSS 2.1, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#inline-formatting |
| C329 | `src/Ave.Documents.Layout/InlineLayout.cs:21` | CSS Fragmentation 3, CSS Text 3, UAX #14, §4.1, §4.1.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.3, §4.1 |
| C330 | `src/Ave.Documents.Layout/InlineLayout.cs:52` | CSS 2.1, CSS Sizing 3, §5.1, §5.2 | RIGHT | https://www.w3.org/TR/css-sizing-3/#intrinsic-contribution (WD 2026-09-04) |
| C331 | `src/Ave.Documents.Layout/InlineLayout.cs:69` | CSS 2.1, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/visuren.html#inline-formatting |
| C334 | `src/Ave.Documents.Layout/LayoutAlgorithm.cs:44` | CSS Sizing 3, §5 | RIGHT | https://www.w3.org/TR/css-sizing-3/ §5 |
| C335 | `src/Ave.Documents.Layout/LineBoxes.cs:11` | CSS 2.1, §10.8 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#line-height |
| C336 | `src/Ave.Documents.Layout/LineBoxes.cs:17` | CSS 2.1, §10.8.1 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#leading |
| C337 | `src/Ave.Documents.Layout/LineBoxes.cs:33` | CSS 2.1, §10.8, §10.8.1 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#line-height |
| C338 | `src/Ave.Documents.Layout/MarginCollapsing.cs:7` | CSS 2.1, §8.3.1 | MISQUOTED | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C339 | `src/Ave.Documents.Layout/MarginCollapsing.cs:40` | §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C340 | `src/Ave.Documents.Layout/MarginCollapsing.cs:64` | §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C341 | `src/Ave.Documents.Layout/MarginCollapsing.cs:73` | §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C342 | `src/Ave.Documents.Layout/MarginCollapsing.cs:79` | CSS 2.1, §8.3.1, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C343 | `src/Ave.Documents.Layout/PageAreas.cs:8` | CSS Paged Media 3, §5 | NOT-SUPPORTED | https://www.w3.org/TR/css-page-3/ §5, §5.3 |
| C344 | `src/Ave.Documents.Layout/PageAreas.cs:36` | CSS 2.1, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C345 | `src/Ave.Documents.Layout/PageContext.cs:17` | CSS Counter Styles 3, §6.1 | RIGHT | https://www.w3.org/TR/css-counter-styles-3/ §6.1 (CRS 2021-07-27) |
| C346 | `src/Ave.Documents.Layout/PagePainter.cs:23` | ISO 32000-1, §14.8.2.2.2 | RIGHT | PDF32000_2008.pdf §14.8.2.2.2 |
| C347 | `src/Ave.Documents.Layout/PagePainter.cs:36` | CSS 2.1, ISO 32000-1, §14.8.2.2, §17.5.1, §17.6.2 | MISQUOTED | https://www.w3.org/TR/CSS21/zindex.html#painting-order; PDF32000_2008.pdf §14.8.2.2.1 |
| C348 | `src/Ave.Documents.Layout/PagePainter.cs:101` | CSS Backgrounds 3, §3 | RIGHT | https://www.w3.org/TR/css-backgrounds-3/ §3, §4.4 |
| C349 | `src/Ave.Documents.Layout/Paginator.cs:92` | CSS Paged Media 3, §3 | RIGHT | https://www.w3.org/TR/css-page-3/ §3 |
| C350 | `src/Ave.Documents.Layout/Paginator.cs:125` | CSS 2.1, CSS Fragmentation 3, §5.4, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C351 | `src/Ave.Documents.Layout/SlicedBox.cs:10` | CSS Fragmentation 3, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.4 |
| C352 | `src/Ave.Documents.Layout/SlicedBox.cs:59` | CSS Fragmentation 3, Chromium, §4.1, §4.4, §5.3 | NOT-SUPPORTED | https://www.w3.org/TR/css-break-3/ §4.4, §5.3 |
| C353 | `src/Ave.Documents.Layout/SlicedBox.cs:126` | §5.3, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/ |
| C354 | `src/Ave.Documents.Layout/Tracks/CollapsedBorders.cs:7` | CSS 2.1, CSS Fragmentation 3, §17.6.1, §17.6.2, §17.6.2.1, §5.4 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C355 | `src/Ave.Documents.Layout/Tracks/CollapsedBorders.cs:28` | §17.6.2.1 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C356 | `src/Ave.Documents.Layout/Tracks/CollapsedBorders.cs:109` | §17.6.2.1 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C357 | `src/Ave.Documents.Layout/Tracks/TableBreaks.cs:12` | CSS Fragmentation 3, §4.1, §4.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.1 |
| C358 | `src/Ave.Documents.Layout/Tracks/TableBreaks.cs:28` | §5.3 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.3; https://drafts.csswg.org/css-tables-3/ §6.1 |
| C359 | `src/Ave.Documents.Layout/Tracks/TableFragments.cs:35` | ISO 32000-1, §14.8.4.3.4 | RIGHT | PDF32000_2008.pdf §14.8.4.3.4, Table 337 |
| C361 | `src/Ave.Documents.Layout/Tracks/TablePage.cs:10` | CSS 2.1, CSS Fragmentation 3, Chromium, §17.2, §17.5.3, §17.6.2, §4.1, §4.5, §5.3, §5.4 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C362 | `src/Ave.Documents.Layout/Tracks/TableSegments.cs:14` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C363 | `src/Ave.Documents.Layout/Tracks/TableSegments.cs:79` | §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C364 | `src/Ave.Documents.Layout/Tracks/TableSegments.cs:111` | §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C365 | `src/Ave.Documents.Layout/Tracks/TableSegments.cs:165` | §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C366 | `src/Ave.Documents.Layout/Tracks/TrackSizing.cs:10` | CSS 2.1, CSS Grid 1, CSS Tables 3, Chromium, §11.4, §11.7, §11.7.1, §17.5.2.1, §17.5.2.2, §7.2.4 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#auto-table-layout; https://www.w3.org/TR/css-grid-1/#algo-find-fr-size |
| C367 | `src/Ave.Documents.Layout/Tracks/TrackSizing.cs:42` | CSS 2.1, §17.5.2.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#auto-table-layout |
| C369 | `src/Ave.Documents.Layout/Tracks/TrackSizing.cs:109` | CSS Grid 1, §11.7.1 | MISQUOTED | https://www.w3.org/TR/css-grid-1/ §11.7.1 |
| C370 | `src/Ave.Documents.Layout/Tracks/TracksLayout.cs:12` | CSS 2.1, CSS Fragmentation 3, §17, §17.2, §17.5.2.1, §17.5.2.2, §17.5.3, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C371 | `src/Ave.Documents.Layout/Tracks/TracksLayout.cs:70` | CSS 2.1, §17.5, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C372 | `src/Ave.Documents.Layout/Tracks/TracksLayout.cs:109` | §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C373 | `src/Ave.Documents.PageModel/DrawItem.cs:79` | ISO 32000-1, §14.8.2.2 | RIGHT | PDF32000_2008.pdf §14.8.2.2, §14.6 |
| C374 | `src/Ave.Documents.PageModel/DrawItem.cs:94` | ISO 32000-1, Table 330, §14.8.2.2.2 | RIGHT | PDF32000_2008.pdf §14.8.2.2.2, Table 330 |
| C375 | `src/Ave.Documents.Pdf/ContentOperation.cs:5` | ISO 32000-1, §7.8.2 | MISQUOTED | PDF32000_2008.pdf §7.8.2 |
| C376 | `src/Ave.Documents.Pdf/ContentOperators.cs:5` | ISO 32000-1, §8.3.2.3 | RIGHT | PDF32000_2008.pdf §8.3.2.3 |
| C377 | `src/Ave.Documents.Pdf/ContentOperators.cs:12` | Table 57, §8.4.4 | RIGHT | PDF32000_2008.pdf §8.4.4, Table 57 |
| C378 | `src/Ave.Documents.Pdf/ContentOperators.cs:16` | Table 57, §8.4.4 | RIGHT | PDF32000_2008.pdf §8.4.4, Table 57 |
| C379 | `src/Ave.Documents.Pdf/ContentOperators.cs:20` | Table 74, §8.6.8 | RIGHT | PDF32000_2008.pdf §8.6.8, Table 74 |
| C380 | `src/Ave.Documents.Pdf/ContentOperators.cs:28` | Table 59, §8.5.2.1 | RIGHT | PDF32000_2008.pdf §8.5.2.1, Table 59 |
| C381 | `src/Ave.Documents.Pdf/ContentOperators.cs:37` | Table 60, §8.5.3.1 | RIGHT | PDF32000_2008.pdf §8.5.3.1, Table 60 |
| C382 | `src/Ave.Documents.Pdf/ContentOperators.cs:41` | Table 57, §8.3.4, §8.4.4, §8.9.4 | NOT-SUPPORTED | PDF32000_2008.pdf §8.4.4 Table 57, §8.3.4, §8.9.4 |
| C383 | `src/Ave.Documents.Pdf/ContentOperators.cs:55` | Table 87, §8.8 | RIGHT | PDF32000_2008.pdf §8.8, Table 87 |
| C384 | `src/Ave.Documents.Pdf/ContentOperators.cs:60` | Table 107, §9.4.1 | RIGHT | PDF32000_2008.pdf §9.4.1, Table 107 |
| C385 | `src/Ave.Documents.Pdf/ContentOperators.cs:64` | Table 107, §9.4.1 | RIGHT | PDF32000_2008.pdf §9.4.1, Table 107 |
| C386 | `src/Ave.Documents.Pdf/ContentOperators.cs:68` | Table 105, §9.3.1 | RIGHT | PDF32000_2008.pdf §9.3.1, Table 105 |
| C387 | `src/Ave.Documents.Pdf/ContentOperators.cs:74` | Table 108, §9.4.2 | RIGHT | P32 §9.4.2, Table 108 |
| C388 | `src/Ave.Documents.Pdf/ContentOperators.cs:84` | Table 109, §9.4.3 | RIGHT | P32 §9.4.3, Table 109 |
| C389 | `src/Ave.Documents.Pdf/ContentOperators.cs:92` | Table 320, §14.6, §14.6.2, §14.7.4.2, §14.8.2.2 | RIGHT | P32 §14.6, Table 320, §14.6.2, §14.7.4.2, §14.8.2.2 |
| C390 | `src/Ave.Documents.Pdf/ContentOperators.cs:102` | Table 320, §14.6 | RIGHT | P32 §14.6, Table 320 |
| C391 | `src/Ave.Documents.Pdf/ContentStream.cs:10` | ISO 32000-1, §7.8.2 | RIGHT | P32 §7.8.2 |
| C392 | `src/Ave.Documents.Pdf/Flate.cs:7` | ISO 32000-1, RFC 1950, RFC 1951, §7.4.4 | RIGHT | P32 §7.4.4.1 |
| C393 | `src/Ave.Documents.Pdf/IndirectObject.cs:3` | ISO 32000-1, §7.3.10 | RIGHT | P32 §7.3.10 |
| C394 | `src/Ave.Documents.Pdf/PdfDate.cs:6` | ISO 32000-1, §7.9.4 | RIGHT | P32 §7.9.4 |
| C395 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:18` | ISO 32000-1, §7.3.3 | RIGHT | P32 §7.3.3 |
| C396 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:25` | §7.3.7 | RIGHT | P32 §7.3.7, §7.3.8.2 Table 5 |
| C397 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:30` | XML 1.0, XMP, §2.2 | RIGHT | XML 1.0 5th ed §2.2 |
| C400 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:62` | ISO 14289-1, ISO 32000-1, veraPDF rule 7.1-3, §14.8.2.2, §14.8.2.4, §7.1 | WRONG-CLAUSE | PDF32000_2008.pdf 14.8.2.2, 14.8.2.4, 14.7.4; PDFUA-1.xml rule 7.1-3 |
| C401 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:69` | ISO 19005-3, PDF/A-3 | NO-CITATION | — |
| C402 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:75` | ISO 19005-3, veraPDF rule 6.8-3, §6.8 | RIGHT | PDFA-3A.xml rule 6.8-3 |
| C403 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:81` | ISO 14289-1, PDF/UA-1 | NO-CITATION | — |
| C404 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:87` | ISO 14289-1, veraPDF rule 7.1-9, §7.1 | RIGHT | PDFUA-1.xml rule 7.1-9 |
| C405 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:91` | ISO 14289-1, veraPDF rule 7.2-34, §7.2 | RIGHT | PDFUA-1.xml rule 7.2-34 |
| C406 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:95` | ISO 14289-1, veraPDF rule 7.3-1, §7.3 | RIGHT | PDFUA-1.xml rule 7.3-1 |
| C407 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:100` | ISO 14289-1, veraPDF rule 7.4.2-1, §7.4.2 | RIGHT | PDFUA-1.xml rule 7.4.2-1 |
| C408 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:105` | ISO 14289-1, veraPDF rule 7.4.2-1, §7.4.2 | RIGHT | PDFUA-1.xml rule 7.4.2-1 |
| C409 | `src/Ave.Documents.Pdf/PdfErrorCodes.cs:110` | 7.2-41, 7.2-42, ISO 14289-1, §7.2 | WRONG-CLAUSE | PDFUA-1.xml rules 7.2-41, 7.2-42, 7.2-43 (v1.30.4) |
| C410 | `src/Ave.Documents.Pdf/PdfFile.cs:5` | ISO 32000-1, §14.3.3, §14.4, §7.5, §7.7.2 | RIGHT | P32 §7.5.1, §7.5.5 Table 15 |
| C411 | `src/Ave.Documents.Pdf/PdfObject.cs:9` | ISO 32000-1, §7.3, §7.3.8.1 | RIGHT | P32 §7.3, §7.3.8.1 |
| C412 | `src/Ave.Documents.Pdf/PdfObject.cs:23` | §7.3.9 | RIGHT | P32 §7.3.9 |
| C413 | `src/Ave.Documents.Pdf/PdfObject.cs:30` | §7.3.2 | RIGHT | P32 §7.3.2 |
| C414 | `src/Ave.Documents.Pdf/PdfObject.cs:34` | §7.3.3 | RIGHT | P32 §7.3.3 |
| C415 | `src/Ave.Documents.Pdf/PdfObject.cs:38` | §7.3.3 | RIGHT | P32 §7.3.3 |
| C416 | `src/Ave.Documents.Pdf/PdfObject.cs:42` | §7.3.5 | RIGHT | P32 §7.3.5 |
| C417 | `src/Ave.Documents.Pdf/PdfObject.cs:57` | §7.3.4.2 | RIGHT | P32 §7.3.4.2 |
| C418 | `src/Ave.Documents.Pdf/PdfObject.cs:60` | §7.3.4.3 | RIGHT | P32 §7.3.4.3 |
| C419 | `src/Ave.Documents.Pdf/PdfObject.cs:64` | §7.3.4 | RIGHT | P32 §7.3.4.1 |
| C420 | `src/Ave.Documents.Pdf/PdfObject.cs:69` | §7.9.2.2 | RIGHT | P32 §7.9.2.2, Annex D |
| C421 | `src/Ave.Documents.Pdf/PdfObject.cs:91` | §7.3.6 | RIGHT | P32 §7.3.6 |
| C422 | `src/Ave.Documents.Pdf/PdfObject.cs:101` | §7.3.7 | RIGHT | P32 §7.3.7 |
| C423 | `src/Ave.Documents.Pdf/PdfObject.cs:127` | §7.3.10 | RIGHT | P32 §7.3.10 |
| C424 | `src/Ave.Documents.Pdf/PdfProfile.cs:3` | 6.8-3, ISO 14289-1, ISO 19005-3, ISO 32000-1, PDF/A-3, PDF/UA-1, Unicode, XMP, §6.2.11.7, §6.7 | PARAPHRASE | ISO-19005-3 sample 3.10, 5.3 |
| C426 | `src/Ave.Documents.Pdf/PdfProfile.cs:22` | ISO 19005-3, PDF/A-3 | RIGHT | ISO-19005-3 sample 3.10, 5.3 |
| C427 | `src/Ave.Documents.Pdf/PdfProfile.cs:25` | ISO 19005-3, PDF/A-3, Unicode | RIGHT | ISO-19005-3 sample 5.2, 5.3, TOC |
| C428 | `src/Ave.Documents.Pdf/PdfProfile.cs:28` | ISO 14289-1, PDF/UA-1 | RIGHT | webstore.ansi.org preview ISO 14289-1:2014 (TOC/scope) |
| C431 | `src/Ave.Documents.Pdf/PdfProfile.cs:37` | ISO 32000-1, PDF/A-3, PDF/UA-1, §14.8 | RIGHT | PDF32000_2008.pdf 14.8; ISO-19005-3 sample 5.3/5.4; PDFUA-1.xml rule 7.1-11 |
| C432 | `src/Ave.Documents.Pdf/PdfProfile.cs:40` | ISO 14289-1, ISO 19005-3, XMP, §5, §6.6.4 | RIGHT | PDFA-3A.xml rule 6.6.4-1; PDFUA-1.xml rule 5-1 |
| C433 | `src/Ave.Documents.Pdf/PdfProfile.cs:44` | ISO 19005-3, veraPDF rule 6.2.4.3-2, §6.2.4.3 | RIGHT | PDFA-3A.xml rule 6.2.4.3-2 |
| C439 | `src/Ave.Documents.Pdf/PdfProfile.cs:64` | ISO 19005-3, PDF/A-3 | RIGHT | ISO-19005-3 sample 5.2, 5.4 |
| C441 | `src/Ave.Documents.Pdf/PdfSerializer.cs:11` | ISO 32000-1, §7.3 | RIGHT | P32 §7.3 |
| C442 | `src/Ave.Documents.Pdf/PdfSerializer.cs:29` | §7.3.3, §7.3.7 | RIGHT | P32 §7.3.3, §7.3.7 |
| C443 | `src/Ave.Documents.Pdf/PdfSerializer.cs:72` | §7.3.5 | RIGHT | P32 §7.3.5 |
| C444 | `src/Ave.Documents.Pdf/PdfSerializer.cs:89` | §7.3.4.2 | RIGHT | P32 §7.3.4.2, Table 3 |
| C445 | `src/Ave.Documents.Pdf/PdfSerializer.cs:110` | §7.3.4.3 | RIGHT | P32 §7.3.4.3 |
| C446 | `src/Ave.Documents.Pdf/PdfStream.cs:11` | ISO 32000-1, §7.4.4 | RIGHT | P32 §7.4.4 |
| C447 | `src/Ave.Documents.Pdf/PdfStream.cs:14` | ISO 32000-1, §7.4.8 | RIGHT | P32 §7.4.8 |
| C448 | `src/Ave.Documents.Pdf/PdfStream.cs:19` | ISO 32000-1, §7.3.8 | RIGHT | P32 §7.3.8, Table 5 |
| C449 | `src/Ave.Documents.Pdf/PdfTrees.cs:8` | ISO 32000-1, §7.9.6, §7.9.7 | MISQUOTED | P32 §7.9.6, Table 36; §7.9.7 |
| C450 | `src/Ave.Documents.Pdf/PdfTrees.cs:15` | §7.9.6 | RIGHT | P32 §7.9.6, Table 36 |
| C451 | `src/Ave.Documents.Pdf/PdfTrees.cs:26` | §7.9.7 | RIGHT | P32 §7.9.7, Table 37 |
| C452 | `src/Ave.Documents.Pdf/PdfWriter.cs:15` | ISO 32000-1, §7.5 | RIGHT | P32 §7.5.1 |
| C453 | `src/Ave.Documents.Pdf/PdfWriter.cs:21` | §7.5.2 | RIGHT | P32 §7.5.2 |
| C454 | `src/Ave.Documents.Pdf/PdfWriter.cs:44` | §7.5.4 | RIGHT | P32 §7.5.4 |
| C455 | `src/Ave.Documents.Pdf/PdfWriter.cs:59` | §7.5.4 | NOT-SUPPORTED | P32 §7.5.4, §7.3.10 |
| C456 | `src/Ave.Documents.Pdf/PdfWriter.cs:72` | §7.3.8.2 | RIGHT | P32 §7.3.8.2, Table 5 |
| C457 | `src/Ave.Documents.Pdf/PdfWriter.cs:87` | §7.3.10, §7.3.8.1 | RIGHT | P32 §7.3.10, §7.3.8.1 |
| C458 | `src/Ave.Documents.Pdf/PdfWriter.cs:108` | §7.3.8.2 | RIGHT | P32 §7.3.8.2, Table 5 |
| C459 | `src/Ave.Documents.Pdf/PdfWriter.cs:125` | §7.5.4 | RIGHT | P32 §7.5.4 |
| C460 | `src/Ave.Documents.Pdf/PdfWriter.cs:141` | §14.4, §7.5.5 | RIGHT | P32 §7.5.5 Table 15, §14.4 |
| C461 | `src/Ave.Documents.Pdf/PdfWriter.cs:155` | §14.4 | NOT-SUPPORTED | P32 §14.4 |
| C462 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:10` | ICC.1, ISO 19005-3, Table 25, §6.2.3, §6.2.4.3, §6.3.2.2 | RIGHT | color.org ICC_Minor_Revision_for_Web.pdf; PDFA-3A.xml 6.2.3, 6.2.4.3-2/-4 |
| C463 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:39` | ICC.1 | RIGHT | archive.color.org/files/ICC_Minor_Revision_for_Web.pdf A.1 |
| C464 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:60` | ICC.1, Table 19, §6, §6.2, §6.2.2, §6.2.3 | RIGHT | color.org ICC_Minor_Revision_for_Web.pdf §6, §6.2, §6.2.2, §6.2.3 |
| C465 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:112` | ICC.1, veraPDF rule 6.2.3-1, §6.1, §6.1.4 | RIGHT | ICC_Minor_Revision_for_Web.pdf 6.1, 6.1.4; PDFA-3A.xml rule 6.2.3-1 |
| C466 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:137` | ICC.1, Table 78, §6.5.26 | RIGHT | ICC.1:2001-04 §6.5.26 |
| C467 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:146` | ICC.1, Table 37, §5.2, §6.5.3 | UNVERIFIABLE | ICC.1:2001-04 §6.5.3; IEC 61966-2-1 (not public) |
| C468 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:166` | §5.2 | UNVERIFIABLE | — |
| C469 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:170` | ICC.1, Table 68, Unicode, §6.5.17 | RIGHT | ICC.1:2001-04 §6.5.17, Tables 68–69 |
| C470 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:186` | ICC.1, Table 70, §6.5.18 | RIGHT | ICC.1:2001-04 §6.5.18 |
| C471 | `src/Ave.Documents.Pdf/SrgbIccProfile.cs:189` | ICC.1, §5.3.10, §5.3.3 | RIGHT | ICC.1:2001-04 §5.3.3, §5.3.10 |
| C472 | `src/Ave.Documents.Pdf/Xmp/XmpDate.cs:6` | ISO 16684-1, ISO 8601, XMP, §8.2.1.2 | RIGHT | Adobe XMP Spec Part 1 (2012) 8.2.1.2; DIN Media TOC ISO 16684-1:2019 |
| C474 | `src/Ave.Documents.Pdf/Xmp/XmpNamespace.cs:3` | 6.6.4-4, ISO 16684-1, XMP, §6.2 | WRONG-CLAUSE | github.com/adobe/XMP-Toolkit-SDK docs/XMPSpecificationPart1.pdf 6.2; PDFUA-1.xml rule 5-3; PDFA-3A.xml 6.6.4-4 |
| C476 | `src/Ave.Documents.Pdf/Xmp/XmpNamespace.cs:22` | ISO 19005-3, §6.6.4 | RIGHT | PDFA-3A.xml 6.6.4-x |
| C477 | `src/Ave.Documents.Pdf/Xmp/XmpNamespace.cs:25` | ISO 14289-1, §5 | RIGHT | PDFUA-1.xml 5-x |
| C478 | `src/Ave.Documents.Pdf/Xmp/XmpNamespace.cs:28` | ISO 19005-3, veraPDF rule 6.6.2.3.3-1, §6.6.2.3.3 | RIGHT | PDFA-3A.xml rule 6.6.2.3.3-1 |
| C481 | `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:14` | ISO 16684-1, ISO 32000-1, XMP, §14.3.2, §7 | RIGHT | XMP Part 1 §7; PDF32000_2008.pdf §14.3.2 |
| C482 | `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:22` | ISO 16684-1, §7.3.2 | RIGHT | XMP Part 1 §7.3.2 |
| C483 | `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:26` | 6.6.2.1-2, XML 1.0, XMP, veraPDF rule 6.6.2.1-5, §2.2 | RIGHT | PDFA-3A.xml rules 6.6.2.1-2, -3, -5 |
| C486 | `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:125` | ISO 16684-1, §7.5, §7.7, §7.9.2 | RIGHT | XMP Part 1 §7.5, §7.7, §7.9.2.3 |
| C488 | `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:7` | ISO 19005-3, XMP, veraPDF rule 6.6.2.3.1-1, §6.6.2.3.1, §6.6.2.3.3. | RIGHT | PDFA-3A.xml rule 6.6.2.3.1-1 |
| C489 | `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:18` | ISO 14289-1, PDF/A-3, §5 | RIGHT | PDFUA-1.xml rules 5-1 to 5-5 |
| C490 | `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:25` | ISO 14289 | NO-CITATION | PDFA-3A.xml rules 6.6.2.3.3-9, -10 |
| C491 | `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:27` | 6.6.2.3.3-1, ISO 19005-3, §6.6.2.3.3 | RIGHT | PDFA-3A.xml rules 6.6.2.3.3-1 to -18 |
| C492 | `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:45` | ISO 19005-3, XMP, §6.6.2.3.3 | RIGHT | PDFA-3A.xml rules 6.6.2.3.3-7 to -10 |
| C493 | `src/Ave.Documents.Pdf/Xmp/XmpValue.cs:5` | ISO 16684-1, XMP, §6.3 | RIGHT | XMP Part 1 §6.3; 2019 TOC |
| C495 | `src/Ave.Documents.Pdf/Xmp/XmpValue.cs:21` | ISO 16684-1, §8.2.2.4 | RIGHT | XMP Part 1 §8.2.2.4 |
| C497 | `src/Ave.Documents.Render.Pdf/AttachmentEmbedding.cs:7` | ISO 32000-1, §7.11.4 | RIGHT | P32 §7.11.4 |
| C498 | `src/Ave.Documents.Render.Pdf/AttachmentEmbedding.cs:10` | 6.8-1, 6.8-2, 6.8-3, 7.11-1, ISO 19005-3, ISO 32000-2, PDF/A-3, Table 44, Table 45, Table 46, §6.8, §7.11.3, §7.11.4 | RIGHT | PDF32000_2008.pdf Tables 44–46; PDFA-3A.xml 6.8-x; PDFUA-1.xml 7.11-1 |
| C499 | `src/Ave.Documents.Render.Pdf/AttachmentEmbedding.cs:49` | Table 31, §7.7.4 | RIGHT | P32 §7.7.4, Table 31 |
| C501 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:48` | ISO 19005-3, veraPDF rule 6.8-3, §6.8 | RIGHT | PDFA-3A.xml rule 6.8-3 |
| C502 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:56` | ISO 19005-3, §6.8 | RIGHT | PDFA-3A.xml rule 6.8-3 |
| C503 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:60` | ISO 14289-1, §7.1, §7.2, §7.3, §7.4.2 | RIGHT | PDFUA-1.xml |
| C504 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:82` | ISO 14289-1, PDF/UA-1, §7.1 | RIGHT | PDFUA-1.xml rule 7.1-9 |
| C505 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:85` | ISO 32000-1, §14.9.2.2 | RIGHT | P32 §14.9.2.2 |
| C506 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:88` | ISO 14289-1, PDF/UA-1, §7.2 | RIGHT | PDFUA-1.xml rule 7.2-34 |
| C507 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:95` | ISO 14289-1, PDF/UA-1, §7.3 | RIGHT | PDFUA-1.xml rule 7.3-1 |
| C508 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:106` | veraPDF rule 7.4.2-1, §7.4.2 | RIGHT | PDFUA-1.xml rule 7.4.2-1 |
| C509 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:118` | ISO 14289-1, PDF/UA-1, §7.4.2 | RIGHT | PDFUA-1.xml rule 7.4.2-1 |
| C510 | `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:124` | ISO 14289-1, PDF/UA-1, §7.4.2 | RIGHT | PDFUA-1.xml rule 7.4.2-1 |
| C511 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:13` | ISO 32000-1, TrueType, §9.6.4, §9.7 | RIGHT | P32 §9.6.4, §9.7 |
| C512 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:25` | §9.2.4 | RIGHT | P32 §9.2.4 |
| C513 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:28` | Table 123, §9.8.2 | RIGHT | P32 §9.8.2, Table 123 |
| C514 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:61` | Table 118, Table 121, §9.7.5.2, §9.7.6.1 | RIGHT | P32 §9.7.6.1 Table 121, §9.7.5.2 Table 118 |
| C515 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:73` | Table 117, §9.7.4.1 | NOT-SUPPORTED | P32 §9.7.4.1 Table 117, §9.7.4.3, §9.7.5.2 |
| C516 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:90` | §9.7.4.3 | RIGHT | P32 §9.7.4.3 |
| C517 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:116` | Table 122, TrueType, §9.8.1 | RIGHT | P32 §9.8.1, Table 122 |
| C518 | `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:143` | TrueType, §9.9 | RIGHT | PDF32000_2008.pdf §9.9, p. 289–290 |
| C519 | `src/Ave.Documents.Render.Pdf/ImageEmbedding.cs:8` | ISO 32000-1, Table 89, §8.9.4, §8.9.5 | RIGHT | P32 §8.9.4, §8.9.5 |
| C520 | `src/Ave.Documents.Render.Pdf/ImageEmbedding.cs:15` | §11.6.5.3, §7.4.8 | MISQUOTED | P32 §8.9.5 Table 89, §11.6.5.3, §7.4.8 |
| C521 | `src/Ave.Documents.Render.Pdf/ImageEmbedding.cs:41` | Table 89 | RIGHT | P32 §8.9.5, Table 89 |
| C522 | `src/Ave.Documents.Render.Pdf/MetadataPacket.cs:8` | ISO 32000-1, XMP, §14.3.2, §14.3.3 | RIGHT | PDF32000_2008.pdf §14.3.2, §14.3.3 |
| C523 | `src/Ave.Documents.Render.Pdf/MetadataPacket.cs:16` | 6.6.4-1, ISO 14289-1, ISO 19005-3, PDF/A-3, §5, §6.6.4 | RIGHT | PDFA-3A.xml / PDFA-3B.xml 6.6.4-x; PDFUA-1.xml 5-1, 5-2 |
| C527 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:14` | ISO 32000-1, §8.3.2.3 | RIGHT | P32 §8.3.2.3 |
| C528 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:20` | ISO 32000-1, §14.6, §14.8 | RIGHT | P32 §14.6, §14.8 |
| C529 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:42` | §8.4.2 | RIGHT | P32 §8.4.2 |
| C530 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:55` | §8.8, §8.9.4 | MISQUOTED | P32 §8.9.4, Table 145, §8.8 |
| C531 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:68` | §9.4, §9.4.3 | RIGHT | P32 §9.4, §9.4.3 |
| C532 | `src/Ave.Documents.Render.Pdf/PageContentStream.cs:110` | §9.4.3, §9.4.4 | RIGHT | P32 §9.4.3, §9.4.4 |
| C533 | `src/Ave.Documents.Render.Pdf/PageModelRules.cs:20` | ISO 32000-1, §7.7.3.2, §7.9.6 | RIGHT | P32 §7.7.3.1–7.7.3.2 |
| C534 | `src/Ave.Documents.Render.Pdf/PageModelRules.cs:47` | ISO 32000-1, §7.7.3.2 | NOT-SUPPORTED | P32 §7.7.3.2 |
| C535 | `src/Ave.Documents.Render.Pdf/PageModelRules.cs:60` | ISO 32000-1, §7.9.6 | RIGHT | P32 §7.9.6 |
| C536 | `src/Ave.Documents.Render.Pdf/PdfRenderOptions.cs:7` | ISO 32000-1, XMP, §14.3.3, §14.4 | NOT-SUPPORTED | PDF32000_2008.pdf §14.4 |
| C537 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:19` | ISO 32000-1 | RIGHT | P32 Introduction |
| C538 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:110` | 6.2-1, 6.7.2.2-1, 6.7.3.3-1, 6.8-4, 7.1-10, 7.1-11, ISO 14289-1, ISO 19005-3, Table 150, Table 28, XMP, §12.2, §14.11.5, §14.3.2, §14.7.1, §14.8.1, §14.9.2, §6.2.3, §6.6.2.1, §6.8, §7.1, §7.7.2, §7.7.4 | WRONG-CLAUSE | PDF32000_2008.pdf 7.7.2 Table 28, 14.7.1, 14.7.2; ISO-19005-3 sample 6.2.3 |
| C539 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:145` | Table 365, §14.11.5 | RIGHT | P32 §14.11.5 |
| C540 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:153` | Table 66, §8.6.5.5 | RIGHT | P32 §8.6.5.5, Table 66 |
| C541 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:157` | Table 315, XMP, veraPDF rule 6.6.2.1-1, §14.3.2 | RIGHT | PDF32000_2008.pdf 14.3.2; PDFA-3A.xml rule 6.6.2.1-1 |
| C542 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:167` | Table 29, §7.7.3.2 | RIGHT | P32 §7.7.3.1–7.7.3.2 |
| C543 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:173` | Table 317, XMP, §14.3.3 | RIGHT | PDF32000_2008.pdf §14.3.3 |
| C544 | `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:182` | ISO 14289-1, Table 30, Table 33, veraPDF rule 7.18.3-1, §12.5, §14.7.4.4, §7.18.3, §7.7.3.3, §7.8.3 | RIGHT | PDF32000_2008.pdf Table 30, 14.7.4.4; PDFUA-1.xml rule 7.18.3-1 |
| C545 | `src/Ave.Documents.Render.Pdf/Tagging/ContentMark.cs:12` | 7.1-1, 7.1-2 | RIGHT | PDFUA-1.xml rules 7.1-1, 7.1-2 |
| C546 | `src/Ave.Documents.Render.Pdf/Tagging/ContentMark.cs:27` | ISO 32000-1, Table 330, §14.7.4.2, §14.8.2.2.2 | RIGHT | P32 §14.7.4.2, §14.8.2.2.2 Table 330 |
| C547 | `src/Ave.Documents.Render.Pdf/Tagging/ContentMark.cs:43` | §14.7.4.2 | RIGHT | P32 §14.7.4.2, §14.6.1 |
| C548 | `src/Ave.Documents.Render.Pdf/Tagging/ElementBounds.cs:10` | ISO 32000-1, Table 344, §14.8.4.5, §14.8.5.4.3 | MISQUOTED | P32 §14.8.5.4.3 Table 344, §14.8.4.5 Table 340 |
| C549 | `src/Ave.Documents.Render.Pdf/Tagging/ElementBounds.cs:138` | ISO 32000-1, §8.3.2.3 | RIGHT | P32 §8.3.2.3 |
| C550 | `src/Ave.Documents.Render.Pdf/Tagging/StructureObjects.cs:11` | ISO 32000-1, Table 322, Table 323, Table 324, §14.7.2, §14.7.4.2, §14.7.4.4, §14.9.3 | RIGHT | P32 §14.7.2, §14.7.4.4 |
| C551 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTree.cs:13` | ISO 32000-1, §14.7, §14.7.4, §14.7.4.4 | RIGHT | P32 §14.7, §14.7.4 |
| C552 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTree.cs:29` | §14.7.2 | MISQUOTED | P32 §14.8.2.3.1 |
| C553 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTree.cs:140` | Table 323, §14.7.2 | RIGHT | P32 §14.7.2, Table 323 |
| C554 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTree.cs:161` | §14.7.4.2 | RIGHT | P32 §14.7.4.2 |
| C555 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTypes.cs:7` | 6.7.3.4-1, 7.1-5, ISO 32000-1, §14.8.4 | RIGHT | PDF32000_2008.pdf 14.8.4; PDFA-3A.xml 6.7.3.4-1; PDFUA-1.xml 7.1-5 |
| C556 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTypes.cs:20` | Table 323, §14.7.5 | RIGHT | P32 Table 323, §14.7.5.1 |
| C557 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTypes.cs:40` | Table 344, §14.8.5.4, §14.8.5.4.3 | RIGHT | P32 §14.8.5.4, Table 344 |
| C558 | `src/Ave.Documents.Render.Pdf/Tagging/StructureTypes.cs:50` | 7.2-41, 7.2-42, Table 349, veraPDF rule 7.5-1, §14.8.5.7 | RIGHT | PDF32000_2008.pdf 14.8.5.7; PDFUA-1.xml 7.5-1, 7.2-41 to -43 |
| C559 | `src/Ave.Documents.Render.Pdf/Tagging/TableRegularity.cs:10` | 7.2-41, 7.2-42, ISO 14289-1, §7.2 | RIGHT | PDFUA-1.xml rules 7.2-41 to -43 |
| C561 | `src/Ave.Documents.Render.Pdf/Tagging/TableRegularity.cs:40` | ISO 14289-1, PDF/UA-1, §7.2 | RIGHT | PDFUA-1.xml rule 7.2-41 |
| C562 | `src/Ave.Documents.Render.Pdf/Tagging/TableRegularity.cs:49` | ISO 14289-1, PDF/UA-1, §7.2 | RIGHT | PDFUA-1.xml rules 7.2-42, -43 |
| C563 | `src/Ave.Documents.Render.Pdf/ToUnicodeCMap.cs:9` | ISO 32000-1, Unicode, §9.10.3 | RIGHT | P32 §9.10.3 |
| C564 | `src/Ave.Documents.Render.Pdf/ToUnicodeCMap.cs:15` | §9.10.3 | MISQUOTED | adobe-type-tools.github.io/font-tech-notes/pdfs/5014.CIDFont_Spec.pdf p. 70–71; P32 §9.10.3 |
| C565 | `src/Ave.Documents.Render.Pdf/ToUnicodeCMap.cs:19` | §9.10.3 | RIGHT | P32 §9.10.3 |
| C566 | `src/Ave.Documents.TestSupport/PdfStructureReadBack.cs:81` | ISO 32000-1, §14.7.4.4 | RIGHT | P32 §14.7.4.4 |
| C567 | `src/Ave.Documents.TestSupport/PdfStructureReadBack.cs:187` | ISO 32000-1, §14.7.5 | RIGHT | P32 §14.7.5.1 |
| C568 | `src/Ave.Documents.Typesetting/GreedyLineBreaker.cs:9` | UAX #14, Unicode | NO-CITATION | — |
| C569 | `src/Ave.Documents.Typesetting/GreedyLineBreaker.cs:20` | CSS Text 3, UAX #14, §4.1.2, §5.3, §5.4 | RIGHT | https://www.w3.org/TR/css-text-3/ §4.1.2; https://www.unicode.org/reports/tr14/ §5.4 |
| C570 | `src/Ave.Documents.Typesetting/LineBreaking/BreakOpportunity.cs:3` | UAX #14 | RIGHT | https://www.unicode.org/reports/tr14/tr14-57.html §6.1 |
| C571 | `src/Ave.Documents.Typesetting/LineBreaking/BreakableSegments.cs:18` | UAX #14 | RIGHT | same, §6 |
| C572 | `src/Ave.Documents.Typesetting/LineBreaking/BreakableSegments.cs:27` | CSS Text 3, §4.1.2 | RIGHT | https://www.w3.org/TR/css-text-3/ §4.1.2 |
| C574 | `src/Ave.Documents.Typesetting/LineBreaking/BreakableSegments.cs:66` | UAX #14 | RIGHT | same, §6.1 |
| C575 | `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:10` | UAX #14, Unicode, Unicode 18.0.0, §6 | RIGHT | same, §6, §6.1 |
| C576 | `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:78` | §6 | RIGHT | same, §6.2 |
| C577 | `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:185` | §6.1, §6.2 | RIGHT | same, §6 |
| C578 | `src/Ave.Documents.Typesetting/LineJustification.cs:7` | CSS Text 3, §6.1, §6.4 | RIGHT | https://www.w3.org/TR/css-text-3/ |
| C579 | `src/Ave.Documents.Typesetting/LineJustification.cs:13` | CSS Text 3, §7.1 | MISQUOTED | https://www.w3.org/TR/css-text-3/ §7.1 |
| C580 | `src/Ave.Documents.Typesetting/LineJustification.cs:24` | §6.1, §6.3, §6.4, §7.1 | RIGHT | https://www.w3.org/TR/css-text-3/ §6.1 |
| C581 | `src/Ave.Documents.Typesetting/ShapedGlyph.cs:19` | CSS Text 3, §4.1.2 | RIGHT | https://www.w3.org/TR/css-text-3/ §4.1.2 |
| C582 | `src/Ave.Documents.Typesetting/ShapedGlyph.cs:23` | Unicode Standard, §5.21.6 | RIGHT | https://www.unicode.org/versions/Unicode18.0.0/core-spec/chapter-5/ §5.21.6 |
| C583 | `src/Ave.Documents.Typesetting/ShapedGlyph.cs:29` | CSS Text 3, UAX #14, §5.3, §5.4 | RIGHT | https://www.unicode.org/reports/tr14/ §5.4 |
| C584 | `src/Ave.Documents.Typesetting/Shaper.cs:18` | OpenType, UAX #24 | RIGHT | chapter2#scriptlist-table |
| C586 | `src/Ave.Documents.Typesetting/Shaper.cs:93` | HarfBuzz, UAX #14 | RIGHT | hb-ot-shape.cc @7621b1c |
| C587 | `src/Ave.Documents.Typesetting/Shaper.cs:135` | CSS Text 3, UAX #14, §5.3, §5.4 | RIGHT | https://www.unicode.org/reports/tr14/ §5.4 (rev 57) |
| C589 | `src/Ave.Documents.Typesetting/Shaper.cs:169` | OpenType, UAX #24 | RIGHT | DFLT" in Script Tags / https://learn.microsoft.com/en-us/typography/opentype/spec/scripttags |
| C591 | `src/Ave.Documents.Typesetting/Shaping/GlyphFilter.cs:6` | OpenType | RIGHT | chapter2#lookup-table |
| C593 | `src/Ave.Documents.Typesetting/Shaping/GlyphFilter.cs:34` | HarfBuzz, Unicode Standard, §23.2 | NOT-SUPPORTED | unicode.org/versions/Unicode18.0.0/core-spec/chapter-23/ §23.2.2; hb-ot-layout-gsubgpos.hh matcher_t::init / may_skip @7621b1c |
| C595 | `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:8` | OpenType | RIGHT | gpos |
| C596 | `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:20` | HarfBuzz, OpenType, hb-ot-shape.cc | PARAPHRASE | gpos type 4 / type 6; hb-ot-shaper-default.cc, hb-ot-shape.cc |
| C597 | `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:32` | OpenType | RIGHT | kern#format-0 |
| C598 | `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:8` | OpenType | RIGHT | gsub#gsub-table-organization |
| C599 | `src/Ave.Documents.Typesetting/TextLine.cs:6` | CSS Text 3, UAX #14, §6.3 | RIGHT | https://www.w3.org/TR/css-text-3/ §6.3 |
| C601 | `src/Ave.Documents.Typesetting/Unicode/CharacterTraits.cs:15` | UAX #14, §6 | RIGHT | UAX #14 rev 57 §6 |
| C602 | `src/Ave.Documents.Typesetting/Unicode/CharacterTraits.cs:37` | Unicode Standard, §5.21.6 | RIGHT | Unicode 18 ch. 5 §5.21.6 |
| C603 | `src/Ave.Documents.Typesetting/Unicode/LineBreakClass.cs:3` | UAX #14, Unicode, §5.1 | RIGHT | UAX #14 rev 57 §5.1 |
| C604 | `src/Ave.Documents.Typesetting/Unicode/TextScript.cs:3` | UAX #24, Unicode | RIGHT | https://www.unicode.org/reports/tr24/ §1.5, Table 3 |
| C609 | `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs:8` | CSS Text 3, §4.1.1 | RIGHT | https://www.w3.org/TR/css-text-3/ §4.1.1 |
| C610 | `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs:14` | CSS Text 3, §4.1, §4.1.2, §4.1.3 | NOT-SUPPORTED | https://www.w3.org/TR/css-text-3/ §4, §4.1.1, §4.1.3 |
| C611 | `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs:55` | CSS 2.1, CSS Text 3, §4.1.2, §9.4.2 | RIGHT | https://www.w3.org/TR/css-text-3/#white-space-phase-2 (CRD 2026-08-14) |
| C612 | `src/UnitTests.Ave.Documents.Architecture/AssemblyRulesTests.cs:62` | §II.22.31 | RIGHT | https://www.ecma-international.org/wp-content/uploads/ECMA-335_6th_edition_june_2012.pdf II.22.31 |
| C613 | `src/UnitTests.Ave.Documents.Core/SemanticRoleTests.cs:6` | ISO 32000-1, §14.8.4 | RIGHT | P32 §14.8.4 |
| C617 | `src/UnitTests.Ave.Documents.Fluent/LineBreakTests.cs:10` | HTML, UAX #14 | RIGHT | UAX #14 rev 57 §2 Table 1 |
| C618 | `src/UnitTests.Ave.Documents.Fluent/MetadataTextTests.cs:13` | XML 1.0, XMP, §2.2 | RIGHT | XML 1.0 5th ed §2.2 |
| C619 | `src/UnitTests.Ave.Documents.Fluent/StyleMappingTests.cs:16` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1 |
| C620 | `src/UnitTests.Ave.Documents.Fluent/StyleMappingTests.cs:30` | HTML | RIGHT | https://html.spec.whatwg.org/multipage/rendering.html#sections-and-headings |
| C622 | `src/UnitTests.Ave.Documents.Fluent/TrailingLineFeedTests.cs:7` | RFC 3066, RFC 6838, §2.1, §4.2 | RIGHT | RFC 3066 §2.1; RFC 6838 §4.2; https://www.w3.org/TR/xml/#charsets |
| C626 | `src/UnitTests.Ave.Documents.Fonts/FontRuleTests.cs:45` | OpenType | PARAPHRASE | kern |
| C630 | `src/UnitTests.Ave.Documents.Fonts/FontSubsetterTests.cs:159` | ISO 32000-1, §9.6.4 | RIGHT | P32 §9.6.4 |
| C631 | `src/UnitTests.Ave.Documents.Fonts/OpenTypeTableTests.cs:87` | HarfBuzz | PARAPHRASE | OT/Layout/GPOS/PairPosFormat2.hh @7621b1c |
| C632 | `src/UnitTests.Ave.Documents.Fonts/OpenTypeTableTests.cs:113` | HarfBuzz, hb-ot-layout.cc | MISQUOTED | hb-ot-layout.cc hb_ot_layout_table_select_script @7621b1c |
| C635 | `src/UnitTests.Ave.Documents.Images/ImageDecoderTests.cs:100` | PNG, §11.3.2.1 | RIGHT | PNG 2nd ed §11.3.2.1 |
| C636 | `src/UnitTests.Ave.Documents.Images/ImageDecoderTests.cs:128` | PNG, §9.2 | RIGHT | PNG 2nd ed §9.2 |
| C639 | `src/UnitTests.Ave.Documents.Images/ImageDecoderTests.cs:196` | PNG, §5.3 | RIGHT | PNG 2nd ed §5.3 |
| C643 | `src/UnitTests.Ave.Documents.Images/ImageStructureTests.cs:10` | PNG, T.81, §5.6, §B.2.1 | RIGHT | PNG §5.6; itu-t81.pdf B.2.1 |
| C645 | `src/UnitTests.Ave.Documents.Images/PngRuleTests.cs:55` | PNG, §9.4 | RIGHT | PNG 2nd ed §9.4 |
| C646 | `src/UnitTests.Ave.Documents.Images/PngRuleTests.cs:70` | PNG, §11.2.3 | MISQUOTED | PNG 2nd ed §11.2.3 |
| C647 | `src/UnitTests.Ave.Documents.Layout/BorderTests.cs:56` | CSS Fragmentation 3, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.4 |
| C648 | `src/UnitTests.Ave.Documents.Layout/BorderTests.cs:71` | §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C652 | `src/UnitTests.Ave.Documents.Layout/ChromiumReferenceTests.cs:67` | CSS 2.1, Chromium, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C653 | `src/UnitTests.Ave.Documents.Layout/ChromiumReferenceTests.cs:86` | Chromium, §11.7.1 | RIGHT | https://www.w3.org/TR/css-grid-1/ (CRD 2025-03-26) §11.7.1 |
| C654 | `src/UnitTests.Ave.Documents.Layout/ChromiumReferenceTests.cs:98` | Chromium | NO-CITATION | — |
| C658 | `src/UnitTests.Ave.Documents.Layout/HalfLeadingTests.cs:9` | CSS 2.1, §10.8.1 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#leading |
| C659 | `src/UnitTests.Ave.Documents.Layout/KeepRulesTests.cs:9` | CSS Fragmentation 3, §3.1, §3.2 | RIGHT | https://www.w3.org/TR/css-break-3/ |
| C660 | `src/UnitTests.Ave.Documents.Layout/KeepRulesTests.cs:69` | §4.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §4.4 |
| C661 | `src/UnitTests.Ave.Documents.Layout/KeepRulesTests.cs:90` | §3.1.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1.1 |
| C662 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:32` | CSS 2.1, §10.8 | RIGHT | https://www.w3.org/TR/CSS21/visudet.html#line-height |
| C663 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:64` | CSS 2.1, §17.5.2.2 | NOT-SUPPORTED | https://www.w3.org/TR/CSS21/tables.html#auto-table-layout |
| C664 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:79` | CSS Grid 1, §11.7.1 | RIGHT | https://www.w3.org/TR/css-grid-1/ §7.2.4, §11.7.1 |
| C665 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:92` | CSS 2.1, §17.6.2.1 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C666 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:130` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C667 | `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:147` | CSS Fragmentation 3, §3.1.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1.1 |
| C668 | `src/UnitTests.Ave.Documents.Layout/MarginCollapsingTests.cs:9` | CSS 2.1, CSS Fragmentation 3, §5.2, §8.3.1 | RIGHT | https://www.w3.org/TR/css-break-3/#break-margins |
| C669 | `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:46` | CSS Fragmentation 3, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.2, §5.4 |
| C670 | `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:61` | CSS 2.1, §8.3.1, §9.4.2 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C671 | `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:78` | CSS 2.1, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C672 | `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:94` | CSS 2.1, §8.3.1 | RIGHT | https://www.w3.org/TR/CSS21/box.html#collapsing-margins |
| C673 | `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:108` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C674 | `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:143` | CSS Fragmentation 3, §3.2 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.2, §4.4 |
| C675 | `src/UnitTests.Ave.Documents.Layout/PaginationTests.cs:57` | CSS Fragmentation 3, §5.3 | RIGHT | https://www.w3.org/TR/css-break-3/ §5.3 |
| C676 | `src/UnitTests.Ave.Documents.Layout/PaginationTests.cs:81` | CSS Fragmentation 3, §5.3, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/ |
| C678 | `src/UnitTests.Ave.Documents.Layout/PaginationTests.cs:167` | CSS Fragmentation 3, §3.1 | RIGHT | https://www.w3.org/TR/css-break-3/ §3.1, §4.3 |
| C679 | `src/UnitTests.Ave.Documents.Layout/TableBreakLineTests.cs:8` | CSS 2.1, CSS Fragmentation 3, Chromium, §17.6.2, §5.4 | RIGHT | https://www.w3.org/TR/css-break-3/#break-decoration |
| C682 | `src/UnitTests.Ave.Documents.Layout/TracksLayoutTests.cs:41` | CSS 2.1, §17.6.2 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#collapsing-borders |
| C683 | `src/UnitTests.Ave.Documents.Layout/TracksLayoutTests.cs:50` | §17.6.2.1 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution |
| C684 | `src/UnitTests.Ave.Documents.Layout/TracksLayoutTests.cs:79` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C685 | `src/UnitTests.Ave.Documents.Layout/TracksLayoutTests.cs:98` | CSS 2.1, §17.5.3 | RIGHT | https://www.w3.org/TR/CSS21/tables.html#height-layout |
| C686 | `src/UnitTests.Ave.Documents.Layout/TrailingMarginTests.cs:9` | CSS 2.1, CSS Fragmentation 3, §4.1, §8.3.1 | RIGHT | https://www.w3.org/TR/css-break-3/#possible-breaks |
| C689 | `src/UnitTests.Ave.Documents.Pdf/PdfDateTests.cs:5` | ISO 32000-1, §7.9.4 | RIGHT | P32 §7.9.4 |
| C690 | `src/UnitTests.Ave.Documents.Pdf/PdfSerializerTests.cs:5` | ISO 32000-1, §7.3 | RIGHT | P32 §7.3 |
| C691 | `src/UnitTests.Ave.Documents.Pdf/PdfWriterTests.cs:32` | §7.5.2 | RIGHT | Adobe PDF32000_2008.pdf §7.5.2 |
| C692 | `src/UnitTests.Ave.Documents.Pdf/PdfWriterTests.cs:123` | ISO 32000-1, §14.4 | RIGHT | P32 §14.4 |
| C693 | `src/UnitTests.Ave.Documents.Render.Pdf/BoundingBoxTests.cs:10` | ISO 32000-1, Table 344, §14.8.5.4.3 | RIGHT | P32 §14.8.5.4.3, Table 344 |
| C695 | `src/UnitTests.Ave.Documents.Render.Pdf/PdfRendererTests.cs:77` | §9.6.4 | RIGHT | P32 §9.6.4 |
| C696 | `src/UnitTests.Ave.Documents.Render.Pdf/PdfRendererTests.cs:138` | ISO 32000-1, §9.6.4 | RIGHT | P32 §9.6.4 |
| C697 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:79` | ISO 32000-1, Table 46, §7.11.4 | RIGHT | P32 §7.11.4, Table 46 |
| C698 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:93` | ISO 14289-1, §7.1 | RIGHT | PDFUA-1.xml rule 7.1-9 |
| C699 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:103` | ISO 32000-1, Table 123, §9.8.2 | RIGHT | P32 §9.8.2, Table 123 |
| C700 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:116` | ISO 32000-1, Table 122, §9.8.1 | RIGHT | P32 §9.8.1 Table 122, §9.2.4 |
| C701 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:131` | ISO 32000-1, §11.6.5.3 | RIGHT | P32 §11.6.5.3, Table 89 |
| C702 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:145` | ISO 19005-3, ISO 32000-1, XMP, §14.3.3, §6.6.3 | NOT-SUPPORTED | ISO-19005-3 sample 6.1.5; PDFA-3A.xml/PDFA-3B.xml (no 6.6.3); PDF32000_2008.pdf 14.3.2, 14.3.3 |
| C703 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:160` | ISO 14289-1, §7.18.3 | RIGHT | PDFUA-1.xml rule 7.18.3-1 |
| C704 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:176` | ISO 32000-1, Table 330, §14.7.4.2, §14.8.2.2.2 | RIGHT | P32 §14.7.4.2, Table 330 |
| C705 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:199` | ISO 32000-1, §9.4.3 | RIGHT | P32 §9.4.3 |
| C706 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:217` | ISO 32000-1, §9.10.3 | MISQUOTED | TN #5014 ch. 7 beginbfchar |
| C707 | `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:237` | ISO 32000-1, §9.10.3 | RIGHT | P32 §9.10.3 |
| C709 | `src/UnitTests.Ave.Documents.Samples/ReferenceValidationTests.cs:65` | 6.6.2.1-1, XMP | RIGHT | PDFA-3B.xml rule 6.6.2.1-1 |
| C710 | `src/UnitTests.Ave.Documents.Samples/ReferenceValidationTests.cs:66` | 6.2.4.3-2 | RIGHT | PDFA-3B.xml rule 6.2.4.3-2 |
| C711 | `src/UnitTests.Ave.Documents.Samples/RowSpanAcrossPagesTests.cs:12` | ISO 32000-1, §14.8.4.3.4 | RIGHT | P32 §14.8.4.3.4, Table 337 |
| C714 | `src/UnitTests.Ave.Documents.Samples/TaggedPdfTests.cs:54` | ISO 14289-1, §7.1 | RIGHT | PDFUA-1.xml rule 7.1-3 |
| C715 | `src/UnitTests.Ave.Documents.Typesetting/DefaultIgnorableTests.cs:7` | CSS Text 3, HarfBuzz, UAX #14, Unicode, Unicode Standard, §5.21.6, §5.3, §5.4 | RIGHT | https://www.unicode.org/versions/latest/core-spec/chapter-5/ (Unicode 18.0.0) |
| C716 | `src/UnitTests.Ave.Documents.Typesetting/GreedyLineBreakerTests.cs:60` | UAX #14 | RIGHT | UAX #14 rev 57 §6.2 |
| C718 | `src/UnitTests.Ave.Documents.Typesetting/LineBreakTestFileTests.cs:10` | Unicode | PARAPHRASE | https://www.unicode.org/Public/18.0.0/ucd/auxiliary/LineBreakTest.txt header |
| C720 | `src/UnitTests.Ave.Documents.Typesetting/LineJustificationTests.cs:7` | CSS Text 3, §6.1, §6.4 | RIGHT | https://www.w3.org/TR/css-text-3/ |
| C721 | `src/UnitTests.Ave.Documents.Typesetting/OpenTypeShapingTests.cs:95` | HarfBuzz, hb-ot-shape.cc | RIGHT | github.com/akryukov/theano TheanoDidot-Regular.sfd; hb-ot-shape.cc |
| C722 | `src/UnitTests.Ave.Documents.Typesetting/PositioningRuleTests.cs:18` | OpenType | RIGHT | Vertical adjustment for placement, in design units." / gpos#value-record |
| C723 | `src/UnitTests.Ave.Documents.Typesetting/Reference/HarfBuzzCorpusTests.cs:9` | HarfBuzz | RIGHT | hb-buffer.h @7621b1c |
| C724 | `src/UnitTests.Ave.Documents.Typesetting/Reference/HarfBuzzCorpusTests.cs:28` | HarfBuzz, hb-ot-shape.cc | RIGHT | hb-ot-shape.cc @7621b1c |
| C725 | `src/UnitTests.Ave.Documents.Typesetting/Reference/HarfBuzzCorpusTests.cs:40` | ISO 15924 | NO-CITATION | — |
| C727 | `src/UnitTests.Ave.Documents.Typesetting/Reference/HarfBuzzCorpusTests.cs:70` | HarfBuzz, UAX #15, Unicode | RIGHT | hb-ot-shape-normalize.cc @7621b1c |
| C737 | `src/UnitTests.Ave.Documents.Typesetting/Reference/HarfBuzzReference.cs:68` | HarfBuzz, ISO 15924 | RIGHT | hb-common.cc @7621b1c |
| C739 | `src/UnitTests.Ave.Documents.Typesetting/UnicodeDataGenerator.cs:9` | UAX #44, Unicode, §4.2 | RIGHT | https://www.unicode.org/reports/tr44/tr44-38.html §4.2, §4.2.10 |
| C748 | `src/UnitTests.Ave.Documents.Typesetting/UnicodeDataGenerator.cs:65` | Unicode | NO-CITATION | — |
| C753 | `tools/generate-test-images.py:55` | PNG, §9.2 | RIGHT | PNG 2nd ed §9.2 |
| C754 | `tools/generate-test-images.py:113` | PNG, §9.2 | RIGHT | PNG 2nd ed §9.2 |
| C768 | `tools/reference/chromium/measure.mjs:1` | Chromium, HTML | NO-CITATION | — |

## Written or changed after the check

| Place | Verified by |
| --- | --- |
| `NOTICE.md:27` | block C006 (WRONG-CLAUSE); corrected as the verdict says |
| `docs/architecture.md:84` | block C029 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Core/DocumentErrorCodes.cs:63` | block C092 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Core/SemanticRole.cs:35` | block C108 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Core/TableSlots.cs:7` | block C118 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Core/TableSlots.cs:37` | block C120 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.DocumentModel/BoxStyle.cs:53` | block C138 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.DocumentModel/TextStyle.cs:5` | block C159 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Fluent/Cascade.cs:6` | block C162 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Fluent/InputChecks.cs:41` | block C173 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Fluent/InputChecks.cs:143` | block C177 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Fluent/TableTranslator.cs:161` | block C190 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Fluent/UserAgentStyles.cs:11` | block C193 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/FontSubsetter.cs:13` | block C205 (PARAPHRASE); corrected as the verdict says |
| `src/Ave.Documents.Fonts/FontSubsetter.cs:160` | block C209 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/FontSubsetter.cs:239` | block C211 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/OpenType/ClassDefinition.cs:8` | block C214 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/OpenType/LanguageSystem.cs:5` | block C218 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/OpenType/LayoutTable.cs:24` | block C220 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Fonts/OpenType/TableReader.cs:10` | block C230 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Fonts/Tables/BigEndian.cs:7` | block C231 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/Tables/CmapTable.cs:8` | block C235 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Fonts/Tables/NameTable.cs:86` | block C249 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Fonts/Tables/TableDirectory.cs:19` | block C256 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Images/Jpeg/JpegReader.cs:77` | block C271 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Images/Png/PngDecoder.cs:52` | block C282 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Images/Png/PngSamples.cs:9` | block C293 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Images/Png/PngSamples.cs:75` | block C295 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Images/Png/ScanlineFilters.cs:13` | block C297 (PARAPHRASE); corrected as the verdict says |
| `src/Ave.Documents.Layout/Fragment.cs:8` | block C319 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Layout/ImageLayout.cs:48` | block C326 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Layout/MarginCollapsing.cs:7` | block C338 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Layout/PageAreas.cs:8` | block C343 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Layout/PagePainter.cs:36` | block C347 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Layout/SlicedBox.cs:59` | block C352 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Layout/Tracks/TablePage.cs:11` | block C361 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Layout/Tracks/TrackSizing.cs:10` | block C366 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Layout/Tracks/TrackSizing.cs:109` | block C369 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Pdf/ContentOperation.cs:5` | block C375 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Pdf/ContentOperators.cs:41` | block C382 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Pdf/PdfProfile.cs:3` | block C424 (PARAPHRASE); corrected as the verdict says |
| `src/Ave.Documents.Pdf/PdfTrees.cs:8` | block C449 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Pdf/SrgbIccProfile.cs:146` | block C467 (UNVERIFIABLE); corrected as the verdict says |
| `src/Ave.Documents.Pdf/Xmp/XmpNamespace.cs:3` | block C474 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:26` | block C483 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:87` | block C504 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:93` | block C506 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:123` | block C509 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Render.Pdf/ConformanceRules.cs:129` | block C510 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Render.Pdf/FontEmbedding.cs:73` | block C515 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/ImageEmbedding.cs:15` | block C520 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/PageContentStream.cs:55` | block C530 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/PdfRenderOptions.cs:7` | block C536 (NOT-SUPPORTED); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:19` | block C537 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Render.Pdf/PdfRenderer.cs:124` | block C538 (WRONG-CLAUSE); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/Tagging/ElementBounds.cs:10` | block C548 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/Tagging/StructureTree.cs:29` | block C552 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Render.Pdf/ToUnicodeCMap.cs:15` | block C564 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Typesetting/LineJustification.cs:13` | block C579 (MISQUOTED); corrected as the verdict says |
| `src/Ave.Documents.Typesetting/Shaper.cs:139` | block C587 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Typesetting/Shaping/GlyphFilter.cs:13` | block C592 (?); unchanged citation, new wording around it |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:20` | block C596 (PARAPHRASE); corrected as the verdict says |
| `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs:14` | block C610 (NOT-SUPPORTED); corrected as the verdict says |
| `src/UnitTests.Ave.Documents.Fonts/OpenTypeTableTests.cs:113` | block C632 (MISQUOTED); corrected as the verdict says |
| `src/UnitTests.Ave.Documents.Images/PngRuleTests.cs:70` | block C646 (MISQUOTED); corrected as the verdict says |
| `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:218` | block C706 (MISQUOTED); corrected as the verdict says |
| `src/UnitTests.Ave.Documents.Typesetting/LineBreakTestFileTests.cs:10` | block C718 (PARAPHRASE); corrected as the verdict says |
| `src/UnitTests.Ave.Documents.Typesetting/Reference/HarfBuzzReference.cs:68` | block C737 (RIGHT); unchanged citation, new wording around it |
| `src/Ave.Documents.Core/LanguageTags.cs:5` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Core/TextCharacters.cs:3` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.DocumentModel/BoxStyle.cs:57` | clauses and quotations confirmed in the check: CSS Fragmentation 3, §5.4 |
| `src/Ave.Documents.DocumentModel/BoxStyle.cs:63` | clauses and quotations confirmed in the check: CSS Fragmentation 3, §5.4 |
| `src/Ave.Documents.Fluent/InputChecks.cs:253` | clauses and quotations confirmed in the check: ISO 32000-1, §14.9.2.2 |
| `src/Ave.Documents.Images/Png/PngChunk.cs:70` | clauses and quotations confirmed in the check: §5.3 |
| `src/Ave.Documents.Pdf/PdfErrorCodes.cs:34` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Pdf/PdfSerializer.cs:20` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Pdf/PdfWriter.cs:59` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Pdf/PdfWriter.cs:67` | clauses and quotations confirmed in the check: §7.5.4 |
| `src/Ave.Documents.Pdf/PdfWriter.cs:164` | clauses and quotations confirmed in the check: §14.4 |
| `src/Ave.Documents.Render.Pdf/PageContentStream.cs:69` | clauses and quotations confirmed in the check: §14.9.4, §9.4, §9.4.1 |
| `src/Ave.Documents.Render.Pdf/PageContentStream.cs:97` | clauses and quotations confirmed in the check: §9.4.3 |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:130` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:161` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:51` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:56` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:60` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:72` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:84` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:89` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:94` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:99` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:103` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:107` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:112` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:122` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:126` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:130` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:135` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:139` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:143` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/PdfRenderErrorCodes.cs:149` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:20` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:23` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:26` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:32` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:35` | clauses and quotations confirmed in the check: ISO 32000-1, §9.6.4 |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:41` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:59` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:64` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:69` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:74` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:81` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:91` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:96` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:102` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:113` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:117` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:123` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:168` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.Render.Pdf/Tagging/TableRegularity.cs:48` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/Ave.Documents.TestSupport/PdfReadBack.cs:28` | clauses and quotations confirmed in the check: ISO 32000-1, §14.9.4 |
| `src/Ave.Documents.Typesetting/GreedyLineBreaker.cs:88` | clauses and quotations confirmed in the check: CSS Text 3, §4.1.2 |
| `src/Ave.Documents.Typesetting/ShapedGlyph.cs:29` | clauses and quotations confirmed in the check: UAX #14 |
| `src/Ave.Documents.Typesetting/Shaper.cs:187` | clauses and quotations confirmed in the check: UAX #14 |
| `src/Ave.Documents.Typesetting/Shaping/GlyphFilter.cs:36` | clauses and quotations confirmed in the check: HarfBuzz, Unicode Standard, hb-ot-layout-gsubgpos.hh, §23.2 |
| `src/Ave.Documents.Typesetting/Shaping/GlyphItem.cs:34` | clauses and quotations confirmed in the check: UAX #14 |
| `src/UnitTests.Ave.Documents.Fluent/ProfileLimitTests.cs:14` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/UnitTests.Ave.Documents.Layout/LayoutRuleTests.cs:64` | clauses and quotations confirmed in the check: CSS 2.1, §17.5.2.2 |
| `src/UnitTests.Ave.Documents.Layout/TableBreakLineTests.cs:173` | clauses and quotations confirmed in the check: §17.6.2.1 |
| `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:145` | clauses and quotations confirmed in the check: ISO 32000-1, PDF/A-1, PDF/A-3, XMP, §14.3.2, §14.3.3 |
| `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:238` | clauses and quotations confirmed in the check: ISO 32000-1, §14.9.4 |
| `src/UnitTests.Ave.Documents.Samples/TaggedPdfTests.cs:38` | veraPDF rule ids and texts, verbatim from PDFA-3B.xml, PDFA-3A.xml and PDFUA-1.xml of the pinned image (docs/conformance-matrix.md) |
| `src/UnitTests.Ave.Documents.Samples/TextReadBackTests.cs:14` | clauses and quotations confirmed in the check: ISO 32000-1, §14.9.4, §9.10.3 |
| `src/UnitTests.Ave.Documents.Typesetting/ForcedLineBreakTests.cs:6` | clauses and quotations confirmed in the check: CSS Text 3, UAX #14, §4.1.2 |
| `src/UnitTests.Ave.Documents.Typesetting/Reference/IgnorableShapingReferenceTests.cs:6` | clauses and quotations confirmed in the check: HarfBuzz |

## Second check, 2026-11-16

The citations written after the first check, in the fix round's later commits, checked by the internet-scout agent
against the public texts on 2026-11-16. Each row: the reference, where it is, the verdict, and what changed.

| Reference | Place | Verdict | Change |
| --- | --- | --- | --- |
| T.81 §B.2.3, Table B.3: Ls = 6 + 2 × Ns, Ns 1–4 | `src/Ave.Documents.Images/Jpeg/JpegReader.cs` | RIGHT (W3C copy of T.81, p. 38) | none |
| T.81 §F.1.2.3, byte stuffing | `JpegReader.cs`, `UnitTests.Ave.Documents.Images/ImageStructureTests.cs` | RIGHT | none |
| T.81 §B.2.1, Figure B.2: ends with EOI; a frame holds one or more scans | `JpegReader.cs`, `ImageStructureTests.cs` | RIGHT (EOI sentence: non-hierarchical format, the only one read) | none |
| OpenType 'maxp': glyph ids 0 to numGlyphs − 1 | `UnitTests.Ave.Documents.Render.Pdf/PageModelContractTests.cs` | NOT-SUPPORTED by 'maxp' alone; 'glyf' says ids are "sequential integers beginning at zero" | now cites 'glyf' with 'maxp' |
| HarfBuzz skipping iterator skips default-ignorables in GPOS matching | `UnitTests.Ave.Documents.Typesetting/PositioningRuleTests.cs` | RIGHT (`hb-ot-layout-gsubgpos.hh`, `may_skip`); mark-to-base search skips them too (`OT/Layout/GPOS/MarkBasePosFormat1.hh`) | none |
| UAX #14: U+000B, U+000C, U+2028, U+2029 class BK; U+0085 class NL | `docs/architecture.md`, `ForcedLineBreakTests.cs` | RIGHT (LineBreak-18.0.0.txt, §5.1) | none |
| CSS Text 3 §5.5: BK and NL characters are forced line breaks | `docs/architecture.md` | section RIGHT; "lets them" understated it ("must be treated as forced line breaks"); §4 conflicts for Cc characters | quoted, and the conflict with §4 named |
| CSS Text 3 §4: carriage return "treated identically to spaces" | `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs` | RIGHT (§4 introduction) | none |
| snapshot.debian.org keeps every state "for good" | `tools/reference/tools.Dockerfile` | WRONG: "all past and current packages", states about six hours apart, removals possible | reworded with the service's own words |

## Third check, 2026-11-16: quotations

The extraction now keeps quotations in blocks that name no specification (see How). The internet-scout agent checked
every such quotation, and every quotation written in fix round 3, against the public text, character by character for
UAX #14 rule notation (batch and verdicts with the exact specification texts: scratch files of the round, summarised
here). Sources: OpenType 1.9.1 (learn.microsoft.com, the pages' own dates), UAX #14 revision 57 (Unicode 18.0.0) and
revision 55, PNG Second and Third Edition, CSS Fragmentation 3 (CR 2018-12-04), CSS Fonts 4 (WD 2026-11-12), CSS 2.1,
CSS Text 3 (CRD 2026-08-14), ISO 32000-1:2008 in Adobe's copy, Namespaces in XML 1.0 (Third Edition), HarfBuzz 15.0.0
source.

Findings beyond single quotations, fixed in the same change:

- ISO 32000-1 Annex C limits strings to 32 767 bytes "only to strings in content streams"; the comments that cited it
  for every string (the file identifier) now cite veraPDF rule 6.1.13-3, PDF/A's rule for all strings.
- HarfBuzz's buffer limits: 64 glyphs per input glyph and at least 16 384 were HarfBuzz's numbers from 2.7.3 to 11.4.1;
  the pinned 15.0.0 has 256 and 65 536, and 4 096 and 65 536 operations. The constants now say so.
- HarfBuzz 15.0.0 does not skip "hidden" default ignorables in GSUB (Mongolian free variation selectors, tag
  characters, a grapheme joiner that blocks a reordering); the shaper's comment claimed it skips every default
  ignorable but ZWNJ, and now says what HarfBuzz does and where the engine differs.
- OS/2 fsType: the least-restrictive rule for several usage bits holds for OS/2 versions 0 to 2 only.

The quotation blocks the tool finds now (64), with the verdict of the check and what changed:

| Place | Quotation (start) | Verdict |
| --- | --- | --- |
| `docs/architecture.md:17` | “no unsafe code, no P/Invoke, no native assets” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `docs/pdf-profiles-and-accessibility.md:92` | “Logo of Versteeg IT Solutions” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.Core/ErrorSourceExtensions.cs:6` | “where in the user's code” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.DocumentModel/DocumentModelText.cs:7` | “page size=595.276x841.89 margin=56.693,56.693,56.693,56.693 box #0 fl” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.Fonts/EmbeddingPermissions.cs:18` | “When this bit is set, the font must not be subsetted prior to embeddin” | CLOSE ("may not"); now verbatim ("must not") |
| `src/Ave.Documents.Fonts/EmbeddingPermissions.cs:22` | “the font must not be modified, embedded or exchanged in any manner wit” | CLOSE ("explicit" missing); now verbatim; the least-restrictive rule now said to be the spec's for OS/2 versions 0 to 2 only |
| `src/Ave.Documents.Fonts/FontSubsetter.cs:37` | “To compute: set it to 0, sum the entire font as uint32, then store 0xB” | CLOSE (no "minus" wording in 1.9.1); now the 'head' text verbatim |
| `src/Ave.Documents.Fonts/OpenType/GlyphDefinitions.cs:21` | “3 Mark glyph (non-spacing combining glyph)” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/GlyphDefinitions.cs:24` | “2 Ligature glyph (multiple character, spacing glyph)” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/GlyphDefinitions.cs:27` | “1 Base glyph (single character, spacing glyph)” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/LayoutTableParser.cs:142` | “Add to original glyph ID to get substitute glyph ID” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/LegacyKerning.cs:24` | “If this bit is set to 1 the value in this table should replace the val” | NOT-FOUND ("Values in subtables are added") and CLOSE ("If set to 1"); now prose plus the override bit's text verbatim |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:10` | “If set, skips over base glyphs” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:13` | “If set, skips over ligatures” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:16` | “If set, skips over all combining marks” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:19` | “If set, indicates that the Lookup table structure includes the markFil” | CLOSE (1.9.1 wording differs); now verbatim |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:23` | “If not zero, skips over all marks not in the specified mark attachment” | CLOSE (1.9.1 wording differs); now verbatim |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:19` | “Add to original glyph ID to get substitute glyph ID” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:53` | “The order in the Ligature offset array defines the preference for usin” | NOT-FOUND ("more components … fewer"); now the preference-order sentence verbatim |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:66` | “start with the second component, ordered in writing direction” | CLOSE ("beginning with"); now verbatim ("start with the second component, ordered in writing direction") |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:86` | “If valueFormat2 is set to 0, then the second glyph of the pair is the” | CLOSE ('first' glyph of the next pair); now verbatim (the “next” glyph) |
| `src/Ave.Documents.Fonts/Tables/CmapEntries.cs:11` | “this character has no glyph” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.Fonts/Tables/CmapFormat4.cs:50` | “The segments are sorted in order of increasing endCode values” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/Tables/CmapFormat4.cs:82` | “If the idRangeOffset is 0, the idDelta value is added directly to the” | CLOSE (wording differs); now verbatim |
| `src/Ave.Documents.Fonts/Tables/CmapFormat4.cs:88` | “glyphId = *(idRangeOffset[i]/2 + (c - startCode[i]) + &idRangeOffset[i” | CLOSE (left-hand side glyphIdArray[i]); now verbatim (glyphId =) |
| `src/Ave.Documents.Fonts/Tables/HeadTable.cs:30` | “unitsPerEm: Set to a value from 16 to 16384.” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/Tables/HeadTable.cs:37` | “indexToLocFormat: 0 for short offsets (Offset16), 1 for long (Offset32” | RIGHT (verbatim) |
| `src/Ave.Documents.Fonts/Tables/HmtxTable.cs:38` | “As an optimization, the number of records can be less than the number” | CLOSE (sentence no longer in 1.9.1); now the 1.9.1 sentence verbatim |
| `src/Ave.Documents.Fonts/Tables/LocaTable.cs:42` | “The local offset divided by 2 is stored.” | CLOSE ("actual" belongs to the long format); now verbatim |
| `src/Ave.Documents.Images/Png/PngPalette.cs:23` | “shall appear for colour type 3, and may appear for colour types 2 and” | RIGHT in the PNG Second Edition (§11.2.3); the class now names that edition for its section numbers |
| `src/Ave.Documents.Layout/LayoutResult.cs:28` | “placed this fragment, continue here” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.PageModel/PageModelText.cs:7` | “page 1 size=595.276x841.89 start #1 paragraph glyphs Lato-Regular 11p” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.Pdf/PdfObject.cs:119` | “this optional entry is absent” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:81` | “Assign a line breaking class to each code point” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:109` | “Treat X (CM / ZWJ)* as if it were X.” | CLOSE (LB9 joined with a comma); now the two sentences verbatim |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:172` | “NU ( SY / IS )* ×” | CLOSE (spaces inside the parentheses); now verbatim |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:188` | “× ( BK / CR / LF / NL )” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:202` | “[^SP HY HH] × GL” | RIGHT in UAX #14 revision 57 (Unicode 18), which the class names; revision 55 reads [^SP BA HY HH] |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:211` | “(sot / BK / CR / LF / NL / OP / QU / GL / SP / ZW) [\p{Pi}&QU] SP* ×” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:214` | “× [\p{Pf}&QU] ( SP / GL / WJ / CL / QU / CP / EX / IS / SY / BK / CR /” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:222` | “(CL / CP) SP* × NS” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:229` | “× [ QU - \p{Pi} ]” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:233` | “× QU ( [^$EastAsian] / eot )” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:240` | “( sot / BK / CR / LF / NL / SP / ZW / CB / GL ) ( HY / HH ) × ( AL / H” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:244` | “HL (HY / HH) × [^HL]” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:252` | “(AL / HL) × NU” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:261` | “JL × (JL / JV / H2 / H3)” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:269` | “(AL / HL) × (AL / HL)” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:273` | “IS × (AL / HL)” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:278` | “sot (RI RI)* RI × RI” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:325` | “NU ( SY / IS )* CL × PO” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:343` | “Do not break inside the orthographic syllables of Brahmic scripts” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:129` | “If valueFormat2 is set to 0, then the second glyph of the pair is the” | CLOSE (as LookupSubtable.cs:86); now verbatim |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:168` | “the mark anchor is aligned with the base anchor for the applicable mar” | CLOSE (anchor wording); now verbatim |
| `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:119` | “replace a single glyph with another” | RIGHT (verbatim) |
| `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:123` | “A multiple substitution replaces a single glyph with more than one gly” | CLOSE ("replace" vs "replaces"); now the full sentence verbatim |
| `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:145` | “replace multiple glyphs with one glyph” | RIGHT (verbatim) |
| `src/UnitTests.Ave.Documents.Fonts/FontSubsetterTests.cs:151` | “When this bit is set, the font must not be subsetted prior to embeddin” | CLOSE ("may not"); now verbatim |
| `src/UnitTests.Ave.Documents.Fonts/TableParserTests.cs:137` | “The segments are sorted in order of increasing endCode values” | RIGHT (verbatim) |
| `src/UnitTests.Ave.Documents.Render.Pdf/BoundingBoxTests.cs:116` | “in its entirety on a single page” | RIGHT (verbatim) |
| `src/UnitTests.Ave.Documents.Typesetting/GlyphGrowthTests.cs:19` | “glyph 5 → glyph 5, glyph 5” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |
| `src/UnitTests.Ave.Documents.Typesetting/GreedyLineBreakerTests.cs:71` | “[^SP HY HH] × GL” | RIGHT in UAX #14 revision 57 (Unicode 18) |
| `src/UnitTests.Ave.Documents.Typesetting/OpenTypeShapingTests.cs:32` | “The order in the Ligature offset array defines the preference for usin” | NOT-FOUND (in neither HarfBuzz nor OpenType); now the GSUB preference-order sentence verbatim |
| `tools/reference/chromium/rectangles.py:1` | “page x0 y0 x1 y1 height” | OWN-WORDS: a term, a format sample or a rule of this engine in quotation marks; it quotes no outside text |

Quotations that were not quotations, now written without quotation marks, so they left this list: the field lists
of GDEF, GSUB, GPOS and the coverage and class tables (a table's field names in order, now code), the CLOSE lines of
`LocaTable.cs:47` (now verbatim, in a citation block), `SlicedBox.cs:41` and `DrawItem.cs:38` (NOT-FOUND: own words,
now prose), `WhiteSpaceCollapsing.cs:31` (NOT-FOUND in CSS Text 3; now cites §4.1.1 verbatim), and the Table 337
quotations of `PageModelRules.cs` and `PageModelContractTests.cs` (CLOSE: "(TR)" for "(structure type TR)"; now
verbatim).

### Blocks new or changed in fix round 3

Every citation or quotation block whose text differs from the reviewed head `ff49463` (236), with how it was checked.
Rule ids were also checked mechanically: every rule id these blocks name is a rule of the pinned profiles.

| Place | References | Verdict |
| --- | --- | --- |
| `docs/pdf-profiles-and-accessibility.md:62` | PDF/A-3, veraPDF rule 6.1.13-11 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:63` | 6.1.13-3, PDF/A-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:64` | 6.1.13-4, PDF/A-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:65` | 6.8-1, PDF/A-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:66` | 6.2.11.7.3-1, PDF/A-3a | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:67` | 6.1.13-7, PDF/A-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:68` | 6.7.4-1, 7.2-29, PDF/A-3a | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:69` | 6.2.11.7.2-2, 7.21.7-2, PDF/A-3a | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:70` | 7.1-3, PDF/A-3a | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:71` | 6.2.11.8-1, 7.21.8-1, PDF/A-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `docs/pdf-profiles-and-accessibility.md:72` | 7.2-14 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.DocumentModel/BoxStyle.cs:57` | CSS Fragmentation 3, §5.4 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.DocumentModel/BoxStyle.cs:63` | CSS Fragmentation 3, §5.4 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Fluent/InputChecks.cs:18` | CSS Paged Media 3, §3 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Fluent/InputChecks.cs:51` | CSS Fonts 4, §2.2, §2.5 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fluent/InputLimits.cs:16` | ISO 32000-1 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Fonts/EmbeddingPermissions.cs:18` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/EmbeddingPermissions.cs:22` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/FontSubsetter.cs:37` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/FontSubsetter.cs:41` | OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/ClassDefinition.cs:8` | OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/Coverage.cs:8` | OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/GlyphDefinitions.cs:7` | OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LayoutTableParser.cs:142` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LegacyKerning.cs:24` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:19` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LookupFlags.cs:23` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:53` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:66` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/LookupSubtable.cs:86` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/OpenType/TableReader.cs:10` | HarfBuzz, OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/Tables/CmapFormat4.cs:82` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/Tables/CmapFormat4.cs:88` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/Tables/HmtxTable.cs:38` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Fonts/Tables/LocaTable.cs:42` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Images/Png/PngPalette.cs:9` | PNG, §11.2.3, §11.3.2.1 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Layout/Tracks/TablePage.cs:11` | CSS 2.1, CSS Fragmentation 3, Chromium, §17.2, §17.5.3, §17.6.2, §4.1, §4.5, §5.3, §5.4 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/PdfFile.cs:5` | ISO 32000-1, §14.3.3, §14.4, §7.5, §7.7.2 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Pdf/PdfNumber.cs:8` | ISO 32000-1, veraPDF rule 6.1.13-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.Pdf/PdfObject.cs:34` | ISO 32000-1, veraPDF rule 6.1.13-1, §7.3.3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.Pdf/PdfObject.cs:40` | §7.3.3 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Pdf/PdfObject.cs:72` | §7.9.2.2 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Pdf/PdfObject.cs:119` | (quotation; context §7.3.7) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/PdfSerializer.cs:34` | §7.3.3, §7.3.7 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Pdf/PdfSerializer.cs:84` | §7.3.3 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/PdfTrees.cs:16` | §7.9.6 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/PdfTrees.cs:28` | §7.9.7 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Pdf/PdfWriter.cs:64` | §7.5.5 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Pdf/PdfWriter.cs:75` | ISO 32000-1, veraPDF rule 6.1.13-3 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/PdfWriter.cs:83` | veraPDF rule 6.1.3-1, §14.4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:26` | 6.6.2.1-2, XML 1.0, XMP, veraPDF rule 6.6.2.1-5, §2.2 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:107` | XML 1.0, §3 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:27` | 6.6.2.3.3-1, ISO 19005-3, §6.6.2.3.3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.Render.Pdf/PageContentStream.cs:123` | ISO 32000-1, veraPDF rule 6.1.13-3, §9.4.3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.Render.Pdf/PageContentStream.cs:131` | §9.4.3, §9.4.4 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:93` | ISO 32000-1, §14.11.2 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:239` | 7.2-11, 7.2-12, 7.2-14, ISO 32000-1, PDF/UA-1, Table 337 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:295` | ISO 32000-1, Table 337 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Render.Pdf/PdfRenderOptions.cs:7` | ISO 32000-1, XMP, §14.3.3, §14.4 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Render.Pdf/ProfileLimits.cs:41` | veraPDF rule 6.8-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/Ave.Documents.Render.Pdf/Tagging/TableRegularity.cs:63` | ISO 14289-1, PDF/UA-1, §7.2 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Typesetting/LineBreaking/BreakableSegments.cs:27` | CSS Text 3, §4.1.2 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:109` | (quotation; context §6) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/LineBreaking/UnicodeLineBreaking.cs:172` | (quotation; context §6) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/GlyphFilter.cs:36` | HarfBuzz, Unicode Standard, hb-ot-layout-gsubgpos.hh, hb-ot-layout.hh, §23.2 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:50` | OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:129` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:168` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:123` | (quotation; context HarfBuzz) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:145` | (quotation; context HarfBuzz) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/Ave.Documents.Typesetting/Shaping/ShapingBudget.cs:15` | HarfBuzz, hb-limits.hh | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/Ave.Documents.Typesetting/WhiteSpaceCollapsing.cs:31` | CSS Text 3, §4.1.1 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/UnitTests.Ave.Documents.Fluent/LengthLimitTests.cs:13` | veraPDF rule 6.1.13-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Fonts/FontSubsetterTests.cs:151` | (quotation; context OpenType) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/UnitTests.Ave.Documents.Fonts/LayoutValueRulesTests.cs:6` | OpenType | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/UnitTests.Ave.Documents.Layout/TableRowCutLineTests.cs:8` | CSS Fragmentation 3, §5.4 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/UnitTests.Ave.Documents.Pdf/ConformanceBuildingBlockTests.cs:117` | §3 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/UnitTests.Ave.Documents.Pdf/PdfObjectRulesTests.cs:67` | ISO 32000-1, veraPDF rule 6.1.13-1, §7.3.3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelContractTests.cs:107` | ISO 32000-1, Table 337, veraPDF rule 7.2-11 | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelContractTests.cs:206` | ISO 32000-1, §14.11.2 | places named, no quotation; each place already checked: ISO 32000-1 clauses as in the first check, Table 337, §7.3.3 and Annex C read by the scout in this check, §14.11.2 as veraPDF rule 6.1.13-11 names it; hb-limits.hh and Namespaces in XML §3 read by the scout in this check; CSS sections as in the first check |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelContractTests.cs:310` | veraPDF rule 6.1.13-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelValidationTests.cs:128` | veraPDF rule 6.1.13-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelValidationTests.cs:130` | 6.1.13-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelValidationTests.cs:138` | 6.1.13-11 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelValidationTests.cs:140` | 7.1-9, PDF/UA-1, §7.1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelValidationTests.cs:142` | 7.4.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelValidationTests.cs:256` | veraPDF rule 6.8-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Render.Pdf/WrittenValueTests.cs:275` | ISO 32000-1, veraPDF rule 6.1.13-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Samples/NumberAndStringLimitTests.cs:17` | 6.1.13-1, 6.1.13-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Samples/NumberAndStringLimitTests.cs:63` | 6.1.13-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Samples/NumberAndStringLimitTests.cs:76` | 6.1.13-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `src/UnitTests.Ave.Documents.Typesetting/OpenTypeShapingTests.cs:32` | (quotation; context HarfBuzz, hb-ot-shape.cc) | quotations checked by the internet-scout (third check); verbatim after the fixes above |
| `tools/conformance/build-matrix.py:57` | 6.1.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:58` | 6.1.2-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:62` | 6.1.4-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:63` | 6.1.6-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:64` | 6.1.6-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:65` | 6.1.7.1-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:66` | 6.1.7.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:67` | 6.1.7.1-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:72` | 6.1.12-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:73` | 6.1.12-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:74` | 6.1.13-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:76` | 6.1.13-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:87` | 6.2.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:88` | 6.2.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:89` | 6.2.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:95` | 6.2.4.4-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:96` | 6.2.4.4-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:97` | 6.2.5-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:98` | 6.2.5-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:99` | 6.2.5-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:100` | 6.2.5-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:101` | 6.2.5-5 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:102` | 6.2.5-6 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:109` | 6.2.8.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:110` | 6.2.8.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:111` | 6.2.8.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:112` | 6.2.8.3-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:113` | 6.2.8.3-5 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:114` | 6.2.9-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:115` | 6.2.9-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:116` | 6.2.9-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:120` | 6.2.11.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:121` | 6.2.11.2-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:122` | 6.2.11.2-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:123` | 6.2.11.2-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:124` | 6.2.11.2-5 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:125` | 6.2.11.2-6 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:126` | 6.2.11.2-7 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:129` | 6.2.11.3.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:130` | 6.2.11.3.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:131` | 6.2.11.3.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:133` | 6.2.11.4.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:137` | 6.2.11.6-1, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:138` | 6.2.11.6-2, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:139` | 6.2.11.6-3, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:140` | 6.2.11.6-4, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:142` | 6.2.11.7.2-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:144` | 6.2.11.8-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:146` | 6.3.1-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:147` | 6.3.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:148` | 6.3.2-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:149` | 6.3.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:150` | 6.3.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:151` | 6.3.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:152` | 6.3.3-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:153` | 6.4.1-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:154` | 6.4.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:155` | 6.4.1-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:158` | 6.4.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:159` | 6.4.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:160` | 6.4.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:161` | 6.5.1-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:162` | 6.5.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:163` | 6.5.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:164` | 6.5.2-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:171` | 6.6.2.3.1-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:172` | 6.6.2.3.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:174` | 6.6.2.3.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:175` | 6.6.2.3.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:176` | 6.6.2.3.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:177` | 6.6.2.3.3-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:178` | 6.6.2.3.3-5 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:179` | 6.6.2.3.3-6 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:180` | 6.6.2.3.3-7 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:181` | 6.6.2.3.3-8 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:182` | 6.6.2.3.3-9 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:183` | 6.6.2.3.3-10 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:184` | 6.6.2.3.3-11 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:185` | 6.6.2.3.3-12 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:186` | 6.6.2.3.3-13 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:187` | 6.6.2.3.3-14 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:188` | 6.6.2.3.3-15 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:189` | 6.6.2.3.3-16 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:190` | 6.6.2.3.3-17 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:191` | 6.6.2.3.3-18 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:192` | 6.6.4-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:193` | 6.6.4-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:194` | 6.6.4-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:195` | 6.6.4-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:196` | 6.6.4-5 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:197` | 6.6.4-6 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:198` | 6.6.4-7 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:202` | 6.7.3.4-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:203` | 6.7.3.4-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:204` | 6.7.3.4-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:205` | 6.7.4-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:207` | 6.8-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:212` | 6.9-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:213` | 6.9-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:214` | 6.9-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:215` | 6.9-4 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:216` | 6.10-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:217` | 6.10-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:248` | 7.2-11 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:251` | 7.2-14 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:258` | 7.2-21, 7.2-30 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:259` | 7.2-22 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:266` | 7.2-29 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:267` | 7.2-30 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:270` | 7.2-33, XMP | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:277` | 7.2-41 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:278` | 7.2-42 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:279` | 7.2-43 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:281` | 7.4.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:285` | 7.5-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:286` | 7.5-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:288` | 7.9-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:289` | 7.9-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:290` | 7.10-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:291` | 7.10-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:295` | 7.18.1-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:296` | 7.18.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:297` | 7.18.1-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:298` | 7.18.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:299` | 7.18.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:300` | 7.18.4-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:301` | 7.18.4-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:302` | 7.18.5-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:303` | 7.18.5-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:304` | 7.18.6.2-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:305` | 7.18.6.2-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:306` | 7.18.8-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:307` | 7.20-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:308` | 7.20-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:311` | 7.21.3.3-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:312` | 7.21.3.3-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:313` | 7.21.3.3-3 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:315` | 7.21.4.1-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:319` | 7.21.6-1, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:320` | 7.21.6-2, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:321` | 7.21.6-3, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:322` | 7.21.6-4, TrueType | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:324` | 7.21.7-2 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |
| `tools/conformance/build-matrix.py:325` | 7.21.8-1 | rule ids and rule texts checked against the pinned veraPDF profiles (the matrix builder stops on an unknown id) |

## Fourth check, 2026-11-16: spot check of fix round 3

The extraction now keeps quotations that hold `<` or `>` (a comparison such as `loca[n] <= loca[n+1]`): an XML or
HTML tag or processing instruction ends a quotation instead of the two characters, so markup still cannot pair
with a quotation mark of the prose. Two quotations were new: `Fonts/Tables/LocaTable.cs:47` ("Offsets must be in
ascending order, with loca[n] <= loca[n+1].", verbatim, OpenType 'loca') and `UnitTests.Ave.Documents.Images/PngRuleTests.cs:54`
(verbatim, see below). The three citation nits of the spot check are fixed, each against the primary source:
GlyphSubstitution (lookup type 2), FlowLayout (forced break, whole sentence), PdfTrees (capital letter, Table 36).

Blocks new or changed in fix round 4 (against `c18c1a7`; the place is the block's first line now). A block whose
wording changed for another reason keeps the verdict of its quotation and claim, said so as "unchanged".

| Place | Kind | Check |
| --- | --- | --- |
| `docs/architecture.md:219` | mention | mention: names a specification or program, claims no place |
| `src/Ave.Documents.Fonts/OpenType/ClassDefinition.cs:11` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Fonts/OpenType/Coverage.cs:10` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Layout/ConstraintSpace.cs:3` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Layout/FlowLayout.cs:99` | citation | RIGHT: both quotations verbatim (CSS Fragmentation 3 §5.2, "When an unforced break occurs before or after a block-level box, any margins adjoining the break are truncated to zero."), scout check of fix round 4 (2026-11-16, primary source read) |
| `src/Ave.Documents.Layout/FlowLayout.cs:271` | citation | FIXED: the forced-break fragment is now the whole sentence of §5.2, verbatim, scout check of fix round 4 (2026-11-16, primary source read); the unforced one is verbatim |
| `src/Ave.Documents.Layout/InFlowChild.cs:21` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Layout/Tracks/TablePage.cs:11` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Pdf/IndirectObject.cs:3` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Pdf/PdfObject.cs:121` | quotation | unchanged: own words in quotation marks, no outside text (third check) |
| `src/Ave.Documents.Pdf/PdfSerializer.cs:42` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Pdf/PdfSerializer.cs:77` | citation | RIGHT: §7.3 "Objects" lists the object types the serializer writes |
| `src/Ave.Documents.Pdf/PdfSerializer.cs:79` | citation | RIGHT: as PdfSerializer.cs:77 |
| `src/Ave.Documents.Pdf/PdfTrees.cs:16` | citation | FIXED: capitalised, and placed where it stands: Table 36, the Names entry, verbatim, scout check of fix round 4 (2026-11-16, primary source read) |
| `src/Ave.Documents.Pdf/PdfTrees.cs:28` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Pdf/PdfWriter.cs:64` | citation | RIGHT: Table 15, Info: "(Optional; shall be an indirect reference)", scout check of fix round 4 (2026-11-16, primary source read) |
| `src/Ave.Documents.Pdf/Xmp/XmpPacket.cs:107` | citation | RIGHT: Namespaces in XML 1.0 §3, "Reserved Prefixes and Namespace Names": other prefixes "MUST NOT be bound to this namespace name" (xml and xmlns), scout check of fix round 4 (2026-11-16, primary source read) |
| `src/Ave.Documents.Pdf/Xmp/XmpSchemaDescription.cs:27` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:98` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:188` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Render.Pdf/PageModelRules.cs:244` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Render.Pdf/PdfRenderOptions.cs:7` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Typesetting/GreedyLineBreaker.cs:20` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Typesetting/GreedyLineBreaker.cs:108` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Typesetting/LineBreaking/BreakableSegments.cs:18` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Typesetting/LineBreaking/BreakableSegments.cs:37` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Typesetting/Shaping/GlyphPositioning.cs:220` | quotation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/Ave.Documents.Typesetting/Shaping/GlyphSubstitution.cs:142` | quotation | FIXED: the earlier sentence was the GSUB overview's; now the heading and first sentence of the lookup type 2 section, verbatim, scout check of fix round 4 (2026-11-16, primary source read) |
| `src/Ave.Documents.Typesetting/Shaping/ShapingBudget.cs:15` | citation | unchanged: no quotation of its own |
| `src/Ave.Documents.Typesetting/Shaping/ShapingBudget.cs:40` | mention | mention: names a specification or program, claims no place |
| `src/UnitTests.Ave.Documents.Core/TableSlotsTests.cs:132` | mention | mention: names a specification or program, claims no place |
| `src/UnitTests.Ave.Documents.Fonts/LayoutValueRulesTests.cs:6` | citation | unchanged: its quotations and their verdicts are those of the earlier checks |
| `src/UnitTests.Ave.Documents.Images/PngRuleTests.cs:54` | citation | RIGHT: verbatim in PNG 2nd and 3rd edition §9.4 "Filter type 4: Paeth", where it is three lines (said in the comment); newly extracted (holds <), scout check of fix round 4 (2026-11-16, primary source read) |
| `src/UnitTests.Ave.Documents.Layout/PageAndTableRuleTests.cs:154` | citation | RIGHT: the quotation of ForcedBreaks.cs (C318, RIGHT), copied verbatim |
| `src/UnitTests.Ave.Documents.Layout/TrailingMarginTests.cs:140` | citation | RIGHT: §5.2 truncates margins adjoining an unforced break (FlowLayout.cs:99) |
| `src/UnitTests.Ave.Documents.Pdf/ConformanceBuildingBlockTests.cs:132` | citation | RIGHT: as XmpPacket.cs:107, scout check of fix round 4 (2026-11-16, primary source read) |
| `src/UnitTests.Ave.Documents.Render.Pdf/PageModelContractTests.cs:254` | citation | unchanged: no quotation of its own |
| `src/UnitTests.Ave.Documents.Render.Pdf/TotalApiTests.cs:7` | mention | mention: names a specification or program, claims no place |
| `src/Ave.Documents.Fonts/Tables/LocaTable.cs:47` | quotation | RIGHT: verbatim (ASCII `<=`), scout check of fix round 4 (2026-11-16, primary source read); newly extracted, block unchanged |

## Fifth check, 2026-11-17: fix round 6

Blocks new or changed in fix round 6 (against `e2f22af`). Every quotation was read from the primary source by the
research agent on 2026-11-17 (raw W3C HTML; the Chromium commit as exported to web-platform-tests).

| Where | Kind | Verdict |
| --- | --- | --- |
| `src/Ave.Documents.Layout/Tracks/TableBreaks.cs:272` | citation | RIGHT: CSS Fragmentation 3 (CR 2018-12-04) §4.1, class A "Between sibling boxes of the following types", list holds "table row boxes", no text on row-spanning cells; §4.4 rule 2 ("a common ancestor of all the elements has a break-inside value of avoid") and rule 4 as in BreakAvoidance.cs; CSS 2.1 §13.3.3 names no table rows, §17 no page breaks; CSS Tables 3 (WD 2025-12-16) §6.1 "if the cells spanning the row do not span any subsequent row", "Other rows are said freely fragmentable."; Chromium commit "[table-fragmentation] Remove rowspan avoidance logic" (review 4136462, bug 1396218, 2023-01-10): "creating large gaps when printing", verbatim |
| `src/Ave.Documents.Layout/Tracks/TablePage.cs:11` | citation | unchanged places; the sentence on a break between two rows of a unit now states the rule of TableBreaks.cs:272, no new quotation |
| `src/UnitTests.Ave.Documents.Layout/ChromiumReferenceTests.cs` (two new tests) | mention | mention: Chromium 159 print of cases `b4-row-span` and `b5-row-span-kept-cell`, measured 2026-11-17 (`tools/reference/chromium/measured/`) |
