using System.Linq;
using System.Text;
using DirSize.Handlers;
using DirSize.Models;
using DirSize.Services;

namespace DirSize.Export;

public class MarkdownExporter : IExporter
{
    public string FileExtension => ".md";
    public string MimeType => "text/markdown";

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
        sb.AppendLine("# Directory Size Report");
        sb.AppendLine();
        
        // Summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total Size**: {SizeParser.FormatSize(handler.TotalSize)}");
        sb.AppendLine($"- **Total Files**: {handler.TotalFiles:N0}");
        sb.AppendLine($"- **Total Directories**: {handler.TotalDirectories:N0}");
        sb.AppendLine($"- **Processing Time**: {SizeParser.FormatTime(handler.ElapsedTime)}");
        if (handler.ErrorCount > 0)
        {
            sb.AppendLine($"- **Errors**: {handler.ErrorCount:N0}");
        }
        sb.AppendLine();

        // Directory table
        sb.AppendLine("## Top Directories");
        sb.AppendLine();
        sb.AppendLine("| Size | Files | Percent | Directory |");
        sb.AppendLine("|------|-------|---------|-----------|");

        foreach (var dir in directories.Take(options.Top))
        {
            var percentage = options.ShowPercentage ? $"{dir.Percentage:F1}%" : "";
            var escapedPath = dir.Path.Replace("|", "\\|");
            sb.AppendLine($"| {SizeParser.FormatSize(dir.Size)} | {dir.Files:N0} | {percentage} | `{escapedPath}` |");
        }

        sb.AppendLine();

        // Errors section
        if (handler.ErrorCount > 0)
        {
            sb.AppendLine("## Errors");
            sb.AppendLine();
            foreach (var error in handler.Errors.Take(10)) // Limit to first 10 errors
            {
                sb.AppendLine($"- {error}");
            }
            if (handler.Errors.Count > 10)
            {
                sb.AppendLine($"- ... and {handler.Errors.Count - 10} more errors");
            }
        }

        return sb.ToString();
    }
}