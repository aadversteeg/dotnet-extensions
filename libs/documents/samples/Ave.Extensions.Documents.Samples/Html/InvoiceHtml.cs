using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Ave.Extensions.Documents.Samples.HtmlText;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// The invoice of the HTML samples, from its data (<c>data/invoice.json</c>): a short HTML document with a linked
/// style sheet that imports a theme of custom properties, and a style element of its own. The style sheets are files
/// beside the document, not template output: <c>invoice.css</c> (with the fonts by <c>@font-face</c>) and
/// <c>invoice-theme.css</c>, kept in <c>tools/reference/chromium/documents</c> with the written document.
/// </summary>
public static class InvoiceHtml
{
    /// <summary>The style sheets the document links, by the URL it names them with.</summary>
    public static IReadOnlyList<string> StyleSheets { get; } = ["invoice.css", "invoice-theme.css"];

    /// <summary>The invoice as an HTML document.</summary>
    /// <param name="data">The invoice data.</param>
    /// <returns>The HTML text.</returns>
    public static string Write(InvoiceData data)
    {
        var culture = CultureInfo.GetCultureInfo(data.Language);
        var lines = string.Join("\n", data.Lines.Select(line =>
            $"""    <tr><td>{Escape(line.Item)}</td><td class="amount">{Escape(Amount(line.Amount, culture))}</td></tr>"""));
        // LF whatever the checkout: the raw string takes the source file's line ends, the measured document has LF.
        return LineFeeds($$"""
            <!DOCTYPE html>
            <html lang="{{Escape(data.Language)}}">
            <head>
            <meta charset="utf-8">
            <title>Invoice {{Escape(data.Number)}}</title>
            <link rel="stylesheet" href="{{StyleSheets[0]}}">
            <style>
              .total { font-weight: bold; border-top: 1px solid currentcolor }
              td.amount { text-align: right }
              .note { color: var(--muted, #666) }
              .closing { text-align: right }
            </style>
            </head>
            <body>
            <header><h1>Invoice <span class="number">{{Escape(data.Number)}}</span></h1></header>
            <p class="note">{{Escape(data.Note)}}</p>
            <table>
              <thead><tr><th>Item</th><th>Amount</th></tr></thead>
              <tbody>
            {{lines}}
              </tbody>
              <tfoot><tr class="total"><td>Total</td><td class="amount">{{Escape(Amount(data.Lines.Sum(line => line.Amount), culture))}}</td></tr></tfoot>
            </table>
            <p class="closing">{{Escape(data.Closing)}}</p>
            </body>
            </html>

            """);
    }

    private static string Amount(decimal amount, CultureInfo culture) => amount.ToString("N2", culture);
}

/// <summary>The data of an invoice.</summary>
/// <param name="Number">The invoice number.</param>
/// <param name="Language">The language of the document and of its number format, for example <c>en</c>.</param>
/// <param name="Note">The payment terms.</param>
/// <param name="Lines">The invoice lines, in order.</param>
/// <param name="Closing">The closing line.</param>
public sealed record InvoiceData(string Number, string Language, string Note, IReadOnlyList<InvoiceLine> Lines, string Closing)
{
    /// <summary>Reads an invoice from JSON (camelCase properties).</summary>
    /// <param name="path">The JSON file.</param>
    /// <returns>The data.</returns>
    public static InvoiceData Load(string path) =>
        JsonSerializer.Deserialize<InvoiceData>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException($"{path} holds no invoice.");
}

/// <summary>One invoice line.</summary>
/// <param name="Item">What is charged.</param>
/// <param name="Amount">The amount.</param>
public sealed record InvoiceLine(string Item, decimal Amount);
