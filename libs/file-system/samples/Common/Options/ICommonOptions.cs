using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Traversal;
using Microsoft.Extensions.Logging;

namespace FileSystemSamples.Common.Options;

public interface ICommonOptions
{
    string Path { get; }
    LogLevel LogLevel { get; }
    bool Verbose { get; }
    string? OutputPath { get; }
    ExportFormat ExportFormat { get; }
    TraversalStrategyHint Strategy { get; }
    int? MaxDepth { get; }
}

public enum ExportFormat
{
    Text,
    Json,
    Csv,
    Xml,
    Markdown
}