using System;
using System.IO;
using System.Linq;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using FileSearch.Models;
using Microsoft.Extensions.Logging;

namespace FileSearch.Handlers;

public class DetailedSearchHandler : SimpleSearchHandler
{
    private readonly int _previewLines = 3;

    public DetailedSearchHandler(SearchCriteria criteria, ILogger<DetailedSearchHandler> logger)
        : base(criteria, logger)
    {
    }

    protected override FoundFile CreateFoundFile(FileSystemEntry file)
    {
        var foundFile = base.CreateFoundFile(file);
        
        if (IsTextFile(foundFile.Extension))
        {
            try
            {
                foundFile.Preview = GetFilePreview(file.Path);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read preview for {Path}: {Error}", 
                    file.Path, ex.Message);
            }
        }

        return foundFile;
    }

    private bool IsTextFile(string? extension)
    {
        var textExtensions = new[] { ".txt", ".log", ".cs", ".js", ".json", ".xml", ".md", ".yml", ".yaml", ".config", ".ini" };
        return extension != null && textExtensions.Contains(extension.ToLower());
    }

    private string? GetFilePreview(CanonicalPath filePath)
    {
        try
        {
            var systemPath = filePath.Value;
            if (systemPath.StartsWith("/mnt/"))
            {
                systemPath = systemPath.Substring(5);
                if (systemPath.Length > 0 && char.IsLetter(systemPath[0]))
                {
                    systemPath = systemPath[0] + ":" + systemPath.Substring(1);
                }
            }

            if (!File.Exists(systemPath))
                return null;

            var lines = File.ReadLines(systemPath).Take(_previewLines).ToArray();
            return lines.Length > 0 ? string.Join(" | ", lines) : null;
        }
        catch
        {
            return null;
        }
    }

    public override string GetSummary()
    {
        var baseMessage = base.GetSummary();
        var previewCount = FoundFiles.Count(f => !string.IsNullOrEmpty(f.Preview));
        return $"{baseMessage} Generated {previewCount} file previews.";
    }
}