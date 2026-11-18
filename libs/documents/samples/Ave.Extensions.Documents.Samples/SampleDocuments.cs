using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// The sample documents, written through the code front-end. Components are plain methods that return elements;
/// data comes in as plain C# values; formatting is set with modifiers and reusable styles.
/// </summary>
public static class SampleDocuments
{
    private static readonly Color Navy = new(0x1F, 0x4E, 0x79);
    private static readonly Color PaleBlue = new(0xDD, 0xEB, 0xF7);
    private static readonly Color Grey = new(0x59, 0x59, 0x59);

    private static readonly PageSetup Page = PageSetup.A4 with { Margins = Edges.All(2.Cm()) };

    /// <summary>Body paragraphs: justified, with a margin above and below. Between two paragraphs the two margins
    /// collapse into one 6 pt gap (CSS 2.1 §8.3.1); next to a heading the larger margin wins.</summary>
    private static readonly Style BodyParagraph = Style.Empty.MarginTop(6.Pt()).MarginBottom(6.Pt()).TextAlign(TextAlign.Justify);

    /// <summary>
    /// Sample A: a two-page letter with a heading, justified paragraphs in regular and bold, kerning and ligatures,
    /// a right-aligned date, a centred heading and a coloured block. The second page starts with an explicit page
    /// break.
    /// </summary>
    /// <param name="closing">The closing line; a parameter so tests can change one leaf of the tree.</param>
    public static Document Letter(string closing = "Kind regards,") =>
        Document(
            [
                Paragraph("Versteeg IT Solutions · Example Street 1 · 1234 AB Example Town")
                    .MarginBottom(6.Pt()).FontSize(9.Pt()).Color(Grey),
                Paragraph("Example Town, 15 November 2026").TextAlign(TextAlign.Right).MarginBottom(18.Pt()).FontSize(9.Pt()).Color(Grey),
                Heading(1, "Quotation for document rendering").MarginTop(6.Pt()).MarginBottom(12.Pt()).Color(Navy),
                Para("Dear reader,"),
                Para([
                    Text("Thank you for your interest in our document engine. This letter was written in C# through the "),
                    Bold("code front-end"),
                    Text(", laid out by the engine's own layout algorithms, and written to PDF by its own PDF writer. "
                        + "No native library and no external tool took part in making it."),
                ]),
                Para("Every paragraph you see is broken into lines where the Unicode line breaking algorithm allows it, "
                    + "with the greedy method that browsers use: each line takes as many words as fit, and the next word starts "
                    + "a new line. The lines are then justified by widening the spaces between words, and the text stays "
                    + "selectable and searchable because the PDF carries a map from every glyph back to its characters."),
                Para([
                    Text("Kerning and ligatures come from the font's own OpenType tables. In "),
                    Bold("AVATAR"),
                    Text(", WAVE and Tyrannosaurus the letters move closer together; in office, fifty, affine and flag the "),
                    Bold("fi"),
                    Text(", ff and fl become single ligature glyphs, which still copy as two or three letters."),
                ]),
                Summary(
                [
                    ("Layers", "code front-end, document model, layout, page model, PDF writer"),
                    ("Text", "Unicode line breaking, OpenType kerning, ligatures and marks"),
                    ("Fonts", "TrueType, only the used glyphs embedded, explicit input only"),
                    ("Output", "the same bytes for the same input, every time"),
                ]),
                Para([
                    Text("The block above has a "),
                    Bold("coloured background"),
                    Text(" and padding on all four sides. Blocks can hold paragraphs and other blocks."),
                ]),
                PageBreak(),
                Heading(2, "Terms").TextAlign(TextAlign.Center).MarginTop(18.Pt()).MarginBottom(10.Pt()).Color(Navy),
                Para("This second page starts with an explicit page break. A page break is not an element of its own in "
                    + "the document model: it sets break-before: page on the element that follows it, as in CSS."),
                Para([Bold("Validity. "), Text("This quotation is an example and is valid for no time at all.")]),
                Para([Text(closing)]),
                Paragraph("The document engine").Bold(),
            ],
            Page,
            title: "Quotation for document rendering",
            author: "Ave.Extensions.Documents samples",
            language: "en")
            .FontFamily("Lato").FontSize(11.Pt());

    /// <summary>Sample B: a long text that runs over several pages, with paragraphs split between pages.</summary>
    /// <param name="paragraphs">How many paragraphs to generate.</param>
    public static Document LongText(int paragraphs = 40) =>
        Document(
            [
                Heading(1, "A long text").MarginBottom(12.Pt()),
                .. Enumerable.Range(1, paragraphs).Select(number => (Element)Numbered(number)),
            ],
            Page,
            title: "A long text",
            language: "en")
            .FontFamily("Lato").FontSize(11.Pt());

    private static ParagraphElement Para(string text) => Paragraph(text).With(BodyParagraph);

    private static ParagraphElement Para(ValueList<Span> spans) => Paragraph(spans).With(BodyParagraph);

    /// <summary>A component: a pale-blue block with one bold label and a description per line.</summary>
    private static BlockElement Summary(ImmutableArray<(string Label, string Description)> rows) =>
        Block(
            [.. rows.Select(row => (Element)Paragraph([Bold(row.Label + ": "), Text(row.Description)]).MarginTop(3.Pt()).MarginBottom(3.Pt()))])
            .MarginTop(12.Pt()).MarginBottom(12.Pt()).Padding(4.Mm()).Background(PaleBlue);

    private static ParagraphElement Numbered(int number) =>
        Para([
            Bold($"Paragraph {number}. "),
            Text(string.Join(" ", Enumerable.Range(0, 3 + number % 4).Select(sentence => Sentences[(number + sentence) % Sentences.Length]))),
        ]);

    private static readonly string[] Sentences =
    [
        "A page break may fall between any two lines of a paragraph, so a long paragraph continues on the next page.",
        "The layout of each page starts from the break token that the previous page left behind.",
        "Break tokens are plain values, which keeps every layout function free of hidden state.",
        "Words are measured with the advance widths from the font's hmtx table, scaled to the font size.",
        "Spaces at the start and at the end of a line are removed, as CSS prescribes for normal white space.",
        "Each fragment remembers the node it came from, and each node remembers the line of code that created it.",
        "When something cannot be laid out, the error names that line instead of showing a stack trace.",
        "The PDF writer numbers its objects in a fixed order, so two runs produce identical files.",
    ];

    /// <summary>
    /// Sample C: content taller than the page, sliced over pages as a browser prints it
    /// (<see cref="DocumentModel.MonolithicOverflow.Slice"/>). On a small page (300 × 204 pt, a page area of 276 × 180 pt)
    /// a 250 pt logo and a line of 200 pt type each continue on the next page; the text and the logo's alternative text
    /// are in the document once.
    /// </summary>
    /// <param name="logo">The logo, a PNG or JPEG file.</param>
    public static Document Sliced(ImmutableArray<byte> logo) =>
        Document(
            [
                Heading(1, "Taller than a page").MarginBottom(6.Pt()),
                Paragraph("The logo below is taller than the page area. It starts on the next page and is sliced over two pages."),
                Image(logo.AsSpan()).Alt("A white letter V on an orange square").Width(250.Pt()),
                Paragraph("The line below is set in 200 point type, also taller than the page area."),
                Paragraph("W").FontSize(200.Pt()),
                Paragraph("The end."),
            ],
            new PageSetup(300.Pt(), 204.Pt(), Edges.All(12.Pt())),
            title: "Taller than a page",
            language: "en")
            .FontFamily("Lato").FontSize(11.Pt()).MonolithicOverflow(DocumentModel.MonolithicOverflow.Slice);
}
