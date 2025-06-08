using System;
using System.Collections.Generic;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using DirSize.Models;

namespace DirSize.Handlers;

public interface ISizeHandler : ITraversalHandler<FileSystemEntry>
{
    Dictionary<string, DirectoryData> DirectorySizes { get; }
    long TotalSize { get; }
    int TotalFiles { get; }
    int TotalDirectories { get; }
    int ErrorCount { get; }
    IReadOnlyList<string> Errors { get; }
    TimeSpan ElapsedTime { get; }
    string GetSummary();
}