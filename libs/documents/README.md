# Ave.Extensions.Documents

A document engine in pure managed .NET: you build a document in C#, and it lays it out and writes a PDF. It has no
native library, no P/Invoke and no external program at run time; the reference tools that check its output (veraPDF,
qpdf, poppler, MuPDF, HarfBuzz, Chromium) run only in its tests and scripts. It writes PDF/A-3b, PDF/A-3a and PDF/UA-1:
archivable, tagged and accessible files, as business documents such as invoices and timesheets need.

Status: preview. Milestone M1, the code front-end to PDF, is released as `0.1.0-preview.2`. Milestone M2, the HTML
front-end, is in progress and not released yet. Its stage 2, the raster back-end for preview images, is in the
packages `Graphics` and `Render.Raster`. Its stage 3, HTML and CSS to the computed style of every element, is in the
packages `Html.Parsing`, `Css` and `Html`, which do not lay out or print anything yet. The packages target `net10.0`
only.

## Packages

| Package | What it is |
| --- | --- |
| `Ave.Extensions.Documents` | The whole engine in one call: `document.ToPdf(fonts, options)`, or preview images with `ToPng`. Applications reference this one. |
| `Ave.Extensions.Documents.Fluent` | The code front-end: a document as an immutable value, built from plain function calls. |
| `Ave.Extensions.Documents.DocumentModel` | The engine-neutral document model: a styled box tree with CSS semantics. |
| `Ave.Extensions.Documents.Layout` | Layout: block flow, inline text, tables, images, pagination with page templates, painting. |
| `Ave.Extensions.Documents.PageModel` | The page model: per page a display list of glyph runs, shapes, images and structure markers. |
| `Ave.Extensions.Documents.Render.Pdf` | The PDF back-end: embedded subset fonts, searchable text, tagging, XMP, the conformance profiles. |
| `Ave.Extensions.Documents.Render.Raster` | The raster back-end: a page as pixels, for previews and visual checks. |
| `Ave.Extensions.Documents.Graphics` | 2D graphics: paths, exact-area anti-aliased filling, an RGBA canvas, image drawing. |
| `Ave.Extensions.Documents.Typesetting` | Text: white-space collapsing, Unicode line breaking (UAX #14), OpenType shaping, justification. |
| `Ave.Extensions.Documents.Pdf` | PDF objects and a PDF file writer (ISO 32000-1); knows nothing about layout or fonts. |
| `Ave.Extensions.Documents.Fonts` | A TrueType reader (with GSUB, GPOS, GDEF, kern, glyph outlines) and subsetter. |
| `Ave.Extensions.Documents.Images` | PNG decoding and encoding; JPEG header reading (a PDF embeds JPEG as it is) and decoding. |
| `Ave.Extensions.Documents.Html` | The HTML front-end, first part: an HTML document and its style sheets to the computed style of every element. |
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
works, for a reviewer). The samples in `samples/Ave.Extensions.Documents.Samples` are a letter, a long text, content
taller than a page sliced over pages, and a timesheet with tables over several pages, in Dutch and English.

## Preview images

The raster back-end draws the same pages the PDF has, as pixels: glyphs as their outlines at their exact positions,
rectangles and images, every edge anti-aliased by the exact share of the pixel it covers, also where contours overlap
or cross inside a pixel (a shape whose overlaps take more work than a fill may do is refused; `docs/architecture.md`
states the limit). Lay the document out once
and give the pages to both back-ends (with `invoice`, `fonts` and `options` from the example above):

```csharp
using Ave.Extensions.Documents.Render.Raster;   // RasterOptions

var pages = fonts.OnSuccessBind(set => invoice.ToPages(set));         // translate and lay out, once
if (pages.IsSuccess)
{
    var pdf = pages.Value.ToPdf(options);                             // the PDF
    var preview = pages.Value.ToPng(1, new RasterOptions(96));        // page 1 as a PNG file, 96 pixels per inch
    var thumbnails = pages.Value.ToPngPages(new RasterOptions(48));   // every page
}
```

`ToImages` gives the pixels instead (8-bit RGBA, sRGB). The output is deterministic: the same pages and options give
the same pixels, checked on x64 Linux, x64 Windows and arm64 Linux for .NET 10. The PNG bytes are compressed by the
runtime's deflate, like PDF streams; see *Deterministic output* below. As a sanity check, the pages are compared with
MuPDF and poppler renders of the engine's own PDF (`tools/reference/raster/README.md`).

## HTML and CSS to computed style (M2, stage 3)

The HTML front-end reads a document as Chromium 159 prints it and refuses what it cannot follow. Stage 3
parses the HTML (the HTML Standard's algorithm), reads the style sheets (CSS Syntax 3, Selectors 4, Cascade 5) and
gives every element its computed style, with every parse error, refusal and ignored declaration as a diagnostic that
carries the file, line and column. Layout of the result comes in a later stage.

```csharp
using Ave.Extensions.Documents.Html;           // HtmlStyleResolver, HtmlStyleOptions, StyledHtmlDocument
using Ave.Extensions.Documents.Html.Parsing;   // HtmlParser, HtmlParseOptions
using Ave.Extensions.Functional;               // OnSuccessBind, Match

var styled = HtmlParser.Parse(File.ReadAllBytes("invoice.html"), new HtmlParseOptions("invoice.html"))
    .OnSuccessBind(document => HtmlStyleResolver.Resolve(document, new HtmlStyleOptions()));
```

A style sheet alone is read with `CssParser.ParseStyleSheet` (`Ave.Extensions.Documents.Css`); a document alone with
`HtmlParser.Parse`. The engine opens no file and no URL: linked style sheets and `@import` are read through
`HtmlStyleOptions.Resources`, a function the caller gives. What is supported, refused and ignored is listed in
`docs/html-css-profile.md`, generated from the code; `samples/Ave.Extensions.Documents.Samples.HtmlStyles` prints the
styled tree and the diagnostics of a file. Its limits are listed in `docs/architecture.md` (stage 3); two of them:
`svg` and `math` are refused, so the matching rules of their content are not built, and `StyleComputer.Compute`,
public for a caller that styles its own tree, throws `InvalidOperationException` when it is called for a child before
its parent.

## The timesheet from HTML

The samples app writes the timesheet a second way: the same data, filled into HTML and CSS by the caller's template,
then laid out by the HTML front-end. The engine takes finished HTML; filling it with data is the caller's step:

```csharp
using Ave.Extensions.Documents;        // HtmlRendering, HtmlRenderOptions, ToPdf
using Ave.Extensions.Documents.Html;   // HtmlStyleOptions, HtmlResource
using Ave.Extensions.Functional;       // OnSuccessBind

var html = TimesheetHtml.Write(data, brand, CultureInfo.GetCultureInfo("nl-NL"));   // the caller's template
var pdf = HtmlRendering.ToPages(html, new HtmlRenderOptions
    {
        Style = new HtmlStyleOptions { BaseUrl = documentUrl, Resources = url => files.GetValueOrDefault(url) },
    })
    .OnSuccessBind(rendered => rendered.Output.ToPdf(options));
```

The template (`samples/Ave.Extensions.Documents.Samples/Html/TimesheetHtml.cs`) is a C# raw string literal, with no
templating library. In a real caller's hands the data is untrusted: every value goes into the HTML through
`HtmlText.Escape` and into a CSS string through `HtmlText.CssString`, never as it is. The engine opens no file: the
fonts of the `@font-face` rules, the logo and the style sheets come from the resolver, which gives only the files the
template names. What the HTML front-end draws, refuses and ignores is the HTML profile, `docs/html-css-profile.md`.

The HTML the sample writes is, byte for byte, the document the family measures against Chromium 159's print
(`tools/reference/chromium/documents/timesheet-*.html`; `HtmlSampleTests`), so the sample's pages are the ones that
comparison holds equal. They are the pages of the code-built timesheet, but not the same page model
(`HtmlSamePagesTests`): the same number of pages and the same texts in the same order, each within 1 pt across the
page. The HTML profile puts rows on whole CSS pixels, as Chromium does: a text row is 21 px = 15.75 pt where the code
front-end draws exactly 16 pt, so every HTML row is 0.25 to 0.72 pt shorter. Down a page that adds up, and on page 4 of
the long month it leaves room for one more entry: that page holds 26 entries from HTML and 25 from code, the last page
17 against 18. The page header and footer are built differently (margin boxes against header and footer areas) and
hold the same texts. The invoice (`Html/InvoiceHtml.cs`, `data/invoice.json`) is written the same way.

The documents name their fonts in `@font-face` rules, so the engine parses the font files for each document, from the
bytes the resolver gives (the sample reads each file once). `HtmlRenderOptions.Fonts` takes fonts parsed once, for
families a document does not name in its own rules. The HTML options take no attachment: to embed the data, as the
code-built timesheet embeds `timesheet.xml`, add it to the pages before the PDF:
`pages with { Metadata = pages.Metadata with { Attachments = [xml] } }`.

## What it does

- **Text:** Unicode line breaking (UAX #14, Unicode 18.0.0), OpenType shaping (GSUB, GPOS, GDEF, legacy kern) for
  Latin, Greek and Cyrillic, justification, soft hyphens and forced line breaks, font subsetting.
- **Blocks and pages:** block flow with CSS margin collapsing, box slicing at page breaks, keep rules, borders, a page
  template with header and footer (optionally different on the first page), page number and page count.
- **Tables:** fixed, auto and fill (`fr`) columns, column and row spans, repeated header rows, row groups, rows that
  break between lines or are kept together, grid lines in the collapsing border model; also a row of blocks side by
  side.
- **Images:** PNG and JPEG.
- **Layout policies:** where layout has a choice the document's profile decides: an image or a line taller than the
  page is refused (the default) or sliced over pages as a browser prints it; a table row is cut only where both parts
  show something (the default) or wherever its cells can break, as a browser does (`docs/code-front-end.md`, "Layout
  policies").
- **PDF:** PDF/A-3b, PDF/A-3a and PDF/UA-1 as profiles that add what each standard requires and refuse what it
  forbids: tagged PDF from the page model's structure markers, XMP metadata, an sRGB output intent, embedded files
  with their relationship (as a Factur-X invoice carries its XML).
- **Preview images:** any page as PNG or pixels, at any resolution, in pure managed code.
- **Deterministic output:** the same document, fonts and creation date give the same bytes on every run, and the same
  raster pixels on x64 Linux, x64 Windows and arm64 Linux (tested). Compressed streams (PDF and PNG) come from the .NET
  runtime's deflate, so another runtime version or zlib build can give other bytes for the same content
  (`docs/architecture.md`, Determinism, says what is tested).

## How the claims are checked

| Claim | Evidence |
| --- | --- |
| The PDFs conform to PDF/A-3b, PDF/A-3a and PDF/UA-1 | veraPDF 1.30.4 (Docker image pinned by digest) validates every sample against all three profiles (`ReferenceValidationTests`, `tools/verify-samples.ps1`), and 100 randomly generated documents per profile are either refused with a reason or pass (`GeneratedDocumentTests`, fixed seed). |
| Every veraPDF rule is accounted for | `docs/conformance-matrix.md` lists all 261 rules of the three profiles: 42 checked by the engine with a named error code and test class, 109 met by construction (with the reason) and 110 about features the engine never writes; `ConformanceMatrixTests` checks that every code and test class it names exists. |
| Line breaking follows UAX #14 | The official `LineBreakTest.txt` of Unicode 18.0.0: 19,346 cases, all pass (`LineBreakTestFileTests`). |
| Shaping matches HarfBuzz | HarfBuzz 15.0.0, run as WebAssembly inside the tests: 47 lines in three fonts, 2,688 glyphs, all identical (`HarfBuzzCorpusTests`), Unicode composition included. |
| Table layout matches a browser | Column widths, page breaks, repeated headers and split rows are compared with numbers measured in Chromium 159 (`ChromiumReferenceTests`, `TableBreakLineTests`; cases and measurements in `tools/reference/chromium`). |
| The files are well-formed and readable by other tools | qpdf `--check`, poppler's `pdftotext` and `pdffonts`, every embedded font opened with fontTools, renderings by poppler and MuPDF, attachments extracted by qpdf and compared byte for byte (`tools/verify-samples.ps1`, reference tests). |
| Preview images are drawn right | Whole pages are checked pixel by pixel against the exact areas their geometry gives: glyphs (overlapping contours, offsets, size), an image in a box of another shape, a rule thinner than a pixel (`PageGeometryTests`); the Graphics tests prove coverage, compositing and the image rule the same way. As a sanity check, every sample page is compared with MuPDF 1.25.1 and poppler 25.03.0 renders of the engine's PDF at 96 and 300 dpi: for the largest difference away from edges, the mean squared error and the pixels over 128 steps the engine is no farther from either viewer than the viewers are from each other, and for the pixels over 32 and 64 steps no farther from the nearer viewer (`RasterReferenceTests`, `tools/reference/raster/README.md`); it catches gross errors, not small ones. The samples' pages are pinned as golden images, which catch change, not error. |
| Glyph outlines and JPEG decoding are right | Every glyph of the test fonts decodes to the points fontTools 4.57.0 reads, exactly (`GlyphShapeReferenceTests`); the ITU-T T.83 compliance streams decode to the reference coefficients, exactly (`JpegConformanceTests`). |
| The HTML parser follows the HTML Standard | The html5lib tokenizer tests (7,046 runs; the 4 of `xmlViolation.test` are left out by that file's own rule) and the tree-construction tests of web-platform-tests (1,959 of 1,973; the 14 that need scripting are left out, as the parser runs with scripting off), counts asserted (`TokenizerSuiteTests`, `TreeConstructionSuiteTests`); where Chromium 159 builds another tree, its tree is measured and the parser follows it or a listed limit says why not (`tools/reference/chromium/html-tree`). |
| CSS is read as Chromium 159 reads it | The css-parsing-tests suite and the CSS cases of web-platform-tests, each answer counted as equal, refused or different, with different pinned at 0 or a named skip (`CssParsingSuiteTests`, `ColorSuiteTests`, `WptCssSuiteTests`, `WptComputedValueTests`); the computed style of every element and property against Chromium's (`ChromiumCorpusTests`, cases in `tools/reference/chromium/computed-style`). |
| No native code | `UnitTests.Extensions.Documents.Architecture` reads every compiled library: no P/Invoke, no module references, IL only, no unsafe code; the samples' publish folder holds IL-only assemblies only. |

What a PDF/UA document needs beyond these checks (reading order, meaningful alternative texts) is for a person to
check; `docs/pdf-profiles-and-accessibility.md` lists it.

## Layers

Each layer is its own package and depends only on the layers below it. The dependencies are checked on the compiled
assemblies by `UnitTests.Extensions.Documents.Architecture`.

```
 Ave.Extensions.Documents                composition: ToPages runs front-end and layout; ToPdf, ToPng draw the pages
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
 Ave.Extensions.Documents.PageModel      page model: per page a display list (glyph runs, rectangles, paths, images,
                                         structure markers)
        │  render
        ▼
 Ave.Extensions.Documents.Render.Pdf     page model → PDF: subset composite fonts, ToUnicode, image XObjects, content
                                         streams, conformance profiles: tagging, XMP, output intent, attachments,
                                         refusals
        │
        ▼
 PDF bytes

 Ave.Extensions.Documents.Render.Raster  page model → pixels (beside Render.Pdf): glyph outlines, rectangles, paths, images
                                         ← uses Graphics

 Ave.Extensions.Documents.Typesetting    white-space collapsing, Unicode line breaking (UAX #14), OpenType shaping,
                                         justification
 Ave.Extensions.Documents.Pdf            PDF objects, content operators, file writer, XMP writer, sRGB ICC profile,
                                         PdfProfile (knows nothing about layout)
 Ave.Extensions.Documents.Graphics       paths, affine transforms, exact-area anti-aliased filling, RGBA canvas, images
 Ave.Extensions.Documents.Fonts          TrueType reader (incl. GSUB, GPOS, GDEF, kern, glyph outlines) and subsetter
 Ave.Extensions.Documents.Images         PNG decoder (8-bit grey/RGB/alpha, palette) and encoder; JPEG header reader and
                                         decoder (baseline and progressive)
 Ave.Extensions.Documents.Core           lengths, geometry, colour, value lists, source locations, roles, metadata,
                                         attachments, error codes, the ASCII rules and encoding labels of the WHATWG
                                         standards
```

The HTML front-end (M2) is not in that row yet: it stops at computed style, and a later stage translates it to the
document model as the code front-end does. Its three packages:

```
 Ave.Extensions.Documents.Html           HTML front-end: a parsed document and its style sheets to the computed
        │                                style of every element (user-agent sheet, presentational hints, style
        │                                attributes)
        ├───────────────────────────────────────┐
        ▼                                       ▼
 Ave.Extensions.Documents.Html.Parsing   Ave.Extensions.Documents.Css
   the HTML Standard's tokenizer and       CSS Syntax 3, selectors, media queries, @supports, the cascade
   tree construction, byte decoding        with layers, nesting and custom properties, the property registry
        │                                       │
        └───────────────────┬───────────────────┘
                            ▼
 Ave.Extensions.Documents.Core
```

`Css` and `Html.Parsing` depend on Core only, and neither knows the other; `Html` joins them.

Where the contracts are:

- Layout contract: `Ave.Extensions.Documents.Layout/LayoutAlgorithm.cs` (the layout delegate and its rules, and the
  intrinsic-widths delegate), `ConstraintSpace.cs`, `BreakToken.cs`, `LayoutResult.cs` (with the break appeal),
  `Fragment.cs`, `LayoutEnvironment.cs` (algorithms by id, the current page).
- Tracks algorithm: `Ave.Extensions.Documents.Layout/Tracks/` (`TracksLayout.cs` lists the steps).
- Document model: `Ave.Extensions.Documents.DocumentModel/Node.cs`, `BoxStyle.cs`, `TextStyle.cs`, `LayoutAlgorithmId.cs`.
- Page model: `Ave.Extensions.Documents.PageModel/PageDocument.cs`, `DrawItem.cs` (with structure and artifact markers).
- Conformance: `Ave.Extensions.Documents.Pdf/PdfProfile.cs`, `Ave.Extensions.Documents.Render.Pdf/ConformanceRules.cs`,
  `Ave.Extensions.Documents.Render.Pdf/Tagging/`.
- HTML and CSS: `Ave.Extensions.Documents.Html.Parsing/HtmlParser.cs`, `Ave.Extensions.Documents.Css/CssParser.cs`,
  `Ave.Extensions.Documents.Css/Properties/PropertyRegistry.cs` (every property, its initial value, inheritance and
  what is supported), `Ave.Extensions.Documents.Html/HtmlStyleResolver.cs`.
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

The samples app writes `sample-a-letter.pdf` (two pages), `sample-b-long.pdf` (five pages), `sample-c-sliced.pdf`
(an image and a line taller than a small page, sliced over pages, and an image sliced in a table under its repeated header row) and the timesheet for three
synthetic data sets in Dutch and English (`timesheet-{normal,long,edge}.{nl,en}.pdf`), all as PDF/A-3a + PDF/UA-1,
each with `.model.txt`, `.fragments.txt` and `.page.txt`: the three text serialisations of its intermediate models.
The timesheets embed `timesheet.xml`, also written next to them as `.attachment.timesheet.xml`.
Then the HTML samples: `html-timesheet-{normal,long,edge}.nl` and `html-invoice`, each as the filled `.html`, the
`.pdf` (PDF/A-3a + PDF/UA-1, no attachment) and `.page.txt` (see *The timesheet from HTML*).
`dotnet run --project samples/Ave.Extensions.Documents.Samples -- time long nl-NL 20` measures one timesheet (cold,
then the median of 20 warm renders). The samples app needs culture data for the timesheet's number formats; the
libraries do not.

Tests that need Docker (veraPDF, qpdf and the other reference tools in pinned images) carry the trait
`Category=Reference`. Without Docker, or where Docker runs Windows containers, they are skipped with a message that
says so; `dotnet test --filter Category!=Reference` leaves them out. On Linux and macOS the containers run as the
current user.

The JPEG conformance tests (`JpegConformanceTests`) download the ITU-T T.83 data, which may not be redistributed, and
carry the trait `Category=Network`, the repository's trait for tests that reach a host outside. They fetch it only when
they run (not when tests are listed or filtered out), check its SHA-256, and keep the zip in the temporary folder for the
next run, checked again each time. When it cannot be fetched they fail on CI (`GITHUB_ACTIONS` or `CI` is `true`) and
are skipped with the reason elsewhere; `dotnet test --filter Category!=Network` leaves them out.

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

Known limits, each documented next to the code: negative margins (reported as unsupported), `vertical-align: baseline` in cells, column gaps and column styles, the separated
border model, diagonal border joins, keep rules that would move a break to a cut inside a row of an earlier table, interlaced
and 16-bit PNG, CMYK and arithmetic-coded JPEG, contextual and other OpenType
lookup types (skipped), complex scripts, bidi, hyphenation, CFF fonts; in preview images: no hinting (small text is lighter than in viewers that hint), two rectangles that meet
inside a pixel let some of the page show through (a quarter at half a pixel, as in MuPDF), and an enlarged image shows
its sample squares, as the PDF defines it, where some viewers smooth it. At most font sizes Chromium's text is narrower
than the font's advances, by a share that depends on the size (0.039 % at 10 pt, 0.071 % at 11 pt, more at small
sizes; none at 9, 10.5 and 12 pt), so a line that fits in Chromium by less than that share of its text width can
break one word earlier in the engine, which keeps the font's exact advances (`tools/reference/chromium/README.md`,
"Where Chromium is not exact"). Tagging limits (row headers from HTML only, no Headers/IDs, a Caption only for a table; links to a URI and inside the document, from the code front-end and from HTML's `a href`; a language per element only where it is a well-formed tag) and
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
