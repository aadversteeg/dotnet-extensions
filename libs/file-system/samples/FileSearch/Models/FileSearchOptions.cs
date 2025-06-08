using System;
using Microsoft.Extensions.Logging;
using FileSystemSamples.Common.Options;
using Ave.Extensions.FileSystem.Traversal;

namespace FileSearch.Models;

public class FileSearchOptions : ICommonOptions
{
    // ICommonOptions implementation
    public required string Path { get; init; }
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;
    public bool Verbose { get; init; }
    public string? OutputPath { get; init; }
    public ExportFormat ExportFormat { get; init; } = ExportFormat.Text;
    public TraversalStrategyHint Strategy { get; init; } = TraversalStrategyHint.Search;
    public int? MaxDepth { get; init; }
    
    // FileSearch specific options
    public string Pattern { get; init; } = "*";
    public string? RegexPattern { get; init; }
    public DateTime? ModifiedAfter { get; init; }
    public DateTime? ModifiedBefore { get; init; }
    public DateTime? CreatedAfter { get; init; }
    public DateTime? CreatedBefore { get; init; }
    public string? LargerThan { get; init; }
    public string? SmallerThan { get; init; }
    public int MaxResults { get; init; } = 100;
    public bool IncludeHidden { get; init; }
    public bool CaseSensitive { get; init; }
    public HandlerType HandlerType { get; init; } = HandlerType.Simple;
}