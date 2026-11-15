# Ave.Documents

A document engine in pure managed .NET 10: a document written in C# goes in, a PDF comes out. No native
library, no P/Invoke and no external engine or tool at run time; tools appear only in tests.

What it does (milestone M1):

- **Text:** Unicode line breaking (UAX #14), OpenType shaping (GSUB, GPOS, GDEF, legacy kern), justification, soft
  hyphens and forced line breaks, font subsetting.
- **Blocks and pages:** block flow with margin collapsing, box slicing at page breaks, keep rules, borders, a page
  template with header and footer (optionally different on the first page), page number and page count.
- **Tables:** fixed, auto and fill columns, column and row spans, repeated header rows, row groups, rows that break
  between lines or are kept together, grid lines in the collapsing border model; also a plain row of blocks side by
  side.
- **Images:** PNG and JPEG.
- **PDF:** PDF/A-3b, PDF/A-3a and PDF/UA-1 as profiles that add what each standard requires and refuse what it
  forbids: tagged PDF from the page model's structure markers, XMP metadata, an sRGB output intent and embedded
  files. veraPDF checks every sample.

Everything that can fail returns an error value (the Ave.Extensions packages), with the author's file and line. The
proof is the SmallBusiness timesheet, written with the code front-end (`samples/Ave.Documents.Samples/Timesheet/`).

For a reviewer, start with `docs/architecture.md`. Template authors: `docs/code-front-end.md` and
`docs/pdf-profiles-and-accessibility.md`.

## Layers

Each layer is its own project and package and depends only on the layers above it in this list. The
dependencies are checked on the compiled assemblies by `UnitTests.Ave.Documents.Architecture`.

```
 Ave.Documents                composition: ToPdf runs front-end, layout and PDF back-end in a row
        │
        ▼
 Ave.Documents.Fluent         code front-end: a document as an immutable value built from function calls
        │  translate (cascade + validation)
        ▼
 Ave.Documents.DocumentModel  document model: styled box tree with CSS semantics, computed values only
        │  lay out (pure algorithms + break tokens), paint
        ▼
 Ave.Documents.Layout         Flow, Inline, Tracks and Image algorithms; pagination driver with page templates
                              and the page-count fixpoint; painter                     ← uses Typesetting
        │
        ▼
 Ave.Documents.PageModel      page model: per page a display list (glyph runs, rectangles, images, structure markers)
        │  render
        ▼
 Ave.Documents.Render.Pdf     page model → PDF: subset composite fonts, ToUnicode, image XObjects, content streams,
                              conformance profiles: tagging, XMP, output intent, attachments, refusals
        │
        ▼
 PDF bytes

 Ave.Documents.Typesetting    white-space collapsing, Unicode line breaking (UAX #14), OpenType shaping, justification
 Ave.Documents.Pdf            PDF objects, content operators, file writer, XMP writer, sRGB ICC profile, PdfProfile
                              (knows nothing about layout)
 Ave.Documents.Fonts          TrueType reader (incl. GSUB, GPOS, GDEF, kern) and subsetter
 Ave.Documents.Images         PNG decoder (8-bit grey/RGB/alpha, palette) and JPEG header reader (embedded as is)
 Ave.Documents.Core           lengths, geometry, colour, value lists, source locations, roles, metadata,
                              attachments, error codes
```

Where the contracts are:

- Layout contract: `src/Ave.Documents.Layout/LayoutAlgorithm.cs` (the layout delegate and its rules, and the
  intrinsic-widths delegate), `ConstraintSpace.cs`, `BreakToken.cs`, `LayoutResult.cs` (with the break appeal),
  `Fragment.cs`, `LayoutEnvironment.cs` (algorithms by id, the current page).
- Tracks algorithm: `src/Ave.Documents.Layout/Tracks/` (`TracksLayout.cs` lists the steps).
- Document model: `src/Ave.Documents.DocumentModel/Node.cs`, `BoxStyle.cs`, `TextStyle.cs`, `LayoutAlgorithmId.cs`.
- Page model: `src/Ave.Documents.PageModel/PageDocument.cs`, `DrawItem.cs` (with structure and artifact markers).
- Conformance: `src/Ave.Documents.Pdf/PdfProfile.cs`, `src/Ave.Documents.Render.Pdf/ConformanceRules.cs`,
  `src/Ave.Documents.Render.Pdf/Tagging/`.
- Error codes: `src/Ave.Documents.Core/DocumentErrorCodes.cs`.

## Writing a document

```csharp
using Ave.Documents;          // ToPdf: the Ave.Documents package joins front-end, layout and PDF back-end
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using static Ave.Documents.Fluent.Elements;

var note = Style.Empty.Padding(4.Mm()).Background(new Color(0xDD, 0xEB, 0xF7));   // a reusable style

var letter = Document(
    [
        Heading(1, "Invoice").MarginBottom(12.Pt()),
        Paragraph([Text("Total: "), Bold("120 EUR")]),
        Block([Paragraph("Thank you.")]).With(note),
    ],
    PageSetup.A4 with { Margins = Edges.All(2.Cm()) })
    .FontFamily("Lato").FontSize(11.Pt());

var fonts = new FontSet([regular, bold]);   // parsed with FontParser.Parse(bytes); no system fonts
var pdf = letter.ToPdf(fonts, new PdfRenderOptions(CreationDate: someFixedDate));
```

Formatting is set with modifiers (`.MarginBottom(…)`, `.Color(…)`, `.FontSize(…)`): each returns a new value
with one property set, and they give the same result in any order. `docs/code-front-end.md` lists them all.

Every step returns `Result<T, Error>` (Ave.Extensions.Functional / ErrorPaths). Errors carry a path code such
as `Documents.Font.GlyphMissing` or `Documents.Layout.DoesNotFit` and the file and line of the call that caused
them. The layer namespaces (`Typesetting`, `DocumentModel`, `PageModel`, …) are named so that they never hide the
front-end's functions (`Text`, `Paragraph`, …) or the page model's `Page`, so documents can be written in any
namespace, including one under `Ave.Documents`.

## Build, test, run the samples

```
cd src
dotnet build Ave.Documents.slnx
dotnet test Ave.Documents.slnx
dotnet run --project ../samples/Ave.Documents.Samples -- ../out/samples
```

The samples app writes `sample-a-letter.pdf` (two pages), `sample-b-long.pdf` (five pages) and the timesheet
for the reporting PoC's three data sets in Dutch and English (`timesheet-{normal,long,edge}.{nl,en}.pdf`), all as
PDF/A-3a + PDF/UA-1, each with `.model.txt`, `.fragments.txt` and `.page.txt`: the three text serialisations of its
intermediate models. The timesheets embed `timesheet.xml`, also written next to them as
`.attachment.timesheet.xml`. `dotnet run --project ../samples/Ave.Documents.Samples -- time long nl-NL 20` measures
one timesheet (cold, then the median of 20 warm renders).
The samples app needs culture data for the timesheet's number formats; the libraries do not.

`pwsh -NoProfile -File tools/verify-samples.ps1` publishes the samples, proves the publish folder contains only
IL-only managed assemblies, runs them, and checks the PDFs with independent tools in Docker: `qpdf --check`,
`pdftotext`, `pdffonts`, every embedded subset font opened with fontTools, PNG renderings by poppler and
MuPDF in `out/samples/png`, veraPDF 1.30.4 for PDF/A-3b, PDF/A-3a and PDF/UA-1 (every failed rule listed), and the
attachments extracted by qpdf and compared byte for byte. With the reporting PoC next to this repository it also writes side-by-side PNGs of
every timesheet page against the PoC's Typst output to `out/samples/sbs`. `tools/generate-test-images.py` makes
the image test files with Pillow, an independent encoder.

Golden files live in `Golden/` folders next to the tests. After a deliberate change, run the tests with
`UPDATE_GOLDEN=1`, review the diff, and commit it.

The veraPDF and qpdf checks also run as tests with the trait `Category=Reference`; without Docker they are
skipped with a message, and `dotnet test --filter Category!=Reference` leaves them out.

Two more reference gates run with the tests. Line breaking runs the official `LineBreakTest.txt` of Unicode 18.0.0
(`assets/unicode/`). Shaping is compared with HarfBuzz, which runs as WebAssembly inside the test process
(`src/UnitTests.Ave.Documents.Typesetting/Reference/`); no library references it. The Unicode property table
in `src/Ave.Documents.Typesetting/Unicode/` is generated from the committed data files; after updating them,
run the tests with `UPDATE_GENERATED=1`.

## Scope and known limits

In: PDF/A-3b, PDF/A-3a and PDF/UA-1 profiles (tagged PDF, XMP, sRGB output intent, embedded files with their
relationship), blocks (margin, padding, solid borders, background), paragraphs of runs (family, size, weight, italic,
colour, alignment), headings, explicit page breaks, keep rules (`break-inside`/`break-before`/`break-after: avoid`,
resolved by break appeal), CSS margin collapsing (CSS 2.1 §8.3.1) with its border conditions, margin truncation and
box slicing at page breaks (CSS Fragmentation 3 §5.2–§5.4), tables and rows of blocks (CSS 2.1 §17 with CSS Grid
`fr` columns, collapsing borders, repeated header rows, rows broken between lines), header and footer areas with a
different first page, page number and page count (a fixpoint of at most 5 passes), PNG and JPEG images, Unicode line
breaking (UAX #14, Unicode 18.0.0), OpenType shaping for Latin, Greek and Cyrillic, text-align and inter-word
justification, TrueType subsetting, deterministic output. Table layout is checked against Chromium 157 and 159
(`src/UnitTests.Ave.Documents.Layout/ChromiumReferenceTests.cs`).

Known limits, each documented next to the code: negative margins (reported as unsupported), orphans/widows, the
line-height property and strut, `vertical-align: baseline` in cells, column gaps and column styles, the separated
border model, diagonal border joins, keep rules that would move a break into an earlier sibling's content,
interlaced and 16-bit PNG, CMYK and arithmetic-coded JPEG, Unicode normalization before shaping, contextual and
other OpenType lookup types (skipped), complex scripts, bidi, hyphenation, CFF fonts. Tagging limits (no row
headers, lists, captions, links or per-element language) and the PDF/UA checks a person must make are in
`docs/pdf-profiles-and-accessibility.md`; all limits together are in `docs/architecture.md`.

## Conventions

Functional C#: immutable records, pure static functions, errors as values, exceptions only for programming
errors. Every public member has XML documentation (a missing one fails the build), every algorithm cites the
rule it implements, and warnings are errors (including unused usings). A closed set of cases — the PDF object
types, the draw items, the fragments, the front-end elements — is kept together in one file so the whole union
can be read at once; everything else is one type per file.
