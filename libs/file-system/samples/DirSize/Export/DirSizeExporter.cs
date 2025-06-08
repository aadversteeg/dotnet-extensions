using System;
using System.IO;
using System.Threading.Tasks;
using DirSize.Handlers;
using DirSize.Models;

namespace DirSize.Export;

public static class DirSizeExporter
{
    public static async Task<string?> ExportAsync(
        ISizeHandler handler, 
        SizeOptions options,
        ExportFormat format, 
        string? outputPath = null)
    {
        var exporter = CreateExporter(format);
        var content = exporter.Export(handler, options);

        if (outputPath != null)
        {
            await File.WriteAllTextAsync(outputPath, content);
            return null;
        }

        return content;
    }

    private static IExporter CreateExporter(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Text => new TextExporter(),
            ExportFormat.Json => new JsonExporter(),
            ExportFormat.Csv => new CsvExporter(),
            ExportFormat.Xml => new XmlExporter(),
            ExportFormat.Markdown => new MarkdownExporter(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format")
        };
    }
}