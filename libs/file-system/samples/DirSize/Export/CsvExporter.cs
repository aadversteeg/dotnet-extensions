using System.Linq;
using System.Text;
using DirSize.Handlers;
using DirSize.Models;

namespace DirSize.Export;

public class CsvExporter : IExporter
{
    public string FileExtension => ".csv";
    public string MimeType => "text/csv";

    public string Export(ISizeHandler handler, DirSizeOptions options)
    {
        var directories = handler.DirectorySizes
            .Where(d => d.Value.TotalSize >= (options.MinSizeBytes ?? 0))
            .Select(d => new
            {
                Path = d.Key,
                Size = d.Value.TotalSize,
                Files = d.Value.FileCount,
                Percentage = handler.TotalSize > 0 
                    ? (d.Value.TotalSize * 100.0 / handler.TotalSize) 
                    : 0
            });

        // Apply sorting
        directories = options.SortBy switch
        {
            SortBy.Size => directories.OrderByDescending(d => d.Size),
            SortBy.Name => directories.OrderBy(d => d.Path),
            SortBy.FileCount => directories.OrderByDescending(d => d.Files),
            _ => directories.OrderByDescending(d => d.Size)
        };

        var sb = new StringBuilder();
        sb.AppendLine("Path,SizeBytes,FileCount,Percentage");

        foreach (var dir in directories.Take(options.Top))
        {
            var escapedPath = EscapeCsvField(dir.Path);
            sb.AppendLine($"{escapedPath},{dir.Size},{dir.Files},{dir.Percentage:F2}");
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}