using System.Linq;
using System.Text.Json;
using DirSize.Handlers;
using DirSize.Models;

namespace DirSize.Export;

public class JsonExporter : IExporter
{
    public string FileExtension => ".json";
    public string MimeType => "application/json";

    public string Export(ISizeHandler handler, DirSizeOptions options)
    {
        var directories = handler.DirectorySizes
            .Where(d => d.Value.TotalSize >= (options.MinSizeBytes ?? 0))
            .Select(d => new
            {
                path = d.Key,
                size = d.Value.TotalSize,
                fileCount = d.Value.FileCount,
                percentage = handler.TotalSize > 0 
                    ? (d.Value.TotalSize * 100.0 / handler.TotalSize) 
                    : 0
            });

        // Apply sorting
        directories = options.SortBy switch
        {
            SortBy.Size => directories.OrderByDescending(d => d.size),
            SortBy.Name => directories.OrderBy(d => d.path),
            SortBy.FileCount => directories.OrderByDescending(d => d.fileCount),
            _ => directories.OrderByDescending(d => d.size)
        };

        var result = new
        {
            summary = new
            {
                totalSize = handler.TotalSize,
                totalFiles = handler.TotalFiles,
                totalDirectories = handler.TotalDirectories,
                processingTimeMs = handler.ElapsedTime.TotalMilliseconds,
                errorCount = handler.ErrorCount
            },
            directories = directories.Take(options.Top).ToList(),
            errors = handler.Errors
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(result, jsonOptions);
    }
}