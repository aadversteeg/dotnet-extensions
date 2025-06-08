using System;
using Ave.Extensions.FileSystem.Traversal;

namespace FileSearch.Models;

public record SearchOptions
{
    public required string Path { get; init; }
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
    public int? MaxDepth { get; init; }
    public TraversalStrategyHint Strategy { get; init; } = TraversalStrategyHint.Search;
    public HandlerType HandlerType { get; init; } = HandlerType.Simple;
    public ExportFormat Format { get; init; } = ExportFormat.Text;
    public string? OutputPath { get; init; }
    public bool Verbose { get; init; }
}