using System;
using System.IO;
using System.Threading.Tasks;
using FileSearch.Handlers;
using FileSearch.Models;
using FileSearch.Services;
using FileSystemSamples.Common.Options;

namespace FileSearch.Export;

public static class SearchExporter
{
    public static async Task<string?> ExportAsync(
        ISearchHandler handler,
        ExportFormat format,
        SizeParser sizeParser,
        string? outputPath = null)
    {
        var exporter = CreateExporter(format, sizeParser);
        var content = exporter.Export(handler);

        if (outputPath != null)
        {
            await File.WriteAllTextAsync(outputPath, content);
            return null;
        }

        return content;
    }

    private static IExporter CreateExporter(ExportFormat format, SizeParser sizeParser)
    {
        return format switch
        {
            ExportFormat.Text => new TextExporter(sizeParser),
            ExportFormat.Json => new JsonExporter(),
            ExportFormat.Csv => new CsvExporter(),
            ExportFormat.Xml => new XmlExporter(),
            ExportFormat.Markdown => new MarkdownExporter(sizeParser),
            _ => throw new ArgumentException($"Unknown export format: {format}", nameof(format))
        };
    }
}