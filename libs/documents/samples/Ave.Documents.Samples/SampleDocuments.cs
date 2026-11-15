using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using static Ave.Documents.Fluent.Elements;

// Not under the Ave.Documents namespace on purpose: inside it, the name Text would resolve to the namespace
// Ave.Documents.Text before the imported Elements.Text function.
namespace Samples;

/// <summary>
/// The stage-0 sample documents, written through the code front-end. Components are plain methods that
/// return elements; data comes in as plain C# values.
/// </summary>
public static class SampleDocuments
{
    private static readonly Color Navy = new(0x1F, 0x4E, 0x79);
    private static readonly Color PaleBlue = new(0xDD, 0xEB, 0xF7);
    private static readonly Color Grey = new(0x59, 0x59, 0x59);

    private static readonly PageSetup Page = PageSetup.A4 with { Margins = Edges.All(2.Cm()) };
    private static readonly TextFormat BodyText = new(FontFamily: "Lato", FontSize: 11.Pt());

    /// <summary>Space below each paragraph: the stage-0 way to separate paragraphs (no margin collapsing yet).</summary>
    private static readonly BoxFormat ParagraphSpacing = new(Margin: Edges.OnlyBottom(8.Pt()));

    /// <summary>
    /// Sample A: a two-page letter with a heading, paragraphs in regular and bold, and a coloured block.
    /// The second page starts with an explicit page break.
    /// </summary>
    public static Document Letter() =>
        Document(
            [
                Paragraph("Versteeg IT Solutions · Example Street 1 · 1234 AB Example Town",
                    new BoxFormat(Margin: Edges.OnlyBottom(24.Pt())), new TextFormat(FontSize: 9.Pt(), Color: Grey)),
                Heading(1, "Quotation for document rendering", new BoxFormat(Margin: Edges.OnlyBottom(12.Pt())), new TextFormat(Color: Navy)),
                Para("Dear reader,"),
                Para([
                    Text("Thank you for your interest in our document engine. This letter was written in C# through the "),
                    Bold("code front-end"),
                    Text(", laid out by the engine's own layout algorithms, and written to PDF by its own PDF writer. "
                        + "No native library and no external tool took part in making it."),
                ]),
                Para("Every paragraph you see is broken into lines at spaces with the greedy method that browsers use: "
                    + "each line takes as many words as fit, and the next word starts a new line. The widths come straight "
                    + "from the font's horizontal metrics, and the text stays selectable and searchable because the PDF "
                    + "carries a map from every glyph back to its characters."),
                Summary(
                [
                    ("Layers", "code front-end, document model, layout, page model, PDF writer"),
                    ("Fonts", "TrueType, embedded whole, explicit input only"),
                    ("Output", "the same bytes for the same input, every time"),
                ]),
                Para([
                    Text("The block above has a "),
                    Bold("coloured background"),
                    Text(" and padding on all four sides. Blocks can hold paragraphs and other blocks."),
                ]),
                PageBreak(),
                Heading(2, "Terms", new BoxFormat(Margin: Edges.OnlyBottom(10.Pt())), new TextFormat(Color: Navy)),
                Para("This second page starts with an explicit page break. A page break is not an element of its own in "
                    + "the document model: it sets break-before: page on the element that follows it, as in CSS."),
                Para([Bold("Validity. "), Text("This quotation is an example and is valid for no time at all.")]),
                Para([Text("Kind regards,")]),
                Paragraph("The document engine", format: new TextFormat(FontWeight: FontWeight.Bold)),
            ],
            BodyText,
            Page,
            title: "Quotation for document rendering",
            author: "Ave.Documents samples");

    /// <summary>Sample B: a long text that runs over several pages, with paragraphs split between pages.</summary>
    /// <param name="paragraphs">How many paragraphs to generate.</param>
    public static Document LongText(int paragraphs = 40) =>
        Document(
            [
                Heading(1, "A long text", new BoxFormat(Margin: Edges.OnlyBottom(12.Pt()))),
                .. Enumerable.Range(1, paragraphs).Select(number => (Element)Numbered(number)),
            ],
            BodyText,
            Page,
            title: "A long text");

    private static ParagraphElement Para(string text) => Paragraph(text, ParagraphSpacing);

    private static ParagraphElement Para(ImmutableArray<Span> spans) => Paragraph(spans, ParagraphSpacing);

    /// <summary>A component: a pale-blue block with one bold label and a description per line.</summary>
    private static BlockElement Summary(ImmutableArray<(string Label, string Description)> rows) =>
        Block(
            [.. rows.Select(row => (Element)Paragraph([Bold(row.Label + ": "), Text(row.Description)], new BoxFormat(Margin: Edges.OnlyBottom(3.Pt()))))],
            new BoxFormat(Margin: Edges.OnlyBottom(12.Pt()), Padding: Edges.All(4.Mm()), Background: PaleBlue));

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
}
