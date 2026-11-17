using System;
using System.Globalization;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Fluent;
using static Ave.Documents.Fluent.Elements;

namespace Ave.Documents.Samples;

/// <summary>
/// The SmallBusiness timesheet written with the code front-end — the same layout as the reporting PoC's Typst
/// template (<c>/d/dev/ave/reporting-poc/templates/timesheet.typ</c>): a header with the logo and the title on every
/// page (with customer, project and period on the right from page 2 on), the customer and period lines, the entries
/// table with repeated header row, each description as a second row under its entry and kept with it, grid lines,
/// right-aligned hours in the culture's number format, a total row, and a footer with the bank details and "page x
/// of y".
/// </summary>
public static class Timesheets
{
    /// <summary>The thin black line of the total box. 1.5pt where the PoC has 1pt: in the collapsing border model two
    /// equal borders resolve to the upper one (CSS 2.1 §17.6.2.1, "further to the top wins"), so a 1pt black line under
    /// a row with 1pt grey lines would be drawn grey; the wider border wins instead.</summary>
    private static readonly Length TotalLine = 1.5.Pt();

    /// <summary>
    /// The top and bottom padding of the entry cells: 1.5pt where the PoC's Typst template has 4pt. The PoC's rows are
    /// denser for another reason than padding: Typst sets a line from cap height to baseline (about 7.2pt for 10pt
    /// Lato), CSS from the line box of <c>line-height: normal</c> (1.2 em = 12pt), and this engine has no line-height
    /// property yet. With 1.5pt a text row is 12 + 3 + 1 = 16pt (Typst about 15.2pt) and a description row 9.6 + 3 + 1 =
    /// 13.6pt (Typst about 13.7pt), so the long month takes 5 pages, as in the PoC. A template choice, not an engine one.
    /// </summary>
    private static readonly Length EntryPaddingVertical = 1.5.Pt();

    /// <summary>The timesheet as a document.</summary>
    /// <param name="data">The report data; its text must already be limited to what the fonts can show.</param>
    /// <param name="brand">The brand kit.</param>
    /// <param name="culture">The language and number format (nl-NL: <c>1.039,56</c>; en-US: <c>1,039.56</c>).</param>
    /// <returns>The document.</returns>
    public static Document Timesheet(TimesheetData data, Brand brand, CultureInfo culture)
    {
        var labels = TimesheetLabels.For(culture);
        var period = $"{Day(data.PeriodStart)} - {Day(data.PeriodEnd)}";

        // The PoC's page: 1 cm side margins; the header in a 4 cm top area whose content starts 1 cm from the top and
        // ends 0.4 cm above the body; the footer starts 0.4 cm below the body, which ends 2 cm above the page bottom.
        var page = PageSetup.A4
            .Margins(new Edges(1.Cm(), 1.Cm(), 0.8.Cm(), 1.Cm()))
            .FirstPageHeader(Header(brand, labels, rightColumn: null), 3.Cm())
            .Header(Header(brand, labels, rightColumn: [data.CustomerName, data.ProjectName, period]), 3.Cm())
            .Footer(Footer(brand, labels), 1.2.Cm());

        return Document(
                [
                    Row(
                        [
                            Block([Paragraph(data.CustomerName).FontSize(13.Pt()), Paragraph(Day(data.ReportDate)).FontSize(11.Pt()).MarginTop(4.Pt())]),
                            Block([Paragraph(data.ProjectName).FontSize(13.Pt()), Paragraph($"{labels.Period}: {period}").FontSize(11.Pt()).MarginTop(4.Pt())])
                                .TextAlign(TextAlign.Right),
                        ]),
                    EntriesTable(data, brand, labels, culture).MarginTop(10.Pt()),
                ],
                page,
                title: $"{labels.Title} {data.CustomerName} {period}",
                language: culture.Name)
            .FontFamily(brand.FontFamily)
            .FontSize(10.Pt())
            .Attach(TimesheetXml.For(data, new DateTimeOffset(data.ReportDate, TimeSpan.Zero)));
    }

    /// <summary>
    /// The page header: logo, title and (from page 2) the customer, project and period on the right, then the accent
    /// band. The PoC's grid has a 1 cm gutter between its columns; here the columns after the logo start with 1 cm of
    /// padding.
    /// </summary>
    private static Element Header(Brand brand, TimesheetLabels labels, string[]? rightColumn) =>
        Block(
        [
            Row(
                [
                    Image(brand.Logo.AsSpan()).Width(2.5.Cm()),
                    Paragraph(labels.Title).FontSize(24.Pt()).Bold().PaddingLeft(1.Cm()),
                    rightColumn is null
                        ? Block([])
                        : Block([.. rightColumn.Select((line, index) => (Element)Paragraph(line).MarginTop(index == 0 ? 0.Pt() : 3.Pt()))])
                            .FontSize(9.Pt()).TextAlign(TextAlign.Right).PaddingLeft(1.Cm()),
                ])
                .Columns(2.5.Cm(), Fill(1), Auto)
                .VerticalAlign(VerticalAlign.Middle),
            Band(brand),
        ]);

    /// <summary>The page footer: the accent band, then four columns with the company details and "page x of y".</summary>
    private static Element Footer(Brand brand, TimesheetLabels labels) =>
        Block(
        [
            Band(brand),
            Row(
                [
                    Paragraph($"{labels.Iban}: {brand.Company.Iban}"),
                    Paragraph($"{labels.Vat}: {brand.Company.Vat}").TextAlign(TextAlign.Center),
                    Paragraph($"{labels.Coc}: {brand.Company.Coc}").TextAlign(TextAlign.Center),
                    Paragraph([Text($"{labels.Page} "), PageNumber(), Text($" {labels.Of} "), PageCount()]).TextAlign(TextAlign.Right),
                ])
                .MarginTop(6.Pt()),
        ])
        .FontSize(8.Pt())
        .PaddingTop(0.4.Cm());

    /// <summary>The 3pt accent band: an empty block whose only content is its bottom border.</summary>
    private static Element Band(Brand brand) => Block([]).BorderBottom(3.Pt(), brand.Accent);

    /// <summary>
    /// The entries: four fill columns (2.5 : 3 : 10 : 2) with light-grey grid lines and 1.5pt × 2pt cell padding, a bold
    /// header row repeated on every page, and per entry either one row or — with a description — a group of two rows
    /// in which the date and the hours span both rows, kept together on one page. Then the total row.
    /// </summary>
    private static TableElement EntriesTable(TimesheetData data, Brand brand, TimesheetLabels labels, CultureInfo culture)
    {
        var total = data.TimeEntries.Sum(entry => entry.Duration);
        return Table(
                Columns(Fill(2.5), Fill(3), Fill(10), Fill(2)),
                [
                    HeaderRow([Cell(labels.Executed), Cell(labels.Task), Cell(labels.Item), Cell(labels.Hours).TextAlign(TextAlign.Right)]).Bold(),
                    .. data.TimeEntries.Select(entry => Entry(entry, brand, culture)),
                    TableRow(
                        [
                            Cell($"{labels.Total}:").ColumnSpan(3).Border(TotalLine, Color.Black).PaddingTop(5.Pt()).PaddingBottom(5.Pt()),
                            Cell(Hours(total, culture)).TextAlign(TextAlign.Right).Border(TotalLine, Color.Black).PaddingTop(5.Pt()).PaddingBottom(5.Pt()),
                        ])
                        .Bold(),
                ])
            .GridLines(1.Pt(), brand.GridLine)
            .CellPadding(EntryPaddingVertical, 2.Pt());
    }

    /// <summary>One entry: a single row, or a kept-together group of the entry row and its description row.</summary>
    private static TablePart Entry(TimeEntry entry, Brand brand, CultureInfo culture)
    {
        var description = entry.Description.Trim();
        var spansDescription = description.Length > 0 ? 2 : 1;
        var row = TableRow(
        [
            Cell(Day(entry.ExecutedAt)).RowSpan(spansDescription),
            Cell(entry.TaskName),
            Cell(entry.Title),
            Cell(Hours(entry.Duration, culture)).TextAlign(TextAlign.Right).RowSpan(spansDescription),
        ]);
        return description.Length == 0
            ? row
            : RowGroup([row, TableRow([Cell(description).ColumnSpan(2).FontSize(8.Pt()).Italic().Color(brand.Muted)])]).KeepTogether();
    }

    /// <summary>Hours in the culture's number format with two decimals: <c>1.039,56</c> (nl-NL), <c>1,039.56</c> (en-US).</summary>
    private static string Hours(double hours, CultureInfo culture) => hours.ToString("N2", culture);

    /// <summary>A day as yyyy-MM-dd, as the PoC shows it; "N/A" for a missing date.</summary>
    private static string Day(DateTime? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "N/A";
}
