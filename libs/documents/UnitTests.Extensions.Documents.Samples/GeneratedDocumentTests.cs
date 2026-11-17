using System.Collections.Immutable;
using System.IO;
using System.Text;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The promise behind every profile, tried on documents nobody wrote by hand: for any input the front-end accepts,
/// <c>ToPdf</c> either returns an error value or writes a file that passes veraPDF for the profile it claims. The
/// documents are drawn from a fixed seed, so a failure is reproducible; its message names the seed, the document's
/// number and what the document holds. The generator leans on the inputs that broke the promise before: hyphens and
/// soft hyphens, no-break spaces, control characters, Private Use characters, long titles, odd media types, page sizes
/// at and past the limits, headings that skip levels, tables with spans, header-only tables, images without
/// description; and on everything the layout draws: borders and backgrounds, column and row spans, tables nested in
/// cells, tables long enough to break across pages, page headers and footers with page numbers, images with alpha,
/// font sizes from 0.5 pt to past the limit, lengths at and past 14 400 pt, and words of thousands of letters. A
/// refusal counts only when its code is one of the expected families (<see cref="ExpectedRefusals"/>): a refusal from the
/// PDF back-end's own contract or from shaping would mean the front-end handed on something it should not have.
/// </summary>
[Trait("Category", "Reference")]
public sealed class GeneratedDocumentTests
{
    /// <summary>The seed of every run. Change it to explore; keep the one that found a fault in a regression test.</summary>
    public const int Seed = 20261115;

    /// <summary>How many documents per profile.</summary>
    public const int DocumentsPerProfile = 100;

    /// <summary>How many of them must be written, so the test is not passed by refusing everything.</summary>
    private const int MinimumWritten = 10;

    private static readonly DateTimeOffset Date = new(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1));

    private static readonly byte[] Logo = TestImages.Bytes("rgb8.png");

    /// <summary>Test images: with and without alpha, PNG and JPEG; the CMYK JPEG and the 16-bit PNG are refused as
    /// unsupported.</summary>
    private static readonly string[] Images =
    [
        "rgb8.png", "rgba8.png", "grey-alpha8.png", "palette2-trns.png", "rgb8-trns.png", "grey8.png", "baseline-rgb.jpg",
        "progressive-rgb.jpg", "baseline-grey.jpg", "cmyk.jpg", "grey16.png",
    ];

    /// <summary>The error codes a refusal may have: the front-end's input checks, a profile's refusals, a glyph the font
    /// lacks, content that cannot be laid out, an image the engine does not decode.</summary>
    private static readonly string[] ExpectedRefusals =
    [
        "Documents.Input.", "Documents.Pdf.Conformance.", "Documents.Font.GlyphMissing", "Documents.Layout.", "Documents.Image.Unsupported",
    ];

    public static TheoryData<string> Flavours => ["3b", "3a", "ua1"];

    private static PdfProfile ProfileFor(string flavour) => flavour switch
    {
        "3b" => PdfProfile.PdfA3b,
        "3a" => PdfProfile.PdfA3a,
        _ => PdfProfile.PdfUa1,
    };

    [DockerTheory]
    [MemberData(nameof(Flavours))]
    public void Every_generated_document_is_refused_or_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory($"ave-extensions-documents-generated-{flavour}-").FullName;
        try
        {
            // The same documents for every profile: string hash codes differ per process, so nothing is derived from the flavour.
            var random = new Random(Seed);
            var descriptions = new Dictionary<string, string>();
            var refused = new List<string>();
            for (var index = 0; index < DocumentsPerProfile; index++)
            {
                var (document, description) = Generate(random);
                Result<ImmutableArray<byte>, Error> result = default;
                var render = () => result = document.ToPdf(TestFonts.SetWithItalic, new PdfRenderOptions(Date) { Profile = ProfileFor(flavour) });

                render.Should().NotThrow($"seed {Seed}, flavour {flavour}, document {index}: {description}");
                if (result.IsFailure)
                {
                    var codes = result.Error.Parts().Select(part => part.Code.ToString()).Distinct().ToList();
                    codes.Should().OnlyContain(code => ExpectedRefusals.Any(family => code.StartsWith(family, StringComparison.Ordinal)),
                        $"seed {Seed}, flavour {flavour}, document {index} is refused only for a reason of the input or the profile: {description}; {result.Error}");
                    refused.AddRange(codes);
                    continue;
                }

                var name = $"doc-{index:D3}.pdf";
                descriptions[name] = description;
                File.WriteAllBytes(Path.Combine(folder, name), [.. result.Value]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            var reasons = string.Join(", ", refused.GroupBy(code => code).OrderByDescending(group => group.Count()).Select(group => $"{group.Key} ×{group.Count()}"));
            descriptions.Should().HaveCountGreaterThanOrEqualTo(MinimumWritten, $"most generated documents should be writable (seed {Seed}; refused: {reasons})");
            results.Should().HaveCount(descriptions.Count);
            var failures = results.Where(result => !result.IsCompliant)
                .Select(result => $"seed {Seed}, flavour {flavour}, {result}\n    document: {descriptions[result.File]}")
                .ToList();
            failures.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>One random document and a description of what it holds.</summary>
    private static (Document Document, string Description) Generate(Random random)
    {
        var description = new StringBuilder();
        var body = Enumerable.Range(0, random.Next(1, 8)).Select(_ => Element(random, description)).ToList();
        var title = Pick(random, ["Report", "Zeitnachweis 2026-01", "Report", null, new string('t', 33000), "Title\u00ADwith soft hyphen", "\uE000 private"]);
        var language = Pick(random, ["en", "nl-NL", "de-DE-1901", "en", null]);
        var page = Pick(random, [PageSetup.A4.Margin(1.Cm()), Page(300.Pt(), 200.Pt()).Margin(10.Pt()), Page(15000.Pt(), 400.Pt()), Page(14400.Pt(), 14400.Pt()).Margin(1.Cm()), PageSetup.A4.Margin(1.Cm())]);
        if (random.Next(3) == 0)
        {
            // Page areas: a header with the page number and a footer with the count, sometimes of a fixed height.
            page = page.Header(Paragraph([Text("Page "), PageNumber()]).FontSize(8.Pt())).Footer(Paragraph([Text("of "), PageCount()]), random.Next(2) == 0 ? null : 1.Cm());
            description.Append(" header+footer");
        }

        description.Append($" title={Describe(title)} language={Describe(language)} page={page.Width}x{page.Height}");
        var document = Document([.. body], page, title: title, language: language).FontFamily("Lato");
        if (random.Next(3) == 0)
        {
            var mediaType = Pick(random, ["application/xml", "text/plain", "application/x!y", "application/vnd.example+json", "application/" + new string('x', 130)]);
            description.Append($" attachment={mediaType}");
            document = document.Attach(Attachment("data.bin", "1"u8, mediaType).WithRelationship(AttachmentRelationship.Data));
        }

        return (document, description.ToString());
    }

    private static Element Element(Random random, StringBuilder description, int depth = 0)
    {
        switch (random.Next(depth > 1 ? 5 : 11))
        {
            case 0:
                var level = Pick(random, [1, 1, 2, 3]);
                var heading = Words(random);
                description.Append($" H{level}({Describe(heading)})");
                return Heading(level, heading);
            case 1:
                var columns = random.Next(1, 4);
                var headerOnly = random.Next(4) == 0;
                var spanning = random.Next(3) == 0 && columns > 1;
                description.Append($" table({columns} columns{(headerOnly ? ", header only" : string.Empty)}{(spanning ? ", a column span" : string.Empty)})");
                ValueList<TablePart> rows = headerOnly
                    ? [HeaderRow([.. Enumerable.Range(0, columns).Select(column => Cell($"h{column}"))])]
                    : [
                        HeaderRow([.. Enumerable.Range(0, columns).Select(column => Cell($"h{column}"))]),
                        spanning ? TableRow([Cell(Words(random)).ColumnSpan(columns)]) : TableRow([.. Enumerable.Range(0, columns).Select(_ => Cell(Words(random)))]),
                        TableRow([.. Enumerable.Range(0, columns).Select(_ => Cell(Words(random)))]),
                    ];
                return Table([.. Enumerable.Range(0, columns).Select(_ => Fill())], rows).GridLines(1.Pt(), Color.Black);
            case 2:
                var alt = Pick(random, ["A red square", "A red square", "Logo", null, " "]);
                var file = Pick(random, Images);
                description.Append($" image({file}, alt={Describe(alt)})");
                var image = Image(TestImages.Bytes(file)).Width(Pick(random, [2.Cm(), 5.Cm(), 0.5.Cm()]));
                return alt is null ? (random.Next(2) == 0 ? image.Decorative() : image) : image.Alt(alt);
            case 5:
                // A box with a border and a background around other elements.
                var width = Pick(random, [0.5, 1, 1, 4, 4, 4, 20000]);
                description.Append($" block(border {width}pt, background){{");
                var children = Enumerable.Range(0, random.Next(1, 4)).Select(_ => Element(random, description, depth + 1)).ToList();
                description.Append('}');
                return Block([.. children]).Border(width.Pt(), Color.Black).Background(new Color(0xEE, 0xEE, 0xFF)).Padding(4.Pt());
            case 6:
                var size = Pick(random, [0.5, 6, 12, 12, 30, 100, 20000]);
                var sized = Words(random);
                description.Append($" P({size}pt, {Describe(sized)})");
                return Paragraph(sized).FontSize(size.Pt());
            case 7:
                // A table long enough to break across pages, in row groups with row spans, its lines repeated or split.
                var rowCount = random.Next(20, 80);
                var clone = random.Next(2) == 0;
                description.Append($" long table({rowCount} rows, {(clone ? "clone" : "slice")})");
                ValueList<TablePart> groups = [HeaderRow([Cell("Day"), Cell("Hours")]), .. Enumerable.Range(0, rowCount / 2).Select(group => (TablePart)RowGroup(
                [
                    TableRow([Cell($"day {group}").RowSpan(2), Cell(Words(random))]),
                    TableRow([Cell(Words(random))]),
                ]))];
                var table = Table(Columns(Fill(1), Fill(2)), groups).GridLines(Pick(random, [0.5, 1, 3]).Pt(), Color.Black);
                return clone ? table : table.SplitLinesAtBreaks();
            case 8:
                // A table nested in a cell of a table.
                description.Append(" nested table");
                var inner = Table(Columns(Fill(1), Fill(1)), [HeaderRow([Cell("a"), Cell("b")]), TableRow([Cell(Words(random)), Cell(Words(random))])]).GridLines(0.5.Pt(), Color.Black);
                return Table(Columns(Fill(1), Fill(1)), [TableRow([Cell([inner]), Cell(Words(random))])]).GridLines(1.Pt(), Color.Black);
            case 9:
                // A word of thousands of letters, small or large.
                var letters = Pick(random, [300, 5000, 20000]);
                var points = Pick(random, [1, 12]);
                description.Append($" word({letters} letters, {points}pt)");
                return Paragraph(new string('i', letters)).FontSize(points.Pt());
            case 10:
                // Lengths at and past the 14 400 pt the front-end allows.
                var margin = Pick(random, [14400, 14400, 14400, 20000, 2147483648.0]);
                description.Append($" margin-left {margin}pt");
                return Paragraph(Words(random)).MarginLeft(margin.Pt());
            default:
                var text = Words(random);
                description.Append($" P({Describe(text)})");
                return Paragraph(text);
        }
    }

    /// <summary>Words of the kinds that went wrong before, joined by spaces and no-break spaces. One word in two thousand is
    /// one the front-end or a profile refuses, so most documents are written and some are refused.</summary>
    private static string Words(Random random) =>
        string.Join(random.Next(4) == 0 ? "\u00A0" : " ", Enumerable.Range(0, random.Next(1, 30)).Select(_ => random.Next(2000) == 0 ? Pick(random, Refusable) : Pick(random, Vocabulary)));

    private static readonly string[] Vocabulary =
    [
        "time", "2026-01-15", "-12.50", "extra\u00ADordinary", "co\u00ADoperate", "a-b", "naïve", "œuvre", "fine", "office",
        "x\u200By", "e\u0301", "word", "€", "‐dash", "ß", "hours", "Zeit\u00ADnachweis",
    ];

    /// <summary>A control character, a Private Use character, and a character Lato has no glyph for.</summary>
    private static readonly string[] Refusable = ["a\u0001b", "\uE001", "一"];

    private static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

    private static string Describe(string? text) =>
        text is null ? "null" : text.Length > 40 ? $"\"{Escape(text[..20])}…\" ({text.Length} chars)" : $"\"{Escape(text)}\"";

    private static string Escape(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~' ? $"\\u{(int)character:X4}" : character.ToString()));
}
