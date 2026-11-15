using System;
using System.Text;
using FileSearch.Handlers;
using FileSearch.Services;

namespace FileSearch.Export;

public class TextExporter : IExporter
{
    private readonly SizeParser _sizeParser;

    public TextExporter(SizeParser sizeParser)
    {
        _sizeParser = sizeParser;
    }

    public string Export(ISearchHandler handler)
    {
        var sb = new StringBuilder();
        
        if (handler.FoundFiles.Count == 0)
        {
            sb.AppendLine("No files found matching the criteria.");
        }
        else
        {
            sb.AppendLine($"Found {handler.FoundFiles.Count} file(s):");
            sb.AppendLine();

            foreach (var file in handler.FoundFiles)
            {
                sb.AppendLine(file.Path);
                sb.AppendLine($"  Size: {_sizeParser.FormatSize(file.Size)}");
                sb.AppendLine($"  Modified: {file.ModifiedUtc:yyyy-MM-dd HH:mm:ss}");
                
                if (!string.IsNullOrEmpty(file.Preview))
                {
                    sb.AppendLine($"  Preview: {file.Preview}");
                }
                
                if (file.Metadata?.Count > 0)
                {
                    sb.AppendLine("  Metadata:");
                    foreach (var kvp in file.Metadata)
                    {
                        sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
                    }
                }
                
                sb.AppendLine();
            }
        }

        sb.AppendLine("Search Statistics:");
        sb.AppendLine($"  Files scanned: {handler.FilesScanned:N0}");
        sb.AppendLine($"  Directories scanned: {handler.DirectoriesScanned:N0}");
        sb.AppendLine($"  Time: {FormatTime(handler.ElapsedTime)}");
        
        if (handler.StoppedEarly)
        {
            sb.AppendLine($"  Search stopped after finding {handler.Criteria.MaxResults} results");
        }

        if (handler.ErrorCount > 0)
        {
            sb.AppendLine($"  Errors: {handler.ErrorCount:N0}");
        }

        return sb.ToString();
    }

    private static string FormatTime(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
            return $"{elapsed.TotalMilliseconds:F0} ms";
        if (elapsed.TotalMinutes < 1)
            return $"{elapsed.TotalSeconds:F2} seconds";
        return $"{elapsed.TotalMinutes:F1} minutes";
    }
}