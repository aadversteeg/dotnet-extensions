using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Ave.Extensions.Documents.Html;

namespace Ave.Extensions.Documents.Samples;

/// <summary>One HTML sample: the file name the samples app writes it under, the name of the same document in the
/// family's Chromium reference documents, and the filled HTML.</summary>
/// <param name="Name">The file name without extension, for example <c>html-timesheet-normal.nl</c>.</param>
/// <param name="ReferenceName">The document's name in <c>tools/reference/chromium/documents</c>, for example
/// <c>timesheet-normal</c>.</param>
/// <param name="Html">The HTML text.</param>
public sealed record HtmlSample(string Name, string ReferenceName, string Html);

/// <summary>
/// The HTML samples, shared by the samples app and its tests so both render the same: the timesheet for the three data
/// sets in Dutch (<see cref="TimesheetHtml"/>) and the invoice (<see cref="InvoiceHtml"/>), each filled from its data,
/// and how the engine gets the files they name.
/// </summary>
public static class HtmlSamples
{
    /// <summary>The timesheets: the sample's name, the reference document's name, and the data set.</summary>
    private static readonly (string Name, string ReferenceName, string DataSet)[] Timesheets =
    [
        ("html-timesheet-normal.nl", "timesheet-normal", "normal"),
        ("html-timesheet-long.nl", "timesheet-pages", "long"),
        ("html-timesheet-edge.nl", "timesheet-one-page", "edge"),
    ];

    /// <summary>Every HTML sample, filled from the data in the app folder.</summary>
    /// <param name="folder">The app folder: <c>data/</c> holds the data.</param>
    /// <param name="brand">The timesheet's brand kit.</param>
    /// <param name="clean">The filter that limits user text to what the fonts can show (<see cref="FontCoverage"/>).</param>
    /// <returns>The samples, the timesheets first.</returns>
    public static ImmutableArray<HtmlSample> Documents(string folder, Brand brand, Func<string, string> clean) =>
    [
        .. Timesheets.Select(timesheet => new HtmlSample(
            timesheet.Name,
            timesheet.ReferenceName,
            TimesheetHtml.Write(TimesheetData.Load(Path.Combine(folder, "data", $"sample-{timesheet.DataSet}.json")).WithCleanText(clean), brand, CultureInfo.GetCultureInfo("nl-NL")))),
        new HtmlSample("html-invoice", "invoice", InvoiceHtml.Write(InvoiceData.Load(Path.Combine(folder, "data", "invoice.json")))),
    ];

    /// <summary>
    /// The address the documents are given: the folder the family keeps them in (<c>tools/reference/chromium/documents</c>),
    /// on a made-up host. Their relative URLs resolve against it to the family's own files, as they do in that folder
    /// on disk, where Chromium prints them.
    /// </summary>
    public const string DocumentsUrl = "https://example.com/documents/tools/reference/chromium/documents/";

    /// <summary>
    /// The resolver the engine asks for every file a document names: the fonts, the logo and the invoice's style sheets,
    /// each read once from the app folder. It gives only these files, by their address, and null for any other URL (the
    /// engine then refuses the document).
    /// </summary>
    /// <param name="folder">The app folder: <c>fonts/</c>, <c>assets/logo.png</c> and <c>html/</c>.</param>
    /// <returns>The resolver.</returns>
    public static ResourceResolver Resources(string folder)
    {
        var files = new Dictionary<string, HtmlResource>(StringComparer.Ordinal)
        {
            [Address(TimesheetHtml.LogoUrl)] = Read(folder, "assets", "logo.png"),
        };
        foreach (var font in new[] { "Lato-Regular.ttf", "Lato-Bold.ttf", "Lato-Italic.ttf" })
        {
            files[Address(TimesheetHtml.FontFolder + font)] = Read(folder, "fonts", font);
        }

        foreach (var sheet in InvoiceHtml.StyleSheets)
        {
            files[Address(sheet)] = Read(folder, "html", sheet);
        }

        return url => files.GetValueOrDefault(url);
    }

    /// <summary>How the samples render a document: its address, the resolver, refusing what the engine does not draw
    /// (the default), and the document's name for the diagnostics. No fonts in the options: the documents bring their
    /// own by <c>@font-face</c>, and those come first.</summary>
    /// <param name="resources">The resolver (<see cref="Resources"/>).</param>
    /// <param name="sample">The sample.</param>
    /// <returns>The options.</returns>
    public static HtmlRenderOptions Options(ResourceResolver resources, HtmlSample sample) => new()
    {
        Style = new HtmlStyleOptions { Resources = resources, BaseUrl = Address(sample.ReferenceName + ".html") },
        SourceName = sample.Name + ".html",
    };

    /// <summary>A URL a document writes, resolved against <see cref="DocumentsUrl"/>.</summary>
    private static string Address(string url) => new Uri(new Uri(DocumentsUrl), url).AbsoluteUri;

    private static HtmlResource Read(string folder, string subfolder, string file) => new(File.ReadAllBytes(Path.Combine(folder, subfolder, file)));
}
