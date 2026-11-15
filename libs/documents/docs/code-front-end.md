# The code front-end

`Ave.Documents.Fluent` lets you write a document as C#: a document is an immutable value built from plain
function calls, shaped with modifiers, and rendered to PDF. Nothing is drawn, measured or validated while you
build it; `DocumentTranslator.ToModel` (or `ToPdf`) checks everything at once and reports every problem with the
file and line of the element that caused it.

## A complete example

```csharp
using System;
using System.IO;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.Fonts;
using Ave.Documents.Render.Pdf;
using static Ave.Documents.Fluent.Elements;

// Reusable styles are values, built with the same modifiers as elements.
var navy = new Color(0x1F, 0x4E, 0x79);
var body = Style.Empty.MarginTop(6.Pt()).MarginBottom(6.Pt()).TextAlign(TextAlign.Justify);
var note = Style.Empty.Padding(4.Mm()).Background(new Color(0xDD, 0xEB, 0xF7)).FontSize(9.Pt());

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
    author: "Example Ltd")
    .FontFamily("Lato").FontSize(11.Pt());

var fonts = new FontSet([
    FontParser.Parse(File.ReadAllBytes("Lato-Regular.ttf")).Value,
    FontParser.Parse(File.ReadAllBytes("Lato-Bold.ttf")).Value,
]);
var pdf = invoice.ToPdf(fonts, new PdfRenderOptions(CreationDate: new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero)));
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

## Elements

| Function | Makes |
| --- | --- |
| `Document(body, page:, title:, author:)` | the document; `page` defaults to A4 without margins |
| `Block(children)` | a box that stacks its children vertically |
| `Paragraph(text)`, `Paragraph([spans])` | a paragraph of one or several spans |
| `Heading(level, text)` | a heading, level 1–6: bold and sized like HTML's h1–h6 |
| `Text(text)`, `Bold(text)` | a span inside a paragraph (`Bold(text)` is `Text(text).Bold()`) |
| `PageBreak()` | starts the next element of the same list on a new page |

Children are collection expressions: `[a, b, .. items.Select(Row)]`.

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
| `.With(Style)` | blocks, paragraphs, spans | applies a reusable style |

Lengths are typed: `12.Pt()`, `4.Mm()`, `2.Cm()`.

Precedence is a fixed rule, never the order of the calls:

1. an element's own modifier values win over its style (`.With`), which wins over what the parent passes down
   (the text properties and `TextAlign` are inherited, like in CSS);
2. within one of those, a single side (`MarginTop`) wins over the all-sides value (`Margin`).

Text properties are inherited by children; box properties (margins, padding, background) are not. Adjoining
vertical margins collapse as in CSS (CSS 2.1 §8.3.1): between two paragraphs with 6 pt margins the gap is 6 pt.

A span has no box and no lines: a style with box properties or a text alignment applied to a span is reported
as `Documents.Input.Invalid.NotApplicable`.

## Values you can compare

Documents are values. Two documents built the same way are equal (`==`), so a component can be unit-tested
without rendering anything; the same holds for the document model, the fragment tree and the page model.
