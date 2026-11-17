using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ave.Documents.Samples;

/// <summary>
/// The data of one timesheet, the shape of SmallBusiness's <c>TimeSheetReportData</c> (as copied into the reporting
/// PoC, /d/dev/ave/reporting-poc src/ReportingPoc/Models.cs). Plain values; the template formats them.
/// </summary>
/// <param name="ReportDate">The date the report was made.</param>
/// <param name="ProjectName">The project.</param>
/// <param name="ProjectTitle">The project's title (not shown, as in the PoC).</param>
/// <param name="PeriodStart">The first day of the period, or null.</param>
/// <param name="PeriodEnd">The last day of the period, or null.</param>
/// <param name="CustomerName">The customer.</param>
/// <param name="TimeEntries">The entries, in the order to show them.</param>
public sealed record TimesheetData(
    DateTime ReportDate,
    string ProjectName,
    string ProjectTitle,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    string CustomerName,
    IReadOnlyList<TimeEntry> TimeEntries)
{
    /// <summary>Reads the PoC's JSON files (camelCase properties).</summary>
    /// <param name="path">The JSON file.</param>
    /// <returns>The data.</returns>
    public static TimesheetData Load(string path) =>
        JsonSerializer.Deserialize<TimesheetData>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException($"{path} holds no timesheet.");

    /// <summary>
    /// The same data with every text passed through <paramref name="clean"/>, for example to drop characters the fonts
    /// cannot show (the engine reports a missing glyph as an error, never by leaving it out silently, so a template that
    /// shows user text decides what to do with such characters before it builds the document).
    /// </summary>
    /// <param name="clean">The text filter.</param>
    /// <returns>The cleaned data.</returns>
    public TimesheetData WithCleanText(Func<string, string> clean) => this with
    {
        ProjectName = clean(ProjectName),
        ProjectTitle = clean(ProjectTitle),
        CustomerName = clean(CustomerName),
        TimeEntries = [.. TimeEntries.Select(entry => entry with
        {
            Title = clean(entry.Title),
            Description = clean(entry.Description ?? string.Empty),
            TaskName = clean(entry.TaskName),
        })],
    };
}

/// <summary>One time entry.</summary>
/// <param name="Title">What was done.</param>
/// <param name="Description">More detail, or empty.</param>
/// <param name="TaskName">The task.</param>
/// <param name="Duration">The hours.</param>
/// <param name="ExecutedAt">The day.</param>
public sealed record TimeEntry(string Title, string Description, string TaskName, double Duration, DateTime ExecutedAt);
