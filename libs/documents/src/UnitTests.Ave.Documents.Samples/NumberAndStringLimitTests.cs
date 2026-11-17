using System.Collections.Immutable;
using System.IO;
using Ave.Documents;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.Samples;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// Files at the number and string limits of PDF/A (veraPDF rules 6.1.13-1 and 6.1.13-3) pass veraPDF: what the engine
/// writes from inputs that once broke these rules. Each file is validated under the profile it claims.
/// </summary>
[Trait("Category", "Reference")]
public sealed class NumberAndStringLimitTests
{
    private static readonly DocumentMetadata Metadata = new("Limits", null) { Language = "en" };

    /// <summary>Writes the page models to a fresh folder, validates them under one veraPDF flavour and deletes the folder.</summary>
    private static void ShouldPass(string flavour, PdfProfile profile, params (string Name, PageDocument Document)[] documents) =>
        AllPass(flavour, [.. documents.Select(entry => (entry.Name, PdfRenderer.Render(entry.Document, SampleOutput.Options with { Profile = profile })))]);

    /// <summary>Writes the documents through the front-end, validates them and deletes the folder.</summary>
    private static void ShouldPass(string flavour, PdfProfile profile, params (string Name, Document Document)[] documents) =>
        AllPass(flavour, [.. documents.Select(entry => (entry.Name, entry.Document.ToPdf(TestFonts.Set, SampleOutput.Options with { Profile = profile })))]);

    /// <summary>Every file was written; each passes veraPDF in one folder.</summary>
    private static void AllPass(string flavour, (string Name, Result<ImmutableArray<byte>, Error> Pdf)[] files)
    {
        var folder = Directory.CreateTempSubdirectory("ave-documents-limits-").FullName;
        try
        {
            foreach (var (name, pdf) in files)
            {
                pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. pdf.Value]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(files.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static PageDocument Rectangle(double width) =>
        new(Metadata, [new Page(new Size(300, 200), [new FillRectangle(new Rect(new Point(0, 0), new Size(width, 10)), Color.Black)])]);

    [DockerFact]
    public void A_rectangle_wider_than_the_largest_PDF_integer_is_written_as_a_real_and_passes()
    {
        // 2 147 483 648 and 1e20 were written as integers, which rule 6.1.13-1 refuses above 2 147 483 647.
        ShouldPass("3b", PdfProfile.PdfA3b, ("width-2147483648", Rectangle(2_147_483_648)), ("width-1e20", Rectangle(1e20)), ("width-2147483647", Rectangle(2_147_483_647)));
    }

    private static Document Wide(Element paragraph) =>
        Document([paragraph], Page(14_400.Pt(), 500.Pt()).Margin(10.Pt()), title: "Limits", language: "en").FontFamily("Lato");

    [DockerTheory]
    [InlineData("3b")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_word_of_20000_letters_is_shown_with_strings_PDF_A_allows_and_passes(string flavour)
    {
        // 20 000 glyphs at 1 pt fit one 14 400 pt line; as one string of glyph codes they were 40 000 bytes, which rule
        // 6.1.13-3 refuses above 32 767. The second document puts 20 000 one-letter words on one line at 0.3 pt.
        var profile = flavour == "3b" ? PdfProfile.PdfA3b : PdfProfile.PdfA3a.AndPdfUa1();
        ShouldPass(flavour, profile,
            ("one-word-20000", Wide(Paragraph(new string('i', 20_000)).FontSize(1.Pt()))),
            ("one-line-20000-words", Wide(Paragraph(string.Join(" ", Enumerable.Repeat("i", 20_000))).FontSize(0.3.Pt()))));
    }
}
