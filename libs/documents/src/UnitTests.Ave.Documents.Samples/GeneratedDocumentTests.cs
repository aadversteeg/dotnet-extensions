using System.Collections.Immutable;
using System.IO;
using System.Text;
using Ave.Documents;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// The promise behind every profile, tried on documents nobody wrote by hand: for any input the front-end accepts,
/// <c>ToPdf</c> either returns an error value or writes a file that passes veraPDF for the profile it claims. The
/// documents are drawn from a fixed seed, so a failure is reproducible; its message names the seed, the document's
/// number and what the document holds. The generator leans on the inputs that broke the promise before: hyphens and
/// soft hyphens, no-break spaces, control characters, Private Use characters, long titles, odd media types, page sizes
/// at and past the limits, headings that skip levels, tables with spans, header-only tables, images without
/// description.
/// </summary>
[Trait("Category", "Reference")]
public sealed class GeneratedDocumentTests
{
    /// <summary>The seed of every run. Change it to explore; keep the one that found a fault in a regression test.</summary>
    public const int Seed = 20261115;

    /// <summary>How many documents per profile.</summary>
    public const int DocumentsPerProfile = 60;

    /// <summary>How many of them must be written, so the test is not passed by refusing everything.</summary>
    private const int MinimumWritten = 10;

    private static readonly DateTimeOffset Date = new(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1));

    private static readonly byte[] Logo = TestImages.Bytes("rgb8.png");

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
        var folder = Directory.CreateTempSubdirectory($"ave-documents-generated-{flavour}-").FullName;
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
                    refused.AddRange(result.Error.Parts().Select(part => part.Code.ToString()).Distinct());
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
        var title = Pick(random, ["Report", "Zeitnachweis 2026-01", "Report", null, new string('t', 33000), "Title­with soft hyphen", " private"]);
        var language = Pick(random, ["en", "nl-NL", "de-DE-1901", "en", null]);
        var page = Pick(random, [PageSetup.A4.Margin(1.Cm()), Page(300.Pt(), 200.Pt()).Margin(10.Pt()), Page(15000.Pt(), 400.Pt()), Page(14400.Pt(), 14400.Pt()).Margin(1.Cm())]);
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

    private static Element Element(Random random, StringBuilder description)
    {
        switch (random.Next(5))
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
                description.Append($" image(alt={Describe(alt)})");
                return alt is null ? (random.Next(2) == 0 ? Image(Logo).Decorative() : Image(Logo)) : Image(Logo).Alt(alt);
            default:
                var text = Words(random);
                description.Append($" P({Describe(text)})");
                return Paragraph(text);
        }
    }

    /// <summary>Words of the kinds that went wrong before, joined by spaces and no-break spaces. One word in four hundred is
    /// one the front-end or a profile refuses, so most documents are written and some are refused.</summary>
    private static string Words(Random random) =>
        string.Join(random.Next(4) == 0 ? " " : " ", Enumerable.Range(0, random.Next(1, 30)).Select(_ => random.Next(400) == 0 ? Pick(random, Refusable) : Pick(random, Vocabulary)));

    private static readonly string[] Vocabulary =
    [
        "time", "2026-01-15", "-12.50", "extra­ordinary", "co­operate", "a-b", "naïve", "œuvre", "fine", "office",
        "x​y", "é", "word", "€", "‐dash", "ß", "hours", "Zeit­nachweis",
    ];

    /// <summary>A control character, a Private Use character, and a character Lato has no glyph for.</summary>
    private static readonly string[] Refusable = ["a\u0001b", "", "一"];

    private static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

    private static string Describe(string? text) =>
        text is null ? "null" : text.Length > 40 ? $"\"{Escape(text[..20])}…\" ({text.Length} chars)" : $"\"{Escape(text)}\"";

    private static string Escape(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~' ? $"\\u{(int)character:X4}" : character.ToString()));
}
