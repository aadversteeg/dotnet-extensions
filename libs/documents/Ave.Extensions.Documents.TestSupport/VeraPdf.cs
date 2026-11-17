using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>
/// veraPDF, the PDF Association's reference validator for PDF/A and PDF/UA, run from its Docker image. Test-only: the
/// engine never calls it.
/// </summary>
public static class VeraPdf
{
    /// <summary>
    /// The image, pinned by digest so every run validates with the same rules: veraPDF 1.30.4 (built 2026-11-09), the
    /// version the reporting proof of concept used. Its rule texts are quoted in the engine's error codes.
    /// </summary>
    public const string Image = "verapdf/cli@sha256:f085855d442ad415e79fabbabcdd1803c2db4ee097cd5b94506c95ecdcfa1728";

    /// <summary>The outcome for one file and one flavour.</summary>
    /// <param name="File">The file name.</param>
    /// <param name="Flavour">The flavour, for example <c>3b</c> or <c>ua1</c>.</param>
    /// <param name="IsCompliant">True when every rule passed.</param>
    /// <param name="PassedChecks">How many checks passed.</param>
    /// <param name="FailedRules">Each failed rule as <c>clause-test (n×): description</c>.</param>
    public sealed record Result(string File, string Flavour, bool IsCompliant, int PassedChecks, ImmutableArray<string> FailedRules)
    {
        /// <summary>One line for a test message or a report.</summary>
        public override string ToString() =>
            $"{(IsCompliant ? "PASS" : "FAIL")} {File} {Flavour}: {PassedChecks} checks passed"
            + string.Concat(FailedRules.Select(rule => "\n    " + rule));
    }

    /// <summary>
    /// Validates every PDF file in a folder against one flavour, in one container (the JVM starts once). Uses the
    /// machine-readable report (<c>--format mrr</c>) and keeps, per file, the verdict, the number of passed checks and
    /// every failed rule with its clause, test number and description.
    /// </summary>
    /// <param name="folder">The folder, mounted read-only.</param>
    /// <param name="flavour">The veraPDF flavour: <c>3a</c>, <c>3b</c>, <c>ua1</c>, …</param>
    /// <returns>One result per file, in file-name order.</returns>
    public static ImmutableArray<Result> Validate(string folder, string flavour)
    {
        var files = Directory.GetFiles(folder, "*.pdf").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        var (_, output) = Docker.Run(
            ["run", "--rm", "-v", $"{Path.GetFullPath(folder)}:/data:ro", Image, "--flavour", flavour, "--format", "mrr", .. files.Select(file => "/data/" + file)],
            TimeSpan.FromMinutes(5));
        var start = output.IndexOf("<?xml", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"veraPDF wrote no report:\n{output}");
        }

        var end = output.IndexOf("</report>", start, StringComparison.Ordinal);
        var report = XDocument.Parse(output[start..(end + "</report>".Length)]);
        return [.. report.Descendants("job").Select(job => Read(job, flavour))];
    }

    private static Result Read(XElement job, string flavour)
    {
        var name = Path.GetFileName(job.Element("item")?.Element("name")?.Value ?? "?");
        var validation = job.Element("validationReport");
        if (validation is null)
        {
            return new Result(name, flavour, false, 0, [$"no validation report: {job}"]);
        }

        var details = validation.Element("details");
        var failed = details?.Elements("rule")
            .Where(rule => (string?)rule.Attribute("status") == "failed")
            .Select(rule => $"{rule.Attribute("clause")?.Value}-{rule.Attribute("testNumber")?.Value} ({rule.Attribute("failedChecks")?.Value}×): {rule.Element("description")?.Value}")
            ?? [];
        return new Result(
            name,
            flavour,
            (string?)validation.Attribute("isCompliant") == "true",
            int.TryParse(details?.Attribute("passedChecks")?.Value, out var passed) ? passed : 0,
            [.. failed]);
    }

    /// <summary>The results as text, one line per file (and one per failed rule).</summary>
    /// <param name="results">The results.</param>
    /// <returns>The text.</returns>
    public static string Describe(IEnumerable<Result> results) => string.Join("\n", results.Select(result => result.ToString()));
}
