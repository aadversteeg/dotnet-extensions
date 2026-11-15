using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using FileSearch.Models;
using Microsoft.Extensions.Logging;

namespace FileSearch.Handlers;

public class MetadataSearchHandler : DetailedSearchHandler
{
    public MetadataSearchHandler(SearchCriteria criteria, ILogger<MetadataSearchHandler> logger)
        : base(criteria, logger)
    {
    }

    protected override FoundFile CreateFoundFile(FileSystemEntry file)
    {
        var foundFile = base.CreateFoundFile(file);
        
        foundFile.Metadata = CollectMetadata(file);

        return foundFile;
    }

    private Dictionary<string, object> CollectMetadata(FileSystemEntry file)
    {
        var metadata = new Dictionary<string, object>();

        try
        {
            metadata["IsReadOnly"] = (file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
            metadata["IsSystem"] = (file.Attributes & FileAttributes.System) == FileAttributes.System;
            metadata["IsArchive"] = (file.Attributes & FileAttributes.Archive) == FileAttributes.Archive;
            metadata["IsCompressed"] = (file.Attributes & FileAttributes.Compressed) == FileAttributes.Compressed;
            metadata["IsEncrypted"] = (file.Attributes & FileAttributes.Encrypted) == FileAttributes.Encrypted;

            var extension = Path.GetExtension(file.Name);
            if (!string.IsNullOrEmpty(extension))
            {
                metadata["FileType"] = GetFileType(extension);
                metadata["IsExecutable"] = IsExecutableFile(extension);
            }

            var systemPath = ConvertToSystemPath(file.Path);
            if (File.Exists(systemPath))
            {
                var fileInfo = new System.IO.FileInfo(systemPath);
                metadata["AccessedUtc"] = fileInfo.LastAccessTimeUtc;
                
                var size = file.Size ?? 0;
                if (size > 0)
                {
                    metadata["SizeCategory"] = GetSizeCategory(size);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error collecting metadata for {Path}: {Error}", file.Path, ex.Message);
            metadata["MetadataError"] = ex.Message;
        }

        return metadata;
    }

    private string ConvertToSystemPath(CanonicalPath canonicalPath)
    {
        var systemPath = canonicalPath.Value;
        if (systemPath.StartsWith("/mnt/"))
        {
            systemPath = systemPath.Substring(5);
            if (systemPath.Length > 0 && char.IsLetter(systemPath[0]))
            {
                systemPath = systemPath[0] + ":" + systemPath.Substring(1);
            }
        }
        return systemPath;
    }

    private string GetFileType(string extension)
    {
        return extension.ToLower() switch
        {
            ".txt" or ".log" or ".md" => "Text",
            ".cs" or ".js" or ".py" or ".java" or ".cpp" or ".c" => "Code",
            ".json" or ".xml" or ".yml" or ".yaml" => "Data",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "Image",
            ".mp4" or ".avi" or ".mkv" or ".mov" => "Video",
            ".mp3" or ".wav" or ".flac" or ".ogg" => "Audio",
            ".pdf" => "Document",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Archive",
            ".exe" or ".dll" or ".msi" => "Executable",
            _ => "Unknown"
        };
    }

    private bool IsExecutableFile(string extension)
    {
        var executableExtensions = new[] { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".sh" };
        return executableExtensions.Contains(extension.ToLower());
    }

    private string GetSizeCategory(long bytes)
    {
        return bytes switch
        {
            < 1024 => "Tiny",
            < 1024 * 1024 => "Small",
            < 10 * 1024 * 1024 => "Medium",
            < 100 * 1024 * 1024 => "Large",
            _ => "Very Large"
        };
    }

    public override string GetSummary()
    {
        var baseMessage = base.GetSummary();
        var metadataCount = FoundFiles.Count(f => f.Metadata?.Count > 0);
        return $"{baseMessage} Collected metadata for {metadataCount} files.";
    }
}