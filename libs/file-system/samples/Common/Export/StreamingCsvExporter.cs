using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileSystemSamples.Common.Export;

/// <summary>
/// Streaming CSV exporter for efficient export of large datasets.
/// </summary>
/// <typeparam name="T">The type of data being exported.</typeparam>
public class StreamingCsvExporter<T> : StreamingExporterBase<T>
{
    private readonly Func<T, string> _itemToCsvRow;
    private readonly string _headerRow;

    /// <summary>
    /// Initializes a new instance of the StreamingCsvExporter class.
    /// </summary>
    /// <param name="headerRow">The CSV header row.</param>
    /// <param name="itemToCsvRow">Function to convert an item to a CSV row.</param>
    public StreamingCsvExporter(string headerRow, Func<T, string> itemToCsvRow)
    {
        _headerRow = headerRow ?? throw new ArgumentNullException(nameof(headerRow));
        _itemToCsvRow = itemToCsvRow ?? throw new ArgumentNullException(nameof(itemToCsvRow));
    }

    /// <inheritdoc />
    protected override async Task WriteHeaderAsync(CancellationToken cancellationToken)
    {
        if (_writer == null)
            throw new InvalidOperationException("Writer is not initialized");

        await _writer.WriteLineAsync(_headerRow);
    }

    /// <inheritdoc />
    protected override async Task WriteItemAsync(T item, CancellationToken cancellationToken)
    {
        if (_writer == null)
            throw new InvalidOperationException("Writer is not initialized");

        var csvRow = _itemToCsvRow(item);
        await _writer.WriteLineAsync(csvRow);
    }

    /// <inheritdoc />
    protected override Task WriteFooterAsync(CancellationToken cancellationToken)
    {
        // CSV doesn't need a footer
        return Task.CompletedTask;
    }

    /// <summary>
    /// Escapes a CSV field value by wrapping it in quotes if necessary.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped CSV field value.</returns>
    public static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // If the value contains comma, quote, or newline, wrap it in quotes
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            // Escape internal quotes by doubling them
            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        return value;
    }
}