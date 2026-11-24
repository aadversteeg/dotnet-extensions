using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>Every sample the samples app writes, rendered in the test process with the same options: the documents of
/// the code front-end and the HTML samples.</summary>
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

    /// <summary>The HTML samples, filled from their data as the samples app fills them.</summary>
    public static ImmutableArray<HtmlSample> HtmlDocuments { get; } =
        HtmlSamples.Documents(System.AppContext.BaseDirectory, Brand, FontCoverage.Displayable(TestFonts.Regular));

    private static readonly ResourceResolver HtmlResources = HtmlSamples.Resources(System.AppContext.BaseDirectory);

    /// <summary>An HTML sample by name.</summary>
    public static HtmlSample Html(string name) => HtmlDocuments.Single(sample => sample.Name == name);

    /// <summary>An HTML sample rendered as the samples app renders it: its pages and the diagnostics.</summary>
    public static HtmlRendered<PageDocument> HtmlPages(HtmlSample sample) =>
        HtmlRendering.ToPages(sample.Html, HtmlSamples.Options(HtmlResources, sample)).Value;

    /// <summary>An HTML sample as a PDF (default: the samples' PDF/A-3a + PDF/UA-1).</summary>
    public static ImmutableArray<byte> HtmlPdf(HtmlSample sample, PdfRenderOptions? options = null) =>
        HtmlPages(sample).Output.ToPdf(options ?? SampleOutput.Options).Value;

    /// <summary>The number of PDF files <see cref="WriteAll"/> writes.</summary>
    public static int PdfCount => Documents.Length + HtmlDocuments.Length;

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

        foreach (var sample in HtmlDocuments)
        {
            File.WriteAllBytes(Path.Combine(folder, sample.Name + ".pdf"), [.. HtmlPdf(sample, options)]);
        }

        return folder;
    }
}
