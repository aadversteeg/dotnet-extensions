using System;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using static Ave.Extensions.Documents.Samples.HtmlText;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// The timesheet of <see cref="Timesheets"/> written as HTML and CSS for print, from the same data: the step a caller of
/// the HTML front-end takes before it calls the engine, which takes finished HTML only. A C# raw string is the
/// template, and every data value goes into it through <see cref="HtmlText"/>. The document is written the way an
/// author writes HTML for print: semantic elements, one style sheet, the entries as a table with a repeated header
/// row, fonts by <c>@font-face</c>, and the page furniture in <c>@page</c> margin boxes: the logo and the title on every
/// page, the customer, project and period at the right from page 2 on, and the footer with "page x of y".
/// </summary>
public static class TimesheetHtml
{
    /// <summary>The folder of the Lato font files, as the document names it. The URLs are relative to the folder the
    /// family keeps the written documents in (<c>tools/reference/chromium/documents</c>), where Chromium prints the same
    /// files; the samples give the engine these files by their URL (<see cref="HtmlSamples.Resources"/>).</summary>
    public const string FontFolder = "../../../../assets/fonts/lato/";

    /// <summary>The logo, as the document names it (relative as <see cref="FontFolder"/>).</summary>
    public const string LogoUrl = "../../../../samples/Ave.Extensions.Documents.Samples/assets/logo.png";

    /// <summary>The timesheet as an HTML document.</summary>
    /// <param name="data">The report data; its text must already be limited to what the fonts can show
    /// (<see cref="FontCoverage"/>).</param>
    /// <param name="brand">The brand kit: its colours, font family and company details. The logo is the file at
    /// <see cref="LogoUrl"/>.</param>
    /// <param name="culture">The language and number format.</param>
    /// <returns>The HTML text.</returns>
    public static string Write(TimesheetData data, Brand brand, CultureInfo culture)
    {
        var labels = TimesheetLabels.For(culture);
        var period = $"{Day(data.PeriodStart)} - {Day(data.PeriodEnd)}";
        var total = data.TimeEntries.Sum(entry => entry.Duration);
        var rows = string.Join("\n", data.TimeEntries.Select(entry => Entry(entry, culture)));
        // LF whatever the checkout: the raw strings (the style sheet's too) take the source file's line ends, the measured document has LF.
        return LineFeeds($$"""
            <!DOCTYPE html>
            <html lang="{{Escape(culture.Name)}}">
            <head>
            <meta charset="utf-8">
            <title>{{Escape(labels.Title)}} {{Escape(data.CustomerName)}} {{Escape(period)}}</title>
            <style>
            {{Style(data, brand, labels, period)}}</style>
            </head>
            <body>
            <main>
            <table class="parties" role="presentation">
            <tr>
            <td><p class="name">{{Escape(data.CustomerName)}}</p><p>{{Escape(Day(data.ReportDate))}}</p></td>
            <td><p class="name">{{Escape(data.ProjectName)}}</p><p>{{Escape(labels.Period)}}: {{Escape(period)}}</p></td>
            </tr>
            </table>
            <table class="entries">
            <colgroup><col class="executed"><col class="task"><col class="item"><col class="hours"></colgroup>
            <thead>
            <tr><th scope="col">{{Escape(labels.Executed)}}</th><th scope="col">{{Escape(labels.Task)}}</th><th scope="col">{{Escape(labels.Item)}}</th><th scope="col" class="hours">{{Escape(labels.Hours)}}</th></tr>
            </thead>
            {{rows}}
            <tbody class="total">
            <tr><th scope="row" colspan="3">{{Escape(labels.Total)}}:</th><td class="hours">{{Escape(Hours(total, culture))}}</td></tr>
            </tbody>
            </table>
            </main>
            </body>
            </html>

            """);
    }

    /// <summary>
    /// One entry as its own row group, kept on one page: a single row, or the entry row and its description row, with
    /// the date and the hours spanning both.
    /// </summary>
    private static string Entry(TimeEntry entry, CultureInfo culture)
    {
        var description = Escape(entry.Description).Trim();
        var span = description.Length > 0 ? " rowspan=\"2\"" : string.Empty;
        var row = $"""<tr><td{span}>{Escape(Day(entry.ExecutedAt))}</td><td>{Escape(entry.TaskName)}</td><td>{Escape(entry.Title)}</td><td class="hours"{span}>{Escape(Hours(entry.Duration, culture))}</td></tr>""";
        string[] rows = description.Length == 0 ? [row] : [row, $"""<tr><td class="description" colspan="2">{description}</td></tr>"""];
        return string.Join("\n", ["<tbody>", .. rows, "</tbody>"]);
    }

    /// <summary>
    /// The style sheet. A4 with 1 cm side margins; the header in the top margin of every page, the footer in the bottom
    /// margin. The margin boxes show data as CSS strings (<see cref="HtmlText.CssString"/>).
    /// </summary>
    private static string Style(TimesheetData data, Brand brand, TimesheetLabels labels, string period)
    {
        var family = brand.FontFamily;
        var accent = Hex(brand.Accent);
        return $$"""
            @font-face { font-family: {{family}}; src: url({{FontFolder}}Lato-Regular.ttf); font-weight: 400; font-style: normal; }
            @font-face { font-family: {{family}}; src: url({{FontFolder}}Lato-Bold.ttf); font-weight: 700; font-style: normal; }
            @font-face { font-family: {{family}}; src: url({{FontFolder}}Lato-Italic.ttf); font-weight: 400; font-style: italic; }

            /* A4 with 1 cm side margins. The header is in the top margin of every page: the logo with the title at the left, and
               from page 2 on the customer, project and period at the right; page 1 shows those in the flow instead. The footer is
               in the bottom margin of every page. The margin boxes inherit from the page, not from the body, so the page names the
               font too. A margin box takes an image only as generated content, at the image's own size: the resolution in
               image-set() sets that size. */
            @page {
              size: A4;
              margin: 4cm 1cm 2cm;
              font-family: {{family}}, sans-serif;

              @top-left {
                content: image-set(url({{LogoUrl}}) 160dpcm) "\2003{{CssString(labels.Title)}}";
                font-size: 24pt;
                font-weight: 700;
                margin: 1cm 0 0.4cm;
                border-bottom: 3pt solid {{accent}};
                vertical-align: middle;
              }
              @top-right {
                content: "{{CssString($"{data.CustomerName}\n{data.ProjectName}\n{period}")}}";
                white-space: pre;
                font-size: 9pt;
                line-height: 1.5;
                text-align: right;
                margin: 1cm 0 0.4cm;
                border-bottom: 3pt solid {{accent}};
                vertical-align: middle;
              }
              @bottom-left {
                content: "{{CssString($"{labels.Iban}: {brand.Company.Iban}")}}";
                font-size: 8pt;
                margin-top: 0.4cm;
                padding-top: 6pt;
                border-top: 3pt solid {{accent}};
                vertical-align: top;
              }
              @bottom-center {
                content: "{{CssString($"{labels.Vat}: {brand.Company.Vat}")}}\2003\2003{{CssString($"{labels.Coc}: {brand.Company.Coc}")}}";
                font-size: 8pt;
                margin-top: 0.4cm;
                padding-top: 6pt;
                border-top: 3pt solid {{accent}};
                vertical-align: top;
              }
              @bottom-right {
                content: "{{CssString(labels.Page)}} " counter(page) " {{CssString(labels.Of)}} " counter(pages);
                font-size: 8pt;
                margin-top: 0.4cm;
                padding-top: 6pt;
                border-top: 3pt solid {{accent}};
                vertical-align: top;
              }
            }

            /* On page 1 the right box stays, empty, so the orange line still runs over the whole width. */
            @page :first {
              @top-right { content: ""; }
            }

            body {
              margin: 0;
              font-family: {{family}}, sans-serif;
              font-size: 10pt;
              color: #000;
            }

            .parties {
              width: 100%;
              border-collapse: collapse;
            }

            .parties td {
              padding: 0;
              vertical-align: top;
            }

            .parties td + td {
              text-align: right;
            }

            .parties p {
              margin: 4pt 0 0;
              font-size: 11pt;
            }

            .parties p.name {
              margin: 0;
              font-size: 13pt;
            }

            .entries {
              width: 100%;
              margin-top: 10pt;
              table-layout: fixed;
              border-collapse: collapse;
            }

            .entries col.executed { width: 14.29%; }
            .entries col.task { width: 17.14%; }
            .entries col.item { width: 57.14%; }
            .entries col.hours { width: 11.43%; }

            .entries th,
            .entries td {
              padding: 1.5pt 2pt;
              border: 1pt solid {{Hex(brand.GridLine)}};
              vertical-align: top;
              text-align: left;
            }

            .entries tbody {
              break-inside: avoid;
            }

            .entries .hours {
              text-align: right;
            }

            .entries .description {
              font-size: 8pt;
              font-style: italic;
              color: {{Hex(brand.Muted)}};
            }

            .entries .total th,
            .entries .total td {
              padding-top: 5pt;
              padding-bottom: 5pt;
              border: 1.5pt solid #000;
              font-weight: 700;
            }

            """;
    }

    private static string Hex(Color color) => string.Create(CultureInfo.InvariantCulture, $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}");

    /// <summary>Hours as the code sample shows them: two decimals in the culture's number format.</summary>
    private static string Hours(double hours, CultureInfo culture) => hours.ToString("N2", culture);

    /// <summary>A day as yyyy-MM-dd; "N/A" for a missing date.</summary>
    private static string Day(DateTime? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "N/A";
}
