# Ave.Extensions.Documents

A document engine in pure managed .NET: you build a document in C#, and it lays it out and writes a PDF. It has no
native library, no P/Invoke and no external program at run time; the reference tools that check its output (veraPDF,
qpdf, poppler, MuPDF, HarfBuzz, Chromium) run only in its tests and scripts. It writes PDF/A-3b, PDF/A-3a and PDF/UA-1:
archivable, tagged and accessible files, as business documents such as invoices and timesheets need.

Status: preview (milestone M1). The first release from this repository is `0.1.0-preview.2`. The packages target
`net10.0` only.

## Packages

| Package | What it is |
| --- | --- |
| `Ave.Extensions.Documents` | The whole engine in one call: `document.ToPdf(fonts, options)`. Applications reference this one. |
| `Ave.Extensions.Documents.Fluent` | The code front-end: a document as an immutable value, built from plain function calls. |
| `Ave.Extensions.Documents.DocumentModel` | The engine-neutral document model: a styled box tree with CSS semantics. |
| `Ave.Extensions.Documents.Layout` | Layout: block flow, inline text, tables, images, pagination with page templates, painting. |
| `Ave.Extensions.Documents.PageModel` | The page model: per page a display list of glyph runs, shapes, images and structure markers. |
| `Ave.Extensions.Documents.Render.Pdf` | The PDF back-end: embedded subset fonts, searchable text, tagging, XMP, the conformance profiles. |
| `Ave.Extensions.Documents.Typesetting` | Text: white-space collapsing, Unicode line breaking (UAX #14), OpenType shaping, justification. |
| `Ave.Extensions.Documents.Pdf` | PDF objects and a PDF file writer (ISO 32000-1); knows nothing about layout or fonts. |
| `Ave.Extensions.Documents.Fonts` | A TrueType reader (with GSUB, GPOS, GDEF, kern) and subsetter. |
| `Ave.Extensions.Documents.Images` | PNG decoding and JPEG header reading (JPEG is embedded as it is). |
| `Ave.Extensions.Documents.Css` | CSS: style sheets, selectors, the cascade with layers, nesting and custom properties, and the property registry. |
| `Ave.Extensions.Documents.Html.Parsing` | The HTML Standard's parsing algorithm: an immutable tree with source positions and every parse error as a diagnostic. |
| `Ave.Extensions.Documents.Core` | Shared vocabulary: lengths, geometry, colour, source locations, roles, metadata, error codes. |

Every package depends only on the packages below it (see [Layers](#layers)). The only packages outside the engine are
`Ave.Extensions.Functional` and `Ave.Extensions.ErrorPaths` (errors as values), referenced by Core.

## A first document

```shell
dotnet add package Ave.Extensions.Documents --prerelease
```

```csharp
using System;
using System.IO;
using Ave.Extensions.Documents;               // ToPdf: front-end, layout and PDF back-end in one call
using Ave.Extensions.Documents.Core;          // lengths (Mm, Pt, Cm), Color, Edges
using Ave.Extensions.Documents.Fluent;        // Style, PageSetup and the modifiers
using Ave.Extensions.Documents.Fonts;         // FontParser, FontSet
using Ave.Extensions.Documents.Pdf;           // PdfProfile
using Ave.Extensions.Documents.Render.Pdf;    // PdfRenderOptions
using Ave.Extensions.Functional;              // Result: OnSuccessBind, OnSuccessMap, Match
using static Ave.Extensions.Documents.Fluent.Elements;

var note = Style.Empty.Padding(4.Mm()).Background(new Color(0xDD, 0xEB, 0xF7));   // a reusable style

var invoice = Document(
    [
        Heading(1, "Invoice").MarginBottom(12.Pt()),
        Paragraph([Text("Total: "), Bold("120 EUR")]),
        Block([Paragraph("Thank you.")]).With(note),
    ],
    PageSetup.A4 with { Margins = Edges.All(2.Cm()) },
    title: "Invoice 2026-001",
    language: "en")
    .FontFamily("Lato").FontSize(11.Pt());

// Fonts are passed in as files: the engine never looks at the fonts installed on the machine.
var fonts = FontParser.Parse(File.ReadAllBytes("Lato-Regular.ttf"))
    .OnSuccessBind(regular => FontParser.Parse(File.ReadAllBytes("Lato-Bold.ttf"))
        .OnSuccessMap(bold => new FontSet([regular, bold])));

// The creation date is an input, so the same document gives the same bytes on every run.
var options = new PdfRenderOptions(CreationDate: new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero))
{
    Profile = PdfProfile.PdfA3a.AndPdfUa1(),   // archivable (PDF/A-3a), tagged and accessible (PDF/UA-1)
};

return fonts
    .OnSuccessBind(set => invoice.ToPdf(set, options))
    .Match(
        pdf =>
        {
            File.WriteAllBytes("invoice.pdf", [.. pdf]);
            return 0;
        },
        error =>
        {
            Console.Error.WriteLine(error);   // the error code and the file and line of the call that caused it
            return 1;
        });
```

This writes `invoice.pdf`, which passes veraPDF for PDF/A-3a and PDF/UA-1. The fonts are TrueType files you pass in
(the samples use Lato, in `assets/fonts/lato`). Formatting is set with modifiers (`.MarginBottom(…)`, `.Color(…)`,
`.FontSize(…)`): each returns a new value with one property set, and they give the same result in any order.

Every step returns `Result<T, Error>` (`Ave.Extensions.Functional`, `Ave.Extensions.ErrorPaths`); exceptions are only
for programming errors. An error carries a path code such as `Documents.Font.GlyphMissing` or
`Documents.Layout.DoesNotFit` and the file and line of the call in your code that caused it. A profile refuses what
its standard forbids, with a code that says why: an image without alternative text under PDF/UA-1 fails with
`Documents.Pdf.Conformance.PdfUa1.FigureWithoutAlternativeText`.

Read next: `docs/code-front-end.md` (every element and modifier), `docs/pdf-profiles-and-accessibility.md` (the
profiles, what each refuses, and the checks a person must still make for PDF/UA), `docs/architecture.md` (how it
works, for a reviewer). The samples in `samples/Ave.Extensions.Documents.Samples` are a letter, a long text and a
timesheet with tables over several pages, in Dutch and English.

## What it does

- **Text:** Unicode line breaking (UAX #14, Unicode 18.0.0), OpenType shaping (GSUB, GPOS, GDEF, legacy kern) for
  Latin, Greek and Cyrillic, justification, soft hyphens and forced line breaks, font subsetting.
- **Blocks and pages:** block flow with CSS margin collapsing, box slicing at page breaks, keep rules, borders, a page
  template with header and footer (optionally different on the first page), page number and page count.
- **Tables:** fixed, auto and fill (`fr`) columns, column and row spans, repeated header rows, row groups, rows that
  break between lines or are kept together, grid lines in the collapsing border model; also a row of blocks side by
  side.
- **Images:** PNG and JPEG.
- **PDF:** PDF/A-3b, PDF/A-3a and PDF/UA-1 as profiles that add what each standard requires and refuse what it
  forbids: tagged PDF from the page model's structure markers, XMP metadata, an sRGB output intent, embedded files
  with their relationship (as a Factur-X invoice carries its XML).
- **Deterministic output:** the same document, fonts and creation date give the same bytes on every machine.

## How the claims are checked

| Claim | Evidence |
| --- | --- |
| The PDFs conform to PDF/A-3b, PDF/A-3a and PDF/UA-1 | veraPDF 1.30.4 (Docker image pinned by digest) validates every sample against all three profiles (`ReferenceValidationTests`, `tools/verify-samples.ps1`), and 100 randomly generated documents per profile are either refused with a reason or pass (`GeneratedDocumentTests`, fixed seed). |
| Every veraPDF rule is accounted for | `docs/conformance-matrix.md` lists all 261 rules of the three profiles: 42 checked by the engine with a named error code and test class, 109 met by construction (with the reason) and 110 about features the engine never writes; `ConformanceMatrixTests` checks that every code and test class it names exists. |
| Line breaking follows UAX #14 | The official `LineBreakTest.txt` of Unicode 18.0.0: 19,346 cases, all pass (`LineBreakTestFileTests`). |
| Shaping matches HarfBuzz | HarfBuzz 15.0.0, run as WebAssembly inside the tests: 44 lines in three fonts, 2,602 glyphs, all identical (`HarfBuzzCorpusTests`); the known differences are Unicode composition only. |
| Table layout matches a browser | Column widths, page breaks, repeated headers and split rows are compared with numbers measured in Chromium 159 (`ChromiumReferenceTests`, `TableBreakLineTests`; cases and measurements in `tools/reference/chromium`). |
| The files are well-formed and readable by other tools | qpdf `--check`, poppler's `pdftotext` and `pdffonts`, every embedded font opened with fontTools, renderings by poppler and MuPDF, attachments extracted by qpdf and compared byte for byte (`tools/verify-samples.ps1`, reference tests). |
| No native code | `UnitTests.Extensions.Documents.Architecture` reads every compiled library: no P/Invoke, no module references, IL only, no unsafe code; the samples' publish folder holds IL-only assemblies only. |

What a PDF/UA document needs beyond these checks (reading order, meaningful alternative texts) is for a person to
check; `docs/pdf-profiles-and-accessibility.md` lists it.

## Layers

Each layer is its own package and depends only on the layers below it. The dependencies are checked on the compiled
assemblies by `UnitTests.Extensions.Documents.Architecture`.

```
 Ave.Extensions.Documents                composition: ToPdf runs front-end, layout and PDF back-end in a row
        │
        ▼
 Ave.Extensions.Documents.Fluent         code front-end: a document as an immutable value built from function calls
        │  translate (cascade + validation)
        ▼
 Ave.Extensions.Documents.DocumentModel  document model: styled box tree with CSS semantics, computed values only
        │  lay out (pure algorithms + break tokens), paint
        ▼
 Ave.Extensions.Documents.Layout         Flow, Inline, Tracks and Image algorithms; pagination driver with page
                                         templates and the page-count fixpoint; painter          ← uses Typesetting
        │
        ▼
 Ave.Extensions.Documents.PageModel      page model: per page a display list (glyph runs, rectangles, images,
                                         structure markers)
        │  render
        ▼
 Ave.Extensions.Documents.Render.Pdf     page model → PDF: subset composite fonts, ToUnicode, image XObjects, content
                                         streams, conformance profiles: tagging, XMP, output intent, attachments,
                                         refusals
        │
        ▼
 PDF bytes

 Ave.Extensions.Documents.Typesetting    white-space collapsing, Unicode line breaking (UAX #14), OpenType shaping,
                                         justification
 Ave.Extensions.Documents.Pdf            PDF objects, content operators, file writer, XMP writer, sRGB ICC profile,
                                         PdfProfile (knows nothing about layout)
 Ave.Extensions.Documents.Fonts          TrueType reader (incl. GSUB, GPOS, GDEF, kern) and subsetter
 Ave.Extensions.Documents.Images         PNG decoder (8-bit grey/RGB/alpha, palette) and JPEG header reader
 Ave.Extensions.Documents.Core           lengths, geometry, colour, value lists, source locations, roles, metadata,
                                         attachments, error codes
```

Where the contracts are:

- Layout contract: `Ave.Extensions.Documents.Layout/LayoutAlgorithm.cs` (the layout delegate and its rules, and the
  intrinsic-widths delegate), `ConstraintSpace.cs`, `BreakToken.cs`, `LayoutResult.cs` (with the break appeal),
  `Fragment.cs`, `LayoutEnvironment.cs` (algorithms by id, the current page).
- Tracks algorithm: `Ave.Extensions.Documents.Layout/Tracks/` (`TracksLayout.cs` lists the steps).
- Document model: `Ave.Extensions.Documents.DocumentModel/Node.cs`, `BoxStyle.cs`, `TextStyle.cs`, `LayoutAlgorithmId.cs`.
- Page model: `Ave.Extensions.Documents.PageModel/PageDocument.cs`, `DrawItem.cs` (with structure and artifact markers).
- Conformance: `Ave.Extensions.Documents.Pdf/PdfProfile.cs`, `Ave.Extensions.Documents.Render.Pdf/ConformanceRules.cs`,
  `Ave.Extensions.Documents.Render.Pdf/Tagging/`.
- Error codes: each library defines the codes it raises in its own `*ErrorCodes.cs` (`Core/DocumentErrorCodes.cs` holds
  the root and the input codes every front-end shares). `UnitTests.Extensions.Documents.Architecture/ErrorCodeTests.cs`
  checks that no library raises another library's codes.

The layer namespaces (`Typesetting`, `DocumentModel`, `PageModel`, …) are named so that they never hide the
front-end's functions (`Text`, `Paragraph`, …) or the page model's `Page`, so documents can be written in any
namespace, including one under `Ave.Extensions.Documents`.

## Target framework

Every project of this family targets `net10.0` only, where the other families of this repository also target
`netstandard2.0`. The engine is written for .NET 10 (span-based parsing, frozen collections and other APIs that
netstandard2.0 lacks), so the `-p:UseNs20=true` and `-p:UseNs21=true` test modes of the repository do not apply to it.

## Build, test, run the samples

From the repository root, or in this folder with the family solution:

```shell
cd libs/documents
dotnet build extensions.slnx
dotnet test extensions.slnx
dotnet run --project samples/Ave.Extensions.Documents.Samples -- out/samples
```

`pwsh -NoProfile -File tools/test.ps1` does the same build and test at low priority (nice 10 on Linux and macOS),
with `-Project` and `-Filter` for one project or a subset.

The samples app writes `sample-a-letter.pdf` (two pages), `sample-b-long.pdf` (five pages) and the timesheet for three
synthetic data sets in Dutch and English (`timesheet-{normal,long,edge}.{nl,en}.pdf`), all as PDF/A-3a + PDF/UA-1,
each with `.model.txt`, `.fragments.txt` and `.page.txt`: the three text serialisations of its intermediate models.
The timesheets embed `timesheet.xml`, also written next to them as `.attachment.timesheet.xml`.
`dotnet run --project samples/Ave.Extensions.Documents.Samples -- time long nl-NL 20` measures one timesheet (cold,
then the median of 20 warm renders). The samples app needs culture data for the timesheet's number formats; the
libraries do not.

Tests that need Docker (veraPDF, qpdf and the other reference tools in pinned images) carry the trait
`Category=Reference`. Without Docker, or where Docker runs Windows containers, they are skipped with a message that
says so; `dotnet test --filter Category!=Reference` leaves them out. On Linux and macOS the containers run as the
current user.

`pwsh -NoProfile -File tools/verify-samples.ps1` publishes the samples, proves the publish folder contains only IL-only
managed assemblies, runs them, and checks the PDFs with the independent tools listed above, all in Docker; the PNG
renderings go to `out/samples/png`. `tools/reference/chromium/measure.ps1` measures the Chromium reference cases
again (see its README). `tools/generate-test-images.py` makes the image test files with Pillow, an independent
encoder.

Golden files live in `Golden/` folders next to the tests. After a deliberate change, run the tests with
`UPDATE_GOLDEN=1`, review the diff, and commit it. The Unicode property table in
`Ave.Extensions.Documents.Typesetting/Unicode/` is generated from the committed data files in `assets/unicode/`; after
updating them, run the tests with `UPDATE_GENERATED=1`.

## Scope and known limits

In: PDF/A-3b, PDF/A-3a and PDF/UA-1 profiles (tagged PDF, XMP, sRGB output intent, embedded files with their
relationship), blocks (margin, padding, solid borders, background), paragraphs of runs (family, size, weight, italic,
colour, alignment), headings, explicit page breaks, keep rules (`break-inside`/`break-before`/`break-after: avoid`,
resolved by break appeal), CSS margin collapsing (CSS 2.1 §8.3.1) with its border conditions, margin truncation and
box slicing at page breaks (CSS Fragmentation 3 §5.2–§5.4), tables and rows of blocks (CSS 2.1 §17 with CSS Grid
`fr` columns, collapsing borders, repeated header rows, rows broken between lines), header and footer areas with a
different first page, page number and page count (a fixpoint of at most 5 passes), PNG and JPEG images, Unicode line
breaking (UAX #14, Unicode 18.0.0), OpenType shaping for Latin, Greek and Cyrillic, text-align and inter-word
justification, TrueType subsetting, deterministic output. Table layout is checked against Chromium 159 in a pinned
container with font hinting off, within the layout units Chromium snaps
(`UnitTests.Extensions.Documents.Layout/ChromiumReferenceTests.cs`, `tools/reference/chromium/README.md`).

Known limits, each documented next to the code: negative margins (reported as unsupported), orphans/widows, the
line-height property and strut, `vertical-align: baseline` in cells, column gaps and column styles, the separated
border model, diagonal border joins, keep rules that would move a break into an earlier sibling's content, interlaced
and 16-bit PNG, CMYK and arithmetic-coded JPEG, Unicode normalization before shaping, contextual and other OpenType
lookup types (skipped), complex scripts, bidi, hyphenation, CFF fonts. At most font sizes Chromium's text is narrower
than the font's advances, by a share that depends on the size (0.039 % at 10 pt, 0.071 % at 11 pt, more at small
sizes; none at 9, 10.5 and 12 pt), so a line that fits in Chromium by less than that share of its text width can
break one word earlier in the engine, which keeps the font's exact advances (`tools/reference/chromium/README.md`,
"Where Chromium is not exact"). Tagging limits (no row headers, lists, captions, links or per-element language) and
the PDF/UA checks a person must make are in `docs/pdf-profiles-and-accessibility.md`; all limits together are in
`docs/architecture.md`.

## Conventions

Functional C#: immutable records, pure static functions, errors as values, exceptions only for programming
errors. Every public member has XML documentation (a missing one fails the build), every algorithm cites the
rule it implements, and warnings are errors (including unused usings). A closed set of cases — the PDF object
types, the draw items, the fragments, the front-end elements — is kept together in one file so the whole union
can be read at once; everything else is one type per file.

## Licence

MIT, see `LICENSE` at the root of the repository. `NOTICE.md` says what the code was written from (the
specifications) and lists the third-party material with its licence: the Unicode Character Database files in
`assets/unicode/` (Unicode License v3; `Ave.Extensions.Documents.Typesetting` carries that licence because its
property table is generated from them), the test fonts Lato and Theano Didot in `assets/fonts/` (SIL Open Font
License 1.1; used by the tests and samples only, the packages ship no fonts), HarfBuzz as WebAssembly for the tests
(MIT), the test-only packages, and the tools the scripts run in containers.
