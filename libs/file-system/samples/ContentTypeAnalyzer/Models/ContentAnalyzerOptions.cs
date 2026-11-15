using Microsoft.Extensions.Logging;
using FileSystemSamples.Common.Options;
using Ave.Extensions.FileSystem.Traversal;

namespace ContentTypeAnalyzer.Models;

public class ContentAnalyzerOptions : ICommonOptions
{
    // ICommonOptions implementation
    public required string Path { get; init; }
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;
    public bool Verbose { get; init; }
    public string? OutputPath { get; init; }
    public ExportFormat ExportFormat { get; init; } = ExportFormat.Text;
    public TraversalStrategyHint Strategy { get; init; } = TraversalStrategyHint.Auto;
    public int? MaxDepth { get; init; }
    
    // ContentTypeAnalyzer specific options
    public HandlerType HandlerType { get; init; } = HandlerType.Simple;
    public FileCategory? FilterCategory { get; init; }
    public double MinimumConfidence { get; init; } = 0.7;
    public string DefinitionPack { get; init; } = "default";
    public string UsageType { get; init; } = "personal";
    public int MaxBytes { get; init; } = 8192;
    public bool FallbackExtension { get; init; } = true;
}