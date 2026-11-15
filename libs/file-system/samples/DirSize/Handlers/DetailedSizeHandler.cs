using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using DirSize.Models;
using DirSize.Services;

namespace DirSize.Handlers;

public class DetailedSizeHandler : SimpleSizeHandler
{
    public override Task<Result<TraversalControl, Error>> OnFileAsync(
        FileSystemEntry file, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        // Call base implementation
        var result = base.OnFileAsync(file, context, cancellationToken);
        
        // Add detailed information
        if (!file.IsDirectory && _directoryStack.Count > 0)
        {
            var currentDir = _directoryStack.Peek();
            if (DirectorySizes.TryGetValue(currentDir, out var data))
            {
                // Update last modified if this file is newer
                if (file.ModifiedUtc > data.LastModified)
                {
                    data.LastModified = file.ModifiedUtc;
                }
            }
        }
        
        return result;
    }

    public override Task<Result<TraversalControl, Error>> OnDirectoryAsync(
        FileSystemEntry directory, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        // Call base implementation
        var result = base.OnDirectoryAsync(directory, context, cancellationToken);
        
        // Update subdirectory count in parent
        if (_directoryStack.Count > 1)
        {
            var parentDir = _directoryStack.Skip(1).First();
            if (DirectorySizes.TryGetValue(parentDir, out var parentData))
            {
                parentData.SubdirectoryCount++;
            }
        }
        
        return result;
    }

    public override string GetSummary()
    {
        var avgSize = TotalDirectories > 0 ? TotalSize / TotalDirectories : 0;
        var avgFiles = TotalDirectories > 0 ? (double)TotalFiles / TotalDirectories : 0;
        
        return base.GetSummary() + 
               $"\nAverage directory size: {SizeParser.FormatSize(avgSize)}" +
               $"\nAverage files per directory: {avgFiles:F1}";
    }
}