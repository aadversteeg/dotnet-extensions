using Microsoft.Extensions.Logging;
using FileSystemSamples.Common.Options;
using Ave.Extensions.FileSystem.Traversal;

namespace DirSize.Models;

public class DirSizeOptions : ICommonOptions
{
    // ICommonOptions implementation
    public required string Path { get; init; }
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;
    public bool Verbose { get; init; }
    public string? OutputPath { get; init; }
    public ExportFormat ExportFormat { get; init; } = ExportFormat.Text;
    public TraversalStrategyHint Strategy { get; init; } = TraversalStrategyHint.Auto;
    public int? MaxDepth { get; init; }
    
    // DirSize specific options
    public int Top { get; init; } = 10;
    public long? MinSizeBytes { get; init; }
    public SortBy SortBy { get; init; } = SortBy.Size;
    public HandlerType HandlerType { get; init; } = HandlerType.Simple;
    public bool ShowPercentage { get; init; } = true;
    public bool IncludeHidden { get; init; } = false;
}