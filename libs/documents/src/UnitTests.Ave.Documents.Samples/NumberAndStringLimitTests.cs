using System.IO;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.Samples;
using Ave.Documents.TestSupport;

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
    private static void ShouldPass(string flavour, PdfProfile profile, params (string Name, PageDocument Document)[] documents)
    {
        var folder = Directory.CreateTempSubdirectory("ave-documents-limits-").FullName;
        try
        {
            foreach (var (name, document) in documents)
            {
                var pdf = PdfRenderer.Render(document, SampleOutput.Options with { Profile = profile });
                pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. pdf.Value]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(documents.Length);
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
}
