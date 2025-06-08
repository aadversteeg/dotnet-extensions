using Microsoft.Extensions.Logging;
using FileSystemSamples.Common.Options;
using Ave.Extensions.FileSystem.Traversal;

namespace FileCounter.Models;

public class FileCounterOptions : ICommonOptions
{
    // ICommonOptions implementation
    public required string Path { get; init; }
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;
    public bool Verbose { get; init; }
    public string? OutputPath { get; init; }
    public ExportFormat ExportFormat { get; init; } = ExportFormat.Text;
    public TraversalStrategyHint Strategy { get; init; } = TraversalStrategyHint.Auto;
    public int? MaxDepth { get; init; }
    
    // FileCounter specific options
    public string? ExtensionFilter { get; init; }
    public long MinSize { get; init; } = 0;
    public long MaxSize { get; init; } = long.MaxValue;
    public bool Recursive { get; init; } = true;
    public HandlerType HandlerType { get; init; } = HandlerType.Simple;
    public int MaxExtensions { get; init; } = 20;
}