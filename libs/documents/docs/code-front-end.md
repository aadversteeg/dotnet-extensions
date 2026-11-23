# The code front-end

`Ave.Extensions.Documents.Fluent` lets you write a document as C#: a document is an immutable value built from plain
function calls and shaped with modifiers. Nothing is drawn, measured or validated while you build it;
`DocumentTranslator.ToModel` checks everything at once and reports every problem with the file and line of the
element that caused it. `ToPdf` (package `Ave.Extensions.Documents`, namespace `Ave.Extensions.Documents`) runs the whole pipeline: it
translates, lays out and writes the PDF. The front-end itself knows neither the layout nor any back-end.

## A complete example

```csharp
using System;
using System.IO;
using System.Linq;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using static Ave.Extensions.Documents.Fluent.Elements;

// Reusable styles are values, built with the same modifiers as elements.
var navy = new Color(0x1F, 0x4E, 0x79);
var body = Style.Empty.MarginTop(6.Pt()).MarginBottom(6.Pt()).TextAlign(TextAlign.Justify);
var note = Style.Empty.Padding(4.Mm()).Background(new Color(0xDD, 0xEB, 0xF7)).FontSize(9.Pt());

// The invoice's data in machine-readable form, embedded in the PDF.
var invoiceXml = File.ReadAllBytes("invoice.xml");

// A component is a plain method that returns an element.
ParagraphElement Line(string label, decimal amount) =>
    Paragraph([Bold(label + ": "), Text($"{amount:0.00} EUR")]).With(body);

var invoice = Document(
    [
        Paragraph("Example Town, 15 November 2026").TextAlign(TextAlign.Right).FontSize(9.Pt()),
        Heading(1, "Invoice 2026-042").MarginBottom(12.Pt()).Color(navy),
        Paragraph("Thank you for your order. The amounts below are due within thirty days.").With(body),
        .. new[] { ("Design", 1200m), ("Development", 4800m) }.Select(item => (Element)Line(item.Item1, item.Item2)),
        Block([Paragraph("Paid invoices are not refunded.")]).With(note),
        PageBreak(),
        Heading(2, "Terms").TextAlign(TextAlign.Center),
        Paragraph("Delivery and payment terms follow on this page.").With(body),
    ],
    page: PageSetup.A4 with { Margins = Edges.All(2.Cm()) },
    title: "Invoice 2026-042",
    author: "Example Ltd",
    language: "en")
    .FontFamily("Lato").FontSize(11.Pt())
    .Attach(Attachment("invoice.xml", invoiceXml, "application/xml").WithRelationship(AttachmentRelationship.Data));

var fonts = new FontSet([
    FontParser.Parse(File.ReadAllBytes("Lato-Regular.ttf")).Value,
    FontParser.Parse(File.ReadAllBytes("Lato-Bold.ttf")).Value,
]);
// PDF/A-3a + PDF/UA-1: archivable and accessible (see pdf-profiles-and-accessibility.md).
var options = new PdfRenderOptions(CreationDate: new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero))
{
    Profile = PdfProfile.PdfA3a.AndPdfUa1(),
};
var pdf = invoice.ToPdf(fonts, options);
if (pdf.IsSuccess)
{
    File.WriteAllBytes("invoice.pdf", [.. pdf.Value]);
}
else
{
    Console.Error.WriteLine(pdf.Error);
}
```

Every step returns `Result<T, Error>`; an error carries a path code such as `Documents.Input.Invalid.FontSize`
or `Documents.Font.GlyphMissing` and the source file and line of the element.

## Pages, PDF and preview images

`ToPdf` is two steps in one: `ToPages` translates the document and lays it out into pages (the page model), and the PDF
back-end writes the pages. A caller that wants a preview and the PDF takes the two steps itself, so the document is laid
out once:

```csharp
using Ave.Extensions.Documents.Render.Raster;   // RasterOptions

var pages = invoice.ToPages(fonts);
if (pages.IsSuccess)
{
    var pdf = pages.Value.ToPdf(options);                                     // the same bytes as invoice.ToPdf(fonts, options)
    var preview = pages.Value.ToPng(1, new RasterOptions(96));                // page 1 as a PNG file
    var all = pages.Value.ToPngPages(new RasterOptions(150) { Background = null });   // every page, transparent paper
    var pixels = pages.Value.ToImages(new RasterOptions(72));                 // every page as 8-bit RGBA pixels
}
```

| Call | On a document | On its pages | Gives |
| --- | --- | --- | --- |
| `ToPages` | `document.ToPages(fonts)` | — | the pages (`PageDocument`) |
| `ToPdf` | `document.ToPdf(fonts, options)` | `pages.ToPdf(options)` | the PDF file |
| `ToPng` | `document.ToPng(fonts, pageNumber, options)` | `pages.ToPng(pageNumber, options)` | one page as a PNG file; pages count from 1 |
| `ToPngPages` | `document.ToPngPages(fonts, options)` | `pages.ToPngPages(options)` | every page as a PNG file |
| `ToImages` | `document.ToImages(fonts, options)` | `pages.ToImages(options)` | every page as pixels (`RgbaImage`) |

`RasterOptions(Resolution)` gives the pixels per inch (96 by default) and `Background` the paper colour (white by
default, `null` for transparent). A page number the document does not have is `Documents.Raster.PageNotFound`.

## Elements

| Function | Makes |
| --- | --- |
| `Document(body, page:, title:, author:, language:)` | the document; `page` defaults to A4 without margins; `language` is a tag such as `nl-NL` |
| `Block(children)` | a box that stacks its children vertically |
| `Paragraph(text)`, `Paragraph([spans])` | a paragraph of one or several spans |
| `Heading(level, text)` | a heading, level 1–6: bold and sized like HTML's h1–h6 |
| `Text(text)`, `Bold(text)` | a span inside a paragraph (`Bold(text)` is `Text(text).Bold()`) |
| `LineBreak()` | a forced line break inside a paragraph (as HTML's `br`; U+2028), used as a span: `Paragraph([Text("a"), LineBreak(), Text("b")])` |
| `PageBreak()` | starts the next element of the same list on a new page |
| `Table(columns, rows)` | a table; `columns` made with `Columns(…)`, `rows` a list of the three below |
| `Columns(2.5.Cm(), Fill(1), Auto)` | the column sizes: a length is fixed, `Fill(weight)` takes a share of the width left over (CSS Grid `fr`), `Auto` is as wide as the content needs |
| `HeaderRow([cells])` | a header row: repeated at the top of every page the table continues on, when the header rows are at most a quarter of the page area (as Chromium) |
| `TableRow([cells])` | a body row; it may break between the lines of its cells unless kept together |
| `RowGroup([rows])` | rows that belong together (a row span ends at the end of its group) |
| `Cell(text)`, `Cell([elements])` | a table cell |
| `Row([elements])` | elements side by side, one column each (equal shares unless `.Columns(…)` is set) |
| `BulletedList([items])` | a list with a bullet beside each item's first line: a disc, in a list inside another list a circle, deeper a square; `.Bullet(ListBullet.Square)` sets one, `ListBullet.None` gives no label. Indented 30 pt (a left padding) unless a padding is set |
| `NumberedList([items])` | a list numbered "1. ", "2. " …, each number right-aligned to its item's start; `.Start(5)` sets the first number (zero and negative numbers too) |
| `ListItem([elements])`, `ListItem(text)` | an item of a list; its content may hold any elements, a list included, which nests one level deeper. Its font, size and colour are its label's |
| `Image(bytes)` | a PNG or JPEG image from the bytes of the file; `.Alt("what it shows")` or `.Decorative()` |
| `Attachment(name, bytes, mediaType)` | a file to embed with `document.Attach(…)`; `.WithRelationship(…)`, `.WithDescription(…)`, `.WithModificationDate(…)` |
| `PageNumber()`, `PageCount()` | spans that show the page number and the number of pages |

Children are collection expressions: `[a, b, .. items.Select(Line)]`.

A list is items, each of any elements; a list in an item nests:

```csharp
NumberedList(
[
    ListItem("Unpack the box."),
    ListItem([Paragraph("Connect the cables:"), BulletedList([ListItem("power"), ListItem("network")])]),
    ListItem("Switch it on."),
]).Start(1)
```

The labels are placed as Chromium places a list's outside markers (CSS Lists 3 §3): beside the first line of the item,
on its baseline, in the item's font. A bullet is a shape whose size and place follow the font's ascent (at 10.5 pt Lato
a disc of 3.75 pt, 12 pt left of the item); a number is right-aligned so that "1. ", its space included, ends where
the item's content starts. A label that would start left of the sheet is `Documents.Layout.MarkerOutsideSheet`: give the
list more left padding. A list has 1 to 10 000 items; lists nest at most 16 deep (`Documents.Input.Invalid.List`).

The page is a value too: `PageSetup.A4.Margin(1.Cm()).Header(header).Footer(footer)`; `Page(width, height)` makes
another size. Header and footer take their height from the page area (from their content, or a fixed height as the
second argument); `.FirstPageHeader(…)` and `.FirstPageFooter(…)` replace them on page 1 (`Block([])` for none).

## Modifiers

Each modifier returns a NEW value with ONE property set. Modifiers are order-free: modifiers for different
properties give the same element in any order, and setting the same property twice keeps the last value.

| Modifier | On | CSS property |
| --- | --- | --- |
| `.FontFamily(string)` | every element, `Style` | `font-family` (one family; must be in the font set) |
| `.FontSize(Length)` | every element, `Style` | `font-size` |
| `.FontWeight(FontWeight)`, `.Bold()` | every element, `Style` | `font-weight` |
| `.Color(Color)` | every element, `Style` | `color` |
| `.TextAlign(TextAlign)` | blocks, paragraphs, `Style` | `text-align`: `Left`, `Right`, `Center`, `Justify` |
| `.Margin(Length)` | blocks, paragraphs, `Style` | `margin` on all four sides |
| `.MarginTop/Right/Bottom/Left(Length)` | blocks, paragraphs, `Style` | `margin-top` … |
| `.Padding(Length)` | blocks, paragraphs, `Style` | `padding` on all four sides |
| `.PaddingTop/Right/Bottom/Left(Length)` | blocks, paragraphs, `Style` | `padding-top` … |
| `.Background(Color)` | blocks, paragraphs, `Style` | `background-color` |
| `.With(Style)` | every element, spans | applies a reusable style |
| `.FontStyle(FontStyle)`, `.Italic()` | every element, `Style` | `font-style` (an italic face must be in the font set) |
| `.Border(Length, Color)` | elements with a box, `Style` | `border` on all four sides, solid |
| `.BorderTop/Right/Bottom/Left(Length, Color)` | elements with a box, `Style` | `border-top` … |
| `.KeepTogether()` | elements with a box, rows, row groups | `break-inside: avoid` |
| `.KeepWithNext()`, `.KeepWithPrevious()` | elements with a box, rows | `break-after: avoid`, `break-before: avoid` |
| `.LineHeight(double)`, `.LineHeight(Length)`, `.LineHeightPercent(double)`, `.LineHeightNormal()` | every element and `Style` | `line-height` (inherited, initial `normal`): a number (a multiple of the font size, inherited as the number), a length, a percentage of the element's font size (inherited as the length), or the font's own spacing; on a paragraph also the height of its strut, so no line is lower |
| `.Orphans(int)`, `.Widows(int)` | the document, elements with a box (blocks, paragraphs, images, tables, table rows, row groups, cells, rows of blocks), `Style` | `orphans`, `widows` (inherited, initial 2, at least 1): the fewest lines of a paragraph a page keeps before a break inside it, and after it; on the document they reach every paragraph (`Document.Orphans(1).Widows(1)` gives the pages of 0.1.0-preview.2 back) |
| `.GridLines(Length, Color)` | tables | a border on every cell that sets none (one shared line between cells) |
| `.CellPadding(Length)`, `.CellPadding(vertical, horizontal)` | tables | padding of every cell that sets none |
| `.RepeatLinesAtBreaks()` (the default) | tables | at a page break between rows the page ends with the whole grid line and the next page starts with it again; a row that is cut inside itself ends the page with the whole line below it and goes on under the whole line above it, so the table is closed on both pages, as word processors print tables; modelled on `box-decoration-break: clone` |
| `.SplitLinesAtBreaks()` | tables | at a page break between rows the grid line is split along its centre, half on each page; a row cut inside itself gets no line at the cut (`box-decoration-break: slice`). Chromium splits a line of several pixels the same way; a one-pixel line it draws whole on both pages, so its next page starts 0.375 pt lower |
| `.ColumnSpan(n)`, `.RowSpan(n)` | cells | `colspan`, `rowspan` |
| `.VerticalAlign(VerticalAlign)` | cells, rows of blocks | `vertical-align`: `Top` (default), `Middle`, `Bottom` |
| `.Columns(sizes…)` | rows of blocks | one size per item |
| `.Width(Length)`, `.Height(Length)` | images | the size; one alone keeps the aspect ratio, both fit the image inside them |

"Elements with a box" are blocks, paragraphs, tables, rows of blocks, cells and images. A table has no padding (the
collapsing border model has none) and rows and row groups have no margin or padding: setting them is reported as
`Documents.Input.Invalid.NotApplicable`. A page break is not allowed inside a cell or a row of blocks.

Lengths are typed: `12.Pt()`, `4.Mm()`, `2.Cm()`. No length may be longer than 14 400 pt (200 inches, the largest
page side PDF names; `InputLimits.MaxLength`): a page side, a margin, a padding, a border, a font size, a column width,
an image size or a header or footer height above it is reported with that property's code
(`Documents.Input.Invalid.Length`, `.PageSize`, `.FontSize`, `.Columns` or `.ImageSize`), in every profile.

Precedence is a fixed rule, never the order of the calls:

1. an element's own modifier values win over its style (`.With`), which wins over what the parent passes down
   (the text properties, `LineHeight`, `TextAlign`, `Orphans` and `Widows` are inherited, like in CSS);
2. within one of those, a single side (`MarginTop`) wins over the all-sides value (`Margin`).

Text properties are inherited by children; box properties (margins, padding, background) are not. Adjoining
vertical margins collapse as in CSS (CSS 2.1 §8.3.1): between two paragraphs with 6 pt margins the gap is 6 pt.

A span has no box and no lines: a style with box properties or a text alignment applied to a span is reported
as `Documents.Input.Invalid.NotApplicable`.

## Tables, page templates and page numbers

```csharp
var grey = new Color(0xD3, 0xD3, 0xD3);
var page = PageSetup.A4.Margin(1.Cm())
    .Header(Row([Image(logo).Width(2.5.Cm()), Paragraph("Timesheet").FontSize(24.Pt()).Bold()]).Columns(2.5.Cm(), Fill(1)))
    .Footer(Paragraph([Text("Page "), PageNumber(), Text(" of "), PageCount()]).TextAlign(TextAlign.Right));

var entries = Table(
        Columns(Fill(2.5), Fill(3), Fill(10), Fill(2)),
        [
            HeaderRow([Cell("Date"), Cell("Task"), Cell("Title"), Cell("Hours").TextAlign(TextAlign.Right)]).Bold(),
            .. items.Select(item => (TablePart)RowGroup(
                [
                    TableRow([Cell(item.Date).RowSpan(2), Cell(item.Task), Cell(item.Title), Cell(item.Hours).RowSpan(2)]),
                    TableRow([Cell(item.Description).ColumnSpan(2).Italic().FontSize(8.Pt())]),
                ]).KeepTogether()),
        ])
    .GridLines(1.Pt(), grey)
    .CellPadding(4.Pt(), 2.Pt());
```

The full timesheet is `samples/Ave.Extensions.Documents.Samples/Timesheet/Timesheet.cs`.

How tables lay out: fixed columns keep their width; `Preferred(width)` columns take their width as a browser takes a
`col` width (wider for a wider word, narrower down to their content when the table cannot give every such column its
width); auto columns get between their narrowest and widest content;
fill columns share what is left, never narrower than their narrowest content. Without a fill column a table is as
wide as its columns need; `FullWidth()` makes it as wide as the space it is in, as a browser lays out an HTML table
with `width: 100%`: the width left over goes to the auto columns in proportion to their widest content, or to the
fixed columns in proportion to their widths when no column is auto. Columns that cannot fit are the
error `Documents.Layout.ColumnsDoNotFit`. A row is as tall as its tallest cell. Between two cells there is one
grid line; where two borders meet, the wider one wins, then a cell's over a row's, then the upper or left one (CSS
2.1 §17.6.2.1). Half of a line lies inside each cell beside it, so a wider border moves only the content of its own
cell; the table's left and right borders are half of the widest line at that edge in any row, on every page, and its
top and bottom borders half of the widest line over its first and last row. Where lines meet, the corner goes to the line that wins there by the same order (then the cell earlier in the table), as Chromium paints it; the other lines stop at its side, so a thin line does not reach into a wider one. A page breaks between rows, or inside a row between the lines of its cells; kept-together rows and
groups move to the next page whole when they fit on one, and break anyway when they do not.

The page count is known only after layout, so layout runs again until the count it shows is the count it produces,
at most five times; otherwise the error is `Documents.Layout.NotConverging` at the `PageCount()` call.

## Layout policies

Where layout has a choice, the document says which one it takes (`Document.Profile`, a `LayoutProfile` of the
document model). Each policy has a document modifier; the defaults suit reports, the other values follow how a
browser (Chromium) prints, with the differences listed in `docs/architecture.md` (Known limits):

| Modifier | Default | Other value |
| --- | --- | --- |
| `.MonolithicOverflow(…)`: an image or a line taller than the page area | `Refuse`: the error `Documents.Layout.DoesNotFit` at the element's line | `Slice`: the content is sliced over as many pages as it needs, each page showing the next part of it, as tall as the room it has there; what follows starts below its end; in tagged PDF every slice of an image is content of its figure and the later slices of a line are artifacts, so text and alternative text are read once; a piece is cut into at most 100 slices and a document into at most 1 000, and one that needs more is the error `Documents.Layout.TooManySlices` |
| `.FontMetricRounding(…)`: a font's ascent, descent and line gap | `Exact`: as the font gives them, scaled to the size | `WholePixels`: each rounded to a whole CSS pixel (0.75 pt), as Chromium does, so a `normal` line is a whole number of pixels (Lato 12 pt: 14.25 pt instead of 14.4) |
| `.EmptyRowParts(…)`: a table row whose part on a page would show nothing | `Avoid`: the row moves to the next page whole where that avoids the empty strip | `Allow`: the row is cut wherever its cells can break |
| `.SplitLineHalves(…)`: each half of a table's grid line where a page break between rows splits it (`.SplitLinesAtBreaks()`) | `Exact`: half the line's width | `AtLeastOnePixel`: a half thinner than one CSS pixel is drawn one pixel (0.75 pt) thick from its top edge, as Chromium prints it; layout does not change |
| `.TableEndOverflow(…)`: a grid line that passes the page end below a table row (under `.EmptyRowParts(Allow)`, where that row is placed whole at the top of a page) | `EndsOnPage`: the table ends on the page; what follows starts at the top of the next page, and a table that ends the document adds no page | `ContinuesOnNextPage`: what passed continues on the next page and takes its height there, painted where part of a cell passed, as Chromium prints it; where the table ends the document, on a page of its own |
| `.BodyClip(…)`: what a page's body draws past its area (the page area less the header and footer areas) | `None`: it shows | `BodyArea`: the body is clipped to its area, as Chromium prints it; the pages and the text do not change |
| `.CellsBesideSlices(…)`: the other cells of a table row, on the pages that go on with the slices of a piece in one of its cells (under `.MonolithicOverflow(Slice)`) | `GoOn`: from the top of each page, beside the slice | `Below`: below the slice each page shows, as Chromium prints it; a page wholly covered by a slice shows nothing of them |
| `.CutRowAlignment(…)`: where the content of a cell aligned middle or bottom goes on the later pages of a table row that a page break cuts | `RestAlignedInItsPart`: aligned in the rest of its row | `TopOnEveryPage`: at the top of its part on every page, as Chromium prints it; rows that are not cut stay aligned |
| `.RepeatedHeaderRows(…)`: on which later pages a table's repeated header rows are drawn, where a piece in one of its cells is sliced (under `.MonolithicOverflow(Slice)`) | `AboveEverySlice`: above every slice, each slice as tall as the room below them; a piece no taller than the page area goes on without them | `WhereContentIsLaidOut`: where the table still lays content out, not on a page that only shows the rest of a piece, as Chromium prints it |
| `.SliceRestEnd(…)`: where the part of a table cell ends that holds nothing but the rest of a piece sliced on an earlier page (under `.MonolithicOverflow(Slice)`) | `InsideCell`: below its half of the grid line below it, as every cell; the line starts where the rest ends | `AtLineCentre`: at the rest's end, the line centred on it, as Chromium prints it |

```csharp
var poster = Document([Image(photo).Alt("The team").Width(18.Cm())], PageSetup.A4.Margin(2.Cm()), title: "Poster", language: "en")
    .FontFamily("Lato")
    .MonolithicOverflow(MonolithicOverflow.Slice);
```

Content in a table cell is sliced the same way when its row starts the page, also right below a repeated header row;
the row after it starts below the last slice (Chromium does so in some table shapes and draws that row over the slice
in others), and the other cells of its row go on beside the slices. A piece in a cell that fits the page area but not
with the cell's padding and grid lines is sliced too, where Chromium moves it whole. Padding and borders taller than a
page are refused under either value.

## PDF/A, PDF/UA and attachments

The standard a file is written to is a render option, `PdfRenderOptions.Profile`: `PdfProfile.Plain` (the default),
`PdfA3b`, `PdfA3a`, `PdfUa1`, or `PdfA3a.AndPdfUa1()` for both. A profile adds what its standard needs (metadata,
colour profile, tagged structure) and refuses what it forbids — a document without a title or language, an image
without `.Alt(…)` or `.Decorative()`, headings that skip a level, a table row with too few cells, an attachment
without a relationship — with an error at your line. `docs/pdf-profiles-and-accessibility.md` explains each rule
and what is left for you to check by hand.

## Values you can compare

Documents are values. Two documents built the same way are equal (`==`), so a component can be unit-tested
without rendering anything; the same holds for the document model, the fragment tree and the page model.
