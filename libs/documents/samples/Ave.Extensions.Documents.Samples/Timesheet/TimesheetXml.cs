using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// The timesheet's data in machine-readable form, embedded in the PDF as an attachment with the relationship
/// <c>Data</c> — the route a Factur-X invoice takes with its XML. Like the reporting PoC's attachment it is small: the
/// period, the customer, the project and the total, not every entry. Numbers and dates are culture-independent
/// (XML Schema <c>xs:decimal</c> and <c>xs:date</c>), whatever language the PDF is in.
/// </summary>
public static class TimesheetXml
{
    /// <summary>The XML namespace of the summary.</summary>
    public const string Namespace = "urn:example:ave-extensions-documents:timesheet";

    /// <summary>The attachment's file name.</summary>
    public const string FileName = "timesheet.xml";

    /// <summary>The summary as an attachment.</summary>
    /// <param name="data">The timesheet data.</param>
    /// <param name="modified">The attachment's modification date: the report date, an input like every date.</param>
    /// <returns>The attachment.</returns>
    public static Attachment For(TimesheetData data, DateTimeOffset modified) =>
        Elements.Attachment(FileName, Encoding.UTF8.GetBytes(Summary(data)), "application/xml")
            .WithRelationship(AttachmentRelationship.Data)
            .WithDescription("Timesheet summary: customer, project, period and total hours")
            .WithModificationDate(modified);

    /// <summary>The XML text, with an XML declaration and two-space indentation.</summary>
    /// <param name="data">The timesheet data.</param>
    /// <returns>The XML.</returns>
    public static string Summary(TimesheetData data)
    {
        XNamespace ns = Namespace;
        var summary = new XElement(
            ns + "timesheet",
            new XElement(ns + "customer", data.CustomerName),
            new XElement(ns + "project", data.ProjectName),
            new XElement(ns + "periodStart", Day(data.PeriodStart)),
            new XElement(ns + "periodEnd", Day(data.PeriodEnd)),
            new XElement(ns + "entries", data.TimeEntries.Count),
            new XElement(ns + "totalHours", Math.Round(data.TimeEntries.Sum(entry => entry.Duration), 2).ToString("0.00", CultureInfo.InvariantCulture)));
        return new XDeclaration("1.0", "UTF-8", null) + "\n" + summary + "\n";
    }

    private static string Day(DateTime? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}
