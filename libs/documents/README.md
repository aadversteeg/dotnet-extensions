# Ave.Documents

A document engine in pure managed .NET 10: a document written in C# goes in, a PDF comes out. No native
library, no P/Invoke and no external engine or tool at run time; tools appear only in tests.

This is **stage 1** of milestone M1. Stage 0 laid one thin path through every layer; stage 1 adds text
quality: Unicode line breaking (UAX #14), OpenType shaping (ligatures, kerning, mark positioning), font
subsetting, text alignment and justification, CSS margin collapsing and box slicing at page breaks, and an
order-free modifier API for the code front-end. It lays out blocks and paragraphs of text over pages and writes a
PDF with subset fonts whose text can be selected, copied and searched.

## Layers

Each layer is its own project and package and depends only on the layers above it in this list. The
dependencies are checked on the compiled assemblies by `UnitTests.Ave.Documents.Architecture`.

```
 Ave.Documents.Fluent         code front-end: a document as an immutable value built from function calls
        │  translate (cascade + validation)
        ▼
 Ave.Documents.DocumentModel  document model: styled box tree with CSS semantics, computed values only
        │  lay out (pure algorithms + break tokens), paint
        ▼
 Ave.Documents.Layout         Flow and Inline algorithms, pagination driver, painter    ← uses Typesetting
        │
        ▼
 Ave.Documents.PageModel      page model: per page a display list (glyph runs, rectangles, structure markers)
        │  render
        ▼
 Ave.Documents.Render.Pdf     page model → PDF: subset composite fonts, ToUnicode, content streams  ← uses Pdf
        │
        ▼
 PDF bytes

 Ave.Documents.Typesetting    white-space collapsing, Unicode line breaking (UAX #14), OpenType shaping, justification
 Ave.Documents.Pdf            PDF objects, content operators, file writer (knows nothing about layout)
 Ave.Documents.Fonts          TrueType reader (incl. GSUB, GPOS, GDEF, kern) and subsetter
 Ave.Documents.Core           lengths, geometry, colour, value lists, source locations, roles, error codes
```

Where the contracts are:

- Layout contract: `src/Ave.Documents.Layout/LayoutAlgorithm.cs` (the delegate and its rules),
  `ConstraintSpace.cs`, `BreakToken.cs`, `LayoutResult.cs`, `Fragment.cs`.
- Document model: `src/Ave.Documents.DocumentModel/Node.cs`, `BoxStyle.cs`, `TextStyle.cs`, `LayoutAlgorithmId.cs`.
- Page model: `src/Ave.Documents.PageModel/PageDocument.cs`, `DrawItem.cs`.
- Error codes: `src/Ave.Documents.Core/DocumentErrorCodes.cs`.

## Writing a document

```csharp
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

The samples app writes `sample-a-letter.pdf` (two pages) and `sample-b-long.pdf` (five pages), each with
`.model.txt`, `.fragments.txt` and `.page.txt`: the three text serialisations of its intermediate models.

`pwsh -NoProfile -File tools/verify-samples.ps1` publishes the samples, proves the publish folder contains only
IL-only managed assemblies, runs them, and checks the PDFs with independent tools in Docker: `qpdf --check`,
`pdftotext`, `pdffonts`, every embedded subset font opened with fontTools, and PNG renderings by poppler and
MuPDF in `out/samples/png`.

Golden files live in `Golden/` folders next to the tests. After a deliberate change, run the tests with
`UPDATE_GOLDEN=1`, review the diff, and commit it.

Two reference gates run with the tests. Line breaking runs the official `LineBreakTest.txt` of Unicode 18.0.0
(`assets/unicode/`). Shaping is compared with HarfBuzz, which runs as WebAssembly inside the test process
(`src/UnitTests.Ave.Documents.Typesetting/Reference/`); no library references it. The Unicode property table
in `src/Ave.Documents.Typesetting/Unicode/` is generated from the committed data files; after updating them,
run the tests with `UPDATE_GENERATED=1`.

## Stage-1 scope and known limits

In: blocks (margin, padding, background), paragraphs of runs (family, size, weight, colour, alignment),
headings, explicit page breaks, CSS margin collapsing (CSS 2.1 §8.3.1), margin truncation and box slicing at
page breaks (CSS Fragmentation 3 §5.2–§5.4), Unicode line breaking (UAX #14, Unicode 18.0.0), OpenType shaping
for Latin, Greek and Cyrillic (GSUB ccmp/liga/clig, GPOS kern/mark, the legacy kern table), text-align and
inter-word justification, TrueType subsetting, deterministic output.

Known limits, each documented next to the code: negative margins (reported as unsupported), orphans/widows,
the line-height strut, Unicode normalization before shaping, contextual and other OpenType lookup types
(skipped), language-specific OpenType systems, complex scripts, bidi, hyphenation, CFF fonts, tagged PDF and
PDF/A, tables, images, headers and footers.

## Conventions

Functional C#: immutable records, pure static functions, errors as values, exceptions only for programming
errors. Every public member has XML documentation (a missing one fails the build), every algorithm cites the
rule it implements, and warnings are errors (including unused usings). A closed set of cases — the PDF object
types, the draw items, the fragments, the front-end elements — is kept together in one file so the whole union
can be read at once; everything else is one type per file.
