using System.Globalization;

namespace Ave.Extensions.Documents.Samples;

/// <summary>The fixed texts of the timesheet in one language (the PoC's label table <c>L</c>).</summary>
/// <param name="Title">The document title.</param>
/// <param name="Period">"Period".</param>
/// <param name="Executed">The date column.</param>
/// <param name="Task">The task column.</param>
/// <param name="Item">The title column.</param>
/// <param name="Hours">The hours column.</param>
/// <param name="Total">The total row.</param>
/// <param name="Page">"Page".</param>
/// <param name="Of">"of".</param>
/// <param name="Iban">The bank account label.</param>
/// <param name="Vat">The VAT label.</param>
/// <param name="Coc">The chamber of commerce label.</param>
public sealed record TimesheetLabels(
    string Title, string Period, string Executed, string Task, string Item, string Hours, string Total, string Page, string Of, string Iban, string Vat, string Coc)
{
    /// <summary>Dutch for a Dutch culture, English otherwise.</summary>
    /// <param name="culture">The culture of the document.</param>
    /// <returns>The labels.</returns>
    public static TimesheetLabels For(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "nl"
            ? new("Urenstaat", "Periode", "Uitgevoerd op", "Taak", "Titel", "Duur (uur)", "Totaal", "Pagina", "van", "Rekening", "BTW", "KvK")
            : new("Timesheet", "Period", "Executed at", "Task", "Title", "Duration (hours)", "Total", "Page", "of", "Account", "VAT", "CoC");
}
