using System.Linq;
using System.Text;
using DirSize.Handlers;
using DirSize.Models;
using DirSize.Services;

namespace DirSize.Export;

public class TextExporter : IExporter
{
    public string FileExtension => ".txt";
    public string MimeType => "text/plain";

    public string Export(ISizeHandler handler, SizeOptions options)
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

        // Take top N
        var topDirectories = directories.Take(options.Top).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"{"Size",-15} {"Files",-10} {"Percent",-8} Directory");
        sb.AppendLine(new string('-', 70));

        foreach (var dir in topDirectories)
        {
            var percentage = options.ShowPercentage ? $"{dir.Percentage:F1}%" : "";
            sb.AppendLine($"{SizeParser.FormatSize(dir.Size),-15} {dir.Files,-10} {percentage,-8} {dir.Path}");
        }

        sb.AppendLine();
        sb.AppendLine($"Total: {SizeParser.FormatSize(handler.TotalSize)} in {handler.TotalFiles:N0} files");
        sb.AppendLine($"Directories: {handler.TotalDirectories:N0}");
        sb.AppendLine($"Processing time: {SizeParser.FormatTime(handler.ElapsedTime)}");

        if (handler.ErrorCount > 0)
        {
            sb.AppendLine($"Errors: {handler.ErrorCount:N0} (access denied or other issues)");
        }

        return sb.ToString();
    }
}