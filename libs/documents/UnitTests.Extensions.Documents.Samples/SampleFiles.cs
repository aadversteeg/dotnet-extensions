using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>Every sample the samples app writes, rendered in the test process with the same options.</summary>
internal static class SampleFiles
{
    private static readonly Brand Brand = Brand.Poc([.. File.ReadAllBytes(Path.Combine(System.AppContext.BaseDirectory, "assets", "logo.png"))]);

    /// <summary>The documents by file name (without extension), as the samples app names them.</summary>
    public static ImmutableArray<(string Name, Document Document)> Documents { get; } =
    [
        ("sample-a-letter", SampleDocuments.Letter()),
        ("sample-b-long", SampleDocuments.LongText()),
        ("sample-c-sliced", SampleDocuments.Sliced(Brand.Logo)),
        .. new[] { "normal", "long", "edge" }.SelectMany(dataSet => new[] { "nl-NL", "en-US" }
            .Select(culture => ($"timesheet-{dataSet}.{culture[..2]}", Timesheet(dataSet, culture)))),
    ];

    /// <summary>A timesheet for one of the PoC's data sets.</summary>
    public static Document Timesheet(string dataSet, string culture) =>
        Timesheets.Timesheet(Data(dataSet), Brand, CultureInfo.GetCultureInfo(culture));

    /// <summary>The PoC's data set, limited to what the fonts can show.</summary>
    public static TimesheetData Data(string dataSet) =>
        TimesheetData.Load(Path.Combine(System.AppContext.BaseDirectory, "data", $"sample-{dataSet}.json"))
            .WithCleanText(FontCoverage.Displayable(TestFonts.Regular));

    /// <summary>Renders a document with the given options (default: the samples' PDF/A-3a + PDF/UA-1).</summary>
    public static ImmutableArray<byte> Pdf(Document document, PdfRenderOptions? options = null) =>
        document.ToPdf(TestFonts.SetWithItalic, options ?? SampleOutput.Options).Value;

    /// <summary>Writes every sample to a new temporary folder and returns its path; the caller deletes it.</summary>
    public static string WriteAll(PdfRenderOptions? options = null)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-samples-").FullName;
        foreach (var (name, document) in Documents)
        {
            File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. Pdf(document, options)]);
        }

        return folder;
    }
}
