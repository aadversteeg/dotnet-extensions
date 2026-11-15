using System;
using System.Collections.Generic;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using FileSearch.Models;

namespace FileSearch.Handlers;

public interface ISearchHandler : ITraversalHandler<FileSystemEntry>
{
    List<FoundFile> FoundFiles { get; }
    bool StoppedEarly { get; }
    int FilesScanned { get; }
    int DirectoriesScanned { get; }
    int ErrorCount { get; }
    IReadOnlyList<string> Errors { get; }
    TimeSpan ElapsedTime { get; }
    SearchCriteria Criteria { get; }
    string GetSummary();
}