using System;
using System.Text;
using FileSearch.Handlers;

namespace FileSearch.Export;

public class CsvExporter : IExporter
{
    public string Export(ISearchHandler handler)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Path,Name,Size,Extension,ModifiedUtc,CreatedUtc,Attributes,Preview");
        
        foreach (var file in handler.FoundFiles)
        {
            sb.AppendLine($"\"{EscapeCSV(file.Path)}\"," +
                         $"\"{EscapeCSV(file.Name)}\"," +
                         $"{file.Size}," +
                         $"\"{EscapeCSV(file.Extension ?? "")}\"," +
                         $"{file.ModifiedUtc:yyyy-MM-dd HH:mm:ss}," +
                         $"{file.CreatedUtc:yyyy-MM-dd HH:mm:ss}," +
                         $"\"{file.Attributes}\"," +
                         $"\"{EscapeCSV(file.Preview ?? "")}\"");
        }

        sb.AppendLine();
        sb.AppendLine("# Search Statistics");
        sb.AppendLine($"# Files scanned: {handler.FilesScanned:N0}");
        sb.AppendLine($"# Directories scanned: {handler.DirectoriesScanned:N0}");
        sb.AppendLine($"# Elapsed time: {handler.ElapsedTime.TotalSeconds:F2} seconds");
        sb.AppendLine($"# Results found: {handler.FoundFiles.Count}");
        sb.AppendLine($"# Stopped early: {handler.StoppedEarly}");
        sb.AppendLine($"# Errors: {handler.ErrorCount}");

        return sb.ToString();
    }

    private static string EscapeCSV(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
            
        return value.Replace("\"", "\"\"");
    }
}