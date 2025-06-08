using Ave.Extensions.FileSystem.Traversal;
using Microsoft.Extensions.Logging;

namespace DirSize.Models;

public class SizeOptions
{
    public string Path { get; set; } = string.Empty;
    public int Top { get; set; } = 10;
    public long? MinSizeBytes { get; set; }
    public int? MaxDepth { get; set; }
    public SortBy SortBy { get; set; } = SortBy.Size;
    public TraversalStrategyHint Strategy { get; set; } = TraversalStrategyHint.Auto;
    public HandlerType HandlerType { get; set; } = HandlerType.Simple;
    public ExportFormat ExportFormat { get; set; } = ExportFormat.Text;
    public string? OutputPath { get; set; }
    public bool Verbose { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Warning;
    public bool ShowPercentage { get; set; } = true;
    public bool IncludeHidden { get; set; } = false;
}