using System.IO;
using System.Text;
using Ave.Documents.Pdf;
using Ave.Documents.Samples;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// The reference checks of the conformance profiles, with tools that run in Docker (skipped with a message when Docker is absent; leave
/// them out with <c>--filter Category!=Reference</c>): veraPDF 1.30.4 on every sample for PDF/A-3b, PDF/A-3a and
/// PDF/UA-1, a negative control that proves the validator and the profile both matter, and the attachment extracted
/// by qpdf byte for byte. A failure message lists every failed rule with its clause and description.
/// </summary>
[Trait("Category", "Reference")]
public sealed class ReferenceValidationTests : IClassFixture<ReferenceValidationTests.SampleFolder>
{
    private readonly SampleFolder _samples;

    public ReferenceValidationTests(SampleFolder samples) => _samples = samples;

    /// <summary>The eight samples written once for all tests of this class, deleted afterwards.</summary>
    public sealed class SampleFolder : IDisposable
    {
        public string Path { get; } = Docker.UnavailableReason is null ? SampleFiles.WriteAll() : string.Empty;

        public void Dispose()
        {
            if (Path.Length > 0)
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    [DockerTheory]
    [InlineData("3b")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void Every_sample_passes_veraPDF(string flavour)
    {
        var results = VeraPdf.Validate(_samples.Path, flavour);

        results.Should().HaveCount(SampleFiles.Documents.Length);
        results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
    }

    [DockerFact]
    public void A_plain_timesheet_fails_PDF_A_3b_so_the_check_is_live()
    {
        var folder = Directory.CreateTempSubdirectory("ave-documents-plain-").FullName;
        try
        {
            File.WriteAllBytes(
                System.IO.Path.Combine(folder, "plain.pdf"),
                [.. SampleFiles.Pdf(SampleFiles.Timesheet("edge", "en-US"), SampleOutput.Options with { Profile = PdfProfile.Plain })]);

            var result = VeraPdf.Validate(folder, "3b").Single();

            result.IsCompliant.Should().BeFalse();
            result.FailedRules.Should().Contain(rule => rule.StartsWith("6.6.2.1-1 ", StringComparison.Ordinal), "a plain file has no XMP metadata");
            result.FailedRules.Should().Contain(rule => rule.StartsWith("6.2.4.3-2 ", StringComparison.Ordinal), "nor an output intent for its DeviceRGB");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [DockerFact]
    public void Qpdf_extracts_the_attachment_byte_for_byte()
    {
        var extracted = System.IO.Path.Combine(_samples.Path, "extracted-timesheet.xml");
        var (exitCode, output) = Docker.Run(
            [
                "run", "--rm", "-v", $"{_samples.Path}:/work", ReferenceTools.Image, "bash", "-c",
                "set -eu -o pipefail && qpdf --list-attachments /work/timesheet-normal.nl.pdf "
                + "&& qpdf --show-attachment=timesheet.xml /work/timesheet-normal.nl.pdf > /work/extracted-timesheet.xml",
            ],
            TimeSpan.FromMinutes(3));

        exitCode.Should().Be(0, output);
        output.Should().Contain("timesheet.xml");
        File.ReadAllBytes(extracted).Should().Equal(Encoding.UTF8.GetBytes(TimesheetXml.Summary(SampleFiles.Data("normal"))));
    }
}
